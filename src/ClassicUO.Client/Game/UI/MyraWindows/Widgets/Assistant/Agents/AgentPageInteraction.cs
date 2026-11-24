#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Hotkeys;
using ClassicUO.Input;
using SDL3;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal sealed class AgentPageInteraction(Action<string, bool, bool>? showTargetStatus = null)
{
    public static AgentPageInteraction InGame { get; } = new();

    private TargetManager? _targetManager;
    private Action<object>? _targetCallback;
    private Action<SDL.SDL_KeyboardEvent>? _captureKey;

    public void SelectTarget(Action<object> callback)
    {
        TargetManager manager = World.Instance.TargetManager;

        if (showTargetStatus == null)
        {
            manager.SetTargeting(callback);
            return;
        }

        CancelTarget();
        if (manager.IsTargeting)
        {
            showTargetStatus("Finish or cancel the current target in the game first.", false, false);
            return;
        }

        Profile profile = ProfileManager.CurrentProfile;
        _targetManager = manager;
        _targetCallback = result =>
        {
            _targetManager = null;
            _targetCallback = null;
            if (!World.Instance.InGame || !ReferenceEquals(profile, ProfileManager.CurrentProfile))
                return;

            if (result != null)
                callback(result);

            showTargetStatus(result == null ? "Selection cancelled." : "Selection returned from the game.", false, true);
        };

        manager.SetTargeting(_targetCallback);
        showTargetStatus("Select in the game, or press Esc there to cancel.", true, false);
        SDL.SDL_RaiseWindow(Client.Game.Window.Handle);
    }

    public void SelectGrabBag()
    {
        if (showTargetStatus == null)
        {
            World.Instance.TargetManager.SetTargeting(CursorTarget.SetGrabBag, 0, TargetType.Neutral);
            return;
        }

        SelectTarget(target =>
        {
            if (target is Item item)
                World.Instance.TargetManager.SetGrabBag(item.Serial);
        });
    }

    public void Update()
    {
        if (_targetManager != null && !_targetManager.IsCurrentTargetingAction(_targetCallback))
        {
            _targetManager = null;
            _targetCallback = null;
            showTargetStatus?.Invoke("Selection ended in the game.", false, false);
        }
    }

    public void CancelTarget()
    {
        if (_targetManager?.IsCurrentTargetingAction(_targetCallback) == true)
            _targetManager.Reset();

        _targetManager = null;
        _targetCallback = null;
    }

    public Action CaptureKey(Action<SDL.SDL_Keycode, SDL.SDL_Keymod> capture)
    {
        if (showTargetStatus == null)
        {
            void HandleGameKey(string hotkey)
            {
                var (key, modifiers) = HotkeyUtil.ParseHotKeyString(hotkey);
                capture(key, modifiers);
            }

            Keyboard.KeyDownEvent += HandleGameKey;
            return () => Keyboard.KeyDownEvent -= HandleGameKey;
        }

        _captureKey = e => capture((SDL.SDL_Keycode)e.key, HotkeyUtil.NormalizeMods(e.mod));
        return CancelKeyCapture;
    }

    public bool HandleKeyDown(SDL.SDL_KeyboardEvent key)
    {
        if (_captureKey == null)
            return false;

        if (!key.repeat && !Keyboard.IgnoreBareModifierKey(key))
            _captureKey(key);

        return true;
    }

    public bool IsCapturingKey => _captureKey != null;

    public void CancelKeyCapture() => _captureKey = null;
}
