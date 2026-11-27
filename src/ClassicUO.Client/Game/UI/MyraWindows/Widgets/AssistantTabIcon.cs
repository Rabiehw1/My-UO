using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets;

internal sealed class AssistantTabIcon : Widget
{
    internal enum Kind { Settings, Agent, List, Keyboard, Book, Code, Filter, Training }

    private readonly Kind _kind;
    private readonly Button _tab;

    internal AssistantTabIcon(Kind kind, Button tab)
    {
        _kind = kind;
        _tab = tab;
        Width = Height = 18;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public override void InternalRender(RenderContext context)
    {
        var bounds = ActualBounds;
        var color = _tab.Enabled ? new Color(24, 47, 72) : Color.White;

        void Line(int x, int y, int endX, int endY) =>
            context.DrawLine(bounds.X + x, bounds.Y + y, bounds.X + endX, bounds.Y + endY, color, 1.3f);

        void Box(int x, int y, int width, int height) =>
            context.DrawRectangle(new Rectangle(bounds.X + x, bounds.Y + y, width, height), color, 1.3f);

        switch (_kind)
        {
            case Kind.Settings:
                context.DrawCircle(bounds.X + 9, bounds.Y + 9, 5.5f, 12, color, 1.5f);
                context.DrawCircle(bounds.X + 9, bounds.Y + 9, 2, 10, color, 1.2f);
                Line(9, 1, 9, 4); Line(9, 14, 9, 17);
                Line(1, 9, 4, 9); Line(14, 9, 17, 9);
                Line(3, 3, 5, 5); Line(13, 13, 15, 15);
                Line(15, 3, 13, 5); Line(5, 13, 3, 15);
                break;
            case Kind.Agent:
                Box(3, 6, 12, 10);
                Line(9, 2, 9, 6); Line(1, 9, 3, 9); Line(15, 9, 17, 9);
                Line(6, 9, 6, 11); Line(12, 9, 12, 11); Line(7, 13, 11, 13);
                break;
            case Kind.List:
                for (int y = 4; y <= 14; y += 5)
                {
                    Line(2, y, 4, y);
                    Line(7, y, 16, y);
                }
                break;
            case Kind.Keyboard:
                Box(1, 4, 16, 10);
                for (int x = 4; x <= 14; x += 3)
                    Line(x, 7, x + 1, 7);
                Line(4, 10, 5, 10); Line(7, 10, 13, 10);
                break;
            case Kind.Book:
                Line(2, 3, 7, 3); Line(7, 3, 9, 5); Line(9, 5, 11, 3); Line(11, 3, 16, 3);
                Line(2, 3, 2, 14); Line(16, 3, 16, 14);
                Line(2, 14, 7, 14); Line(7, 14, 9, 16); Line(9, 16, 11, 14); Line(11, 14, 16, 14);
                Line(9, 5, 9, 16);
                break;
            case Kind.Code:
                Line(5, 5, 1, 9); Line(1, 9, 5, 13);
                Line(13, 5, 17, 9); Line(17, 9, 13, 13);
                Line(11, 3, 7, 15);
                break;
            case Kind.Filter:
                Line(2, 3, 16, 3); Line(2, 3, 7, 9); Line(16, 3, 11, 9);
                Line(7, 9, 7, 16); Line(11, 9, 11, 13); Line(7, 16, 11, 13);
                break;
            case Kind.Training:
                Box(2, 11, 3, 5); Box(7, 7, 3, 9); Box(12, 3, 3, 13);
                break;
        }
    }
}
