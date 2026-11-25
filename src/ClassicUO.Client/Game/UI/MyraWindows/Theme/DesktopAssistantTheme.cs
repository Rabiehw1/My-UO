#nullable enable
using System;
using ClassicUO.Assets;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.Styles;

namespace ClassicUO.Game.UI.MyraWindows.Theme;

internal sealed class DesktopAssistantTheme
{
    internal const int FontSize = 14;
    internal const int SidebarWidth = 172;
    internal static readonly Color Text = new(32, 37, 43);
    internal static readonly Color SecondaryText = new(76, 89, 104);
    internal static readonly Color Accent = new(33, 100, 155);
    internal static readonly SolidBrush Canvas = new(Color.White);
    internal static readonly SolidBrush TabStrip = new(new Color(233, 237, 241));
    internal static readonly SolidBrush Sidebar = new(new Color(242, 245, 248));
    internal static readonly SolidBrush Divider = new(new Color(203, 211, 220));
    internal static readonly SolidBrush Hover = new(new Color(237, 243, 248));
    internal static readonly SolidBrush Selected = new(new Color(229, 238, 247));

    private static readonly SolidBrush Focus = new(Accent);
    private static readonly SolidBrush GroupFill = new(new Color(249, 250, 252));
    private static readonly SolidBrush Disabled = new(new Color(240, 242, 245));
    private static readonly SolidBrush ControlBorder = new(new Color(174, 185, 198));
    private static readonly Color DisabledText = new(107, 117, 129);
    private readonly Stylesheet _stylesheet;
    private static Stylesheet? _gameStylesheet;

    internal static DesktopAssistantTheme? Current { get; private set; }

    internal static SpriteFontBase Font(int offset = 0, bool bold = false) =>
        TrueTypeLoader.Instance.GetFont(bold ? EmbeddedFontNames.ROBOTO_BOLD : EmbeddedFontNames.ROBOTO, FontSize + offset);

    internal DesktopAssistantTheme()
    {
        Stylesheet game = Stylesheet.Current;
        _stylesheet = game.Clone();

        var label = new LabelStyle { Font = Font(), TextColor = Text, DisabledTextColor = DisabledText };
        _stylesheet.LabelStyle = label;
        _stylesheet.TooltipStyle = new LabelStyle(label)
        {
            Background = Canvas, Border = Divider, BorderThickness = new Thickness(1), Padding = new Thickness(6, 4)
        };

        var button = new ButtonStyle
        {
            LabelStyle = label, Background = GroupFill, OverBackground = Hover, PressedBackground = Selected,
            DisabledBackground = Disabled, Border = ControlBorder, FocusedBorder = Focus, BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 3), MinHeight = 24
        };
        _stylesheet.ButtonStyle = button;
        _stylesheet.ButtonStyles["blue"] = new ButtonStyle(button) { Background = Selected };

        _stylesheet.CheckBoxStyle = ChoiceStyle(label, new CheckMark(false), new CheckMark(true));
        _stylesheet.RadioButtonStyle = ChoiceStyle(label, new CheckMark(false, round: true), new CheckMark(true, round: true));

        var field = new TextBoxStyle
        {
            Font = label.Font, TextColor = Text, DisabledTextColor = DisabledText,
            Background = Canvas, DisabledBackground = Disabled, Border = ControlBorder, FocusedBorder = Focus,
            BorderThickness = new Thickness(1), Padding = new Thickness(5, 3), MinHeight = 24,
            Cursor = new FlatImage(1, 14, Text), Selection = Selected
        };
        _stylesheet.TextBoxStyle = field;

        var list = new ListBoxStyle
        {
            Background = Canvas, Border = Divider, BorderThickness = new Thickness(1),
            ListItemStyle = new ImageTextButtonStyle
            {
                LabelStyle = label, Background = Canvas, OverBackground = Hover, PressedBackground = Selected,
                Padding = new Thickness(5, 3)
            },
            SeparatorStyle = _stylesheet.ListBoxStyle.SeparatorStyle
        };
        _stylesheet.ListBoxStyle = list;
        var combo = _stylesheet.ComboBoxStyle;
        combo.LabelStyle = label;
        combo.Background = new ComboFace(Canvas);
        combo.OverBackground = new ComboFace(Hover);
        combo.PressedBackground = new ComboFace(Selected);
        combo.DisabledBackground = new ComboFace(Disabled);
        combo.Border = ControlBorder;
        combo.FocusedBorder = Focus;
        combo.BorderThickness = new Thickness(1);
        combo.Padding = new Thickness(6, 3, 19, 3);
        combo.MinHeight = 24;
        combo.ListBoxStyle = list;

        var spin = _stylesheet.SpinButtonStyle;
        spin.Background = Canvas;
        spin.Border = ControlBorder;
        spin.BorderThickness = new Thickness(1);
        spin.TextBoxStyle = new TextBoxStyle(field) { BorderThickness = new Thickness(0) };
        foreach (ImageButtonStyle step in new[] { spin.UpButtonStyle, spin.DownButtonStyle })
        {
            step.Background = Sidebar;
            step.OverBackground = Hover;
            step.PressedBackground = Selected;
            step.DisabledBackground = Disabled;
        }

        var scroll = _stylesheet.ScrollViewerStyle;
        scroll.HorizontalScrollBackground = new FlatImage(1, 10, Sidebar.Color);
        scroll.HorizontalScrollKnob = new FlatImage(20, 10, ControlBorder.Color);
        scroll.VerticalScrollBackground = new FlatImage(10, 1, Sidebar.Color);
        scroll.VerticalScrollKnob = new FlatImage(10, 20, ControlBorder.Color);

        var slider = _stylesheet.HorizontalSliderStyle;
        slider.Height = 22;
        slider.Background = Sidebar;
        slider.OverBackground = Hover;
        slider.DisabledBackground = Disabled;
        slider.KnobStyle.ImageStyle = new PressableImageStyle { Image = new FlatImage(9, 22, new Color(134, 169, 198)) };

        var tabs = _stylesheet.TabControlStyle;
        tabs.CloseButtonStyle = game.TabControlStyle.CloseButtonStyle;
        tabs.TabItemStyle = new ImageTextButtonStyle(button)
        {
            Background = TabStrip, LabelStyle = new LabelStyle(label) { PressedTextColor = Accent },
            Padding = new Thickness(9, 4), BorderThickness = new Thickness(1, 1, 1, 0), Margin = new Thickness(1, 0)
        };
        tabs.ContentStyle = new WidgetStyle { Background = Canvas, Border = Divider, BorderThickness = new Thickness(1), Padding = new Thickness(8) };

        var window = _stylesheet.WindowStyle;
        window.Background = Sidebar;
        window.Border = ControlBorder;
        window.BorderThickness = new Thickness(1);
        window.Padding = new Thickness(6);
        window.TitleStyle = new LabelStyle(label) { Font = Font(1, bold: true), Padding = new Thickness(5, 4) };

        foreach (MenuStyle menu in new[] { _stylesheet.HorizontalMenuStyle, _stylesheet.VerticalMenuStyle })
        {
            menu.Background = Canvas;
            menu.Border = Divider;
            menu.BorderThickness = new Thickness(1);
            menu.LabelStyle = label;
            menu.ShortcutStyle = label;
            menu.SelectionBackground = Selected;
            menu.SelectionHoverBackground = Hover;
        }

        var tree = _stylesheet.TreeStyle;
        tree.LabelStyle = label;
        tree.SelectionBackground = Selected;
        tree.SelectionHoverBackground = Hover;

        _stylesheet.DesktopStyle = new DesktopStyle { Background = Canvas };
        _stylesheet.FileDialogStyle = game.FileDialogStyle;
        _stylesheet.ColorPickerDialogStyle = game.ColorPickerDialogStyle;
    }

    internal static MyraButton NavigationButton(string text, bool tab = false) => new(text)
    {
        Background = tab ? TabStrip : Sidebar,
        OverBackground = Hover,
        PressedBackground = Selected,
        DisabledBackground = tab ? Canvas : Selected,
        Border = tab ? Divider : null,
        OverBorder = Divider,
        DisabledBorder = tab ? Divider : Focus,
        BorderThickness = tab ? new Thickness(1, 1, 1, 0) : new Thickness(2, 0, 0, 0),
        Padding = tab ? new Thickness(9, 4) : new Thickness(5, 1),
        Margin = new Thickness(0),
        HorizontalAlignment = tab ? HorizontalAlignment.Left : HorizontalAlignment.Stretch
    };

    internal static void SelectNavigation(MyraButton button, bool selected)
    {
        button.LabelFont = Font(bold: selected);
        ((Label)button.Content).DisabledTextColor = Accent;
        button.Enabled = !selected;
    }

    internal static Widget Group(string label, params Widget[] children)
    {
        var grid = new MyraGrid { RowSpacing = 0 };
        grid.AddRow(new Proportion(ProportionType.Pixels, 9));
        grid.AddRow(new Proportion(ProportionType.Pixels, 10));
        grid.AddRow(new Proportion(ProportionType.Fill));
        grid.AddWidget(new Panel { Background = GroupFill, Border = Divider, BorderThickness = new Thickness(1) }, 1, 0, rowspan: 2);

        var fields = new VerticalStackPanel { Spacing = 3, Padding = new Thickness(9, 1, 9, 8) };
        foreach (Widget child in children)
            fields.Widgets.Add(child);
        grid.AddWidget(fields, 2, 0);
        grid.AddWidget(new MyraLabel(label, MyraLabel.TextStyle.P)
        {
            Font = Font(bold: true), Background = Canvas, Margin = new Thickness(9, 0, 0, 0), Padding = new Thickness(4, 0)
        }, 0, 0, rowspan: 2);
        return grid;
    }

    private static ImageTextButtonStyle ChoiceStyle(LabelStyle label, IImage uncheckedImage, IImage checkedImage) => new()
    {
        LabelStyle = label, ImageTextSpacing = 4,
        ImageStyle = new PressableImageStyle { Image = uncheckedImage, PressedImage = checkedImage },
        FocusedBorder = Focus
    };

    internal Scope Use() => new(this);
    internal static Scope? UseGameStyle() => Current == null ? null : new Scope(null);

    // Myra picks styles in widget constructors and during popup input. Limit the alternate
    // stylesheet to desktop passes and target callbacks that update the desktop's widgets.
    internal readonly struct Scope : IDisposable
    {
        private readonly DesktopAssistantTheme? _previousTheme;
        private readonly Stylesheet _previousStylesheet;
        private readonly Stylesheet? _previousGameStylesheet;

        internal Scope(DesktopAssistantTheme? theme)
        {
            _previousTheme = Current;
            _previousStylesheet = Stylesheet.Current;
            _previousGameStylesheet = _gameStylesheet;
            _gameStylesheet ??= _previousStylesheet;
            Stylesheet.Current = theme?._stylesheet ?? _gameStylesheet;
            Current = theme;
        }

        public void Dispose()
        {
            Current = _previousTheme;
            Stylesheet.Current = _previousStylesheet;
            _gameStylesheet = _previousGameStylesheet;
        }
    }

    private sealed class FlatImage(int width, int height, Color fill) : IImage
    {
        private readonly SolidBrush _brush = new(fill);
        public Point Size => new(width, height);
        public void Draw(RenderContext context, Rectangle dest, Color color) => _brush.Draw(context, dest, color);
    }

    private sealed class CheckMark(bool selected, bool round = false) : IImage
    {
        public Point Size => new(13, 13);

        public void Draw(RenderContext context, Rectangle dest, Color color)
        {
            SolidBrush outline = selected ? Focus : ControlBorder;
            SolidBrush fill = selected && !round ? Focus : Canvas;
            if (round)
            {
                outline.Draw(context, new Rectangle(dest.X + 2, dest.Y, 9, 13), color);
                outline.Draw(context, new Rectangle(dest.X, dest.Y + 2, 13, 9), color);
                fill.Draw(context, new Rectangle(dest.X + 2, dest.Y + 2, 9, 9), color);
                if (selected)
                    Focus.Draw(context, new Rectangle(dest.X + 4, dest.Y + 4, 5, 5), color);
                return;
            }

            outline.Draw(context, dest, color);
            fill.Draw(context, new Rectangle(dest.X + 1, dest.Y + 1, 11, 11), color);
            if (!selected)
                return;

            for (int i = 0; i < 3; i++)
                Canvas.Draw(context, new Rectangle(dest.X + 2 + i, dest.Y + 6 + i, 2, 2), color);
            for (int i = 0; i < 6; i++)
                Canvas.Draw(context, new Rectangle(dest.X + 4 + i, dest.Y + 8 - i, 2, 2), color);
        }
    }

    private sealed class ComboFace(IBrush background) : IBrush
    {
        private static readonly SolidBrush Glyph = new(Text);

        public void Draw(RenderContext context, Rectangle dest, Color color)
        {
            background.Draw(context, dest, color);
            int center = dest.Right - 10;
            int top = dest.Center.Y - 1;
            for (int row = 0; row < 4; row++)
                Glyph.Draw(context, new Rectangle(center - (3 - row), top + row, 7 - row * 2, 1), color);
        }
    }
}
