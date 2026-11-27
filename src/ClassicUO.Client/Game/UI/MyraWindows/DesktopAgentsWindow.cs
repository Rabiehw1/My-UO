#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Launcher;
using ClassicUO.Game.Managers.Updates;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Trainer;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Launcher;
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

    private const ulong RUNNING_CHECK_MS = 2000;

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
        new(HotkeysMacrosTabContent.Title, AgentTab.AgentGroup.Controls, HotkeysMacrosTabContent.Build),
        new(ScriptsTabContent.Title, AgentTab.AgentGroup.Controls, ScriptsTabContent.Build),
        new(ShardSpellBooksTabContent.Title, AgentTab.AgentGroup.Controls, ShardSpellBooksTabContent.Build),
        new(TrainerTabContent.Title, AgentTab.AgentGroup.Controls, TrainerTabContent.Build, RebuildOnSelect: true),
        new(GeneralTab.Title, AgentTab.AgentGroup.Settings, GeneralTab.Build)
    ];
    private readonly Dictionary<int, MyraButton> _navigation = new();
    private readonly Dictionary<string, MyraButton> _primaryNavigation = new(StringComparer.Ordinal);
    private readonly MyraLabel _session;
    private readonly MyraLabel _pageTitle;
    private readonly MyraLabel _status;
    private readonly ScrollViewer _content;
    private readonly Panel _fullPage = new();
    private readonly WrapPanel _agentTabs;
    private readonly LauncherBar _launcherBar;
    private readonly bool _launcherOnly;
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
    private ulong _nextRunningCheck;
    private volatile bool _launcherListChanged;

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
        _agentTabs = CreateTabStrip();
        _launcherOnly = ClientLauncher.IsLauncherOnly;
        _launcherBar = new LauncherBar();
        LauncherSave.ListChanged += OnLauncherListChanged;
        LauncherSave.ViewChanged += OnLauncherViewChanged;
        _desktop = new Desktop { BoundsFetcher = () => _bounds };
        // Game text is broadcast globally by FNA. Feed this desktop only its own SDL text events.
        TextInputEXT.TextInput -= _desktop.OnChar;
        _desktop.Root = BuildLayout();
        SelectPage(_selectedPage);
    }

    public static void Show() => Show(null);

    /// <summary>Rebuilds the open page on the next frame, e.g. after an agent reloaded its settings.</summary>
    internal static void RequestRefresh()
    {
        if (_instance is { } window)
            window._refreshRequested = true;
    }

    /// <summary>Opens the window as the launcher: only launcher settings, and closing it closes the program.</summary>
    internal static void ShowLauncher() => Show(LauncherTabContent.Title);

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

        string title = ClientLauncher.IsLauncherOnly
            ? TazLang.Get("launcher_window_title", [ClientBranding.SHORT_NAME])
            : TazLang.Get("assistantwindow_title", "Legion Assistant");
        nint window = SDL_CreateWindow(title, 1120, 750,
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

        LauncherSave.ListChanged -= assistant.OnLauncherListChanged;
        LauncherSave.ViewChanged -= assistant.OnLauncherViewChanged;
        assistant._interaction.CancelTarget();
        assistant._interaction.CancelKeyCapture();
        assistant._interaction.CancelDialog();
        assistant.RestoreCursor();
        assistant._desktop.Dispose();
        assistant._frame?.Dispose();
        SDL_DestroyWindow(assistant._window);
        _instance = null;
        Client.Game?.Audio?.RefreshWindowFocus();

        // The launcher's game window is hidden, so nothing would be left to close the program with.
        if (ClientLauncher.IsLauncherOnly)
            Client.Game?.Exit();
    }

    private static void ReportFailure(string error)
    {
        Log.Error($"Desktop Assistant window failed: {error}");
        if (World.Instance?.InGame == true)
            GameActions.Print(TazLang.Get("assistant_desktop_failed", "Could not open the Desktop Assistant. The in-game Assistant is available."));
    }

    private Widget BuildLayout()
    {
        var topTabs = CreateTabStrip();
        foreach (var (name, icon) in new[]
        {
            (GeneralTab.Title, AssistantTabIcon.Kind.Settings), (_agentsTitle, AssistantTabIcon.Kind.Agent),
            (ItemListsTabContent.Title, AssistantTabIcon.Kind.List), (HotkeysMacrosTabContent.Title, AssistantTabIcon.Kind.Keyboard),
            (ShardSpellBooksTabContent.Title, AssistantTabIcon.Kind.Book), (ScriptsTabContent.Title, AssistantTabIcon.Kind.Code),
            (TrainerTabContent.Title, AssistantTabIcon.Kind.Training)
        })
        {
            var tab = DesktopAssistantTheme.PrimaryTabButton(name, icon);
            tab.Click += (_, _) => SelectPage(name);
            _primaryNavigation.Add(name, tab);
            topTabs.Widgets.Add(tab);
        }

        for (int index = 0; index < _pages.Length; index++)
        {
            var page = _pages[index];
            if (page.Group is AgentTab.AgentGroup.ItemLists or AgentTab.AgentGroup.Settings or AgentTab.AgentGroup.Controls)
                continue;

            int pageIndex = index;
            var button = DesktopAssistantTheme.NavigationButton(page.Name, tab: true);
            // Selecting disables this button. Let Myra release it before changing pages.
            button.Click += (_, _) => SelectPage(pageIndex);
            _navigation.Add(index, button);
            _agentTabs.Widgets.Add(button);
        }

        var panel = new MyraGrid { RowSpacing = 0, Background = DesktopAssistantTheme.Canvas };
        panel.AddRow();
        panel.AddRow(new Proportion(ProportionType.Fill));
        panel.AddWidget(_pageTitle, 0, 0);
        panel.AddWidget(_content, 1, 0);
        panel.AddWidget(_fullPage, 1, 0);

        var footer = new MyraGrid
        {
            Padding = new Thickness(6, 2), Background = DesktopAssistantTheme.Sidebar,
            Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(0, 1, 0, 0)
        };
        footer.AddColumn(new Proportion(ProportionType.Fill));
        footer.AddColumn();
        footer.AddWidget(_session, 0, 0);
        footer.AddWidget(_status, 0, 1);

        topTabs.Visible = !_launcherOnly;

        var layout = new MyraGrid { RowSpacing = 0, ColumnSpacing = 0 };
        layout.AddRow();
        layout.AddRow();
        layout.AddRow();
        layout.AddRow(new Proportion(ProportionType.Fill));
        layout.AddRow();
        layout.AddWidget(_launcherBar, 0, 0);
        layout.AddWidget(topTabs, 1, 0);
        layout.AddWidget(_agentTabs, 2, 0);
        layout.AddWidget(panel, 3, 0);
        layout.AddWidget(footer, 4, 0);
        return layout;
    }

    private static WrapPanel CreateTabStrip() => new()
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

    private void SelectPage(string? name)
    {
        if (_launcherOnly || name == LauncherTabContent.Title)
        {
            SelectLauncherPage();
            return;
        }

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
        if (_launcherOnly)
        {
            SelectLauncherPage();
            return;
        }

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
        _agentTabs.Visible = isAgent;
        foreach (var (agent, button) in _navigation)
            DesktopAssistantTheme.SelectNavigation(button, agent == index);

        string activeTab = isAgent ? _agentsTitle : _pages[index].Name;
        foreach (var (name, button) in _primaryNavigation)
            DesktopAssistantTheme.SelectPrimaryTab(button, name == activeTab);

        _selectedPage = index;
        _pageTitle.Text = _pages[index].Name;
        bool isAutoLoot = _pages[index].Name == AutoLootAgentTabContent.Title;
        _pageTitle.Visible = !isAutoLoot || _profile == null;
        _session.Text = _profile == null
            ? TazLang.Get("assistant_agents_disconnected", "No character connected")
            : $"{_profile.CharacterName} · {_profile.ServerName}";
        // Launcher settings don't need a character, so General still offers them before logging in.
        Widget page = _profile != null ? _pages[index].Build(_interaction)
            : _pages[index].Name == GeneralTab.Title ? LauncherTabContent.Build(_interaction)
            : new MyraLabel(TazLang.Get("assistant_agents_sign_in", "Log in to a character to edit agent settings."), MyraLabel.TextStyle.P);
        bool fullHeight = _profile != null && (isAutoLoot || _pages[index].Name == TrainerTabContent.Title || _pages[index].Name == HotkeysMacrosTabContent.Title ||
            _pages[index].Name == ShoppingAssistantTabContent.Title || _pages[index].Name == DressAgentTabContent.Title);
        _content.Visible = !fullHeight;
        _content.Content = fullHeight ? null : page;
        _fullPage.Widgets.Clear();
        _fullPage.Visible = fullHeight;
        if (fullHeight)
        {
            _fullPage.Padding = _content.Padding;
            _fullPage.Widgets.Add(page);
        }
        _content.ResetScroll();
    }

    /// <summary>The launcher's only page; it needs no character.</summary>
    private void SelectLauncherPage()
    {
        using var styles = _theme.Use();
        _interaction.CancelDialog();
        _desktop.FocusedKeyboardWidget = null;
        _desktop.HideContextMenu();
        _agentTabs.Visible = false;
        _pageTitle.Text = LauncherTabContent.Title;
        _pageTitle.Visible = true;
        _session.Text = TazLang.Get("launcher_session", [ClientBranding.NAME, ClientUpdater.CurrentVersion.ToString()]);
        _status.Visible = false;
        _fullPage.Widgets.Clear();
        _fullPage.Visible = false;
        _content.Visible = true;
        _content.Content = LauncherTabContent.Build(_interaction);
        _content.ResetScroll();
    }

    private void OnLauncherListChanged() => _launcherListChanged = true;

    // Raised from Myra click handlers and update callbacks; rebuild on the next frame, not inside the handler.
    private void OnLauncherViewChanged()
    {
        _launcherListChanged = true;
        _refreshRequested = true;
    }

    private void SetTargetStatus(string text, bool waiting, bool returnToWindow)
    {
        bool connected = text.Length == 0 && _profile != null;
        _status.Text = connected ? $"● {TazLang.Get("assistant_desktop_connected", "Connected")}" : text;
        _status.TextColor = connected ? new Color(37, 111, 69) : DesktopAssistantTheme.SecondaryText;
        _status.Visible = connected || text.Length > 0;
        _content.Enabled = !waiting;
        _fullPage.Enabled = !waiting || (_pages[_selectedPage].Name != ShoppingAssistantTabContent.Title &&
            _pages[_selectedPage].Name != DressAgentTabContent.Title);
        _returnToWindow = returnToWindow;
        _nextFrame = 0;
    }

    private void UpdateWindow(GraphicsDevice device)
    {
        using var styles = _theme.Use();
        Profile? profile = World.Instance?.InGame == true ? ProfileManager.CurrentProfile : null;
        if (_launcherListChanged)
        {
            _launcherListChanged = false;
            _launcherBar.Rebuild();
            _nextFrame = 0;
        }

        ulong ticks = SDL_GetTicks();
        if (ticks >= _nextRunningCheck)
        {
            _nextRunningCheck = ticks + RUNNING_CHECK_MS;
            _launcherBar.RefreshRunning();
            _nextFrame = 0;
        }

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

        if (_content.Content is HealingAssistantTabContent.PlannerView healingPlanner)
            healingPlanner.RefreshStatus();
        if (_content.Content is SupportAgentTabContent.Overview supportOverview)
            supportOverview.RefreshStatus();

        if (_content.Content is MyraTabControl scriptsTabs)
        {
            if (scriptsTabs.SelectedContent is ScriptExecutorTab executor)
                executor.Refresh();
            else if (scriptsTabs.SelectedContent is ScriptLibraryTab library)
                library.RefreshRunning();
        }

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

    private void Present() => DesktopWindowSurface.Present(_window, _frame!, _pixels);

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

        Keys key = MapKey((SDL_Keycode)e.key);
        if (key != Keys.None)
        {
            _keys[(int)key] = down;
            _desktop.UpdateKeyboardInput();
        }
    }

    internal static Keys MapKey(SDL_Keycode keycode) => keycode switch
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
}
