#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Macros;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;
using static SDL3.SDL;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed class DesktopAgentsWindow
{
    private static volatile DesktopAgentsWindow? _instance;

    internal static bool HasInputFocus => _instance?._hasInputFocus == true;

    private readonly nint _window;
    private readonly Desktop _desktop;
    private readonly DesktopAssistantTheme _theme;
    private readonly AgentPageInteraction _interaction;
    private readonly string _agentsTitle = TazLang.Get("assistantwindow_tab_agents", "Agents");
    private readonly AgentTab.AgentPage[] _pages =
    [
        ..AgentTab.GetPages(),
        new(ItemListsTabContent.Title, AgentTab.AgentGroup.ItemLists, ItemListsTabContent.Build),
        new(HotkeysTabContent.Title, AgentTab.AgentGroup.Controls, HotkeysTabContent.Build),
        new(MacrosTabContent.Title, AgentTab.AgentGroup.Controls, MacrosTabContent.Build),
        new(FiltersTab.Title, AgentTab.AgentGroup.Settings, FiltersTab.Build),
        new(GeneralTab.Title, AgentTab.AgentGroup.Settings, GeneralTab.Build)
    ];
    private readonly Dictionary<int, MyraButton> _navigation = new();
    private readonly Dictionary<string, MyraButton> _primaryNavigation = new(StringComparer.Ordinal);
    private readonly MyraLabel _session;
    private readonly MyraLabel _pageTitle;
    private readonly MyraLabel _status;
    private readonly ScrollViewer _content;
    private readonly ScrollViewer _sidebar;
    // SDL may invoke the event filter off-thread. Copy text while SDL still owns its buffer.
    private readonly ConcurrentQueue<(SDL_Event Event, string? Text)> _events = new();
    private readonly bool[] _keys = new bool[256];

    private Profile? _profile;
    private RenderTarget2D? _frame;
    private Color[] _pixels = [];
    private MouseInfo _mouse = new() { Position = new Point(-1, -1) };
    private Rectangle _bounds;
    private int _selectedPage;
    private int _selectedAgent;
    private bool _returnToWindow;
    private bool _refreshRequested;
    private volatile bool _hasInputFocus;
    private bool? _priorCursorVisibility;
    private ulong _nextFrame;

    private DesktopAgentsWindow(nint window)
    {
        _window = window;
        _theme = new DesktopAssistantTheme();
        using var styles = _theme.Use();
        _interaction = new AgentPageInteraction(SetTargetStatus, () => SelectPage(ItemListsTabContent.Title), _theme);
        _session = new MyraLabel("", MyraLabel.TextStyle.P) { Font = DesktopAssistantTheme.Font(-1), TextColor = DesktopAssistantTheme.SecondaryText };
        _status = new MyraLabel("", MyraLabel.TextStyle.P) { Font = DesktopAssistantTheme.Font(-1), HorizontalAlignment = HorizontalAlignment.Right };
        _pageTitle = new MyraLabel("", MyraLabel.TextStyle.P)
        {
            Font = DesktopAssistantTheme.Font(3, bold: true),
            Margin = new Thickness(13, 10, 13, 5)
        };
        _content = new ScrollViewer { Padding = new Thickness(14, 0, 14, 12) };
        _sidebar = new ScrollViewer
        {
            Width = DesktopAssistantTheme.SidebarWidth,
            Background = DesktopAssistantTheme.Sidebar,
            Border = DesktopAssistantTheme.Divider,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(7, 6),
            ShowHorizontalScrollBar = false
        };
        _desktop = new Desktop { BoundsFetcher = () => _bounds };
        // Game text is broadcast globally by FNA. Feed this desktop only its own SDL text events.
        TextInputEXT.TextInput -= _desktop.OnChar;
        _desktop.Root = BuildLayout();
        SelectPage(_selectedPage);
    }

    public static void Show() => Show(null);

    internal static void Show(string? page) => MainThreadQueue.EnqueueAction(() => Open(page));

    private static void Open(string? page)
    {
        if (_instance is { } existing)
        {
            existing.SelectPage(page);
            existing._refreshRequested = true;
            SDL_RestoreWindow(existing._window);
            SDL_RaiseWindow(existing._window);
            return;
        }

        nint window = SDL_CreateWindow(TazLang.Get("assistantwindow_title", "Legion Assistant"), 1120, 750,
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
            assistant.SelectPage(page);
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
        if (assistant == null)
            return false;

        // Gamepad events have no window ID. The desktop owns them only while its capture dialog is open.
        bool isCapturedGamepad = e.type == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN
            && assistant._interaction.IsCapturingDesktopHotkey;
        if (!isCapturedGamepad && SDL_GetWindowFromEvent(ref e) != assistant._window)
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
        assistant._interaction.CancelDialog();
        assistant.RestoreCursor();
        assistant._desktop.Dispose();
        assistant._frame?.Dispose();
        SDL_DestroyWindow(assistant._window);
        _instance = null;
        Client.Game?.Audio?.RefreshWindowFocus();
    }

    private static void ReportFailure(string error)
    {
        Log.Error($"Desktop Assistant window failed: {error}");
        if (World.Instance?.InGame == true)
            GameActions.Print(TazLang.Get("assistant_desktop_failed", "Could not open the Desktop Assistant. The in-game Assistant is available."));
    }

    private Widget BuildLayout()
    {
        var topTabs = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Background = DesktopAssistantTheme.TabStrip,
            Border = DesktopAssistantTheme.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(7, 5, 7, 0),
            HorizontalSpacing = 2,
            VerticalSpacing = 2,
            UniformSizing = false
        };
        foreach (string name in new[] { GeneralTab.Title, _agentsTitle, ItemListsTabContent.Title, HotkeysTabContent.Title, MacrosTabContent.Title, FiltersTab.Title })
        {
            var tab = DesktopAssistantTheme.NavigationButton(name, tab: true);
            tab.Click += (_, _) => SelectPage(name);
            _primaryNavigation.Add(name, tab);
            topTabs.Widgets.Add(tab);
        }

        var navigation = new VerticalStackPanel { Spacing = 1 };
        AgentTab.AgentGroup[] groups =
        [
            AgentTab.AgentGroup.LootAndScavenge, AgentTab.AgentGroup.HealingAndStats,
            AgentTab.AgentGroup.Vendors, AgentTab.AgentGroup.GearAndInventory, AgentTab.AgentGroup.Support
        ];
        foreach (AgentTab.AgentGroup group in groups)
        {
            if (group != AgentTab.AgentGroup.Support)
            {
                navigation.Widgets.Add(new MyraLabel(GetGroupName(group), MyraLabel.TextStyle.P)
                {
                    Font = DesktopAssistantTheme.Font(-2),
                    Margin = new Thickness(1, navigation.Widgets.Count > 0 ? 6 : 0, 0, 0),
                    TextColor = DesktopAssistantTheme.SecondaryText
                });
            }

            foreach (var (page, index) in _pages.Select((page, index) => (page, index)).Where(item => item.page.Group == group))
            {
                var button = DesktopAssistantTheme.NavigationButton(page.Name);
                // Selecting disables this button. Let Myra release it before changing pages.
                button.Click += (_, _) => SelectPage(index);
                _navigation.Add(index, button);
                navigation.Widgets.Add(button);
            }
        }
        _sidebar.Content = navigation;

        var panel = new MyraGrid { RowSpacing = 0, Background = DesktopAssistantTheme.Canvas };
        panel.AddRow();
        panel.AddRow(new Proportion(ProportionType.Fill));
        panel.AddWidget(_pageTitle, 0, 0);
        panel.AddWidget(_content, 1, 0);

        var footer = new MyraGrid
        {
            Padding = new Thickness(6, 2), Background = DesktopAssistantTheme.Sidebar,
            Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(0, 1, 0, 0)
        };
        footer.AddColumn(new Proportion(ProportionType.Fill));
        footer.AddColumn();
        footer.AddWidget(_session, 0, 0);
        footer.AddWidget(_status, 0, 1);

        var layout = new MyraGrid { RowSpacing = 0, ColumnSpacing = 0 };
        layout.AddColumn();
        layout.AddColumn(new Proportion(ProportionType.Fill));
        layout.AddRow();
        layout.AddRow(new Proportion(ProportionType.Fill));
        layout.AddRow();
        layout.AddWidget(topTabs, 0, 0, colspan: 2);
        layout.AddWidget(_sidebar, 1, 0);
        layout.AddWidget(panel, 1, 1);
        layout.AddWidget(footer, 2, 0, colspan: 2);
        return layout;
    }

    private static string GetGroupName(AgentTab.AgentGroup group) => group switch
    {
        AgentTab.AgentGroup.HealingAndStats => TazLang.Get("assistant_agents_healing_stats", "Healing & Stats"),
        AgentTab.AgentGroup.LootAndScavenge => TazLang.Get("assistant_agents_loot_scavenge", "Loot & Scavenge"),
        AgentTab.AgentGroup.Vendors => TazLang.Get("assistant_agents_vendors", "Vendors"),
        _ => TazLang.Get("assistant_agents_gear_inventory", "Gear & Inventory")
    };

    private void SelectPage(string? name)
    {
        if (name == _agentsTitle)
        {
            SelectPage(_selectedAgent);
            return;
        }

        int index = Array.FindIndex(_pages, page => page.Name == name);
        if (index >= 0)
            SelectPage(index);
    }

    private void SelectPage(int index)
    {
        using var styles = _theme.Use();
        _interaction.CancelTarget();
        _interaction.CancelKeyCapture();
        _interaction.CancelDialog();
        _desktop.FocusedKeyboardWidget = null;
        _desktop.HideContextMenu();
        SetTargetStatus("", false, false);
        bool isAgent = _navigation.ContainsKey(index);
        if (isAgent)
            _selectedAgent = index;
        _sidebar.Visible = isAgent;
        foreach (var (agent, button) in _navigation)
            DesktopAssistantTheme.SelectNavigation(button, agent == index);

        string activeTab = isAgent ? _agentsTitle : _pages[index].Name;
        foreach (var (name, button) in _primaryNavigation)
            DesktopAssistantTheme.SelectNavigation(button, name == activeTab);

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
        bool connected = text.Length == 0 && _profile != null;
        _status.Text = connected ? $"● {TazLang.Get("assistant_desktop_connected", "Connected")}" : text;
        _status.TextColor = connected ? new Color(37, 111, 69) : DesktopAssistantTheme.SecondaryText;
        _status.Visible = connected || text.Length > 0;
        _content.Enabled = !waiting;
        _returnToWindow = returnToWindow;
        _nextFrame = 0;
    }

    private void UpdateWindow(GraphicsDevice device)
    {
        using var styles = _theme.Use();
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
        device.Clear(DesktopAssistantTheme.Canvas.Color);
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

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                _hasInputFocus = true;
                Client.Game.Audio?.RefreshWindowFocus();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                _hasInputFocus = false;
                _interaction.CancelDesktopHotkeyCapture();
                Array.Clear(_keys);
                _mouse = new MouseInfo { Position = new Point(-1, -1), Wheel = _mouse.Wheel };
                _desktop.FocusedKeyboardWidget = null;
                Client.Game.Audio?.RefreshWindowFocus();
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
        if (_interaction.HandleDesktopHotkeyInput(e))
            return false;

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
