#nullable enable
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

internal static class DesktopAssistantForm
{
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

        internal SectionPair(Widget first, Widget second)
        {
            _first = first;
            _second = second;
            Widgets.Add(first);
            Widgets.Add(second);
        }

        protected override Point InternalMeasure(Point availableSize)
        {
            var (first, second, sideBySide) = MeasureSections(availableSize);
            return sideBySide
                ? new Point(2 * Math.Max(first.X, second.X) + Spacing, Math.Max(first.Y, second.Y))
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
            Point column = new(ColumnWidth(size.X), size.Y);
            Point first = _first.Measure(column);
            Point second = _second.Measure(column);
            return column.X > 0 && first.X <= column.X && second.X <= column.X
                ? (first, second, true)
                : (_first.Measure(size), _second.Measure(size), false);
        }

        private static int ColumnWidth(int width) => Math.Max(0, (width - Spacing) / 2);
    }
}
