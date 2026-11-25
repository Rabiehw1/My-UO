#nullable enable
using System;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Microsoft.Xna.Framework;
using Myra.Events;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets;

public class MyraHorizontalSlider : HorizontalSlider
{
    private readonly bool _desktop = DesktopAssistantTheme.Current != null;
    private static readonly Color TrackOutline = new(112, 131, 151);
    private static readonly Color TrackBackground = new(224, 232, 241);

    public override void InternalRender(RenderContext context)
    {
        if (_desktop && Bounds.Width >= 24 && Bounds.Height >= 10)
        {
            int inset = ImageButton.Bounds.Width / 2;
            var track = new Rectangle(inset, Bounds.Height / 2 - 4, Bounds.Width - inset * 2, 8);
            context.FillRectangle(track, TrackOutline);
            track.Inflate(-1, -1);
            context.FillRectangle(track, TrackBackground);

            int filled = Math.Clamp(ImageButton.Left + inset - track.X, 0, track.Width);
            if (filled > 0)
                context.FillRectangle(new Rectangle(track.X, track.Y, filled, track.Height),
                    Enabled ? DesktopAssistantTheme.Accent : TrackOutline);
        }
        base.InternalRender(context);
    }

    public override void OnMouseEntered()
    {
        if (!Enabled)
            return;
        base.OnMouseEntered();
    }

    public override void OnTouchEntered()
    {
        if (!Enabled)
            return;
        base.OnTouchEntered();
    }

    public override void OnMouseWheel(float delta)
    {
        if (!Enabled)
            return;
        base.OnMouseWheel(delta);
    }

    public override void OnTouchDown(TouchEventArgs args)
    {
        if (!Enabled || args.Button != TouchButton.Left)
            return;
        base.OnTouchDown(args);
    }
}
