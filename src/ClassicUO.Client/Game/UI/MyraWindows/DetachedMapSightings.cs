#nullable enable
using System;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Microsoft.Xna.Framework;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed partial class DetachedMapMarkers
{
    private void ShowSightings()
    {
        Show();
        _content.Widgets.Add(new MyraButton(TazLang.Get("detached_map_files"), () => ShowFiles()));
        var profile = ProfileManager.CurrentProfile;
        var sightings = _map.MapWorld.MapSightings;
        AddToggle("Record nearby NPCs and creatures", profile.WorldMapRecordSightings,
            value =>
            {
                profile.WorldMapRecordSightings = value;
                if (!value)
                    sightings.Flush();
            });
        _content.Widgets.Add(DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_profession_picker"), () => ShowNearbyProfessions(ShowSightings)));
        _content.Widgets.Add(Label("Show saved sightings"));
        foreach (var (label, layer) in new[]
        {
            ("NPCs", MapSightingLayers.Npcs), ("Vendors and shops", MapSightingLayers.Vendors),
            ("Monsters", MapSightingLayers.Monsters), ("Bosses", MapSightingLayers.Bosses),
            ("Super bosses", MapSightingLayers.SuperBosses)
        })
        {
            AddToggle(label, (profile.WorldMapSightingLayers & layer) != 0, enabled =>
                profile.WorldMapSightingLayers = enabled
                    ? profile.WorldMapSightingLayers | layer : profile.WorldMapSightingLayers & ~layer);
        }

        AddToggle("Vendors in current town only", profile.WorldMapVendorsCurrentTownOnly,
            value => profile.WorldMapVendorsCurrentTownOnly = value);
        _townStatus = Label(CurrentTownText());
        _content.Widgets.Add(_townStatus);
        _content.Widgets.Add(new MyraButton("Manage town areas", ShowTownAreas));
        _content.Widgets.Add(new MyraButton("Set creature tiers", ShowCreatureTiers));
    }

    private void ShowNearbyProfessions(Action back)
    {
        Show();
        var world = _map.MapWorld;
        int facet = world.MapIndex;
        var people = world.MapSightings.NearbyPeople().ToArray();
        var status = Label("");
        var profession = new MyraInputBox { HintText = TazLang.Get("detached_map_profession_hint") };
        var picker = Choice(people.Select(person =>
        {
            string name = person.Name.Length == 0 ? $"0x{person.Serial:X8}" : person.Name;
            return person.Profession.Length == 0 ? name : $"{name} · {person.Profession}";
        }), 0);
        _content.Widgets.Add(Row(
            DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_back"), back),
            DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_profession_refresh"), () => ShowNearbyProfessions(back))));
        if (people.Length == 0)
        {
            _content.Widgets.Add(Label(TazLang.Get("detached_map_profession_empty")));
            return;
        }

        _content.Widgets.Add(Label(TazLang.Get("detached_map_profession_help")));
        _content.Widgets.Add(picker);
        _content.Widgets.Add(profession);
        _content.Widgets.Add(DesktopAssistantTheme.ActionButton(TazLang.Get("detached_map_profession_save"), Save, primary: true));
        _content.Widgets.Add(status);
        picker.ListView.SelectedIndexChanged += (_, _) => FillProfession();
        FillProfession();

        void FillProfession()
        {
            if (picker.ListView.SelectedIndex is not int index)
                return;
            string suggested = people[index].Profession;
            profession.Text = suggested.Length == 0 ? "" : char.ToUpperInvariant(suggested[0]) + suggested[1..];
            status.Text = "";
        }

        void Save()
        {
            if (picker.ListView.SelectedIndex is not int index)
                return;
            string label = profession.Text?.Trim() ?? "";
            if (label.Length is 0 or > 25 || label.IndexOfAny([',', '\r', '\n']) >= 0)
            {
                status.Text = TazLang.Get("detached_map_profession_invalid");
                return;
            }

            if (world.MapIndex != facet || !world.Mobiles.TryGetValue(people[index].Serial, out var person)
                || person.IsDestroyed || person.IsDead || person.Distance > world.ClientViewRange)
            {
                status.Text = TazLang.Get("detached_map_profession_missing");
                return;
            }

            var point = new Point(person.X, person.Y);
            if (_map.DesktopUserMarkers?.Any(marker => marker.MapId == facet && marker.X == point.X
                && marker.Y == point.Y && marker.Name.Equals(label, StringComparison.OrdinalIgnoreCase)) == true)
            {
                status.Text = TazLang.Get("detached_map_profession_already_saved");
                return;
            }

            bool saved = _map.TryChangeDesktopMarker(null, new WorldMapGump.WMapMarker
            {
                X = point.X, Y = point.Y, MapId = facet, Name = label, ColorName = "yellow", ZoomIndex = 4
            });
            status.Text = saved
                ? TazLang.Get("detached_map_profession_saved", [label])
                : TazLang.Get("detached_map_marker_write_failed");
        }
    }

    private string CurrentTownText()
    {
        var sightings = _map.MapWorld.MapSightings;
        if (sightings.CurrentTown() is { } town)
            return TazLang.Get("detached_map_current_town", [town.Name]);

        return TazLang.Get(sightings.Towns.Count == 0 ? "detached_map_no_towns" : "detached_map_outside_town");
    }

    private void ShowTownAreas()
    {
        Show();
        _content.Widgets.Add(Row(new MyraButton("Back", ShowSightings), Label("Town areas")));
        _content.Widgets.Add(Label("Stand near the town center. Enter its name and the distance to its edge in tiles."));
        var sightings = _map.MapWorld.MapSightings;
        var name = new MyraInputBox { HintText = "Town name" };
        var radius = new MyraInputBox { Width = 65, Text = "96", InputFilter = MyraInputBox.DigitInputFilter };
        var status = Label("");
        _content.Widgets.Add(name);
        _content.Widgets.Add(Row(Label("Distance"), radius));
        _content.Widgets.Add(new MyraButton("Save around my position", () =>
        {
            if (name.Text is { Length: <= 60 } && int.TryParse(radius.Text, out int tiles) && sightings.SaveTown(name.Text, tiles))
            {
                sightings.Flush();
                ShowTownAreas();
            }
            else
                status.Text = "Enter a town name and a distance from 8 to 512 tiles.";
        }));
        _content.Widgets.Add(status);

        var towns = sightings.Towns.ToArray();
        if (towns.Length == 0)
            return;

        var list = new ListView { Height = 110 };
        foreach (MapTown town in towns)
            list.Widgets.Add(Label($"{town.Name} · Facet {town.Facet} · {town.X}, {town.Y} · {town.Radius} tiles"));
        _content.Widgets.Add(list);
        var remove = new MyraButton("Remove area", () =>
        {
            if (list.SelectedIndex is not int index)
                return;

            Confirm(TazLang.Get("remove"), TazLang.Get("detached_map_remove_town", [towns[index].Name]), () =>
            {
                sightings.RemoveTown(towns[index]);
                sightings.Flush();
                ShowTownAreas();
            }, destructive: true);
        }) { Enabled = false };
        MyraStyle.ApplyButtonDangerStyle(remove);
        list.SelectedIndexChanged += (_, _) => remove.Enabled = list.SelectedIndex.HasValue;
        _content.Widgets.Add(remove);
    }

    private void ShowCreatureTiers()
    {
        Show();
        _content.Widgets.Add(Row(new MyraButton("Back", ShowSightings), Label("Creature tiers")));
        var sightings = _map.MapWorld.MapSightings;
        string[] names = sightings.MonsterNames().ToArray();
        if (names.Length == 0)
        {
            _content.Widgets.Add(Label("Record a creature sighting to assign its tier."));
            return;
        }

        var filter = new MyraInputBox { HintText = "Find a recorded creature" };
        var list = new ListView { Height = 125 };
        var tier = Choice(["Automatic", "Monster", "Boss", "Super boss"], 0);
        var tiers = new[] { MapSightingLayers.None, MapSightingLayers.Monsters, MapSightingLayers.Bosses, MapSightingLayers.SuperBosses };
        string[] shown = [];
        void Refresh()
        {
            list.Widgets.Clear();
            shown = names.Where(name => name.Contains(filter.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string name in shown)
                list.Widgets.Add(Label(name));
        }

        var apply = new MyraButton("Save tier", () =>
        {
            if (list.SelectedIndex is int index && tier.ListView.SelectedIndex is int selection)
            {
                sightings.SetTier(shown[index], tiers[selection]);
                sightings.Flush();
            }
        }) { Enabled = false };
        list.SelectedIndexChanged += (_, _) =>
        {
            apply.Enabled = list.SelectedIndex.HasValue;
            if (list.SelectedIndex is int index && index < shown.Length)
                tier.ListView.SelectedIndex = Math.Max(0, Array.IndexOf(tiers, sightings.TierFor(shown[index])));
        };
        filter.TextChangedByUser += (_, _) => Refresh();
        Refresh();
        _content.Widgets.Add(filter);
        _content.Widgets.Add(list);
        _content.Widgets.Add(tier);
        _content.Widgets.Add(apply);
    }
}
