#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using static SDL3.SDL;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed class DesktopAgentsWindow
{
    private const string Title = "Legion Assistant — Agents";
    private static volatile DesktopAgentsWindow? _instance;

    private readonly nint _window;
    private readonly Desktop _desktop;
    private readonly AgentPageInteraction _interaction;
    private readonly AgentTab.AgentPage[] _pages =
    [
        ..AgentTab.GetPages(),
        new(ItemListsTabContent.Title, AgentTab.AgentGroup.ItemLists, ItemListsTabContent.Build)
    ];
    private readonly Dictionary<int, MyraButton> _navigation = new();
    private readonly MyraLabel _session = new("", MyraLabel.TextStyle.P);
    private readonly MyraLabel _pageTitle = new("", MyraLabel.TextStyle.H2);
    private readonly MyraLabel _status = new("", MyraLabel.TextStyle.P) { Visible = false };
    private readonly ScrollViewer _content = new();
    // SDL may invoke the event filter off-thread. Copy text while SDL still owns its buffer.
    private readonly ConcurrentQueue<(SDL_Event Event, string? Text)> _events = new();
    private readonly bool[] _keys = new bool[256];

    private Profile? _profile;
    private RenderTarget2D? _frame;
    private Color[] _pixels = [];
    private MouseInfo _mouse = new() { Position = new Point(-1, -1) };
    private Rectangle _bounds;
    private int _selectedPage;
    private bool _returnToWindow;
    private bool _refreshRequested;
    private bool? _priorCursorVisibility;
    private ulong _nextFrame;

    private DesktopAgentsWindow(nint window)
    {
        _window = window;
        _interaction = new AgentPageInteraction(SetTargetStatus, () => SelectPage(_pages.Length - 1));
        _desktop = new Desktop { BoundsFetcher = () => _bounds };
        // Game text is broadcast globally by FNA. Feed this desktop only its own SDL text events.
        TextInputEXT.TextInput -= _desktop.OnChar;
        _desktop.Root = BuildLayout();
    }

    public static void Show() => MainThreadQueue.EnqueueAction(Open);

    private static void Open()
    {
        if (_instance is { } existing)
        {
            existing._refreshRequested = true;
            SDL_RestoreWindow(existing._window);
            SDL_RaiseWindow(existing._window);
            return;
        }

        nint window = SDL_CreateWindow(Title, 1120, 750,
            SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIDDEN);

        if (window == 0)
        {
            ReportFailure(SDL_GetError());
            return;
        }

        try
        {
            var assistant = new DesktopAgentsWindow(window);
            _instance = assistant;
            SDL_SetWindowMinimumSize(window, 740, 500);
            if (!SDL_ShowWindow(window) || !SDL_StartTextInput(window))
                throw new InvalidOperationException(SDL_GetError());
        }
        catch (Exception ex)
        {
            if (_instance != null)
                Close();
            else
                SDL_DestroyWindow(window);

            ReportFailure(ex.ToString());
        }
    }

    public static unsafe bool TryHandleEvent(ref SDL_Event e)
    {
        DesktopAgentsWindow? assistant = _instance;
        if (assistant == null || SDL_GetWindowFromEvent(ref e) != assistant._window)
            return false;

        string? text = e.type == (uint)SDL_EventType.SDL_EVENT_TEXT_INPUT
            ? Marshal.PtrToStringUTF8((nint)e.text.text)
            : null;
        assistant._events.Enqueue((e, text));
        return true;
    }

    public static void Update(GraphicsDevice device)
    {
        if (_instance is not { } assistant)
            return;

        try
        {
            assistant.UpdateWindow(device);
        }
        catch (Exception ex)
        {
            Close();
            ReportFailure(ex.ToString());
        }
    }

    public static void Close()
    {
        if (_instance is not { } assistant)
            return;

        assistant._interaction.CancelTarget();
        assistant._interaction.CancelKeyCapture();
        assistant.RestoreCursor();
        assistant._desktop.Dispose();
        assistant._frame?.Dispose();
        SDL_DestroyWindow(assistant._window);
        _instance = null;
    }

    private static void ReportFailure(string error)
    {
        Log.Error($"Desktop Agents window failed: {error}");
        if (World.Instance?.InGame == true)
            GameActions.Print("Could not open or update Desktop Agents. You can still use the in-game Agents tab.");
    }

    private Widget BuildLayout()
    {
        var navigation = new VerticalStackPanel
        {
            Background = new SolidBrush(new Color(27, 35, 46)),
            Padding = new Thickness(10),
            Spacing = 3
        };

        var buttons = _pages.Select((page, index) => (page, index))
            .GroupBy(item => item.page.Group)
            .OrderBy(group => group.Key);
        var buttonBackground = new SolidBrush(new Color(31, 42, 55));
        var hoveredBackground = new SolidBrush(new Color(39, 57, 75));
        var selectedBackground = new SolidBrush(new Color(50, 75, 104));

        foreach (var group in buttons)
        {
            if (group.Key != AgentTab.AgentGroup.ItemLists)
            {
                navigation.Widgets.Add(new MyraLabel(GetGroupName(group.Key), MyraLabel.TextStyle.P)
                {
                    Margin = new Thickness(3, 9, 0, 1),
                    TextColor = new Color(173, 198, 220)
                });
            }

            foreach (var (page, index) in group)
            {
                var button = new MyraButton(page.Name)
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = buttonBackground,
                    OverBackground = hoveredBackground,
                    PressedBackground = selectedBackground,
                    DisabledBackground = selectedBackground
                };
                // Selecting disables this button. Let Myra release it before changing pages.
                button.Click += (_, _) => SelectPage(index);
                ((MyraLabel)button.Content).DisabledTextColor = Color.White;
                _navigation.Add(index, button);
                navigation.Widgets.Add(button);
            }
        }

        var header = new VerticalStackPanel { Spacing = 3 };
        header.Widgets.Add(new MyraLabel(TazLang.Get("assistantwindow_tab_agents", "Agents"), MyraLabel.TextStyle.H1));
        header.Widgets.Add(_session);

        var panel = new MyraGrid
        {
            RowSpacing = 10,
            Padding = new Thickness(14),
            Background = new SolidBrush(new Color(24, 28, 36))
        };
        panel.AddRow();
        panel.AddRow();
        panel.AddRow(new Proportion(ProportionType.Fill));
        panel.AddWidget(_pageTitle, 0, 0);
        panel.AddWidget(_status, 1, 0);
        panel.AddWidget(_content, 2, 0);

        var layout = new MyraGrid { Padding = new Thickness(16), RowSpacing = 12, ColumnSpacing = 12 };
        layout.AddColumn(new Proportion(ProportionType.Pixels, 175));
        layout.AddColumn(new Proportion(ProportionType.Fill));
        layout.AddRow();
        layout.AddRow(new Proportion(ProportionType.Fill));
        layout.AddWidget(header, 0, 0, colspan: 2);
        layout.AddWidget(new ScrollViewer { Content = navigation }, 1, 0);
        layout.AddWidget(panel, 1, 1);
        _desktop.Background = new SolidBrush(new Color(18, 22, 28));

        SelectPage(_selectedPage);
        return layout;
    }

    private static string GetGroupName(AgentTab.AgentGroup group) => group switch
    {
        AgentTab.AgentGroup.HealingAndStats => TazLang.Get("assistant_agents_healing_stats", "Healing & Stats"),
        AgentTab.AgentGroup.LootAndScavenge => TazLang.Get("assistant_agents_loot_scavenge", "Loot & Scavenge"),
        AgentTab.AgentGroup.Vendors => TazLang.Get("assistant_agents_vendors", "Vendors"),
        _ => TazLang.Get("assistant_agents_gear_inventory", "Gear & Inventory")
    };

    private void SelectPage(int index)
    {
        _interaction.CancelTarget();
        _interaction.CancelKeyCapture();
        _desktop.FocusedKeyboardWidget = null;
        _desktop.HideContextMenu();
        SetTargetStatus("", false, false);
        _navigation[_selectedPage].Enabled = true;
        _navigation[index].Enabled = false;
        _selectedPage = index;
        _pageTitle.Text = _pages[index].Name;
        _session.Text = _profile == null
            ? TazLang.Get("assistant_agents_disconnected", "No character connected")
            : $"{_profile.CharacterName} · {_profile.ServerName}";
        _content.Content = _profile == null
            ? new MyraLabel(TazLang.Get("assistant_agents_sign_in", "Log in to a character to edit agent settings."), MyraLabel.TextStyle.P)
            : _pages[index].Build(_interaction);
        _content.ResetScroll();
    }

    private void SetTargetStatus(string text, bool waiting, bool returnToWindow)
    {
        _status.Text = text;
        _status.Visible = text.Length > 0;
        _content.Enabled = !waiting;
        _returnToWindow = returnToWindow;
        _nextFrame = 0;
    }

    private void UpdateWindow(GraphicsDevice device)
    {
        Profile? profile = World.Instance?.InGame == true ? ProfileManager.CurrentProfile : null;
        if (_refreshRequested || !ReferenceEquals(_profile, profile))
        {
            _refreshRequested = false;
            _profile = profile;
            SelectPage(_selectedPage);
            _nextFrame = 0;
        }

        _interaction.Update();
        if (_returnToWindow)
        {
            _returnToWindow = false;
            SDL_RaiseWindow(_window);
        }

        // Handle close and minimize without waiting on the renderer.
        SDL_WindowFlags flags = SDL_GetWindowFlags(_window);
        bool minimized = (flags & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0;
        ulong now = SDL_GetTicks();
        if (!minimized && now < _nextFrame)
            return;

        if (minimized)
        {
            while (_events.TryDequeue(out var item))
            {
                if (!ApplyWindowEvent((SDL_EventType)item.Event.type))
                {
                    Close();
                    return;
                }
            }

            return;
        }

        if (!SDL_GetWindowSize(_window, out int width, out int height) || width <= 0 || height <= 0)
            return;

        EnsureFrame(device, width, height);
        _nextFrame = now + ((flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0 ? 33UL : 200UL);

        RenderTargetBinding[] previousTargets = device.GetRenderTargets();
        Viewport previousViewport = device.Viewport;
        Rectangle previousScissor = device.ScissorRectangle;
        Func<MouseInfo> previousMouse = MyraEnvironment.MouseInfoGetter;
        Action<bool[]> previousKeys = MyraEnvironment.DownKeysGetter;

        bool closeRequested = false;
        try
        {
            // A separate SDL window has its own input. Existing Myra pages can use that input
            // through the usual delegates for this pass, then the game gets its providers back.
            MyraEnvironment.MouseInfoGetter = GetMouse;
            MyraEnvironment.DownKeysGetter = GetKeys;
            device.SetRenderTarget(_frame);

            for (int i = 0; i < 128 && _events.TryDequeue(out var item); i++)
            {
                if (!ApplyWindowEvent((SDL_EventType)item.Event.type))
                {
                    closeRequested = true;
                    break;
                }

                if (ApplyInput(item.Event, item.Text))
                    RenderDesktop(device);
            }

            if (!closeRequested)
                RenderDesktop(device);
        }
        finally
        {
            MyraEnvironment.MouseInfoGetter = previousMouse;
            MyraEnvironment.DownKeysGetter = previousKeys;
            device.SetRenderTargets(previousTargets);
            device.Viewport = previousViewport;
            device.ScissorRectangle = previousScissor;
        }

        if (closeRequested)
            Close();
        else
            Present();
    }

    private MouseInfo GetMouse() => _mouse;

    private void GetKeys(bool[] keys) => Array.Copy(_keys, keys, keys.Length);

    private void EnsureFrame(GraphicsDevice device, int width, int height)
    {
        if (_frame is { IsDisposed: false } && _bounds.Width == width && _bounds.Height == height)
            return;

        _frame?.Dispose();
        _frame = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _bounds = new Rectangle(0, 0, width, height);
        _pixels = new Color[width * height];
    }

    private void RenderDesktop(GraphicsDevice device)
    {
        device.Clear(new Color(18, 22, 28));
        _desktop.Render();
    }

    private unsafe void Present()
    {
        nint surface = GetWindowSurface();
        if (surface == 0)
            throw new InvalidOperationException(SDL_GetError());

        _frame!.GetData(_pixels);
        fixed (Color* pixels = _pixels)
        {
            nint source = SDL_CreateSurfaceFrom(_bounds.Width, _bounds.Height, SDL_PixelFormat.SDL_PIXELFORMAT_RGBA32,
                (nint)pixels, _bounds.Width * sizeof(Color));
            if (source == 0)
                throw new InvalidOperationException(SDL_GetError());

            try
            {
                // These pixels already contain the final opaque desktop.
                SDL_SetSurfaceBlendMode(source, 0);
                if (!SDL_BlitSurfaceScaled(source, 0, surface, 0, SDL_ScaleMode.SDL_SCALEMODE_LINEAR)
                    || !SDL_UpdateWindowSurface(_window))
                    throw new InvalidOperationException(SDL_GetError());
            }
            finally
            {
                SDL_DestroySurface(source);
            }
        }
    }

    private nint GetWindowSurface()
    {
        // Avoid creating an SDL GL renderer that could take over the game's context.
        string? originalHint = SDL_GetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION);
        SDL_SetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION, "0");

        try
        {
            return SDL_GetWindowSurface(_window);
        }
        finally
        {
            if (originalHint == null)
                SDL_ResetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION);
            else
                SDL_SetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION, originalHint);
        }
    }

    private bool ApplyWindowEvent(SDL_EventType type)
    {
        switch (type)
        {
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                return false;

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                Array.Clear(_keys);
                _mouse = new MouseInfo { Position = new Point(-1, -1), Wheel = _mouse.Wheel };
                _desktop.FocusedKeyboardWidget = null;
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER:
                _priorCursorVisibility ??= SDL_CursorVisible();
                SDL_ShowCursor();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
                RestoreCursor();
                if (!_mouse.IsLeftButtonDown && !_mouse.IsMiddleButtonDown && !_mouse.IsRightButtonDown)
                    _mouse.Position = new Point(-1, -1);
                break;
        }

        return true;
    }

    private void RestoreCursor()
    {
        if (_priorCursorVisibility == false)
            SDL_HideCursor();

        _priorCursorVisibility = null;
    }

    private bool ApplyInput(SDL_Event e, string? text)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                _mouse.Position = new Point((int)e.motion.x, (int)e.motion.y);
                break;

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                _mouse.Position = new Point((int)e.button.x, (int)e.button.y);
                bool down = e.type == (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;
                switch (e.button.button)
                {
                    case 1: _mouse.IsLeftButtonDown = down; break;
                    case 2: _mouse.IsMiddleButtonDown = down; break;
                    case 3: _mouse.IsRightButtonDown = down; break;
                    default: return false;
                }

                // Process each edge, so a click between frames still reaches the widget.
                return true;

            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                _mouse.Position = new Point((int)e.wheel.mouse_x, (int)e.wheel.mouse_y);
                _mouse.Wheel += (e.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -1 : 1) * e.wheel.y * 120;
                break;

            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL_EventType.SDL_EVENT_KEY_UP:
                SetKey(e.key);
                break;

            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                if (text != null && !_interaction.IsCapturingKey)
                    foreach (char c in text)
                        _desktop.OnChar(c);
                break;
        }

        return false;
    }

    private void SetKey(SDL_KeyboardEvent e)
    {
        bool down = e.type == SDL_EventType.SDL_EVENT_KEY_DOWN;
        if (down && _interaction.HandleKeyDown(e))
            return;

        var keycode = (SDL_Keycode)e.key;
        Keys key = keycode switch
        {
            >= SDL_Keycode.SDLK_A and <= SDL_Keycode.SDLK_Z => Keys.A + (int)(keycode - SDL_Keycode.SDLK_A),
            >= SDL_Keycode.SDLK_0 and <= SDL_Keycode.SDLK_9 => Keys.D0 + (int)(keycode - SDL_Keycode.SDLK_0),
            SDL_Keycode.SDLK_TAB => Keys.Tab,
            SDL_Keycode.SDLK_BACKSPACE => Keys.Back,
            SDL_Keycode.SDLK_DELETE => Keys.Delete,
            SDL_Keycode.SDLK_LEFT => Keys.Left,
            SDL_Keycode.SDLK_RIGHT => Keys.Right,
            SDL_Keycode.SDLK_UP => Keys.Up,
            SDL_Keycode.SDLK_DOWN => Keys.Down,
            SDL_Keycode.SDLK_HOME => Keys.Home,
            SDL_Keycode.SDLK_END => Keys.End,
            SDL_Keycode.SDLK_PAGEUP => Keys.PageUp,
            SDL_Keycode.SDLK_PAGEDOWN => Keys.PageDown,
            SDL_Keycode.SDLK_ESCAPE => Keys.Escape,
            SDL_Keycode.SDLK_RETURN or SDL_Keycode.SDLK_KP_ENTER => Keys.Enter,
            SDL_Keycode.SDLK_SPACE => Keys.Space,
            SDL_Keycode.SDLK_LSHIFT => Keys.LeftShift,
            SDL_Keycode.SDLK_RSHIFT => Keys.RightShift,
            SDL_Keycode.SDLK_LCTRL => Keys.LeftControl,
            SDL_Keycode.SDLK_RCTRL => Keys.RightControl,
            SDL_Keycode.SDLK_LALT => Keys.LeftAlt,
            SDL_Keycode.SDLK_RALT => Keys.RightAlt,
            SDL_Keycode.SDLK_LGUI => Keys.LeftWindows,
            SDL_Keycode.SDLK_RGUI => Keys.RightWindows,
            _ => Keys.None
        };

        if (key != Keys.None)
        {
            _keys[(int)key] = down;
            _desktop.UpdateKeyboardInput();
        }
    }
}
