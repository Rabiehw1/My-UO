#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.File;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed partial class DetachedMapMarkers
{
    private void ShowTools()
    {
        Show();
        _content.Widgets.Add(Label("Map tools"));
        _content.Widgets.Add(new MyraButton("Navigation", ShowNavigation));
        _content.Widgets.Add(new MyraButton("Display and coordinates", ShowDisplay));
        _content.Widgets.Add(new MyraButton("Marker and zone files", ShowFiles));
        _content.Widgets.Add(new MyraButton("Sightings", ShowSightings));
        _content.Widgets.Add(new MyraButton("Reset map cache", ConfirmResetCache));
    }

    private void ShowNavigation()
    {
        Show();
        AddToolsBack("Navigation");
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
        var location = new MyraInputBox { HintText = "Coordinates" };
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
                status.Text = "Pathfinding is available on your character’s facet.";
                return;
            }
            if (!walk)
                _window.GoTo(tile);
            Hide();
        }

        _content.Widgets.Add(Row(new MyraButton("Go to", () => Navigate(false)), new MyraButton("Walk to", () => Navigate(true))));
        _content.Widgets.Add(Row(new MyraButton("Clear destination", _map.ClearGoToMarker),
            new MyraButton("Stop walking", _map.StopDesktopNavigation)));
        _content.Widgets.Add(status);
    }

    private void ShowDisplay()
    {
        Show();
        AddToolsBack("Display and coordinates");
        foreach (var (label, enabled, set) in _map.DesktopDisplayOptions())
            AddToggle(label, enabled, value => { set(value); _map.SaveSettings(); });
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
    }

    private void ShowFiles()
    {
        Show();
        AddToolsBack("Marker and zone files");
        var status = Label("");
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
                try
                {
                    if (_map.TryImportMarkerFile(dialog.FilePath))
                        ShowFiles();
                    else
                        status.Text = "Choose an existing .map, .csv, or .xml marker file.";
                }
                catch (ArgumentException)
                {
                    status.Text = "Choose a valid marker file.";
                }
            };
            dialog.ShowModal(_desktop);
        }));
        _content.Widgets.Add(status);
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
        Show();
        AddToolsBack("Reset map cache");
        _content.Widgets.Add(Label("Delete the generated map images and rebuild the current facet."));
        var status = Label("");
        _content.Widgets.Add(new MyraButton("Reset and rebuild", () =>
        {
            if (_map.TryResetDesktopCache())
                Hide();
            else
                status.Text = "The map cache could not be reset. Try again after the map finishes loading.";
        }));
        _content.Widgets.Add(status);
    }

    private void AddToolsBack(string title) =>
        _content.Widgets.Add(Row(new MyraButton("Back", ShowTools), Label(title)));

    private void AddToggle(string text, bool enabled, Action<bool> change)
    {
        var check = new CheckButton { Content = Label(text), IsChecked = enabled };
        check.IsCheckedChanged += (_, _) => change(check.IsChecked);
        _content.Widgets.Add(check);
    }

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
