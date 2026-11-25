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

    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        if (ProfileManager.CurrentProfile is not { } profile)
            return new MyraLabel("Log in to configure Auto Loot.", MyraLabel.TextStyle.P);

        var loot = AutoLootManager.Instance;
        if (!loot.IsLoaded)
            return new MyraLabel("Auto Loot settings could not be loaded.", MyraLabel.TextStyle.P);

        var root = new VerticalStackPanel { Spacing = 8 };
        root.Widgets.Add(AgentPresetPanel.Build(
            interaction, "Auto Loot presets", loot.Lists, loot.CurrentList, preset => preset.Name,
            name => loot.AddPreset(name), loot.RenameList,
            preset => loot.AddPreset(preset.Name + " Copy", preset), loot.DeleteList,
            BuildPreset, minimumCount: 1, display: preset =>
                loot.IsEnabled && ReferenceEquals(preset, loot.CurrentList) ? $"{preset.Name} (running)" : preset.Name));

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

        var retryRow = new HorizontalStackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        retryRow.Widgets.Add(new MyraLabel(interaction.IsDesktop ? "Corpse retry delay" : "Corpse retry delay (ms):", MyraLabel.TextStyle.P)
        {
            Tooltip = "Milliseconds before a failed corpse is retried. Minimum 1000ms.",
            VerticalAlignment = Myra.Graphics2D.UI.VerticalAlignment.Center
        });
        var retrySpinner = new SpinButton
        {
            Integer = true,
            Value = profile.AutoLootRetryDelay,
            Minimum = 1000,
            Maximum = 600000,
            MinWidth = interaction.IsDesktop ? 76 : 100,
            Tooltip = "Milliseconds before a failed corpse is retried. Minimum 1000ms."
        };
        retrySpinner.ValueChangedByUser += (_, _) =>
            profile.AutoLootRetryDelay = (int)Math.Clamp(retrySpinner.Value ?? 5000f, 1000f, 600000f);
        retryRow.Widgets.Add(retrySpinner);
        if (interaction.IsDesktop)
            retryRow.Widgets.Add(new MyraLabel("ms", MyraLabel.TextStyle.P));
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

        var skinGraphicsRow = new HorizontalStackPanel { Spacing = 8, VerticalAlignment = Myra.Graphics2D.UI.VerticalAlignment.Center };
        skinGraphicsRow.Widgets.Add(new MyraLabel(TazLang.Get("autoskinning_graphics", "Knife graphic IDs:"), MyraLabel.TextStyle.P)
        {
            Tooltip = TazLang.Get("autoskinning_graphics_tooltip", "Graphic IDs of knives/daggers used to skin corpses. The first one found in your backpack is used. Separate multiple with ';'. Accepts hex (0x0F52) or decimal."),
            VerticalAlignment = Myra.Graphics2D.UI.VerticalAlignment.Center
        });
        skinGraphicsBox.TextChangedByUser += (_, _) => profile.AutoSkinningKnifeGraphics = skinGraphicsBox.Text;
        skinGraphicsRow.Widgets.Add(skinGraphicsBox);

        string skinningTitle = TazLang.Get("autoskinning_title", "Auto Skinning");
        if (interaction.IsDesktop)
        {
            var options = new MyraGrid { ColumnSpacing = 10 };
            options.AddColumn(new Proportion(ProportionType.Part, 1));
            options.AddColumn(new Proportion(ProportionType.Part, 1));
            options.AddWidget(DesktopAssistantTheme.Group(TazLang.Get("assistant_desktop_corpse_handling", "Corpse handling"),
                showProgress, hueCorpse, retryRow, disableRetry), 0, 0);
            options.AddWidget(DesktopAssistantTheme.Group(skinningTitle,
                enableSkinning, skinHumans, targetKnife, skinGraphicsRow), 0, 1);
            root.Widgets.Add(options);
        }
        else
        {
            var corpseChecks = new HorizontalStackPanel { Spacing = 8 };
            corpseChecks.Widgets.Add(hueCorpse);
            retryRow.Widgets.Add(disableRetry);
            root.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = "Options:" }, showProgress, corpseChecks, retryRow));

            var skinChecks = new HorizontalStackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            skinChecks.Widgets.Add(enableSkinning);
            skinChecks.Widgets.Add(skinHumans);
            skinChecks.Widgets.Add(targetKnife);
            root.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = skinningTitle }, skinChecks, skinGraphicsRow));
        }

        return root;

        Widget BuildPreset(AutoLootManager.AutoLootList? preset, Action<AutoLootManager.AutoLootList?> refresh)
        {
            var details = new VerticalStackPanel { Spacing = 6 };
            if (preset == null)
                return details;

            bool running = loot.IsEnabled && ReferenceEquals(preset, loot.CurrentList);
            string status = running ? "Running" : loot.IsEnabled ? $"Running: {loot.CurrentList.Name}" : "Auto Loot stopped";
            var controls = AgentPresetPanel.Actions();
            var start = new MyraButton(running ? "Stop Auto Loot" : "Start Selected", () =>
            {
                if (running)
                    loot.SetEnabled(false);
                else
                    loot.StartPreset(preset);
                refresh(preset);
            });
            if (interaction.IsDesktop && !running)
                DesktopAssistantTheme.ApplyActionButtonStyle(start, primary: true);
            controls.Widgets.Add(start);
            if (!running && loot.IsEnabled)
            {
                controls.Widgets.Add(new MyraButton("Stop Auto Loot", () =>
                {
                    loot.SetEnabled(false);
                    refresh(preset);
                }));
            }

            details.Widgets.Add(ItemListsTabContent.Section(interaction, preset.Name,
                new MyraLabel(status, MyraLabel.TextStyle.P), controls));
            details.Widgets.Add(SharedItemListPicker.BuildGathering(interaction, preset.MainItemListId,
                id =>
                {
                    preset.MainItemListId = id;
                    loot.SavePreset(preset);
                },
                (preset.IgnoreItemListId, id =>
                {
                    preset.IgnoreItemListId = id;
                    loot.SavePreset(preset);
                })));

            string bag = preset.GrabBagSerial switch
            {
                null => profile.GrabBagSerial == 0 ? "Shared grab bag (backpack)" : $"Shared grab bag (0x{profile.GrabBagSerial:X8})",
                0 => "Backpack",
                uint serial => $"0x{serial:X8}"
            };
            var bagActions = AgentPresetPanel.Actions();
            bagActions.Widgets.Add(new MyraButton("Set Grab Bag", () =>
            {
                GameActions.Print("Target the loot bag for this preset.");
                interaction.SelectTarget(target =>
                {
                    if (target is Item { ItemData.IsContainer: true } item)
                        SetBag(item.Serial);
                    else
                        GameActions.Print("Select a container in the game.", Constants.HUE_ERROR);
                });
            }));
            bagActions.Widgets.Add(new MyraButton("Use Backpack", () => SetBag(0)) { Enabled = preset.GrabBagSerial != 0 });
            bagActions.Widgets.Add(new MyraButton("Use Shared Bag", () => SetBag(null)) { Enabled = preset.GrabBagSerial.HasValue });
            var lootHumans = MyraCheckButton.CreateWithCallback(preset.LootHumanCorpses, value =>
            {
                preset.LootHumanCorpses = value;
                loot.SavePreset(preset);
            }, "Auto Loot Human Corpses");
            details.Widgets.Add(ItemListsTabContent.Section(interaction, "Loot destination",
                new MyraLabel(bag, MyraLabel.TextStyle.P), bagActions, lootHumans));
            return details;

            void SetBag(uint? serial)
            {
                preset.GrabBagSerial = serial;
                loot.SavePreset(preset);
                refresh(preset);
            }
        }
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
