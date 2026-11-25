#nullable enable
using System;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed partial class DetachedMapMarkers
{
    private void ShowSightings()
    {
        Show();
        AddToolsBack("Sightings");
        var profile = ProfileManager.CurrentProfile;
        var sightings = _map.MapWorld.MapSightings;
        AddToggle("Record nearby NPCs and creatures", profile.WorldMapRecordSightings,
            value =>
            {
                profile.WorldMapRecordSightings = value;
                if (!value)
                    sightings.Flush();
            });
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
        var town = sightings.CurrentTown();
        _content.Widgets.Add(Label(town == null ? "Outside saved town areas" : $"Current town: {town.Name}"));
        _content.Widgets.Add(new MyraButton("Manage town areas", ShowTownAreas));
        _content.Widgets.Add(new MyraButton("Set creature tiers", ShowCreatureTiers));
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
        bool confirm = false;
        var remove = new MyraButton("Remove area", () =>
        {
            if (list.SelectedIndex is not int index)
                return;

            if (!confirm)
            {
                confirm = true;
                status.Text = "Click Remove area again to confirm.";
                return;
            }
            sightings.RemoveTown(towns[index]);
            sightings.Flush();
            ShowTownAreas();
        }) { Enabled = false };
        list.SelectedIndexChanged += (_, _) =>
        {
            remove.Enabled = list.SelectedIndex.HasValue;
            confirm = false;
            status.Text = "";
        };
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
