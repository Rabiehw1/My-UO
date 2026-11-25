#nullable enable

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
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
    private int _button = -1;
    private Gump? _gumpBeforeUse;
    private long _deadline;
    private string _sourceName = "";

    /// <param name="world">The game world.</param>
    public PendingGumpButton(World world) => _world = world;

    /// <summary>Whether a button is waiting for its gump.</summary>
    public bool IsArmed => _button >= 0;

    /// <summary>Waits for the next gump and presses <paramref name="button" /> on it.</summary>
    /// <param name="button">Gump button to press.</param>
    /// <param name="sourceName">What was used, for the message if no gump opens.</param>
    /// <param name="now">Current <see cref="Time.Ticks" />.</param>
    public void Arm(int button, string sourceName, long now)
    {
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

        PlayerMobile? player = _world.Player;
        Gump? gump = player == null ? null : UIManager.GetGumpServer(player.LastGumpID);

        if (gump is { IsDisposed: false } && !ReferenceEquals(gump, _gumpBeforeUse))
        {
            GameActions.ReplyGump(_world, gump.LocalSerial, gump.ServerSerial, _button);
            gump.Dispose();
            Clear();
            return;
        }

        if (now > _deadline)
        {
            Log.Warn($"No gump opened after using '{_sourceName}'; button {_button} not pressed.");
            GameActions.Print(TazLang.Get("gumpbutton_nogump", [_sourceName]), Constants.HUE_WARN);
            Clear();
        }
    }

    /// <summary>Stops waiting.</summary>
    public void Clear()
    {
        _button = -1;
        _gumpBeforeUse = null;
    }
}
