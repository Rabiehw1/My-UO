#nullable enable
using System;
using ClassicUO.Configuration;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;
using static ClassicUO.Game.UI.Gumps.WorldMapGump;
using static SDL3.SDL;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed partial class DetachedMapMarkers
{
    private readonly WrapPanel _toolbar;
    private readonly Label _mapStatus;
    private readonly List<MyraButton> _tabs = new();
    private readonly MyraButton _markerTab;
    private Label? _townStatus;
    private string? _markerFilePath;

    private readonly WorldMapGump _map;
    private readonly DetachedWorldMapWindow _window;
    private readonly Desktop _desktop;
    private readonly DesktopAssistantTheme _theme = new();
    private readonly bool[] _keys = new bool[256];
    private const int PanelWidth = 300;
    private readonly VerticalStackPanel _panel;
    private readonly ScrollViewer _editor;
    private readonly MyraButton _close;
    private readonly VerticalStackPanel _content = new() { Spacing = 6 };
    private readonly Queue<(SDL_Event Event, string? Text, float Scale)> _events = new();
    private MouseInfo _mouse;
    private Rectangle _bounds;
    private bool _open;

    internal DetachedMapMarkers(WorldMapGump map, DetachedWorldMapWindow window)
    {
        _map = map;
        _window = window;
        using var styles = _theme.Use();
        _desktop = new Desktop { BoundsFetcher = () => _bounds, Background = null };
        TextInputEXT.TextInput -= _desktop.OnChar;
        _panel = new VerticalStackPanel
        {
            Width = PanelWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = DesktopAssistantTheme.Canvas
        };
        _toolbar = new WrapPanel
        {
            HorizontalSpacing = 2, VerticalSpacing = 2, UniformSizing = false,
            Padding = new Thickness(4), Background = DesktopAssistantTheme.TabStrip
        };
        AddTab("detached_map_navigation", ShowNavigation);
        _markerTab = AddTab("detached_map_markers", Browse);
        AddTab("detached_map_layers", ShowSightings);
        AddTab("detached_map_settings", ShowDisplay);
        _close = DesktopAssistantTheme.ActionButton(TazLang.Get("uicommons_close"), Hide);
        _toolbar.Widgets.Add(_close);
        _panel.Widgets.Add(_toolbar);
        _editor = new ScrollViewer { Content = _content, Padding = new Thickness(8) };
        _panel.Widgets.Add(_editor);
        _mapStatus = Label("");
        _mapStatus.Background = DesktopAssistantTheme.Sidebar;
        _mapStatus.Padding = new Thickness(6, 3);
        _mapStatus.HorizontalAlignment = HorizontalAlignment.Right;
        _mapStatus.VerticalAlignment = VerticalAlignment.Bottom;
        var desktop = new Panel();
        desktop.Widgets.Add(_mapStatus);
        desktop.Widgets.Add(_panel);
        _desktop.Root = desktop;

        MyraButton AddTab(string key, Action show)
        {
            var button = DesktopAssistantTheme.NavigationButton(TazLang.Get(key), tab: true);
            button.Click += (_, _) =>
            {
                SelectTab(button);
                show();
            };
            _tabs.Add(button);
            _toolbar.Widgets.Add(button);
            return button;
        }
        Hide();
    }

    internal bool IsOpen => _open;

    internal void Dispose()
    {
        _events.Clear();
        _desktop.Dispose();
    }

    internal void Resize(int width, int height)
    {
        _bounds = new Rectangle(0, 0, width, height);
        _panel.Width = Math.Min(width, PanelWidth);
        _editor.MaxHeight = Math.Max(1, height - Math.Max(45, _toolbar.Bounds.Height) - 22);
    }

    internal void AddAtPlayer()
    {
        using var styles = _theme.Use();
        Edit(null, new Point(_map.MapWorld.Player.X, _map.MapWorld.Player.Y), _map.MapWorld.MapIndex);
    }

    internal void AddAt(Point location)
    {
        using var styles = _theme.Use();
        Edit(null, location, _window.SelectedFacet);
    }

    internal void QueueInput(SDL_Event e, string? text, float scale) => _events.Enqueue((e, text, scale));

    internal void Draw() => WithInput(() =>
    {
        _desktop.Background = null;
        string route = _map.NavigationStatus switch
        {
            MapRouteStatus.Planning => TazLang.Get("detached_map_planning"),
            MapRouteStatus.Walking => TazLang.Get("detached_map_walking"),
            MapRouteStatus.Sailing => TazLang.Get("detached_map_sailing"),
            MapRouteStatus.BoatUnknown => TazLang.Get("detached_map_boat_unknown"),
            MapRouteStatus.NoBoatRoute => TazLang.Get("detached_map_no_boat_route"),
            MapRouteStatus.BoatBlocked => TazLang.Get("detached_map_boat_blocked"),
            MapRouteStatus.Arrived => TazLang.Get("detached_map_arrived"),
            MapRouteStatus.NoRoute => TazLang.Get("detached_map_no_route"),
            _ => ""
        };
        _mapStatus.Text = $"Facet {_window.SelectedFacet} · {_window.FollowStatus}"
            + (route.Length == 0 ? "" : $" · {route}");
        if (_townStatus != null)
            _townStatus.Text = CurrentTownText();
        while (_events.TryDequeue(out var item))
        {
            ApplyInput(item.Event, item.Text, item.Scale);
            if ((SDL_EventType)item.Event.type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN
                or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP or SDL_EventType.SDL_EVENT_MOUSE_WHEEL)
                _desktop.Render();
        }
        _desktop.Render();
    });

    private void ApplyInput(SDL_Event e, string? text, float scale)
    {
        var type = (SDL_EventType)e.type;
        switch (type)
        {
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                _mouse.Position = new Point((int)(e.motion.x * scale), (int)(e.motion.y * scale));
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                _mouse.Position = new Point((int)(e.button.x * scale), (int)(e.button.y * scale));
                if (e.button.button == 1)
                    _mouse.IsLeftButtonDown = type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                _mouse.Position = new Point((int)(e.wheel.mouse_x * scale), (int)(e.wheel.mouse_y * scale));
                _mouse.Wheel += (e.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -1 : 1) * e.wheel.y * 120;
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                Array.Clear(_keys);
                _mouse.IsLeftButtonDown = false;
                break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL_EventType.SDL_EVENT_KEY_UP:
                Keys key = DesktopAgentsWindow.MapKey((SDL_Keycode)e.key.key);
                if (key != Keys.None)
                    _keys[(int)key] = type == SDL_EventType.SDL_EVENT_KEY_DOWN;
                if (_open && !_desktop.HasModalWidget && type == SDL_EventType.SDL_EVENT_KEY_DOWN && key == Keys.Escape)
                    Hide();
                break;
            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                if (_open && text != null)
                {
                    foreach (char character in text)
                        _desktop.OnChar(character);
                }
                break;
        }

        if (type is SDL_EventType.SDL_EVENT_KEY_DOWN or SDL_EventType.SDL_EVENT_KEY_UP)
            _desktop.UpdateKeyboardInput();
    }

    internal bool Contains(float x, float y) => _desktop.HasModalWidget
        || (x >= 0 && y >= 0 && x < _panel.Bounds.Width && y < _panel.Bounds.Height);

    private void WithInput(Action action)
    {
        var previousMouse = MyraEnvironment.MouseInfoGetter;
        var previousKeys = MyraEnvironment.DownKeysGetter;
        using var styles = _theme.Use();
        try
        {
            MyraEnvironment.MouseInfoGetter = () => _mouse;
            MyraEnvironment.DownKeysGetter = keys => Array.Copy(_keys, keys, keys.Length);
            action();
        }
        finally
        {
            MyraEnvironment.MouseInfoGetter = previousMouse;
            MyraEnvironment.DownKeysGetter = previousKeys;
        }
    }

    private void Show()
    {
        _open = true;
        _editor.Visible = true;
        _close.Visible = true;
        _townStatus = null;
        _desktop.FocusedKeyboardWidget = null;
        _editor.ResetScroll();
        _content.Widgets.Clear();
    }

    private void Hide()
    {
        _open = false;
        _townStatus = null;
        foreach (var tab in _tabs)
            DesktopAssistantTheme.SelectNavigation(tab, false);
        _editor.Visible = false;
        _close.Visible = false;
        _desktop.FocusedKeyboardWidget = null;
        _content.Widgets.Clear();
    }

    private void SelectTab(MyraButton button)
    {
        foreach (var tab in _tabs)
            DesktopAssistantTheme.SelectNavigation(tab, ReferenceEquals(tab, button));
    }

    private void Browse()
    {
        SelectTab(_markerTab);
        Show();
        _content.Widgets.Add(Row(DesktopAssistantTheme.ActionButton(TazLang.Get("add_marker_on_player"), AddAtPlayer, primary: true),
            DesktopAssistantTheme.ActionButton(TazLang.Get("reload_markers"), () => { _map.ReloadDesktopMarkers(); Browse(); })));
        _content.Widgets.Add(DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_profession_picker"), () => ShowNearbyProfessions(Browse)));
        var files = _markerFiles.ToArray();
        if (files.Length == 0)
        {
            _content.Widgets.Add(Label(TazLang.Get("no_map_files")));
            return;
        }

        int fileIndex = Array.FindIndex(files, file => file.FullPath == _markerFilePath);
        var picker = Choice(files.Select(file => file.Name), Math.Max(fileIndex, 0));
        var search = new MyraInputBox { HintText = TazLang.Get("detached_map_marker_search") };
        var list = new ListView { Height = 175 };
        var status = Label("");
        var matches = new List<WMapMarker>();
        WMapMarkerFile File() => files[picker.ListView.SelectedIndex ?? 0];
        WMapMarker? Selection() => list.SelectedIndex is int index && index < matches.Count ? matches[index] : null;

        var edit = DesktopAssistantTheme.ActionButton(TazLang.Get("edit"), () =>
        {
            if (Selection() is { } marker)
                Edit(marker, new Point(marker.X, marker.Y), file: File());
        });
        var go = DesktopAssistantTheme.ActionButton(TazLang.Get("marker_go_to"), () =>
        {
            if (Selection() is { } marker && _map.DesktopFacets().Contains(marker.MapId))
            {
                _window.BrowseFacet(marker.MapId);
                _window.GoTo(new Point(marker.X, marker.Y));
                Hide();
            }
        });
        var targets = Choice([], -1);
        WMapMarkerFile[] destinations = [];
        var move = DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_move_shown"));
        var remove = DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_remove_shown"));
        move.Click += (_, _) =>
        {
            if (targets.ListView.SelectedIndex is not int targetIndex)
                return;
            var source = File();
            var target = destinations[targetIndex];
            Confirm(TazLang.Get("detached_map_move_shown"), TazLang.Get("detached_map_move_confirm", [matches.Count.ToString(), target.Name]), () =>
            {
                if (TrySaveDesktopMarkers(target, target.Markers.Concat(matches).ToList())
                    && TrySaveDesktopMarkers(source, source.Markers.Except(matches).ToList()))
                    Refresh();
                else
                    status.Text = TazLang.Get("detached_map_move_failed");
            });
        };
        remove.Click += (_, _) =>
        {
            var source = File();
            Confirm(TazLang.Get("detached_map_remove_shown"), TazLang.Get("detached_map_remove_confirm", [matches.Count.ToString()]), () =>
            {
                if (TrySaveDesktopMarkers(source, source.Markers.Except(matches).ToList()))
                    Refresh();
                else
                    status.Text = TazLang.Get("detached_map_marker_write_failed");
            }, destructive: true);
        };
        MyraStyle.ApplyButtonDangerStyle(remove);

        picker.ListView.SelectedIndexChanged += (_, _) => Refresh();
        search.TextChangedByUser += (_, _) => Refresh();
        list.SelectedIndexChanged += (_, _) => UpdateActions();
        Refresh();
        _content.Widgets.Add(picker);
        _content.Widgets.Add(search);
        _content.Widgets.Add(list);
        _content.Widgets.Add(Row(go, edit));
        _content.Widgets.Add(Row(remove, targets, move));
        _content.Widgets.Add(status);

        void UpdateActions()
        {
            edit.Enabled = Selection() != null && File().IsEditable;
            go.Enabled = Selection() != null;
        }

        void Refresh()
        {
            var file = File();
            _markerFilePath = file.FullPath;
            list.Widgets.Clear();
            matches.Clear();
            string query = search.Text?.Trim() ?? "";
            matches.AddRange(file.Markers.Where(marker => marker.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
            foreach (var marker in matches)
                list.Widgets.Add(Label($"{marker.Name} · {marker.X}, {marker.Y} · Map {marker.MapId}"));

            destinations = files.Where(other => other.IsEditable && other != file).ToArray();
            targets.ListView.Widgets.Clear();
            foreach (var other in destinations)
                targets.ListView.Widgets.Add(new Label { Text = other.Name });
            targets.ListView.SelectedIndex = destinations.Length > 0 ? 0 : null;

            bool canEdit = file.IsEditable && matches.Count > 0;
            remove.Visible = file.IsEditable;
            remove.Enabled = canEdit;
            targets.Visible = move.Visible = file.IsEditable && destinations.Length > 0;
            move.Enabled = canEdit && destinations.Length > 0;
            status.Text = matches.Count == 0 ? TazLang.Get("detached_map_no_markers") : "";
            UpdateActions();
        }
    }

    private void Confirm(string title, string message, Action action, bool destructive = false)
    {
        var accept = DesktopAssistantTheme.ActionButton(title);
        var dialog = AgentPageInteraction.CreateDesktopDialog(title, Label(message), [accept], preferredWidth: 380);
        if (destructive)
            MyraStyle.ApplyButtonDangerStyle(accept);
        accept.OnClick = () =>
        {
            dialog.Close();
            action();
        };
        dialog.ShowModal(_desktop);
    }

    private void Edit(WMapMarker? previous, Point location, int? newFacet = null, WMapMarkerFile? file = null)
    {
        SelectTab(_markerTab);
        Show();
        int facet = previous?.MapId ?? newFacet ?? _map.MapWorld.MapIndex;
        _content.Widgets.Add(Label(previous == null ? "New marker" : "Edit marker"));
        var coordinates = new MyraInputBox { Text = $"{location.X}, {location.Y}" };
        var facets = _map.DesktopFacets().Append(facet).Distinct().OrderBy(index => index).ToArray();
        var mapPicker = Choice(facets.Select(index => $"Facet {index}"), Array.IndexOf(facets, facet));
        var zoom = new IntegerInputBox { MinValue = 1, MaxValue = 9, Value = previous?.ZoomIndex is > 0 ? previous.ZoomIndex : 4 };
        string[] colors = ["Default", "white", "black", "blue", "green", "red", "yellow", "orange", "purple", "cyan"];
        var color = Choice(colors, Math.Max(0, Array.IndexOf(colors, previous?.ColorName)));
        string[] icons = new[] { "" }.Concat(_markerIcons.Keys.OrderBy(key => key)).ToArray();
        var icon = Choice(icons.Select(key => key == "" ? TazLang.Get("detached_map_default") : key),
            Math.Max(0, Array.IndexOf(icons, previous?.MarkerIconName)));
        var name = new MyraInputBox { Text = previous?.Name ?? "", HintText = "Marker name" };
        var type = new ComboView();
        foreach (string category in new[] { "Landmark", "House", "Vendor", "Monster" })
            type.ListView.Widgets.Add(new Label { Text = category });

        string existingKey = previous?.MarkerIconName ?? "";
        type.ListView.SelectedIndex = existingKey switch
        {
            "map-house" => 1,
            "map-vendor" => 2,
            _ when existingKey.StartsWith("monster-", StringComparison.Ordinal) => 3,
            _ => 0
        };

        var monsters = WeaponSwitchCatalog.Monsters
            .Concat(GameScene.Instance?.WeaponSwitch.Settings.CustomMonsters ?? [])
            .OrderBy(monster => monster.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var filter = new MyraInputBox { HintText = "Find a monster" };
        var choices = new ListView { Height = 120 };
        var monsterPanel = new VerticalStackPanel { Spacing = 4 };
        monsterPanel.Widgets.Add(filter);
        monsterPanel.Widgets.Add(choices);
        var status = Label("");
        string selectedKey = type.ListView.SelectedIndex == 3 ? existingKey : "";
        string selectedName = "";
        var visible = new List<WeaponSwitchMonster>();
        var selection = Label("");
        UpdateSelectedMonsterLabel();
        monsterPanel.Widgets.Add(selection);

        bool refreshing = false;
        void RefreshChoices()
        {
            refreshing = true;
            choices.Widgets.Clear();
            visible.Clear();
            visible.AddRange(monsters.Where(monster => monster.Name.Contains(filter.Text ?? "", StringComparison.OrdinalIgnoreCase)));
            foreach (var monster in visible)
                choices.Widgets.Add(Label(monster.Hue is ushort hue ? $"{monster.Name} · {hue:X4}" : monster.Name));
            int index = visible.FindIndex(monster => MonsterMapIcon.Key(monster) == selectedKey);
            choices.SelectedIndex = index >= 0 ? index : null;
            refreshing = false;
        }

        void UpdateSelectedMonsterLabel()
        {
            selection.Text = monsters.FirstOrDefault(monster => MonsterMapIcon.Key(monster) == selectedKey)?.Name
                ?? (selectedKey.Length > 0 ? "Saved monster" : "Select a monster");
        }

        filter.TextChangedByUser += (_, _) => RefreshChoices();
        choices.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing || choices.SelectedIndex is not int index)
                return;

            selectedKey = MonsterMapIcon.Key(visible[index]);
            if (string.IsNullOrWhiteSpace(name.Text) || name.Text == selectedName)
                name.Text = selectedName = visible[index].Name[..Math.Min(visible[index].Name.Length, 25)];
            UpdateSelectedMonsterLabel();
        };
        type.ListView.SelectedIndexChanged += (_, _) => monsterPanel.Visible = type.ListView.SelectedIndex == 3;
        monsterPanel.Visible = type.ListView.SelectedIndex == 3;
        RefreshChoices();

        void Save()
        {
            string label = name.Text.Trim();
            if (label.Length is 0 or > 25 || label.IndexOfAny([',', '\r', '\n']) >= 0)
            {
                status.Text = "Use 1–25 characters and omit commas.";
                return;
            }

            int category = type.ListView.SelectedIndex ?? 0;
            if (category == 3 && selectedKey.Length == 0)
            {
                status.Text = "Select a monster first.";
                return;
            }

            int targetFacet = facets[mapPicker.ListView.SelectedIndex ?? 0];
            if (!_map.TryParseDesktopLocation(coordinates.Text, targetFacet, out Point target))
            {
                status.Text = TazLang.Get("detached_map_invalid_coordinates");
                return;
            }
            int colorIndex = color.ListView.SelectedIndex ?? 0;
            string customIcon = icons[icon.ListView.SelectedIndex ?? 0];
            var marker = new WMapMarker
            {
                X = target.X, Y = target.Y, MapId = targetFacet, Name = label,
                ZoomIndex = zoom.Value,
                MarkerIconName = category switch
                {
                    1 => "map-house", 2 => "map-vendor", 3 => selectedKey,
                    _ => customIcon
                },
                ColorName = colorIndex != 0 ? colors[colorIndex] : category switch { 1 => "blue", 2 => "green", 3 => "red", _ => previous?.ColorName ?? "yellow" }
            };
            if (_map.TryChangeDesktopMarker(previous, marker, file))
                Hide();
            else
                status.Text = "Could not save the marker.";
        }

        _content.Widgets.Add(name);
        _content.Widgets.Add(type);
        _content.Widgets.Add(Widgets.Assistant.DesktopAssistantForm.Fields(
            new("Coordinates", coordinates), new("Facet", mapPicker), new("Zoom", zoom),
            new("Color", color), new("Custom landmark icon", icon)));
        _content.Widgets.Add(monsterPanel);
        var actions = Row(new MyraButton("Save marker", Save), new MyraButton("Cancel", previous == null ? Hide : Browse));
        if (previous != null)
        {
            var delete = DesktopAssistantTheme.ActionButton(TazLang.Get("remove"), () =>
                Confirm(TazLang.Get("remove"), TazLang.Get("detached_map_remove_confirm", ["1"]), () =>
                {
                    if (_map.TryChangeDesktopMarker(previous, null, file))
                        Browse();
                    else
                        status.Text = TazLang.Get("detached_map_marker_write_failed");
                }, destructive: true));
            MyraStyle.ApplyButtonDangerStyle(delete);
            actions.Widgets.Add(delete);
        }
        _content.Widgets.Add(actions);
        _content.Widgets.Add(status);
    }

    private static Label Label(string text) => new() { Text = text, Wrap = true };

    private static WrapPanel Row(params Widget[] children) =>
        Widgets.Assistant.DesktopAssistantForm.Actions(children);
}
