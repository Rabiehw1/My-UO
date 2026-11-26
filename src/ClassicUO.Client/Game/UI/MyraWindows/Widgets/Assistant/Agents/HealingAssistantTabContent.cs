#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Utility;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal static partial class HealingAssistantTabContent
{
    private sealed record MethodSetting(string Name, string ToggleLabel, Action<bool> SetEnabled);

    private static MyraButton BuildActivityButton(Profile profile, Action? onChanged = null) =>
        DesktopAssistantTheme.ActivityButton(TazLang.Get("healingassistant_start"), TazLang.Get("healingassistant_stop"),
            () => profile.EnableHealingAssistant, () =>
            {
                profile.EnableHealingAssistant = !profile.EnableHealingAssistant;
                onChanged?.Invoke();
            });

    public static Widget Build(AgentPageInteraction interaction)
    {
        Profile? profile = ProfileManager.CurrentProfile;
        if (profile == null)
            return new MyraLabel(TazLang.Get("bandageagent_profilenotloaded"), MyraLabel.TextStyle.P);

        MethodSetting[] methods =
        [
            new(TazLang.Get("healingassistant_bandages", "Bandages"), TazLang.Get("healingassistant_use_bandages", "Use bandages"),
                value => profile.EnableBandageAgent = value),
            new(TazLang.Get("healingassistant_greater_potion_name", "Greater Heal potion"), TazLang.Get("healingassistant_use_greater", "Use Greater Heal potions"),
                value => profile.HealingAssistantUseGreaterHeal = value),
            new(TazLang.Get("healingassistant_tamala", "Tamala Heal"), TazLang.Get("healingassistant_use_tamala", "Use Tamala Heal potions"),
                value => profile.HealingAssistantUseTamalaHeal = value),
            new(TazLang.Get("healingassistant_fish", "Magic fish"), TazLang.Get("healingassistant_use_fish", "Use magic healing fish"),
                value => profile.HealingAssistantUseMagicFish = value),
            new(TazLang.Get("healingassistant_natures_touch"), TazLang.Get("healingassistant_natures_touch"), value => profile.HealingAssistantUseNaturesTouch = value),
            new(TazLang.Get("healingassistant_heal_spell"), TazLang.Get("healingassistant_heal_spell"), value => profile.HealingAssistantUseHealSpell = value),
            new(TazLang.Get("healingassistant_greater_spell"), TazLang.Get("healingassistant_greater_spell"), value => profile.HealingAssistantUseGreaterHealSpell = value),
            new(TazLang.Get("healingassistant_angelic_aura"), TazLang.Get("healingassistant_angelic_aura"), value => profile.HealingAssistantUseAngelicAura = value),
            new(TazLang.Get("healingassistant_song_of_life"), TazLang.Get("healingassistant_song_of_life"), value => profile.HealingAssistantUseSongOfLife = value)
        ];

        if (interaction.IsDesktop)
            return BuildPlanner(interaction, profile, methods, AllowedRange);

        var root = new VerticalStackPanel { Spacing = 8 };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var sliders = new LabeledHorizontalSlider[methods.Length];
        var ranges = new MyraLabel[methods.Length];

        root.Widgets.Add(BuildActivityButton(profile));
        var firstMethod = new ComboView
        {
            MinWidth = 160,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = TazLang.Get("healingassistant_first_range_tooltip",
                "Moves this method to the highest HP range. Healing uses the range that matches your current HP.")
        };
        foreach (MethodSetting method in methods)
            firstMethod.ListView.Widgets.Add(new Label { Text = method.Name });
        firstMethod.ListView.SelectedIndex = (int)profile.HealingAssistantFirstMethod;
        root.Widgets.Add(new MyraLabel(TazLang.Get("healingassistant_first_range", "First HP range"), MyraLabel.TextStyle.P).PlaceBefore(firstMethod));
        root.Widgets.Add(status);

        root.Widgets.Add(Section(TazLang.Get("healingassistant_bandages", "Bandages"),
            BuildThresholdRow(HealingMethod.Bandages), BandageAgentTabContent.BuildOptions(profile)));

        root.Widgets.Add(Section(TazLang.Get("healingassistant_potions", "Healing potions"),
            BuildThresholdRow(HealingMethod.GreaterHeal),
            SerialRows(BuildSerialField(interaction, TazLang.Get("healingassistant_greater_serial", "Greater Heal stack serial"),
                profile.HealingAssistantGreaterHealSerial, value => profile.HealingAssistantGreaterHealSerial = value)),
            BuildThresholdRow(HealingMethod.TamalaHeal),
            SerialRows(
                BuildSerialField(interaction, TazLang.Get("healingassistant_full_tamala_serial", "Full Tamala serial"),
                    profile.HealingAssistantFullTamalaSerial, value => profile.HealingAssistantFullTamalaSerial = value),
                BuildSerialField(interaction, TazLang.Get("healingassistant_half_tamala_serial", "Half-full Tamala serial"),
                    profile.HealingAssistantHalfTamalaSerial, value => profile.HealingAssistantHalfTamalaSerial = value)),
            MyraInputBox.NumberWithLabel(TazLang.Get("healingassistant_potion_cooldown", "Shared potion cooldown (ms)"),
                profile.HealingAssistantPotionCooldownMs, HealingAssistantManager.MinimumCooldownMs, int.MaxValue,
                value => profile.HealingAssistantPotionCooldownMs = value)));

        root.Widgets.Add(Section(TazLang.Get("healingassistant_fish", "Magic fish"),
            BuildThresholdRow(HealingMethod.MagicFish),
            SerialRows(BuildSerialField(interaction, TazLang.Get("healingassistant_fish_serial", "Magic fish stack serial"),
                profile.HealingAssistantMagicFishSerial, value => profile.HealingAssistantMagicFishSerial = value)),
            MyraInputBox.NumberWithLabel(TazLang.Get("healingassistant_fish_cooldown", "Fish cooldown (ms)"),
                profile.HealingAssistantFishCooldownMs, HealingAssistantManager.MinimumCooldownMs, int.MaxValue,
                value => profile.HealingAssistantFishCooldownMs = value)));

        root.Widgets.Add(Section("Healing spells",
            new MyraLabel("Set your Earth Book, Holy Book, and Book of Songs in the Spell Books tab to use their spells.", MyraLabel.TextStyle.P),
            BuildThresholdRow(HealingMethod.NaturesTouch),
            BuildThresholdRow(HealingMethod.HealSpell),
            BuildThresholdRow(HealingMethod.GreaterHealSpell),
            BuildThresholdRow(HealingMethod.AngelicAura),
            BuildThresholdRow(HealingMethod.SongOfLife),
            MyraInputBox.NumberWithLabel("Shared spell cooldown (ms)",
                profile.HealingAssistantSpellCooldownMs, HealingAssistantManager.MinimumSpellCooldownMs, int.MaxValue,
                value => profile.HealingAssistantSpellCooldownMs = value)));

        firstMethod.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (firstMethod.ListView.SelectedIndex is not int index)
                return;

            HealingAssistantManager.ChangeFirstMethod(profile, (HealingMethod)index);
            RefreshRanges();
            status.Visible = false;
        };
        RefreshRanges();

        // The desktop already has a scrollable page. The in-game tab needs its own viewport.
        return ReferenceEquals(interaction, AgentPageInteraction.InGame)
            ? new ScrollViewer { Content = root, MaxHeight = Math.Max(180, ScaleHelper.LogicalWindowHeight - 140) }
            : root;

        Widget Section(string title, params Widget[] children) => ItemListsTabContent.Section(interaction, title, children);

        Widget SerialRows(params DesktopAssistantForm.Field[] fields)
        {
            var rows = new VerticalStackPanel { Spacing = 4 };
            foreach (var field in fields)
            {
                var row = new HorizontalStackPanel { Spacing = 5 };
                row.Widgets.Add(new MyraLabel(field.Label, MyraLabel.TextStyle.P));
                row.Widgets.Add(field.Control);
                row.Widgets.Add(field.Actions!);
                rows.Widgets.Add(row);
            }
            return rows;
        }

        Widget BuildThresholdRow(HealingMethod kind)
        {
            MethodSetting method = methods[(int)kind];
            var toggle = MyraCheckButton.CreateWithCallback(HealingAssistantManager.IsEnabled(profile, kind), value =>
            {
                method.SetEnabled(value);
                if (value)
                {
                    var (min, max) = AllowedRange(kind);
                    if (min <= max)
                        HealingAssistantManager.SetThreshold(profile, kind,
                            Math.Clamp(HealingAssistantManager.GetThreshold(profile, kind), min, max));
                    else
                        HealingAssistantManager.FitEnabledThresholds(profile);
                }
                RefreshRanges();
                status.Visible = false;
            }, method.ToggleLabel);

            var row = LabeledHorizontalSlider.SliderWithLabel(
                TazLang.Get("healingassistant_start_hp", "Start at HP (%)"),
                out LabeledHorizontalSlider slider, min: 1, max: 99, value: 100 - HealingAssistantManager.GetThreshold(profile, kind),
                valueSuffix: "%");
            sliders[(int)kind] = slider;
            slider.ValueChangedByUser += (_, _) =>
            {
                int value = 100 - (int)slider.Value;
                var (min, max) = HealingAssistantManager.IsEnabled(profile, kind) ? AllowedRange(kind) : (1, 99);
                if (min > max)
                {
                    RefreshRanges();
                    ShowRange(min, max);
                    return;
                }

                int validValue = Math.Clamp(value, min, max);
                HealingAssistantManager.SetThreshold(profile, kind, validValue);
                RefreshRanges();
                status.Visible = value != validValue;
                if (status.Visible)
                    ShowRange(min, max);
            };
            row.Widgets.Insert(0, toggle);
            var range = new MyraLabel("", MyraLabel.TextStyle.P);
            ranges[(int)kind] = range;
            var section = new VerticalStackPanel { Spacing = 2 };
            section.Widgets.Add(row);
            section.Widgets.Add(range);
            return section;
        }

        void RefreshRanges()
        {
            HealingMethod[] order = HealingAssistantManager.GetThresholdOrder(profile.HealingAssistantFirstMethod);
            int lowerHP = 0;
            for (int i = order.Length - 1; i >= 0; i--)
            {
                HealingMethod method = order[i];
                int upperHP = 100 - HealingAssistantManager.GetThreshold(profile, method);
                sliders[(int)method].Value = upperHP;
                MyraLabel range = ranges[(int)method];
                range.Visible = HealingAssistantManager.IsEnabled(profile, method);
                if (!range.Visible)
                    continue;

                range.Text = lowerHP == 0
                    ? TazLang.GetEx("healingassistant_lowest_range", "{0}% HP or lower", [upperHP.ToString()])
                    : TazLang.GetEx("healingassistant_active_range", "Above {0}% through {1}% HP", [lowerHP.ToString(), upperHP.ToString()]);
                lowerHP = upperHP;
            }
        }

        (int Min, int Max) AllowedRange(HealingMethod kind)
        {
            HealingMethod[] order = HealingAssistantManager.GetThresholdOrder(profile.HealingAssistantFirstMethod);
            int index = Array.IndexOf(order, kind);
            int min = 1;
            int max = 99;
            for (int i = 0; i < order.Length; i++)
            {
                HealingMethod other = order[i];
                if (i == index || !HealingAssistantManager.IsEnabled(profile, other))
                    continue;

                int threshold = HealingAssistantManager.GetThreshold(profile, other);
                if (i < index)
                    min = Math.Max(min, threshold + 1);
                else
                    max = Math.Min(max, threshold - 1);
            }

            return (min, max);
        }

        void ShowRange(int min, int max)
        {
            status.Text = min > max
                ? TazLang.Get("healingassistant_no_threshold_range", "Adjust the other enabled thresholds to make room for this method.")
                : TazLang.GetEx("healingassistant_hp_threshold_range", "Set this cutoff between {0}% and {1}% HP.",
                    [(100 - max).ToString(), (100 - min).ToString()]);
            status.Visible = true;
        }
    }

    private static DesktopAssistantForm.Field BuildSerialField(AgentPageInteraction interaction, string label, uint serial, Action<uint> setSerial)
    {
        const string unset = "0";
        var box = new MyraInputBox
        {
            Text = FormatSerial(),
            Tooltip = TazLang.Get("healingassistant_serial_tooltip", "Enter a decimal or 0x hexadecimal item serial. Set to 0 to clear it.")
        };
        if (interaction.IsDesktop)
            box.MinWidth = 112;
        else
            box.Width = 112;

        box.TextChangedByUser += (_, _) =>
        {
            if (StringHelper.TryParseUint(box.Text.Trim(), out uint value) && (value == 0 || SerialHelper.IsItem(value)))
                Set(value);
        };
        box.LostFocus = () => box.Text = FormatSerial();

        void Target()
        {
            GameActions.Print(TazLang.Get("healingassistant_target_stack", "Select the potion or fish stack to use."));
            interaction.SelectTarget(target =>
            {
                if (target is Item item && SerialHelper.IsItem(item.Serial))
                {
                    Set(item.Serial);
                    box.Text = FormatSerial();
                }
            });
        }
        string caption = TazLang.Get("healingassistant_select_serial", "Select item");
        var target = interaction.IsDesktop ? DesktopAssistantForm.Button(caption, Target) : new MyraButton(caption, Target);
        return new DesktopAssistantForm.Field(label, box, target);

        string FormatSerial() => serial == 0 ? unset : $"0x{serial:X8}";

        void Set(uint value)
        {
            serial = value;
            setSerial(value);
        }
    }
}
