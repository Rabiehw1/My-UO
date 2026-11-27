#nullable enable

using System.IO;
using ClassicUO.Assets;
using ClassicUO.Game.Managers;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI;

internal static class MacroButtonArt
{
    private const string MainDressResource = "ClassicUO.Game.UI.Assets.MainDress.png";
    private static SpriteInfo _mainDress;

    internal static bool IncludesCaption(int? graphic) => graphic == Macro.MAIN_DRESS_GRAPHIC;

    internal static Point DisplaySize(int graphic, SpriteInfo sprite) =>
        graphic == Macro.MAIN_DRESS_GRAPHIC ? new Point(182, 60) : new Point(sprite.UV.Width, sprite.UV.Height);

    internal static bool IsPartialHue(int graphic)
    {
        if ((uint)graphic > ushort.MaxValue)
            return false;

        StaticTiles[] tiles = Client.Game.UO.FileManager.TileData.StaticData;
        return (uint)graphic < (uint)tiles.Length && tiles[graphic].IsPartialHue;
    }

    internal static SpriteInfo GetSprite(int graphic)
    {
        if (graphic != Macro.MAIN_DRESS_GRAPHIC)
            return (uint)graphic <= ushort.MaxValue ? Client.Game.UO.Gumps.GetGump((ushort)graphic) : SpriteInfo.Empty;

        GraphicsDevice device = Client.Game.GraphicsDevice;
        if (_mainDress.Texture is not { IsDisposed: false } texture || texture.GraphicsDevice != device)
        {
            _mainDress.Texture?.Dispose();
            using Stream artwork = typeof(MacroButtonArt).Assembly.GetManifestResourceStream(MainDressResource)
                ?? throw new FileNotFoundException(MainDressResource);
            texture = Texture2D.FromStream(device, artwork);
            _mainDress = new SpriteInfo { Texture = texture, UV = new Rectangle(0, 0, texture.Width, texture.Height) };
        }

        return _mainDress;
    }
}
