#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal static class HealingAssistantTabContent
{
    private sealed record MethodSetting(string Name, string ToggleLabel, Func<bool> IsEnabled, Action<bool> SetEnabled);

    public static Widget Build(AgentPageInteraction interaction)
    {
        Profile? profile = ProfileManager.CurrentProfile;
        if (profile == null)
            return new MyraLabel(TazLang.Get("bandageagent_profilenotloaded"), MyraLabel.TextStyle.P);

        MethodSetting[] methods =
        [
            new(TazLang.Get("healingassistant_bandages", "Bandages"), TazLang.Get("healingassistant_use_bandages", "Use bandages"),
                () => profile.EnableBandageAgent, value => profile.EnableBandageAgent = value),
            new(TazLang.Get("healingassistant_greater", "Greater Heal"), TazLang.Get("healingassistant_use_greater", "Use Greater Heal potions"),
                () => profile.HealingAssistantUseGreaterHeal, value => profile.HealingAssistantUseGreaterHeal = value),
            new(TazLang.Get("healingassistant_tamala", "Tamala Heal"), TazLang.Get("healingassistant_use_tamala", "Use Tamala Heal potions"),
                () => profile.HealingAssistantUseTamalaHeal, value => profile.HealingAssistantUseTamalaHeal = value),
            new(TazLang.Get("healingassistant_fish", "Magic fish"), TazLang.Get("healingassistant_use_fish", "Use magic healing fish"),
                () => profile.HealingAssistantUseMagicFish, value => profile.HealingAssistantUseMagicFish = value)
        ];

        var root = new VerticalStackPanel { Spacing = 8 };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var sliders = new LabeledHorizontalSlider[methods.Length];

        root.Widgets.Add(MyraCheckButton.CreateWithCallback(
            profile.EnableHealingAssistant,
            value => profile.EnableHealingAssistant = value,
            TazLang.Get("healingassistant_enable", "Enable Healing Assistant")));
        var firstMethod = new ComboView
        {
            MinWidth = 160,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = TazLang.Get("healingassistant_first_tooltip",
                "Assigns the earliest threshold to this method. If more healing is needed afterward, a ready backup can go next.")
        };
        foreach (MethodSetting method in methods)
            firstMethod.ListView.Widgets.Add(new Label { Text = method.Name });
        firstMethod.ListView.SelectedIndex = (int)profile.HealingAssistantFirstMethod;
        root.Widgets.Add(new MyraLabel(TazLang.Get("healingassistant_first", "First choice"), MyraLabel.TextStyle.P).PlaceBefore(firstMethod));
        root.Widgets.Add(status);

        root.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("healingassistant_bandages", "Bandages") },
            BuildThresholdRow(HealingMethod.Bandages), BandageAgentTabContent.BuildOptions(profile)));

        root.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("healingassistant_potions", "Healing potions") },
            BuildThresholdRow(HealingMethod.GreaterHeal),
            BuildSerialRow(interaction, TazLang.Get("healingassistant_greater_serial", "Greater Heal stack serial"),
                profile.HealingAssistantGreaterHealSerial, value => profile.HealingAssistantGreaterHealSerial = value),
            BuildThresholdRow(HealingMethod.TamalaHeal),
            BuildSerialRow(interaction, TazLang.Get("healingassistant_full_tamala_serial", "Full Tamala serial"),
                profile.HealingAssistantFullTamalaSerial, value => profile.HealingAssistantFullTamalaSerial = value),
            BuildSerialRow(interaction, TazLang.Get("healingassistant_half_tamala_serial", "Half-full Tamala serial"),
                profile.HealingAssistantHalfTamalaSerial, value => profile.HealingAssistantHalfTamalaSerial = value),
            MyraInputBox.NumberWithLabel(TazLang.Get("healingassistant_potion_cooldown", "Shared potion cooldown (ms)"),
                profile.HealingAssistantPotionCooldownMs, HealingAssistantManager.MinimumCooldownMs, int.MaxValue,
                value => profile.HealingAssistantPotionCooldownMs = value)));

        root.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("healingassistant_fish", "Magic fish") },
            BuildThresholdRow(HealingMethod.MagicFish),
            BuildSerialRow(interaction, TazLang.Get("healingassistant_fish_serial", "Magic fish stack serial"),
                profile.HealingAssistantMagicFishSerial, value => profile.HealingAssistantMagicFishSerial = value),
            MyraInputBox.NumberWithLabel(TazLang.Get("healingassistant_fish_cooldown", "Fish cooldown (ms)"),
                profile.HealingAssistantFishCooldownMs, HealingAssistantManager.MinimumCooldownMs, int.MaxValue,
                value => profile.HealingAssistantFishCooldownMs = value)));

        firstMethod.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (firstMethod.ListView.SelectedIndex is not int index)
                return;

            HealingAssistantManager.ChangeFirstMethod(profile, (HealingMethod)index);
            for (int i = 0; i < sliders.Length; i++)
                sliders[i].Value = HealingAssistantManager.GetThreshold(profile, (HealingMethod)i);
            status.Visible = false;
        };

        // The desktop already has a scrollable page. The in-game tab needs its own viewport.
        return ReferenceEquals(interaction, AgentPageInteraction.InGame)
            ? new ScrollViewer { Content = root, MaxHeight = Math.Max(180, ScaleHelper.LogicalWindowHeight - 140) }
            : root;

        Widget BuildThresholdRow(HealingMethod kind)
        {
            MethodSetting method = methods[(int)kind];
            var toggle = MyraCheckButton.CreateWithCallback(method.IsEnabled(), value =>
            {
                method.SetEnabled(value);
                status.Visible = false;
            }, method.ToggleLabel);

            toggle.PressedChangingByUser += (_, args) =>
            {
                if (!args.NewValue)
                    return;

                var (min, max) = AllowedRange(kind);
                int threshold = HealingAssistantManager.GetThreshold(profile, kind);
                if (threshold >= min && threshold <= max)
                    return;

                args.Cancel = true;
                ShowRange(min, max);
            };

            var row = LabeledHorizontalSlider.SliderWithLabel(
                TazLang.Get("healingassistant_missing_hp", "Missing HP (%)"),
                out LabeledHorizontalSlider slider, min: 1, max: 99, value: HealingAssistantManager.GetThreshold(profile, kind));
            sliders[(int)kind] = slider;
            slider.ValueChangedByUser += (_, _) =>
            {
                int value = (int)slider.Value;
                var (min, max) = method.IsEnabled() ? AllowedRange(kind) : (1, 99);
                if (min > max)
                {
                    slider.Value = HealingAssistantManager.GetThreshold(profile, kind);
                    ShowRange(min, max);
                    return;
                }

                int validValue = Math.Clamp(value, min, max);
                HealingAssistantManager.SetThreshold(profile, kind, validValue);
                slider.Value = validValue;
                status.Visible = value != validValue;
                if (status.Visible)
                    ShowRange(min, max);
            };
            row.Widgets.Insert(0, toggle);
            return row;
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
                if (i == index || !methods[(int)other].IsEnabled())
                    continue;

                int threshold = Math.Clamp(HealingAssistantManager.GetThreshold(profile, other), 1, 99);
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
                : TazLang.GetEx("healingassistant_threshold_range", "Set this threshold between {0}% and {1}% missing HP.",
                    [min.ToString(), max.ToString()]);
            status.Visible = true;
        }
    }

    private static Widget BuildSerialRow(AgentPageInteraction interaction, string label, uint serial, Action<uint> setSerial)
    {
        const string unset = "0";
        var box = new MyraInputBox
        {
            Text = FormatSerial(),
            Width = 112,
            Tooltip = TazLang.Get("healingassistant_serial_tooltip", "Enter a decimal or 0x hexadecimal item serial. Set to 0 to clear it.")
        };

        box.TextChangedByUser += (_, _) =>
        {
            if (StringHelper.TryParseUint(box.Text.Trim(), out uint value) && (value == 0 || SerialHelper.IsItem(value)))
                Set(value);
        };
        box.LostFocus = () => box.Text = FormatSerial();

        var row = new HorizontalStackPanel { Spacing = 5 };
        row.Widgets.Add(new MyraLabel(label, MyraLabel.TextStyle.P));
        row.Widgets.Add(box);
        row.Widgets.Add(new MyraButton(TazLang.Get("healingassistant_select_serial", "Select item"), () =>
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
        }));
        return row;

        string FormatSerial() => serial == 0 ? unset : $"0x{serial:X8}";

        void Set(uint value)
        {
            serial = value;
            setSerial(value);
        }
    }
}
