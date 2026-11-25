#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers.Support;

/// <summary>
/// Shows what the support agent is doing as text above the helper's head. The text is client-only: it is not said,
/// not sent to the shard and not written to the journal. Repeats are suppressed so the head never fills with the
/// same line, and each kind of problem is shown at most once a minute. Game thread only.
/// </summary>
internal sealed class SupportStatus
{
    #region Public accessors

    public const ushort HUE_GOOD = 0x0044;
    public const ushort HUE_BUFF = 0x0059;
    public const ushort HUE_INFO = 0x0481;
    public const ushort HUE_COMBAT = 0x002B;
    public const ushort HUE_DANGER = 0x0021;

    #endregion

    #region Private members

    private const ushort HUE_PROBLEM = 0x0035;
    private const byte FONT = 3;

    /// <summary>The same line isn't shown again within this time.</summary>
    private const int REPEAT_MS = 4000;

    /// <summary>Each kind of problem is shown at most once in this time.</summary>
    private const int PROBLEM_REPEAT_MS = 60_000;

    private readonly World _world;
    private readonly Func<bool> _isEnabled;
    private readonly Dictionary<string, long> _problemShownAt = new();
    private string _lastText = "";
    private long _lastShownAt;

    #endregion

    #region Ctor

    /// <param name="world">The game world.</param>
    /// <param name="isEnabled">Whether the player wants status text shown; checked on every message.</param>
    public SupportStatus(World world, Func<bool> isEnabled)
    {
        _world = world;
        _isEnabled = isEnabled;
    }

    #endregion

    #region Public methods

    /// <summary>Shows <paramref name="text" /> above the helper's head unless it was just shown.</summary>
    /// <param name="text">What the helper is doing.</param>
    /// <param name="hue">Text colour; one of the HUE_ constants.</param>
    public void Show(string text, ushort hue)
    {
        long now = Time.Ticks;

        if (text == _lastText && now - _lastShownAt < REPEAT_MS)
            return;

        _lastText = text;
        _lastShownAt = now;
        Draw(text, hue);
    }

    /// <summary>Shows a problem, at most once a minute per <paramref name="key" />.</summary>
    /// <param name="key">Identifies the kind of problem, e.g. "bandages".</param>
    /// <param name="text">What is wrong.</param>
    public void Problem(string key, string text)
    {
        long now = Time.Ticks;

        if (_problemShownAt.TryGetValue(key, out long shownAt) && now - shownAt < PROBLEM_REPEAT_MS)
            return;

        _problemShownAt[key] = now;
        Draw(text, HUE_PROBLEM);
    }

    #endregion

    #region Private methods

    private void Draw(string text, ushort hue)
    {
        PlayerMobile? player = _world.Player;

        if (player == null || !_isEnabled())
            return;

        player.AddMessage(MessageType.Regular, text, FONT, hue, true, TextType.CLIENT);
    }

    #endregion
}
