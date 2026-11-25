#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows.Theme;
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
    private const int ToolbarHeight = 36;

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
        _close = new MyraButton("Close", Hide);
        _panel.Widgets.Add(Row(
            new MyraButton("Mark player", AddAtPlayer),
            new MyraButton("Markers", Browse), new MyraButton("Map tools", ShowTools), _close));
        _editor = new ScrollViewer { Content = _content, Padding = new Thickness(8) };
        _panel.Widgets.Add(_editor);
        _desktop.Root = _panel;
        Hide();
    }

    internal bool IsOpen => _open;

    internal void Resize(int width, int height)
    {
        _bounds = new Rectangle(0, 0, width, height);
        _editor.MaxHeight = Math.Max(1, height - ToolbarHeight);
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

    internal bool Contains(float x, float y) => _open || (x >= 0 && x < PanelWidth && y >= 0 && y < ToolbarHeight);

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
        _content.Widgets.Clear();
    }

    private void Hide()
    {
        _open = false;
        _editor.Visible = false;
        _close.Visible = false;
        _desktop.FocusedKeyboardWidget = null;
        _content.Widgets.Clear();
    }

    private void Browse()
    {
        Show();
        _content.Widgets.Add(Label("Saved markers"));
        var markers = _map.DesktopUserMarkers?.ToArray() ?? [];
        if (markers.Length == 0)
        {
            _content.Widgets.Add(Label("No saved markers."));
            return;
        }

        var list = new ListView { Height = 175 };
        foreach (var marker in markers)
            list.Widgets.Add(Label($"{marker.Name} · {marker.X}, {marker.Y} · Map {marker.MapId}"));
        var edit = new MyraButton("Edit", () =>
        {
            if (list.SelectedIndex is int index && index < markers.Length)
                Edit(markers[index], new Point(markers[index].X, markers[index].Y));
        }) { Enabled = false };
        list.SelectedIndexChanged += (_, _) => edit.Enabled = list.SelectedIndex.HasValue;
        _content.Widgets.Add(list);
        _content.Widgets.Add(edit);
    }

    private void Edit(WMapMarker? previous, Point location, int? newFacet = null)
    {
        Show();
        int facet = previous?.MapId ?? newFacet ?? _map.MapWorld.MapIndex;
        _content.Widgets.Add(Label(previous == null ? "New marker" : "Edit marker"));
        _content.Widgets.Add(Label($"{location.X}, {location.Y} · Map {facet}"));
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

            var marker = new WMapMarker
            {
                X = location.X, Y = location.Y, MapId = facet, Name = label,
                ZoomIndex = previous?.ZoomIndex ?? 4,
                MarkerIconName = category switch
                {
                    1 => "map-house", 2 => "map-vendor", 3 => selectedKey,
                    _ => existingKey is "map-house" or "map-vendor" || existingKey.StartsWith("monster-", StringComparison.Ordinal) ? "" : existingKey
                },
                ColorName = category switch { 1 => "blue", 2 => "green", 3 => "red", _ => previous?.ColorName ?? "yellow" }
            };
            if (_map.TryChangeDesktopMarker(previous, marker))
                Hide();
            else
                status.Text = "Could not save the marker.";
        }

        _content.Widgets.Add(name);
        _content.Widgets.Add(type);
        _content.Widgets.Add(monsterPanel);
        var actions = Row(new MyraButton("Save marker", Save), new MyraButton("Cancel", previous == null ? Hide : Browse));
        if (previous != null)
        {
            bool confirm = false;
            actions.Widgets.Add(new MyraButton("Delete", () =>
            {
                if (!confirm)
                {
                    confirm = true;
                    status.Text = "Click Delete again to remove this marker.";
                }
                else if (_map.TryChangeDesktopMarker(previous, null))
                    Browse();
                else
                    status.Text = "Could not delete the marker.";
            }));
        }
        _content.Widgets.Add(actions);
        _content.Widgets.Add(status);
    }

    private static Label Label(string text) => new() { Text = text, Wrap = true };

    private static HorizontalStackPanel Row(params Widget[] children)
    {
        var row = new HorizontalStackPanel { Spacing = 4, Padding = new Thickness(3) };
        foreach (var child in children)
            row.Widgets.Add(child);
        return row;
    }
}
