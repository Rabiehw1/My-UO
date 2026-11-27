#nullable enable
using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls;

internal sealed class BraidedGoldProgress : Control
{
    private static Texture2D? _frame;
    private readonly TextBox _title;
    private readonly TextBox _value;
    private readonly ushort _graphic;
    private readonly ushort _hue;
    private readonly bool _itemArt;
    private readonly Color _fillColor;
    private static readonly Color Ivory = new(255, 236, 190);

    internal double Fraction { get; set; }
    internal string Value { get => _value.Text; set { if (_value.Text != value) _value.Text = value; } }

    internal BraidedGoldProgress(int width, int height, string title, ushort graphic, bool itemArt, ushort hue = 0, ushort accentHue = 0)
    {
        Width = width;
        Height = height;
        AcceptMouseInput = false;
        WantUpdateSize = false;
        _graphic = graphic;
        _hue = hue;
        _itemArt = itemArt;
        _fillColor = accentHue == 0 ? new Color(26, 97, 206) : TextBox.ConvertHueToColor(accentHue);
        int left = graphic == ushort.MaxValue ? 8 : 33;
        _title = TextBox.GetOne(title, TrueTypeLoader.EMBEDDED_FONT, 12, Ivory,
            TextBox.RTLOptions.Default(Width - left - 52));
        _title.X = left;
        _title.Y = 3;
        Add(_title);
        _value = TextBox.GetOne("", TrueTypeLoader.EMBEDDED_FONT, 12, Ivory,
            TextBox.RTLOptions.DefaultCenterStroked(46));
        _value.X = Width - 51;
        _value.Y = 3;
        Add(_value);
    }

    internal void SetFont(string font, float size)
    {
        if (_title.Font != font)
            _title.Font = _value.Font = font;
        float fittedSize = Math.Clamp(size, 8, 14);
        if (_title.FontSize != fittedSize)
            _title.FontSize = _value.FontSize = fittedSize;
    }

    public override bool Draw(UltimaBatcher2D batcher, int x, int y)
    {
        if (IsDisposed)
            return false;

        GraphicsDevice device = Client.Game.GraphicsDevice;
        if (_frame is not { IsDisposed: false } || _frame.GraphicsDevice != device)
        {
            _frame?.Dispose();
            using Stream artwork = typeof(BraidedGoldProgress).Assembly.GetManifestResourceStream(
                "ClassicUO.Game.UI.Assets.BraidedGoldTimer.png")!;
            _frame = Texture2D.FromStream(device, artwork);
        }

        var plain = ShaderHueTranslator.GetHueVector(0);
        batcher.Draw(_frame, new Rectangle(x, y, Width, Height), new Rectangle(0, 0, _frame.Width, _frame.Height), plain);

        if (_graphic != ushort.MaxValue)
        {
            SpriteInfo sprite = _itemArt ? Client.Game.UO.Arts.GetArt(_graphic) : Client.Game.UO.Gumps.GetGump(_graphic);
            if (sprite.Texture != null && sprite.UV.Width > 0 && sprite.UV.Height > 0)
            {
                float scale = Math.Min(25f / sprite.UV.Width, (Height - 8f) / sprite.UV.Height);
                int w = Math.Max(1, (int)(sprite.UV.Width * scale));
                int h = Math.Max(1, (int)(sprite.UV.Height * scale));
                batcher.Draw(sprite.Texture, new Rectangle(x + 18 - w / 2, y + (Height - h) / 2, w, h),
                    sprite.UV, ShaderHueTranslator.GetHueVector(_hue));
            }
        }

        int start = x + (_graphic == ushort.MaxValue ? 8 : 33);
        int trackWidth = x + Width - 8 - start;
        int trackY = y + Height - 10;
        Fill(new Rectangle(start, trackY, trackWidth, 6), new Color(166, 111, 45));
        Fill(new Rectangle(start + 1, trackY + 1, trackWidth - 2, 4), new Color(12, 21, 47));
        int fill = double.IsFinite(Fraction) ? (int)((trackWidth - 2) * Math.Clamp(Fraction, 0, 1)) : 0;
        if (fill > 0)
        {
            Fill(new Rectangle(start + 1, trackY + 1, fill, 4), _fillColor);
            Fill(new Rectangle(start + 1, trackY + 1, fill, 1), Color.Lerp(_fillColor, Color.White, 0.35f));
        }

        return base.Draw(batcher, x, y);

        void Fill(Rectangle rectangle, Color color) =>
            batcher.Draw(SolidColorTextureCache.GetTexture(color), rectangle, plain);
    }
}
