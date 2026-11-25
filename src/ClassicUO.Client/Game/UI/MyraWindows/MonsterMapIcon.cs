#nullable enable
using System;
using System.Globalization;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.MyraWindows;

internal static class MonsterMapIcon
{
    private const string Prefix = "monster-";

    internal static string Key(WeaponSwitchMonster monster) => $"{Prefix}{monster.Graphic:x4}-{monster.Hue ?? 0:x4}";

    internal static bool Draw(UltimaBatcher2D batcher, string? key, Point center)
    {
        if (key is not { Length: 17 } || !key.StartsWith(Prefix, StringComparison.Ordinal) || key[12] != '-'
            || !ushort.TryParse(key.AsSpan(8, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort graphic)
            || !ushort.TryParse(key.AsSpan(13, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort hue))
            return false;

        var animations = Client.Game.UO.Animations;
        if (graphic >= animations.MaxAnimationCount)
            return false;

        byte group = AnimationDisplay.GetAnimGroup(graphic);
        var frames = animations.GetAnimationFrames(graphic, group, 1, out ushort defaultHue, out _);
        // A creature that has never appeared on screen gets its animation type on the first load.
        byte loadedGroup = AnimationDisplay.GetAnimGroup(graphic);
        if (loadedGroup != group)
            frames = animations.GetAnimationFrames(graphic, loadedGroup, 1, out defaultHue, out _);
        foreach (ref var frame in frames)
        {
            if (frame.Texture == null || frame.UV.Width <= 0 || frame.UV.Height <= 0)
                continue;

            float scale = 28f / Math.Max(frame.UV.Width, frame.UV.Height);
            int width = Math.Max(1, (int)(frame.UV.Width * scale));
            int height = Math.Max(1, (int)(frame.UV.Height * scale));
            batcher.Draw(frame.Texture, new Rectangle(center.X - width / 2, center.Y - height / 2, width, height),
                frame.UV, ShaderHueTranslator.GetHueVector(hue == 0 ? defaultHue : hue, false, 1f));
            return true;
        }

        return false;
    }
}
