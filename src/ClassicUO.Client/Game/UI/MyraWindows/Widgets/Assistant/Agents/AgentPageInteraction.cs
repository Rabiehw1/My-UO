#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Hotkeys;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Input;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using SDL3;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal sealed class AgentPageInteraction(Action<string, bool, bool>? showTargetStatus = null, Action? openItemLists = null,
    DesktopAssistantTheme? desktopTheme = null)
{
    public static AgentPageInteraction InGame { get; } = new();

    private TargetManager? _targetManager;
    private Action<object>? _targetCallback;
    private Action<SDL.SDL_KeyboardEvent>? _captureKey;
    private Window? _dialog;
    private HotkeyCapture? _hotkeyCapture;

    public bool IsDesktop => showTargetStatus != null;
    public bool IsCapturingDesktopHotkey => _hotkeyCapture != null;

    public void OpenItemLists()
    {
        if (openItemLists != null)
            openItemLists();
        else
            AssistantWindow.ShowItemLists();
    }

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
            {
                using var styles = desktopTheme?.Use();
                callback(result);
            }

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

    public void RunInGame(Action action)
    {
        using var styles = DesktopAssistantTheme.UseGameStyle();
        action();
        if (IsDesktop)
            SDL.SDL_RaiseWindow(Client.Game.Window.Handle);
    }

    public void Confirm(Widget anchor, string title, string message, Action confirmed)
    {
        ShowDialog(anchor, title, new MyraLabel(message, MyraLabel.TextStyle.P), ok =>
        {
            if (ok)
                confirmed();
        });
    }

    public void ShowDialog(Widget anchor, string title, Widget body, Action<bool> onClose, Func<bool>? canClose = null)
    {
        if (!IsDesktop)
        {
            new MyraDialog(title, body, onClose, canClose);
            return;
        }

        var dialog = new Window { Title = title };
        var content = new VerticalStackPanel { Spacing = 8, Padding = new Thickness(8) };
        var buttons = new HorizontalStackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Widgets.Add(new MyraButton(TazLang.Get("mog_kw_ok", "OK"), () =>
        {
            if (canClose?.Invoke() == false)
                return;

            dialog.Close();
            onClose(true);
        }));
        buttons.Widgets.Add(new MyraButton(TazLang.Get("hotkeycapture_cancel", "Cancel"), () =>
        {
            dialog.Close();
            onClose(false);
        }));
        content.Widgets.Add(body);
        content.Widgets.Add(buttons);
        dialog.Content = content;
        ShowDesktopDialog(anchor, dialog);
        if (body is TextBox && body.Desktop is Desktop desktop)
            desktop.FocusedKeyboardWidget = body;
    }

    public void ShowDesktopDialog(Widget anchor, Window dialog, Action<HotkeyBinding>? capture = null)
    {
        if (anchor.Desktop is not Desktop desktop)
            return;

        CancelDialog();
        desktop.HideContextMenu();
        dialog.Closed += (_, _) =>
        {
            _hotkeyCapture?.Stop();
            _hotkeyCapture = null;
            _dialog = null;
        };
        dialog.ShowModal(desktop);
        _dialog = dialog;
        if (capture != null)
        {
            _hotkeyCapture = new HotkeyCapture { AutoStop = false };
            _hotkeyCapture.StartForDesktop(capture);
        }
    }

    public void CancelDialog() => _dialog?.Close();

    public void CancelDesktopHotkeyCapture()
    {
        if (_hotkeyCapture != null)
            CancelDialog();
    }

    public bool HandleDesktopHotkeyInput(SDL.SDL_Event e)
    {
        if (_hotkeyCapture is not { } capture)
            return false;

        switch ((SDL.SDL_EventType)e.type)
        {
            case SDL.SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL.SDL_EventType.SDL_EVENT_KEY_UP:
                if (e.key.repeat)
                    return true;

                if (Keyboard.IgnoreBareModifierKey(e.key))
                    capture.HandleModifiers(e.key.mod);
                else if (e.key.type == SDL.SDL_EventType.SDL_EVENT_KEY_DOWN)
                {
                    var key = (SDL.SDL_Keycode)e.key.key;
                    if (key == SDL.SDL_Keycode.SDLK_ESCAPE)
                        CancelDialog();
                    else
                        capture.HandleKey(key, e.key.mod);
                }
                return true;
            case SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN when e.button.button is 2 or 4 or 5:
                var button = e.button.button switch
                {
                    2 => MouseButtonType.Middle,
                    4 => MouseButtonType.XButton1,
                    _ => MouseButtonType.XButton2
                };
                capture.HandleMouseButton(button, SDL.SDL_GetModState());
                return true;
            case SDL.SDL_EventType.SDL_EVENT_MOUSE_WHEEL when e.wheel.y != 0:
                bool up = e.wheel.y > 0;
                if (e.wheel.direction == SDL.SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED)
                    up = !up;
                capture.HandleWheel(up, SDL.SDL_GetModState());
                return true;
            case SDL.SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
                capture.HandleController((SDL.SDL_GamepadButton)e.gbutton.button);
                return true;
            case SDL.SDL_EventType.SDL_EVENT_TEXT_INPUT:
                return true;
            default:
                return false;
        }
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
