#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Hotkeys;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Input;
using Microsoft.Xna.Framework;
using Myra.Events;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;
using SDL3;
using Keys = Microsoft.Xna.Framework.Input.Keys;

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
    private Func<bool>? _desktopCaptureReady;

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

    public void Confirm(Widget anchor, string title, string message, Action confirmed,
        string? confirmText = null, bool destructive = false)
    {
        ShowDialog(anchor, title, new MyraLabel(message, MyraLabel.TextStyle.P), ok =>
        {
            if (ok)
                confirmed();
        }, confirmText: confirmText, destructive: destructive);
    }

    public void ShowDialog(Widget anchor, string title, Widget body, Action<bool> onClose, Func<bool>? canClose = null,
        string? confirmText = null, bool destructive = false, int preferredWidth = 480, MyraButton[]? secondaryActions = null)
    {
        if (!IsDesktop)
        {
            new MyraDialog(title, body, onClose, canClose);
            return;
        }

        var confirm = new MyraButton(confirmText ?? TazLang.Get("uicommons_ok"));
        var dialog = CreateDesktopDialog(title, body, [confirm], secondaryActions, preferredWidth);
        if (destructive)
            MyraStyle.ApplyButtonDangerStyle(confirm);

        confirm.OnClick = () =>
        {
            if (canClose?.Invoke() == false)
                return;

            dialog.Result = true;
            dialog.Close();
        };
        ShowDesktopDialog(anchor, dialog);
        dialog.Closed += (_, _) => onClose(dialog.Result);
        if ((body as TextBox ?? body.FindChild<TextBox>()) is { Desktop: { } desktop } input)
            desktop.FocusedKeyboardWidget = input;
    }

    internal static Window CreateDesktopDialog(string title, Widget body, MyraButton[] confirmActions,
        MyraButton[]? secondaryActions = null, int preferredWidth = 480)
    {
        var dialog = new AssistantModalWindow(preferredWidth) { Title = title };
        var footer = new MyraGrid
        {
            ColumnSpacing = 8, Padding = new Thickness(0, 8, 0, 0),
            Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(0, 1, 0, 0)
        };
        footer.AddColumn(new Proportion(ProportionType.Fill));
        footer.AddColumn();
        if (secondaryActions is { Length: > 0 })
        {
            var secondary = Actions();
            foreach (MyraButton action in secondaryActions)
                secondary.Widgets.Add(action);
            footer.AddWidget(secondary, 0, 0);
        }

        var primary = Actions();
        primary.HorizontalAlignment = HorizontalAlignment.Right;
        primary.Widgets.Add(new MyraButton(TazLang.Get("uicommons_cancel"), dialog.Close));
        foreach (MyraButton action in confirmActions)
        {
            DesktopAssistantTheme.ApplyActionButtonStyle(action, primary: true);
            primary.Widgets.Add(action);
        }
        footer.AddWidget(primary, 0, 1);

        var content = new MyraGrid { RowSpacing = 8, Padding = new Thickness(8) };
        content.AddColumn(new Proportion(ProportionType.Fill));
        content.AddRow(new Proportion(ProportionType.Fill));
        content.AddRow();
        content.AddWidget(new ScrollViewer { Content = body }, 0, 0);
        content.AddWidget(footer, 1, 0);
        dialog.Content = content;
        return dialog;

        static WrapPanel Actions() => new()
        {
            Orientation = Orientation.Horizontal, HorizontalSpacing = 6, VerticalSpacing = 4, UniformSizing = false
        };
    }

    public void ShowDesktopDialog(Widget anchor, Window dialog, Action<HotkeyBinding>? capture = null, Func<bool>? captureReady = null)
    {
        if (anchor.Desktop is not Desktop desktop)
            return;

        CancelDialog();
        desktop.HideContextMenu();
        dialog.CloseKey = null;
        desktop.KeyDown += CloseOnEscape;
        dialog.Closed += (_, _) =>
        {
            desktop.KeyDown -= CloseOnEscape;
            _hotkeyCapture?.Stop();
            _hotkeyCapture = null;
            _desktopCaptureReady = null;
            _dialog = null;
        };
        dialog.ShowModal(desktop);
        _dialog = dialog;
        if (capture != null)
        {
            _desktopCaptureReady = captureReady;
            _hotkeyCapture = new HotkeyCapture { AutoStop = false };
            _hotkeyCapture.StartForDesktop(capture);
        }

        void CloseOnEscape(object? sender, GenericEventArgs<Keys> args)
        {
            if (args.Data == Keys.Escape && desktop.ContextMenu == null)
                dialog.Close();
        }
    }

    public void CancelDialog() => _dialog?.Close();

    private sealed class AssistantModalWindow(int preferredWidth) : Window
    {
        private const int ViewportInset = 12;

        protected override Point InternalMeasure(Point availableSize)
        {
            availableSize.X = Math.Max(1, Math.Min(preferredWidth, availableSize.X - 2 * ViewportInset));
            availableSize.Y = Math.Max(1, availableSize.Y - 2 * ViewportInset);
            Point measured = base.InternalMeasure(availableSize);
            return new Point(Math.Min(measured.X, availableSize.X), Math.Min(measured.Y, availableSize.Y));
        }

        protected override void InternalArrange()
        {
            base.InternalArrange();
            // A modal can grow after displaying validation or be open when the viewport shrinks.
            Left = Math.Clamp(Left, 0, Math.Max(0, ContainerBounds.Width - Bounds.Width));
            Top = Math.Clamp(Top, 0, Math.Max(0, ContainerBounds.Height - Bounds.Height));
        }
    }

    public void CancelDesktopHotkeyCapture()
    {
        if (_hotkeyCapture != null)
            CancelDialog();
    }

    public bool HandleDesktopHotkeyInput(SDL.SDL_Event e)
    {
        if (_hotkeyCapture is not { } capture || _desktopCaptureReady?.Invoke() == false)
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
