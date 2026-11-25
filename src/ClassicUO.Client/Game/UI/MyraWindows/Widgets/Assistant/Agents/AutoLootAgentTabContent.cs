#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;
using Myra.Graphics2D;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

public static class AutoLootAgentTabContent
{
    internal static string Title => TazLang.Get("assistant_agent_tab_autoloot");
    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        if (ProfileManager.CurrentProfile is not { } profile)
            return new MyraLabel("Log in to configure Auto Loot.", MyraLabel.TextStyle.P);

        var loot = AutoLootManager.Instance;
        if (!loot.IsLoaded)
            return new MyraLabel("Auto Loot settings could not be loaded.", MyraLabel.TextStyle.P);

        var root = new VerticalStackPanel { Spacing = 8 };
        Widget? desktopOptions = null;

        // Options
        var showProgress = MyraCheckButton.CreateWithCallback(
            profile.EnableAutoLootProgressBar,
            b => profile.EnableAutoLootProgressBar = b,
            "Enable Progress Bar",
            "Shows a progress bar gump.");

        var hueCorpse = MyraCheckButton.CreateWithCallback(
            profile.HueCorpseAfterAutoloot,
            b => profile.HueCorpseAfterAutoloot = b,
            "Hue Corpse After Processing",
            "Hue corpses after processing to make it easier to see if autoloot has processed them.");

        string retryLabel = TazLang.Get("autoloot_retry_delay");
        string retryHelp = TazLang.Get("autoloot_retry_delay_tooltip");
        var retrySpinner = new SpinButton
        {
            Integer = true,
            Value = profile.AutoLootRetryDelay,
            Minimum = 1000,
            Maximum = 600000,
            MinWidth = interaction.IsDesktop ? 76 : 100,
            Tooltip = retryHelp
        };
        retrySpinner.ValueChangedByUser += (_, _) =>
            profile.AutoLootRetryDelay = (int)Math.Clamp(retrySpinner.Value ?? 5000f, 1000f, 600000f);
        var disableRetry = MyraCheckButton.CreateWithCallback(
            profile.DisableAutolootCorpseRetry,
            b => profile.DisableAutolootCorpseRetry = b,
            TazLang.Get("autoloot_disableretry"),
            TazLang.Get("autoloot_disableretry_tooltip"));

        // Auto skinning section
        var skinGraphicsBox = new MyraInputBox
        {
            Text = profile.AutoSkinningKnifeGraphics,
            MinWidth = interaction.IsDesktop ? 120 : 250,
            Tooltip = TazLang.Get("autoskinning_graphics_tooltip", "Graphic IDs of knives/daggers used to skin corpses. The first one found in your backpack is used. Separate multiple with ';'. Accepts hex (0x0F52) or decimal.")
        };

        var enableSkinning = MyraCheckButton.CreateWithCallback(
            profile.EnableAutoSkinning,
            b => profile.EnableAutoSkinning = b,
            TazLang.Get("autoskinning_enable", "Enable Auto Skinning"),
            TazLang.Get("autoskinning_enable_tooltip", "When a corpse is opened, automatically use a knife/dagger from the graphic list below on it (double clicks the knife and targets the corpse). Uses the action queue."));
        var skinHumans = MyraCheckButton.CreateWithCallback(
            profile.AutoSkinningHumanCorpses,
            b => profile.AutoSkinningHumanCorpses = b,
            TazLang.Get("autoskinning_humancorpses", "Skin Human Corpses"),
            TazLang.Get("autoskinning_humancorpses_tooltip", "Also auto skin human/humanoid corpses."));
        var targetKnife = new MyraButton(TazLang.Get("autoskinning_targetweapon", "Target Skinning Weapon"), () =>
        {
            interaction.SelectTarget(targeted =>
            {
                if (targeted is Entity entity && SerialHelper.IsItem(entity))
                {
                    string appended = AppendSkinningGraphic(profile.AutoSkinningKnifeGraphics, entity.Graphic);
                    profile.AutoSkinningKnifeGraphics = appended;
                    skinGraphicsBox.Text = appended;
                }
            });
        }) { Tooltip = TazLang.Get("autoskinning_targetweapon_tooltip", "Target a weapon to add its graphic to the skinning knife list."), HorizontalAlignment = HorizontalAlignment.Left };

        string skinGraphicsLabel = TazLang.Get("autoskinning_graphics", "Knife graphic IDs:");
        skinGraphicsBox.TextChangedByUser += (_, _) => profile.AutoSkinningKnifeGraphics = skinGraphicsBox.Text;

        string skinningTitle = TazLang.Get("autoskinning_title", "Auto Skinning");
        if (interaction.IsDesktop)
        {
            var corpseOptions = DesktopAssistantTheme.Group(TazLang.Get("assistant_desktop_corpse_handling", "Corpse handling"),
                showProgress, hueCorpse,
                DesktopAssistantForm.Fields(new DesktopAssistantForm.Field(retryLabel, retrySpinner)), disableRetry);
            var skinning = DesktopAssistantTheme.Group(skinningTitle, enableSkinning, skinHumans, targetKnife,
                DesktopAssistantForm.Fields(new DesktopAssistantForm.Field(skinGraphicsLabel, skinGraphicsBox)));
            desktopOptions = new DesktopAssistantForm.SectionPair(corpseOptions, skinning);
        }
        else
        {
            var retryRow = GameFieldRow(retryLabel, retrySpinner);
            var corpseChecks = new HorizontalStackPanel { Spacing = 8 };
            corpseChecks.Widgets.Add(hueCorpse);
            retryRow.Widgets.Add(disableRetry);
            root.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = "Options:" }, showProgress, corpseChecks, retryRow));

            var skinChecks = new HorizontalStackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            skinChecks.Widgets.Add(enableSkinning);
            skinChecks.Widgets.Add(skinHumans);
            skinChecks.Widgets.Add(targetKnife);
            root.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = skinningTitle },
                skinChecks, GameFieldRow(skinGraphicsLabel, skinGraphicsBox)));
        }

        var presets = AgentPresetPanel.Build(
            interaction, TazLang.Get(interaction.IsDesktop ? "agent_preset_title" : "autoloot_presets"),
            loot.Lists, loot.CurrentList, preset => preset.Name,
            name => loot.AddPreset(name), loot.RenameList,
            preset => loot.AddPreset(preset.Name + " Copy", preset), loot.DeleteList,
            BuildPreset, minimumCount: 1, isRunning: IsRunning, desktopOptions: desktopOptions);
        if (interaction.IsDesktop)
            return presets;

        root.Widgets.Insert(0, presets);
        return root;

        bool IsRunning(AutoLootManager.AutoLootList preset) => loot.IsEnabled && ReferenceEquals(preset, loot.CurrentList);

        Widget BuildPreset(AutoLootManager.AutoLootList? preset, Action<AutoLootManager.AutoLootList?> refresh)
        {
            var details = new VerticalStackPanel { Spacing = 6 };
            if (preset == null)
                return details;

            bool running = IsRunning(preset);
            var start = new MyraButton(TazLang.Get("autoloot_start"), () =>
            {
                loot.StartPreset(preset);
                refresh(preset);
            });
            var stop = new MyraButton(TazLang.Get("autoloot_stop"), () =>
            {
                loot.SetEnabled(false);
                refresh(preset);
            });

            string bag = preset.GrabBagSerial switch
            {
                null => TazLang.Get("autoloot_shared_bag_value",
                    [profile.GrabBagSerial == 0 ? TazLang.Get("autoloot_backpack") : $"0x{profile.GrabBagSerial:X8}"]),
                0 => TazLang.Get("autoloot_backpack"),
                uint serial => $"0x{serial:X8}"
            };
            var bagActions = interaction.IsDesktop ? DesktopAssistantForm.Actions() : AgentPresetPanel.Actions();
            var setBag = new MyraButton(TazLang.Get("autoloot_set_grab_bag"), () =>
            {
                GameActions.Print("Target the loot bag for this preset.");
                interaction.SelectTarget(target =>
                {
                    if (target is Item { ItemData.IsContainer: true } item)
                        SetBag(item.Serial);
                    else
                        GameActions.Print("Select a container in the game.", Constants.HUE_ERROR);
                });
            });
            bagActions.Widgets.Add(new MyraButton(TazLang.Get("autoloot_use_backpack"), () => SetBag(0)) { Enabled = preset.GrabBagSerial != 0 });
            bagActions.Widgets.Add(new MyraButton(TazLang.Get("autoloot_use_shared_bag"), () => SetBag(null)) { Enabled = preset.GrabBagSerial.HasValue });
            var lootHumans = MyraCheckButton.CreateWithCallback(preset.LootHumanCorpses, value =>
            {
                preset.LootHumanCorpses = value;
                loot.SavePreset(preset);
            }, TazLang.Get("autoloot_human_corpses"));
            var bagValue = new MyraLabel(bag, MyraLabel.TextStyle.P);
            if (interaction.IsDesktop)
            {
                var title = MyraGrid.TableText(preset.Name);
                title.Font = DesktopAssistantTheme.Font(1, bold: true);
                title.VerticalAlignment = VerticalAlignment.Center;
                var controls = DesktopAssistantForm.Actions();
                controls.HorizontalAlignment = HorizontalAlignment.Right;
                if (loot.IsEnabled)
                    controls.Widgets.Add(stop);
                if (!running)
                {
                    DesktopAssistantTheme.ApplyActionButtonStyle(start, primary: true);
                    controls.Widgets.Add(start);
                }
                var header = new MyraGrid { ColumnSpacing = 8 };
                header.AddColumn(new Proportion(ProportionType.Fill));
                header.AddColumn();
                header.AddWidget(title, 0, 0);
                header.AddWidget(controls, 0, 1);
                details.Widgets.Add(header);

                var library = ItemListLibrary.Instance;
                string none = TazLang.Get("autoloot_no_list");
                var main = SharedItemListPicker.CreateSelector(library, preset.MainItemListId, none, true, SetMainList).Picker;
                var ignore = SharedItemListPicker.CreateSelector(library, preset.IgnoreItemListId, none, true, SetIgnoreList).Picker;
                main.MinWidth = ignore.MinWidth = 170;
                main.MaxWidth = ignore.MaxWidth = 205;
                var manage = new MyraButton(TazLang.Get("autoloot_manage_lists"), interaction.OpenItemLists);
                details.Widgets.Add(DesktopAssistantForm.Fields(
                    new DesktopAssistantForm.Field(TazLang.Get("autoloot_main_list"), main),
                    new DesktopAssistantForm.Field(TazLang.Get("autoloot_ignore_list"), ignore, manage),
                    new DesktopAssistantForm.Field(TazLang.Get("autoloot_grab_bag"), bagValue, setBag)));
                details.Widgets.Add(bagActions);
                details.Widgets.Add(lootHumans);
            }
            else
            {
                string status = running ? TazLang.Get("agent_preset_running")
                    : loot.IsEnabled ? TazLang.Get("autoloot_running_preset", [loot.CurrentList.Name]) : TazLang.Get("autoloot_stopped");
                var controls = AgentPresetPanel.Actions();
                controls.Widgets.Add(running ? stop : start);
                if (!running && loot.IsEnabled)
                    controls.Widgets.Add(stop);
                details.Widgets.Add(ItemListsTabContent.Section(interaction, preset.Name,
                    new MyraLabel(status, MyraLabel.TextStyle.P), controls));
                details.Widgets.Add(SharedItemListPicker.BuildGathering(interaction, preset.MainItemListId,
                    SetMainList, (preset.IgnoreItemListId, SetIgnoreList)));
                bagActions.Widgets.Insert(0, setBag);
                details.Widgets.Add(ItemListsTabContent.Section(interaction, TazLang.Get("autoloot_loot_destination"),
                    bagValue, bagActions, lootHumans));
            }
            return details;

            void SetMainList(Guid? id)
            {
                preset.MainItemListId = id;
                loot.SavePreset(preset);
            }

            void SetIgnoreList(Guid? id)
            {
                preset.IgnoreItemListId = id;
                loot.SavePreset(preset);
            }

            void SetBag(uint? serial)
            {
                preset.GrabBagSerial = serial;
                loot.SavePreset(preset);
                refresh(preset);
            }
        }
    }

    private static HorizontalStackPanel GameFieldRow(string text, Widget control)
    {
        var row = new HorizontalStackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        row.Widgets.Add(new MyraLabel(text, MyraLabel.TextStyle.P)
        {
            Tooltip = control.Tooltip,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Widgets.Add(control);
        return row;
    }

    private static string AppendSkinningGraphic(string current, ushort graphic)
    {
        current ??= string.Empty;

        foreach (string part in current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (StringHelper.TryParseInt(part, out int existing) && existing == graphic)
                return current;

        string entry = $"0x{graphic:X4}";
        return string.IsNullOrWhiteSpace(current) ? entry : $"{current.TrimEnd(';', ' ')};{entry}";
    }
}
