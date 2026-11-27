#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Myra.Graphics2D;
using Microsoft.Xna.Framework;
using static SDL3.SDL;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.File;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed partial class DetachedMapMarkers
{
    private void ShowNavigation()
    {
        Show();
        _content.Widgets.Add(Label(TazLang.Get("detached_map_gestures")));
        var status = Label("");
        var targets = _map.DesktopFollowTargets().ToArray();
        var follow = Choice(new[] { "My character" }.Concat(targets.Select(target => target.Name)),
            _window.FollowSerial is uint serial ? Array.FindIndex(targets, target => target.Serial == serial) + 1 : 0);
        follow.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (follow.ListView.SelectedIndex is int index)
            {
                _window.Follow(index == 0 ? null : targets[index - 1].Serial);
                Hide();
            }
        };
        _content.Widgets.Add(Label("Follow"));
        _content.Widgets.Add(follow);
        _content.Widgets.Add(new MyraButton("Follow my character", () => { _window.Follow(null); Hide(); }));

        int[] facets = _map.DesktopFacets().ToArray();
        var facet = Choice(facets.Select(index => $"Facet {index}"), Array.IndexOf(facets, _window.SelectedFacet));
        facet.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (facet.ListView.SelectedIndex is int index)
            {
                _window.BrowseFacet(facets[index]);
                Hide();
            }
        };
        _content.Widgets.Add(Label("Browse facet"));
        _content.Widgets.Add(facet);

        _content.Widgets.Add(Label("Map X, Y or sextant coordinates"));
        var player = _map.MapWorld.Player;
        var location = new MyraInputBox { HintText = "Coordinates", Text = $"{player.X}, {player.Y}" };
        _content.Widgets.Add(location);
        void Navigate(bool walk)
        {
            if (!_map.TryParseDesktopLocation(location.Text, _window.SelectedFacet, out var tile))
            {
                status.Text = "Enter valid coordinates on this facet.";
                return;
            }
            if (walk && !_window.TryWalkTo(tile))
            {
                status.Text = player.IsDrivingBoat && _window.SelectedFacet == _map.MapWorld.MapIndex
                    ? TazLang.Get("detached_map_boat_unknown") : "Pathfinding is available on your character’s facet.";
                return;
            }
            if (!walk)
                _window.GoTo(tile);
            Hide();
        }

        var go = DesktopAssistantTheme.ActionButton("Go to", () => Navigate(false));
        var walkTo = DesktopAssistantTheme.ActionButton(player.IsDrivingBoat ? TazLang.Get("detached_map_sail_to") : "Walk to", () => Navigate(true), primary: true);
        walkTo.Enabled = _window.SelectedFacet == _map.MapWorld.MapIndex;
        location.TextChangedByUser += (_, _) =>
        {
            bool valid = _map.TryParseDesktopLocation(location.Text, _window.SelectedFacet, out _);
            go.Enabled = valid;
            walkTo.Enabled = valid && _window.SelectedFacet == _map.MapWorld.MapIndex;
            status.Text = valid ? "" : TazLang.Get("detached_map_invalid_coordinates");
        };
        location.KeyDown += (_, args) =>
        {
            if (args.Data == Microsoft.Xna.Framework.Input.Keys.Enter && go.Enabled)
                Navigate(false);
        };
        _content.Widgets.Add(Row(go, walkTo, DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_copy_coordinates"),
            () => SDL_SetClipboardText($"{player.X}, {player.Y}, {player.Z}"))));
        _content.Widgets.Add(Row(new MyraButton("Clear destination", _map.ClearGoToMarker),
            new MyraButton(TazLang.Get("detached_map_stop_navigation"), _map.StopDesktopNavigation)));
        _content.Widgets.Add(status);
    }

    private void ShowDisplay()
    {
        Show();
        _content.Widgets.Add(Label(TazLang.Get("detached_map_display")));
        foreach (var group in _map.DesktopDisplayOptions().GroupBy(option => option.Group))
            _content.Widgets.Add(DesktopAssistantTheme.Group(group.Key, group.Select(option =>
                (Widget)MyraCheckButton.CreateWithCallback(option.Enabled, value =>
                {
                    option.Set(value);
                    _map.SaveSettings();
                }, option.Label)).ToArray()));
        _content.Widgets.Add(Label("Click a map tile with an active position cursor to target it."));

        var (fontNames, _) = TrueTypeLoader.Instance.GetSortedFontNames();
        string[] fonts = fontNames ?? [];
        int selected = Array.IndexOf(fonts, _map.DesktopTtfFont);
        var font = Choice(Enumerable.Range(1, 6).Select(index => $"Map font {index}").Concat(fonts),
            selected >= 0 ? selected + 6 : _map.DesktopSpriteFont - 1);
        font.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (font.ListView.SelectedIndex is not int index)
                return;
            if (index < 6)
                _map.SetDesktopSpriteFont(index + 1);
            else
                _map.SetDesktopTtfFont(fonts[index - 6]);
        };
        _content.Widgets.Add(Label("Map labels font"));
        _content.Widgets.Add(font);
        var fontSize = new MyraInputBox
        {
            Width = 65, Text = _map.DesktopTtfSize.ToString(), InputFilter = MyraInputBox.DigitInputFilter
        };
        _content.Widgets.Add(Row(Label("Text font size"), fontSize, new MyraButton("Apply", () =>
        {
            if (int.TryParse(fontSize.Text, out int size))
            {
                _map.SetDesktopTtfSize(size);
                fontSize.Text = _map.DesktopTtfSize.ToString();
            }
        })));

        var profile = ProfileManager.CurrentProfile;
        var x = new MyraInputBox { Width = 70, Text = profile.WorldMapSextantBaseX.ToString() };
        var y = new MyraInputBox { Width = 70, Text = profile.WorldMapSextantBaseY.ToString() };
        var status = Label("");
        _content.Widgets.Add(Label("Sextant base X, Y"));
        _content.Widgets.Add(Row(x, y, new MyraButton("Apply", () =>
        {
            if (!int.TryParse(x.Text, out int baseX) || !int.TryParse(y.Text, out int baseY)
                || baseX < 0 || baseX > ushort.MaxValue || baseY < 0 || baseY > ushort.MaxValue)
            {
                status.Text = "Enter valid base coordinates.";
                return;
            }
            profile.WorldMapSextantBaseX = baseX;
            profile.WorldMapSextantBaseY = baseY;
            status.Text = "Sextant base updated.";
        })));
        _content.Widgets.Add(status);
        _content.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("detached_map_maintenance"),
            DesktopAssistantTheme.ActionButton(TazLang.Get("reset_maps_cache"), ConfirmResetCache)));
    }

    private void ShowFiles(string message = "")
    {
        Show();
        _content.Widgets.Add(Row(DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_back"), ShowSightings),
            Label(TazLang.Get("detached_map_files"))));
        var status = Label(message);
        var replace = new MyraCheckButton(TazLang.Get("detached_map_replace"), false);
        _content.Widgets.Add(new MyraButton("Import marker file", () =>
        {
            var dialog = new FileDialog(FileDialogMode.OpenFile)
            {
                Title = "Import marker file (.map, .csv, .xml)",
                Width = Math.Min(620, _bounds.Width),
                Height = Math.Min(450, _bounds.Height)
            };
            dialog.Closed += (_, _) =>
            {
                if (!dialog.Result)
                    return;
                string source = dialog.FilePath;
                if (replace.IsChecked)
                    Confirm(TazLang.Get("detached_map_import_replace_title"), TazLang.Get("detached_map_import_replace_confirm"),
                        () => Import(source, true), destructive: true);
                else
                    Import(source, false);
            };
            dialog.ShowModal(_desktop);
        }));
        void Import(string source, bool overwrite)
        {
            if (_map.TryImportMarkerFile(source, overwrite, out string result))
                ShowFiles(result);
            else
                status.Text = result;
        }

        _content.Widgets.Add(replace);
        _content.Widgets.Add(status);
        _content.Widgets.Add(DesktopAssistantTheme.ActionButton(TazLang.Get("reload_markers"), () => { _map.ReloadDesktopMarkers(); ShowFiles(); }));
        AddFiles("Marker files", false);
        _content.Widgets.Add(new MyraButton("Reload zone files", () => { _map.ReloadDesktopZones(); ShowFiles(); }));
        AddFiles("Zones", true);
    }

    private void AddFiles(string title, bool zones)
    {
        _content.Widgets.Add(Label(title));
        var options = _map.DesktopFileOptions(zones).ToArray();
        if (options.Length == 0)
            _content.Widgets.Add(Label("No files loaded."));
        foreach (var (label, enabled, set) in options)
            AddToggle(label, enabled, set);
    }

    private void ConfirmResetCache()
    {
        Confirm(TazLang.Get("reset_maps_cache"), TazLang.Get("detached_map_reset_message"), () =>
        {
            if (_map.TryResetDesktopCache())
                Hide();
            else
                _content.Widgets.Add(Label(TazLang.Get("detached_map_reset_failed")));
        }, destructive: true);
    }

    private void AddToggle(string text, bool enabled, Action<bool> change) =>
        _content.Widgets.Add(MyraCheckButton.CreateWithCallback(enabled, change, text));

    private static ComboView Choice(IEnumerable<string> items, int selected)
    {
        var choice = new ComboView();
        foreach (string item in items)
            choice.ListView.Widgets.Add(new Label { Text = item });
        if (selected >= 0 && selected < choice.ListView.Widgets.Count)
            choice.ListView.SelectedIndex = selected;
        return choice;
    }
}
