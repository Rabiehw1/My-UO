#nullable enable
using System;
using System.Linq;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Myra.Graphics2D;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

internal static class DesktopAssistantForm
{
    internal const int ListSidebarWidth = 238;

    internal readonly record struct Field(string Label, Widget Control, Widget? Actions = null);

    internal static MyraGrid Fields(params Field[] fields)
    {
        bool hasActions = fields.Any(field => field.Actions != null);
        MyraGrid form = hasActions ? new FieldsWithActions() : new MyraGrid();
        form.ColumnSpacing = 8;
        form.RowSpacing = 4;
        form.VerticalAlignment = VerticalAlignment.Top;
        form.AddColumn();
        form.AddColumn(hasActions ? new Proportion(ProportionType.Auto) : new Proportion(ProportionType.Fill));
        if (hasActions)
            form.AddColumn(new Proportion(ProportionType.Fill));

        for (int row = 0; row < fields.Length; row++)
        {
            Field field = fields[row];
            form.AddWidget(new MyraLabel(field.Label, MyraLabel.TextStyle.P)
            {
                VerticalAlignment = VerticalAlignment.Center,
                Tooltip = field.Control.Tooltip
            }, row, 0);
            field.Control.HorizontalAlignment = HorizontalAlignment.Left;
            field.Control.VerticalAlignment = VerticalAlignment.Center;
            form.AddWidget(field.Control, row, 1);
            if (field.Actions != null)
                form.AddWidget(field.Actions, row, 2);
        }
        return form;
    }

    internal static WrapPanel Actions(params Widget[] buttons)
    {
        var row = new WrapPanel
        {
            HorizontalSpacing = 6,
            VerticalSpacing = 4,
            UniformSizing = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (Widget button in buttons)
            row.Widgets.Add(button);
        return row;
    }

    internal static MyraGrid ListSidebar(string title, ListView list, MyraButton[] actions,
        Widget? filter = null, Widget? footer = null)
    {
        var buttons = new MyraGrid { ColumnSpacing = 5, RowSpacing = 4 };
        buttons.AddColumn(new Proportion(ProportionType.Part, 1), count: 2);
        for (int index = 0; index < actions.Length; index++)
        {
            actions[index].HorizontalAlignment = HorizontalAlignment.Stretch;
            buttons.AddWidget(actions[index], index / 2, index % 2);
        }

        var sidebar = new MyraGrid
        {
            Background = DesktopAssistantTheme.Sidebar, Border = DesktopAssistantTheme.Divider,
            BorderThickness = new Thickness(1), Padding = new Thickness(7), RowSpacing = 5
        };
        list.HorizontalAlignment = HorizontalAlignment.Stretch;
        list.VerticalAlignment = VerticalAlignment.Stretch;
        list.ScrollViewer.ShowHorizontalScrollBar = false;
        int row = 0;
        Add(new MyraLabel(title, MyraLabel.TextStyle.H3));
        if (filter != null)
            Add(filter);
        Add(list, new Proportion(ProportionType.Fill));
        Add(buttons);
        if (footer != null)
            Add(footer);
        StyleListSelection(list);
        return sidebar;

        void Add(Widget child, Proportion? height = null)
        {
            sidebar.AddRow(height);
            sidebar.AddWidget(child, row++, 0);
        }
    }

    internal static MyraGrid ListRow(string name, Widget? detail = null)
    {
        var row = new MyraGrid { MinHeight = 24, ColumnSpacing = 4 };
        row.AddColumn(new Proportion(ProportionType.Fill));
        row.AddColumn();
        row.AddWidget(MyraGrid.TableText(name), 0, 0);
        if (detail != null)
        {
            detail.VerticalAlignment = VerticalAlignment.Center;
            row.AddWidget(detail, 0, 1);
        }
        return row;
    }

    internal static void StyleListSelection(ListView list)
    {
        MyraLabel? selected = null;
        list.SelectedIndexChanged += (_, _) => StyleSelection();
        StyleSelection();

        void StyleSelection()
        {
            if (selected != null)
            {
                selected.Font = DesktopAssistantTheme.Font();
                selected.TextColor = DesktopAssistantTheme.Text;
            }
            selected = list.SelectedItem?.FindChild<MyraLabel>();
            if (selected != null)
            {
                selected.Font = DesktopAssistantTheme.Font(bold: true);
                selected.TextColor = DesktopAssistantTheme.Accent;
            }
        }
    }

    internal static void RevealSelection(ListView list)
    {
        if (list.SelectedItem?.Parent is not Widget row)
            return;

        var scroll = list.ScrollViewer;
        scroll.UpdateArrange();
        int top = scroll.ToLocal(row.ToGlobal(Point.Zero)).Y;
        int bottom = top + row.Bounds.Height;
        int delta = top < 0 ? top : Math.Max(0, bottom - scroll.Bounds.Height);
        scroll.ScrollPosition = new Point(0, Math.Clamp(scroll.ScrollPosition.Y + delta, 0, scroll.ScrollMaximum.Y));
    }

    internal static MyraButton Button(string text, Action? click = null, bool primary = false, bool destructive = false)
    {
        var button = new MyraButton(text, click);
        DesktopAssistantTheme.ApplyActionButtonStyle(button, primary);
        if (destructive)
            MyraStyle.ApplyButtonDangerStyle(button);
        return button;
    }

    private sealed class FieldsWithActions : MyraGrid
    {
        protected override Point InternalMeasure(Point availableSize)
        {
            // Grid gives each cell the full form width on its first measurement. Give the
            // action rows their actual remaining width so a wrapped button adds to row height.
            int labelWidth = 0;
            int controlWidth = 0;
            foreach (Widget child in Widgets)
            {
                switch (GetColumn(child))
                {
                    case 0:
                        labelWidth = Math.Max(labelWidth, child.Measure(availableSize).X);
                        break;
                    case 1:
                        controlWidth = Math.Max(controlWidth, child.Measure(availableSize).X);
                        break;
                }
            }
            int remainingWidth = Math.Max(1, availableSize.X - labelWidth - controlWidth - 2 * ColumnSpacing);
            foreach (Widget child in Widgets)
                if (GetColumn(child) == 2)
                    child.MaxWidth = remainingWidth;

            return base.InternalMeasure(availableSize);
        }
    }

    internal sealed class FixedSidebarLayout(Widget content, int sidebarWidth) : MyraGrid
    {
        internal bool FitContent { get; set; } = true;

        protected override Point InternalMeasure(Point availableSize)
        {
            // Myra measures each grid cell against the whole page before it assigns column widths.
            // Feed the form the width that remains after the fixed sidebar.
            content.MaxWidth = FitContent ? Math.Max(1, availableSize.X - sidebarWidth - ColumnSpacing) : null;
            return base.InternalMeasure(availableSize);
        }
    }

    internal sealed class SectionPair : Container
    {
        private const int Spacing = 8;
        private readonly Widget _first;
        private readonly Widget _second;
        private readonly int _firstWeight;
        private readonly int _secondWeight;

        internal SectionPair(Widget first, Widget second, int firstWeight = 1, int secondWeight = 1)
        {
            _first = first;
            _second = second;
            _firstWeight = firstWeight;
            _secondWeight = secondWeight;
            Widgets.Add(first);
            Widgets.Add(second);
        }

        protected override Point InternalMeasure(Point availableSize)
        {
            var (first, second, sideBySide) = MeasureSections(availableSize);
            return sideBySide
                ? new Point(DesiredWidth(first.X, second.X), Math.Max(first.Y, second.Y))
                : new Point(Math.Max(first.X, second.X), first.Y + second.Y + Spacing);
        }

        protected override void InternalArrange()
        {
            Rectangle bounds = ActualBounds;
            var (first, second, sideBySide) = MeasureSections(new Point(bounds.Width, bounds.Height));
            int firstWidth = sideBySide ? ColumnWidth(bounds.Width) : bounds.Width;
            _first.Arrange(new Rectangle(bounds.X, bounds.Y, firstWidth, first.Y));
            _second.Arrange(sideBySide
                ? new Rectangle(bounds.X + firstWidth + Spacing, bounds.Y, bounds.Width - firstWidth - Spacing, second.Y)
                : new Rectangle(bounds.X, bounds.Y + first.Y + Spacing, bounds.Width, second.Y));
        }

        private (Point First, Point Second, bool SideBySide) MeasureSections(Point size)
        {
            int firstWidth = ColumnWidth(size.X);
            int secondWidth = Math.Max(0, size.X - Spacing - firstWidth);
            Point first = _first.Measure(new Point(firstWidth, size.Y));
            Point second = _second.Measure(new Point(secondWidth, size.Y));
            return firstWidth > 0 && secondWidth > 0 && first.X <= firstWidth && second.X <= secondWidth
                ? (first, second, true)
                : (_first.Measure(size), _second.Measure(size), false);
        }

        private int ColumnWidth(int width) => Math.Max(0, width - Spacing) * _firstWeight / (_firstWeight + _secondWeight);

        private int DesiredWidth(int first, int second)
        {
            int weight = _firstWeight + _secondWeight;
            return Math.Max((first * weight + _firstWeight - 1) / _firstWeight,
                (second * weight + _secondWeight - 1) / _secondWeight) + Spacing;
        }
    }
}
