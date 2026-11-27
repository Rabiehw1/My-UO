#nullable enable
using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls;

internal enum BraidedGoldIcon
{
    Potion, Fish, Loot, Bless, Protection, Agility, Strength, Cunning
}

internal sealed class BraidedGoldProgress : Control
{
    internal const int CardWidth = 164;
    internal const int CardHeight = 78;
    internal const int BuffWidth = 140;
    internal const int BuffHeight = 66;
    private static Texture2D? _frame;
    private static Texture2D? _icons;
    private readonly TextBox _title;
    private readonly TextBox _value;
    private readonly ushort _graphic;
    private readonly BraidedGoldIcon? _icon;
    private readonly Color _fillColor;
    private float _titleSize = 12;
    private static readonly Color Ivory = new(255, 236, 190);

    internal double Fraction { get; set; }
    internal string Value { get => _value.Text; set { if (_value.Text != value) _value.Text = value; } }

    internal BraidedGoldProgress(int width, int height, string title, BraidedGoldIcon? icon, ushort graphic = ushort.MaxValue, ushort accentHue = 0)
    {
        Width = width;
        Height = height;
        AcceptMouseInput = false;
        WantUpdateSize = false;
        _graphic = graphic;
        _icon = icon;
        _fillColor = accentHue == 0 ? new Color(26, 97, 206) : TextBox.ConvertHueToColor(accentHue);
        _title = TextBox.GetOne(title, EmbeddedFontNames.AVADONIAN, 12, Ivory, TextBox.RTLOptions.Default());
        _value = TextBox.GetOne("", EmbeddedFontNames.AVADONIAN, 18, Ivory,
            new TextBox.RTLOptions { StrokeEffect = true });
        Add(_title);
        Add(_value);
        Resize(width, height);
    }

    internal void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        int left = (int)(width * 0.38f);
        _title.X = _value.X = left;
        _title.Y = height * 14 / 100;
        _value.Y = height * 34 / 100;
        _title.Width = _value.Width = width - left - 8;
        UpdateFontSizes();
    }

    internal void SetFont(string font, float size)
    {
        if (_title.Font != font)
            _title.Font = _value.Font = font;
        float fittedSize = Math.Clamp(size, 8, 14);
        if (_titleSize != fittedSize)
        {
            _titleSize = fittedSize;
            UpdateFontSizes();
        }
    }

    private void UpdateFontSizes()
    {
        _title.FontSize = Math.Clamp(_titleSize * Height / BuffHeight, 7, 14);
        _value.FontSize = Height * 0.31f;
    }

    public override bool Draw(UltimaBatcher2D batcher, int x, int y)
    {
        if (IsDisposed)
            return false;

        Texture2D frame = LoadTexture(ref _frame, "BraidedGoldTimer.png");
        var plain = ShaderHueTranslator.GetHueVector(0);
        batcher.Draw(frame, new Rectangle(x, y, Width, Height), frame.Bounds, plain);

        int iconSize = Math.Min(Width * 29 / 100, Height * 58 / 100);
        int iconX = x + Width * 18 / 100;
        int iconY = y + Height * 43 / 100;
        if (_icon is { } icon)
        {
            Texture2D atlas = LoadTexture(ref _icons, "BraidedGoldIcons.png");
            const int cell = 96;
            int index = (int)icon;
            var source = new Rectangle(index % 3 * cell, index / 3 * cell, cell, cell);
            batcher.Draw(atlas, new Rectangle(iconX - iconSize / 2, iconY - iconSize / 2, iconSize, iconSize), source, plain);
        }
        else if (_graphic != ushort.MaxValue)
        {
            ref readonly SpriteInfo sprite = ref Client.Game.UO.Gumps.GetGump(_graphic);
            if (sprite.Texture != null && sprite.UV.Width > 0 && sprite.UV.Height > 0)
            {
                float scale = Math.Min((float)iconSize / sprite.UV.Width, (float)iconSize / sprite.UV.Height);
                int w = Math.Max(1, (int)(sprite.UV.Width * scale));
                int h = Math.Max(1, (int)(sprite.UV.Height * scale));
                batcher.Draw(sprite.Texture, new Rectangle(iconX - w / 2, iconY - h / 2, w, h), sprite.UV, plain);
            }
        }

        // Normalized bounds of the inset track in BraidedGoldTimer.png.
        int start = x + Width * 104 / 1000;
        int trackWidth = Width * 791 / 1000;
        int trackY = y + Height * 766 / 1000;
        int trackHeight = Math.Max(1, Height * 78 / 1000);
        int fill = double.IsFinite(Fraction) ? (int)(trackWidth * Math.Clamp(Fraction, 0, 1)) : 0;
        if (fill > 0)
        {
            Fill(new Rectangle(start, trackY, fill, trackHeight), _fillColor);
            Fill(new Rectangle(start, trackY, fill, 1), Color.Lerp(_fillColor, Color.White, 0.45f));
        }

        return base.Draw(batcher, x, y);

        void Fill(Rectangle rectangle, Color color) =>
            batcher.Draw(SolidColorTextureCache.GetTexture(color), rectangle, plain);
    }

    private static Texture2D LoadTexture(ref Texture2D? texture, string filename)
    {
        GraphicsDevice device = Client.Game.GraphicsDevice;
        if (texture is { IsDisposed: false } && texture.GraphicsDevice == device)
            return texture;

        texture?.Dispose();
        using Stream artwork = typeof(BraidedGoldProgress).Assembly.GetManifestResourceStream(
            "ClassicUO.Game.UI.Assets." + filename) ?? throw new FileNotFoundException(filename);
        return texture = Texture2D.FromStream(device, artwork);
    }
}
