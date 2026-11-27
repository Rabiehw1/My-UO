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
    private const string GetDressedResource = "ClassicUO.Game.UI.Assets.GetDressed.png";
    private static SpriteInfo _mainDress;
    private static SpriteInfo _getDressed;

    internal static bool IncludesCaption(int? graphic) => graphic is Macro.MAIN_DRESS_GRAPHIC or Macro.GET_DRESSED_GRAPHIC;

    internal static Point DisplaySize(int graphic, SpriteInfo sprite) =>
        IncludesCaption(graphic) ? new Point(182, 60) : new Point(sprite.UV.Width, sprite.UV.Height);

    internal static bool IsPartialHue(int graphic)
    {
        if ((uint)graphic > ushort.MaxValue)
            return false;

        StaticTiles[] tiles = Client.Game.UO.FileManager.TileData.StaticData;
        return (uint)graphic < (uint)tiles.Length && tiles[graphic].IsPartialHue;
    }

    internal static SpriteInfo GetSprite(int graphic)
    {
        return graphic switch
        {
            Macro.MAIN_DRESS_GRAPHIC => GetEmbeddedSprite(MainDressResource, ref _mainDress),
            Macro.GET_DRESSED_GRAPHIC => GetEmbeddedSprite(GetDressedResource, ref _getDressed),
            >= 0 and <= ushort.MaxValue => Client.Game.UO.Gumps.GetGump((ushort)graphic),
            _ => SpriteInfo.Empty
        };
    }

    private static SpriteInfo GetEmbeddedSprite(string resource, ref SpriteInfo sprite)
    {
        GraphicsDevice device = Client.Game.GraphicsDevice;
        if (sprite.Texture is not { IsDisposed: false } texture || texture.GraphicsDevice != device)
        {
            sprite.Texture?.Dispose();
            using Stream artwork = typeof(MacroButtonArt).Assembly.GetManifestResourceStream(resource)
                ?? throw new FileNotFoundException(resource);
            texture = Texture2D.FromStream(device, artwork);
            sprite = new SpriteInfo { Texture = texture, UV = new Rectangle(0, 0, texture.Width, texture.Height) };
        }

        return sprite;
    }
}
