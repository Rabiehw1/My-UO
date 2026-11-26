#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers.Trainer;

/// <summary>
/// Trains the ticked skills in turn: every <see cref="TrainerSettings.WaitSeconds"/> it uses the next skill once,
/// since the shard lets you use one skill per shared cooldown. Main thread only; driven by <see cref="Update"/> each
/// frame while in game.
/// </summary>
internal sealed class TrainerManager
{
    #region Public accessors

    /// <summary>Default and minimum gap between uses, in seconds.</summary>
    public const int DEFAULT_WAIT_SECONDS = 10;
    public const int MIN_WAIT_SECONDS = 1;
    public const int MAX_WAIT_SECONDS = 600;

    public static TrainerManager Instance { get; } = new();

    /// <summary>Whether training is running.</summary>
    public bool IsRunning => _profile != null;

    #endregion

    #region Private members

    // Pause before trying again when every ticked skill had to be skipped this turn.
    private const uint ALL_SKIPPED_RETRY_MS = 1000;

    // A busy skill whose end line never came is tried again after this long.
    private const uint BUSY_GIVE_UP_MS = 5 * 60 * 1000;

    // How long after a list-opening use (Tracking) its gump is waited for and closed.
    private const uint CLOSE_LIST_WINDOW_MS = 3000;

    private readonly Dictionary<string, uint> _busyUntil = new(StringComparer.Ordinal);

    // Skills whose missing target was already reported, so the message shows once until it is found again.
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);

    private Profile? _profile;
    private uint _nextUseAt;
    private int _turn;
    private uint _closeListUntil;
    private uint _gumpBeforeList;

    #endregion

    #region Ctor

    private TrainerManager() { }

    #endregion

    #region Public methods

    /// <summary>Starts training the ticked skills, the first use right away.</summary>
    /// <returns>False with a message printed when nothing is ticked or scripting is not ready.</returns>
    public bool Start()
    {
        Profile? profile = ProfileManager.CurrentProfile;
        if (profile == null || World.Instance?.Player == null)
            return false;

        if (profile.Trainer.Selected.Count == 0)
        {
            GameActions.Print(TazLang.Get("trainer_nothing_selected"), Constants.HUE_WARN);
            return false;
        }

        _profile = profile;
        _nextUseAt = 0;
        _turn = 0;
        _busyUntil.Clear();
        _reportedMissing.Clear();
        EventSink.RawMessageReceived -= OnMessage;
        EventSink.RawMessageReceived += OnMessage;
        GameActions.Print(TazLang.Get("trainer_started"), Constants.HUE_WARN);
        return true;
    }

    /// <summary>Stops training; a use already sent finishes on its own.</summary>
    /// <param name="announce">Print that training stopped.</param>
    public void Stop(bool announce = true)
    {
        if (_profile == null)
            return;

        LumberjackTrainer.Reset(clearCooldowns: true);
        TailoringTrainer.Reset();
        BlacksmithTrainer.Reset();
        _profile = null;
        EventSink.RawMessageReceived -= OnMessage;
        if (announce)
            GameActions.Print(TazLang.Get("trainer_stopped"), Constants.HUE_WARN);
    }

    /// <summary>Runs the next skill when its turn is due. Cheap when idle: a couple of comparisons per frame.</summary>
    public void Update()
    {
        if (_profile == null)
            return;

        World? world = World.Instance;
        if (world?.Player == null || !ReferenceEquals(_profile, ProfileManager.CurrentProfile))
        {
            Stop(announce: false);
            return;
        }

        if (TailoringTrainer.Waiting(world) || BlacksmithTrainer.Waiting(world) || LumberjackTrainer.Waiting(world))
            return;

        if (_closeListUntil != 0)
            CloseOpenedList(world);

        if (Time.Ticks < _nextUseAt)
            return;

        // Another feature is waiting on a target cursor; try again shortly rather than cross cursors with it.
        if (TrainerSkillUser.CursorBusy(world))
        {
            _nextUseAt = Time.Ticks + ALL_SKIPPED_RETRY_MS;
            return;
        }

        TrainerSettings settings = _profile.Trainer;
        if (settings.Selected.Count == 0)
        {
            Stop();
            return;
        }

        uint gapMs = (uint)Math.Clamp(settings.WaitSeconds, MIN_WAIT_SECONDS, MAX_WAIT_SECONDS) * 1000;
        _nextUseAt = TryNextSkill(world, settings) ? Time.Ticks + gapMs : Time.Ticks + ALL_SKIPPED_RETRY_MS;
    }

    #endregion

    #region Private methods

    /// <summary>Runs the first ticked skill, from this turn on, that is ready.</summary>
    /// <returns>False when every ticked skill had to be skipped.</returns>
    private bool TryNextSkill(World world, TrainerSettings settings)
    {
        int count = settings.Selected.Count;
        for (int attempt = 0; attempt < count; attempt++)
        {
            string id = settings.Selected[_turn++ % count];
            TrainerSkill? skill = TrainerCatalog.Find(id);
            if (skill == null || IsBusy(skill) || !TryResolveTarget(world, settings, skill, out uint target))
                continue;

            if (skill.Use == TrainerUse.CloseList)
            {
                _gumpBeforeList = world.Player!.LastGumpID;
                _closeListUntil = Time.Ticks + CLOSE_LIST_WINDOW_MS;
            }

            if (TrainerSkillUser.Use(world, skill, target))
                return true;

            _closeListUntil = 0;
        }

        return false;
    }

    /// <summary>Closes the server gump a list-opening use brought up, once it arrives.</summary>
    private void CloseOpenedList(World world)
    {
        if (Time.Ticks > _closeListUntil)
        {
            _closeListUntil = 0;
            return;
        }

        uint gumpId = world.Player!.LastGumpID;
        if (gumpId == 0 || gumpId == _gumpBeforeList || UIManager.GetGumpServer(gumpId) is not { } gump)
            return;

        _closeListUntil = 0;
        if (gump.CanCloseWithRightClick)
            gump.InvokeMouseCloseGumpWithRClick();
        else
            gump.Dispose();
    }

    private bool IsBusy(TrainerSkill skill) =>
        skill.Busy != null && _busyUntil.TryGetValue(skill.Id, out uint until) && Time.Ticks < until;

    /// <summary>Finds the skill's picked target, reporting once when it is missing or was never picked.</summary>
    private bool TryResolveTarget(World world, TrainerSettings settings, TrainerSkill skill, out uint serial)
    {
        serial = 0;
        if (skill.Target == TrainerTargetKind.None)
            return true;

        settings.Targets.TryGetValue(skill.Id, out TrainerTarget? picked);
        bool found = picked != null && picked.Serial != 0 && (skill.Target == TrainerTargetKind.Item
            ? world.Items.Get(picked.Serial) is { IsDestroyed: false }
            : world.Mobiles.Get(picked.Serial) is { IsDestroyed: false });

        if (found)
        {
            _reportedMissing.Remove(skill.Id);
            serial = picked!.Serial;
            return true;
        }

        if (_reportedMissing.Add(skill.Id))
        {
            string message = picked == null || picked.Serial == 0
                ? TazLang.Get("trainer_target_not_picked", [skill.Name])
                : TazLang.Get(
                    skill.Target == TrainerTargetKind.Item ? "trainer_item_missing" : "trainer_creature_missing",
                    [skill.Name, picked.Name]
                );
            GameActions.Print(world, message, Constants.HUE_WARN);
        }

        return false;
    }

    /// <summary>Tracks busy skills (e.g. a meditation trance) from the player's journal lines.</summary>
    private void OnMessage(object? sender, MessageEventArgs e)
    {
        if (_profile == null || string.IsNullOrEmpty(e.Text))
            return;

        foreach (TrainerSkill skill in TrainerCatalog.Skills)
        {
            if (skill.Busy == null)
                continue;

            if (e.Text.Contains(skill.Busy.Start, StringComparison.OrdinalIgnoreCase))
            {
                _busyUntil[skill.Id] = Time.Ticks + BUSY_GIVE_UP_MS;
                continue;
            }

            foreach (string end in skill.Busy.Ends)
                if (e.Text.Contains(end, StringComparison.OrdinalIgnoreCase))
                    _busyUntil.Remove(skill.Id);
        }
    }

    #endregion
}
