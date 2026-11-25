#nullable enable
using System;
using System.Linq;
using ClassicUO.Configuration;
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

            var source = ContainerField(
                TazLang.Get("organizer_source"), TazLang.Get("organizer_target_source"),
                preset.SourceContSerial, serial => preset.SourceContSerial = serial);
            var destination = ContainerField(
                TazLang.Get("organizer_destination"), TazLang.Get("organizer_target_destination"),
                preset.DestContSerial, serial => preset.DestContSerial = serial);
            details.Widgets.Add(interaction.IsDesktop
                ? DesktopAssistantTheme.Group(TazLang.Get("mog_buttoncontainers"), DesktopAssistantForm.Fields(source, destination))
                : ItemListsTabContent.Section(interaction, TazLang.Get("mog_buttoncontainers"), source.Actions!, destination.Actions!));
            return details;

            DesktopAssistantForm.Field ContainerField(string label, string prompt, uint serial, Action<uint> set)
            {
                string location = serial == 0 ? TazLang.Get("backpack") : $"0x{serial:X8}";
                var value = new MyraLabel(location, MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };
                string targetText = interaction.IsDesktop ? TazLang.Get("organizer_target")
                    : TazLang.Get("organizer_set_container", [label]);
                var targetButton = new MyraButton(targetText, () =>
                {
                    GameActions.Print(prompt);
                    interaction.SelectTarget(target =>
                    {
                        if (target is Item { ItemData.IsContainer: true } container)
                            SaveContainer(container.Serial);
                        else
                            GameActions.Print(TazLang.Get("organizer_pick_valid_container"), Constants.HUE_ERROR);
                    });
                }) { Tooltip = prompt };
                var useBackpack = new MyraButton(TazLang.Get("organizer_use_backpack"), () => SaveContainer(0)) { Enabled = serial != 0 };
                if (interaction.IsDesktop)
                    return new DesktopAssistantForm.Field(label, value, DesktopAssistantForm.Actions(targetButton, useBackpack));

                var gameRow = AgentPresetPanel.Actions();
                value.Text = $"{label}  {location}";
                gameRow.Widgets.Add(value);
                gameRow.Widgets.Add(targetButton);
                gameRow.Widgets.Add(useBackpack);
                return new DesktopAssistantForm.Field(label, value, gameRow);

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
