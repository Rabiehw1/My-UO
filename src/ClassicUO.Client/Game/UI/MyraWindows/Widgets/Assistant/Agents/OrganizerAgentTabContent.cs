#nullable enable
using System;
using System.Linq;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

public static class OrganizerAgentTabContent
{
    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var organizer = OrganizerAgent.Instance;
        return AgentPresetPanel.Build(interaction, "Organizer presets", organizer.OrganizerConfigs, null,
            preset => preset.Name, organizer.NewOrganizerConfig, organizer.RenameConfig, organizer.DupeConfig,
            organizer.DeleteConfig, BuildPreset);

        Widget BuildPreset(OrganizerConfig? preset, Action<OrganizerConfig?> refresh)
        {
            var details = new VerticalStackPanel { Spacing = 6 };
            var actions = AgentPresetPanel.Actions();
            var import = new MyraButton("Import Organizer", () =>
            {
                if (Clipboard.GetClipboardText() is { Length: > 0 } json && organizer.ImportFromJson(json))
                    refresh(organizer.OrganizerConfigs.Last());
                else
                    GameActions.Print("Copy a valid organizer export first.", Constants.HUE_ERROR);
            });
            if (preset == null)
            {
                details.Widgets.Add(new MyraLabel("Create or import an organizer to get started.", MyraLabel.TextStyle.P));
                actions.Widgets.Add(import);
                details.Widgets.Add(actions);
                return details;
            }

            var run = new MyraButton("Run Selected", () => organizer.RunSelected(preset));
            if (interaction.IsDesktop)
                DesktopAssistantTheme.ApplyActionButtonStyle(run, primary: true);
            actions.Widgets.Add(run);
            actions.Widgets.Add(new MyraButton("Create Macro", () => interaction.RunInGame(() =>
            {
                organizer.CreateOrganizerMacroButton(preset);
                GameActions.Print($"Created Organizer Macro: {preset.Name}");
            })));
            actions.Widgets.Add(import);
            actions.Widgets.Add(new MyraButton("Export Organizer", () =>
            {
                if (organizer.GetJsonExport(preset) is { } json)
                {
                    json.CopyToClipboard();
                    GameActions.Print("Copied the organizer to your clipboard.", Constants.HUE_SUCCESS);
                }
            }));
            details.Widgets.Add(ItemListsTabContent.Section(interaction, preset.Name, actions,
                MyraCheckButton.CreateWithCallback(preset.Enabled, enabled =>
                {
                    preset.Enabled = enabled;
                    organizer.Save();
                }, "Enabled", "Enable this preset for Run Selected, organizer commands, and macros.")));

            details.Widgets.Add(SharedItemListPicker.BuildGathering(interaction, preset.SharedItemListId, id =>
            {
                preset.SharedItemListId = id;
                preset.LegacyItemConfigs = null;
                organizer.Save();
            }, (preset.IgnoreItemListId, id =>
            {
                preset.IgnoreItemListId = id;
                organizer.Save();
            })));

            details.Widgets.Add(ItemListsTabContent.Section(interaction, "Containers",
                ContainerRow("Source", preset.SourceContSerial, serial => preset.SourceContSerial = serial),
                ContainerRow("Destination", preset.DestContSerial, serial => preset.DestContSerial = serial)));
            return details;

            Widget ContainerRow(string label, uint serial, Action<uint> set)
            {
                var row = AgentPresetPanel.Actions();
                string location = serial == 0 ? "Backpack" : $"0x{serial:X8}";
                row.Widgets.Add(new MyraLabel($"{label}  {location}", MyraLabel.TextStyle.P)
                {
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Widgets.Add(new MyraButton($"Set {label}", () =>
                {
                    GameActions.Print($"Target the organizer's {label.ToLowerInvariant()} container.");
                    interaction.SelectTarget(target =>
                    {
                        if (target is Item { ItemData.IsContainer: true } container)
                            SaveContainer(container.Serial);
                        else
                            GameActions.Print("Select a container in the game.", Constants.HUE_ERROR);
                    });
                }));
                row.Widgets.Add(new MyraButton("Use Backpack", () => SaveContainer(0)) { Enabled = serial != 0 });
                return row;

                void SaveContainer(uint selected)
                {
                    set(selected);
                    organizer.Save();
                    refresh(preset);
                }
            }
        }
    }
}
