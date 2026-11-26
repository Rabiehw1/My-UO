#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ClassicUO.Assets;
using ClassicUO.LegionScripting;
using ClassicUO.Utility.Logging;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets;

/// <summary>Edits plain script source with a scrollable syntax display and a pinned line-number gutter.</summary>
internal sealed class ScriptSourceEditor : MyraGrid
{
    private const int PreferredWidth = 780;
    private const int PreferredHeight = 420;
    private const int TopInset = 6;
    private static readonly Color SourceColor = new(228, 235, 244);
    private static readonly SolidBrush EditorBackground = new(new Color(31, 37, 45));
    private static readonly SolidBrush GutterBackground = new(new Color(37, 46, 56));
    private static readonly SolidBrush EditorBorder = new(new Color(91, 108, 128));

    private readonly SourceInput _input;

    /// <summary>The editable source without any presentation markup.</summary>
    internal string Source => _input.Text ?? string.Empty;

    /// <summary>Raised when the user edits the source.</summary>
    internal event EventHandler? SourceChanged;

    /// <summary>Creates a code editor for the supplied script language.</summary>
    /// <param name="source">Initial source text.</param>
    /// <param name="type">Language used to highlight the source.</param>
    internal ScriptSourceEditor(string source, ScriptFile.ScriptType type)
    {
        var font = TrueTypeLoader.Instance.GetFont(EmbeddedFontNames.ROBOTO_MONO, 16);
        _input = new SourceInput(source, type, font);
        var viewport = new ScrollViewer
        {
            Content = _input, Background = EditorBackground,
            Padding = new Thickness(0), Margin = new Thickness(0), BorderThickness = new Thickness(0)
        };
        var gutter = new LineNumbers(_input, viewport);
        _input.TextChanged += (_, _) => gutter.UpdateWidth();
        _input.TextChangedByUser += (_, _) => SourceChanged?.Invoke(this, EventArgs.Empty);

        Background = EditorBackground;
        Border = EditorBorder;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(0, TopInset, 6, TopInset);
        ColumnSpacing = 9;
        AddColumn();
        AddColumn(new Proportion(ProportionType.Fill));
        AddRow(new Proportion(ProportionType.Fill));
        AddWidget(gutter, 0, 0);
        AddWidget(viewport, 0, 1);
    }

    protected override Point InternalMeasure(Point availableSize)
    {
        var size = new Point(Math.Min(PreferredWidth, availableSize.X), Math.Min(PreferredHeight, availableSize.Y));
        base.InternalMeasure(size);
        return size;
    }

    // SourceInput stays directly inside the ScrollViewer so Myra owns both horizontal and vertical caret scrolling.
    private sealed class SourceInput : MyraInputBox
    {
        private readonly ScriptFile.ScriptType _type;
        private readonly List<int> _lineStarts = [];
        private readonly Dictionary<int, SourceRun[]> _lineRuns = [];
        private readonly DeferredCaret _caret = new();
        private IReadOnlyList<ScriptSyntaxSpan> _syntax = [];
        private Task<IReadOnlyList<ScriptSyntaxSpan>>? _highlighting;
        private string? _requestedSource;

        public int LineCount => _lineStarts.Count;

        public SourceInput(string source, ScriptFile.ScriptType type, SpriteFontBase font)
        {
            _type = type;
            Font = font;
            Multiline = true;
            Wrap = false;
            Padding = new Thickness(0);
            Margin = new Thickness(0);
            BorderThickness = new Thickness(0);
            MinHeight = 0;
            Background = EditorBackground;
            FocusedBackground = EditorBackground;
            Selection = new SolidBrush(new Color(57, 83, 111));
            TextColor = Color.Transparent;
            FocusedTextColor = Color.Transparent;
            Cursor = _caret;
            VerticalAlignment = VerticalAlignment.Stretch;

            Text = source;
            SourceChanged();
            TextChanged += (_, _) => SourceChanged();
        }

        private void SourceChanged()
        {
            _lineStarts.Clear();
            _lineStarts.Add(0);
            for (int index = 0; index < Text.Length; index++)
            {
                if (Text[index] == '\n')
                    _lineStarts.Add(index + 1);
            }
            _syntax = [];
            _lineRuns.Clear();
        }

        private void RefreshHighlighting()
        {
            if (_highlighting is { IsCompleted: true })
            {
                if (_highlighting.IsCompletedSuccessfully && ReferenceEquals(Text, _requestedSource))
                {
                    _syntax = _highlighting.Result;
                    _lineRuns.Clear();
                }
                else if (_highlighting.Exception is { } error)
                {
                    Log.Warn($"Could not highlight script source: {error.GetBaseException().Message}");
                }
                _highlighting = null;
            }

            if (_highlighting == null && !ReferenceEquals(Text, _requestedSource))
            {
                string source = _requestedSource = Text;
                ScriptFile.ScriptType type = _type;
                if (source.Length > 0)
                    _highlighting = Task.Run(() => ScriptSyntaxHighlighting.Classify(source, type));
            }
        }

        public override void InternalRender(RenderContext context)
        {
            RefreshHighlighting();
            _caret.Bounds = null;
            base.InternalRender(context);

            var viewport = (ScrollViewer)Parent;
            var (first, end) = VisibleLines(viewport, Font.LineHeight, LineCount);
            Rectangle origin = ActualBounds;
            for (int line = first; line < end; line++)
            {
                int y = origin.Top + line * Font.LineHeight;
                foreach (SourceRun run in GetLineRuns(line))
                    context.DrawString(Font, run.Text, new Vector2(origin.Left + run.Offset, y), run.Color);
            }
            if (_caret.Bounds is { } caret)
                context.FillRectangle(caret, SourceColor);
        }

        // Reserve a full row for the caret on the final blank line.
        protected override Point InternalMeasure(Point availableSize)
        {
            Point size = base.InternalMeasure(availableSize);
            size.Y = Math.Max(size.Y, LineCount * Font.LineHeight);
            return size;
        }

        private SourceRun[] GetLineRuns(int line)
        {
            if (_lineRuns.TryGetValue(line, out var cached))
                return cached;

            int position = _lineStarts[line];
            int end = line + 1 < LineCount ? _lineStarts[line + 1] - 1 : Text.Length;
            float offset = 0;
            var runs = new List<SourceRun>();
            for (int index = FirstSpanEndingAfter(position); index < _syntax.Count; index++)
            {
                ScriptSyntaxSpan span = _syntax[index];
                if (span.Start >= end)
                    break;

                AddRun(Math.Max(position, span.Start), SourceColor);
                AddRun(Math.Min(end, span.Start + span.Length), SyntaxColor(span.Kind));
            }
            AddRun(end, SourceColor);

            return _lineRuns[line] = runs.ToArray();

            void AddRun(int until, Color color)
            {
                if (until <= position)
                    return;

                string text = Text[position..until];
                runs.Add(new SourceRun(text, color, offset));
                offset += Font.TextBounds(text, Vector2.Zero).X2;
                position = until;
            }
        }

        private int FirstSpanEndingAfter(int position)
        {
            int start = 0;
            int end = _syntax.Count;
            while (start < end)
            {
                int middle = start + (end - start) / 2;
                ScriptSyntaxSpan span = _syntax[middle];
                if (span.Start + span.Length <= position)
                    start = middle + 1;
                else
                    end = middle;
            }
            return start;
        }

        private static Color SyntaxColor(ScriptSyntaxKind kind) => kind switch
        {
            ScriptSyntaxKind.Comment => new Color(154, 171, 189),
            ScriptSyntaxKind.Keyword => new Color(218, 166, 249),
            ScriptSyntaxKind.String => new Color(179, 216, 145),
            ScriptSyntaxKind.Number => new Color(135, 220, 225),
            ScriptSyntaxKind.Api => new Color(255, 203, 137),
            _ => SourceColor
        };

        private readonly record struct SourceRun(string Text, Color Color, float Offset);

        // Myra still decides the caret's position and blink. Draw it after the colored text pass.
        private sealed class DeferredCaret : IImage
        {
            public Point Size => new(1, 1);
            public Rectangle? Bounds { get; set; }
            public void Draw(RenderContext context, Rectangle dest, Color color) => Bounds = dest;
        }
    }

    private sealed class LineNumbers : Widget
    {
        private static readonly Color NumberColor = new(155, 171, 190);
        private const int SideInset = 8;

        private readonly SourceInput _input;
        private readonly ScrollViewer _viewport;
        private readonly float _digitWidth;
        private readonly Dictionary<int, string> _labels = [];

        public LineNumbers(SourceInput input, ScrollViewer viewport)
        {
            _input = input;
            _viewport = viewport;
            _digitWidth = input.Font.TextBounds("0", Vector2.Zero).X2;
            Background = GutterBackground;
            VerticalAlignment = VerticalAlignment.Stretch;
            ClipToBounds = true;
            UpdateWidth();
        }

        public void UpdateWidth()
        {
            int digits = Math.Max(2, _input.LineCount.ToString(CultureInfo.InvariantCulture).Length);
            Width = (int)Math.Ceiling(digits * _digitWidth) + 2 * SideInset;
        }

        public override void InternalRender(RenderContext context)
        {
            var (first, end) = VisibleLines(_viewport, _input.Font.LineHeight, _input.LineCount);
            Rectangle bounds = ActualBounds;
            int y = bounds.Top + first * _input.Font.LineHeight - _viewport.ScrollPosition.Y;
            for (int line = first; line < end; line++, y += _input.Font.LineHeight)
            {
                if (!_labels.TryGetValue(line, out string? label))
                    _labels[line] = label = (line + 1).ToString(CultureInfo.InvariantCulture);
                float x = bounds.Right - SideInset - label.Length * _digitWidth;
                context.DrawString(_input.Font, label, new Vector2(x, y), NumberColor);
            }
        }
    }

    private static (int First, int End) VisibleLines(ScrollViewer viewport, int lineHeight, int lineCount)
    {
        int top = viewport.ScrollPosition.Y;
        int first = Math.Max(0, top / lineHeight);
        int end = Math.Min(lineCount, (top + viewport.ActualBounds.Height) / lineHeight + 1);
        return (first, end);
    }
}
