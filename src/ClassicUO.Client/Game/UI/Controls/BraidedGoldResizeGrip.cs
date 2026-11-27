#nullable enable

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls;

internal sealed class BraidedGoldResizeGrip : Control
{
    internal const int MinScale = 50;
    internal const int MaxScale = 100;
    internal const int Size = 13;

    private readonly Control _owner;
    private readonly Func<int> _getScale;
    private readonly Action<int> _setScale;
    private Point _start;
    private int _startScale;
    private bool _dragging;

    internal BraidedGoldResizeGrip(Control owner, Func<int> getScale, Action<int> setScale)
    {
        _owner = owner;
        _getScale = getScale;
        _setScale = setScale;
        Width = Height = Size;
        CanMove = false;
        AcceptMouseInput = true;
        WantUpdateSize = false;
        SetTooltip(TazLang.Get("assistant_braided_resize_grip"));
    }

    internal static int ScalePixels(int pixels, int scale) =>
        (pixels * Math.Clamp(scale, MinScale, MaxScale) + 50) / 100;

    public override void OnMouseDown(int x, int y, MouseButtonType button)
    {
        if (button != MouseButtonType.Left)
            return;

        _start = Mouse.Position;
        _startScale = _getScale();
        _dragging = true;
    }

    public override void OnMouseUp(int x, int y, MouseButtonType button)
    {
        if (button != MouseButtonType.Left || !_dragging)
            return;

        ApplyResize();
        Finish();
    }

    public override void Update()
    {
        base.Update();
        if (!_dragging)
            return;

        if (Mouse.LButtonPressed)
            ApplyResize();
        else
            Finish();
    }

    private void ApplyResize()
    {
        Point delta = Mouse.Position - _start;
        int scale = Math.Clamp(_startScale + (int)MathF.Round((delta.X + delta.Y) / 2f), MinScale, MaxScale);
        if (scale != _getScale())
            _setScale(scale);
    }

    private void Finish()
    {
        _dragging = false;
        if (_getScale() != _startScale)
            ProfileManager.CurrentProfile?.Save();
    }

    public override bool Draw(UltimaBatcher2D batcher, int x, int y)
    {
        if (_dragging || UIManager.MouseOverControl?.RootParent == _owner)
        {
            var texture = SolidColorTextureCache.GetTexture(Color.White);
            var hue = ShaderHueTranslator.GetHueVector(0, false, 0.8f);
            for (int i = 0; i < 3; i++)
                batcher.Draw(texture, new Rectangle(x + Width - 3 - i * 3, y + Height - 3 - i * 3, 2, 2), hue);
        }
        return true;
    }
}
