#nullable enable

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

/// <summary>
/// Presses a button on the next gump the server opens, like an Orion gump hook: arm it right before using an item
/// (a songbook, a spell book) and call <see cref="Update" /> every frame. One button at a time; game thread only.
/// </summary>
internal sealed class PendingGumpButton
{
    /// <summary>How long to wait for the gump before giving up.</summary>
    private const int GUMP_WAIT_MS = 3000;

    private readonly World _world;
    private readonly Action<string>? _reportNoGump;
    private int _button = -1;
    private Gump? _gumpBeforeUse;
    private long _deadline;
    private Action? _onPressed;
    private Func<bool>? _canReply;
    private bool _requireButton;
    private string _sourceName = "";

    /// <param name="world">The game world.</param>
    /// <param name="reportNoGump">Shows the "no gump opened" message (given the item's name); null prints it to the
    /// system messages.</param>
    public PendingGumpButton(World world, Action<string>? reportNoGump = null)
    {
        _world = world;
        _reportNoGump = reportNoGump;
    }

    /// <summary>Whether a button is waiting for its gump.</summary>
    public bool IsArmed => _button >= 0;

    /// <summary>Waits for the next gump and presses <paramref name="button" /> on it.</summary>
    /// <param name="button">Gump button to press.</param>
    /// <param name="sourceName">What was used, for the message if no gump opens.</param>
    /// <param name="now">Current <see cref="Time.Ticks" />.</param>
    public void Arm(int button, string sourceName, long now, Action? onPressed = null, Func<bool>? canReply = null, bool requireButton = false)
    {
        _onPressed = onPressed;
        _canReply = canReply;
        _requireButton = requireButton;
        PlayerMobile? player = _world.Player;

        // Remember the gump showing now, so only a newly opened one is answered.
        _gumpBeforeUse = player == null ? null : UIManager.GetGumpServer(player.LastGumpID);
        _button = button;
        _sourceName = sourceName;
        _deadline = now + GUMP_WAIT_MS;
    }

    /// <summary>Presses the button once the gump appears, or gives up after a few seconds. Call every frame.</summary>
    /// <param name="now">Current <see cref="Time.Ticks" />.</param>
    public void Update(long now)
    {
        if (_button < 0)
            return;

        if (_canReply?.Invoke() == false)
        {
            Clear();
            return;
        }

        PlayerMobile? player = _world.Player;
        Gump? gump = player == null ? null : UIManager.GetGumpServer(player.LastGumpID);

        if (gump is { IsDisposed: false } && !ReferenceEquals(gump, _gumpBeforeUse)
            && (!_requireButton || HasButton(gump, _button)))
        {
            GameActions.ReplyGump(_world, gump.LocalSerial, gump.ServerSerial, _button);
            gump.Dispose();
            Action? onPressed = _onPressed;
            Clear();
            onPressed?.Invoke();
            return;
        }

        if (now > _deadline)
        {
            Log.Warn($"No gump opened after using '{_sourceName}'; button {_button} not pressed.");
            if (_reportNoGump != null)
                _reportNoGump(_sourceName);
            else
                GameActions.Print(TazLang.Get("gumpbutton_nogump", [_sourceName]), Constants.HUE_WARN);
            Clear();
        }
    }

    private static bool HasButton(Control parent, int button)
    {
        foreach (var child in parent.Children)
            if (child is Control control && (control is Button candidate && candidate.ButtonID == button || HasButton(control, button)))
                return true;

        return false;
    }

    /// <summary>Stops waiting.</summary>
    public void Clear()
    {
        _button = -1;
        _onPressed = null;
        _canReply = null;
        _gumpBeforeUse = null;
    }
}
