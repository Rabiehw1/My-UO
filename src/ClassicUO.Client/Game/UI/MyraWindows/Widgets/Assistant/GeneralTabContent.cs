using System;
using System.Collections.Generic;
using ClassicUO.Common.Enums;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Processes;
using ClassicUO.Game.UI.MyraWindows.Options.Tabs;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

public static class GeneralTabContent
{
    private const int RECONNECT_INDENT = 18;

    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        Profile profile = ProfileManager.CurrentProfile;
        float gameScale = Client.Game.RenderScale;

        Widget[] visualToggles =
        [
            MyraCheckButton.CreateWithCallback(profile.HighlightGameObjects,
                b => profile.HighlightGameObjects = b, TazLang.Get("assistant_highlightgameobjects")),
            MyraCheckButton.CreateWithCallback(profile.NameOverheadToggled,
                b => profile.NameOverheadToggled = b, TazLang.Get("assistant_shownameplates")),
            MyraCheckButton.CreateWithCallback(profile.EnablePetScaling, b =>
            {
                profile.EnablePetScaling = b;
                Dictionary<uint, Mobile>.ValueCollection mobs = World.Instance.Mobiles.Values;
                foreach (Mobile mob in mobs)
                    if (mob != null && mob.IsRenamable)
                        mob.Scale = b ? 0.6f : 1f;
            }, TazLang.Get("assistant_petscaling"), TazLang.Get("assistant_petscaling_tooltip")),
            MyraCheckButton.CreateWithCallback(profile.OutlineMobilesNotoriety,
                b => profile.OutlineMobilesNotoriety = b, TazLang.Get("assistant_outlinemobiles"))
        ];
        var dragDistance = LabeledHorizontalSlider.CreateSliderWithCallback(0, 20, profile.MinGumpMoveDistance,
            v => profile.MinGumpMoveDistance = (int)v);
        var scale = LabeledHorizontalSlider.CreateSliderWithCallback(Constants.MIN_GAME_SCALE * 100, Constants.MAX_GAME_SCALE * 100,
            gameScale * 100, v => gameScale = Math.Clamp(v / 100, Constants.MIN_GAME_SCALE, Constants.MAX_GAME_SCALE), valueSuffix: "%");
        scale.Tooltip = TazLang.Get("assistant_gamescale_tooltip");
        var applyScale = interaction.IsDesktop
            ? DesktopAssistantTheme.ActionButton(TazLang.Get("assistant_apply_scale"), ApplyScale, primary: true)
            : new MyraButton(TazLang.Get("assistant_apply_scale"), ApplyScale);

        var objectDelay = LabeledHorizontalSlider.CreateSliderWithCallback(0, 3000, profile.MoveMultiObjectDelay,
            v => profile.MoveMultiObjectDelay = (int)v, valueSuffix: " ms");
        var checkDelay = new MyraButton(TazLang.Get("assistant_autodelaychecker"),
            () => interaction.RunInGame(() => AutomatedObjectDelay.Begin(() => objectDelay.Value = profile.MoveMultiObjectDelay)))
        { Tooltip = TazLang.Get("assistant_autodelaychecker_tooltip") };

        Widget[] gameplayToggles =
        [
            MyraCheckButton.CreateWithCallback(profile.QueueManualItemMoves, b => profile.QueueManualItemMoves = b,
                TazLang.Get("assistant_queueitemmoves"), TazLang.Get("assistant_queueitemmoves_tooltip")),
            MyraCheckButton.CreateWithCallback(profile.QueueManualItemUses, b => profile.QueueManualItemUses = b,
                TazLang.Get("assistant_queueobjectuses"), TazLang.Get("assistant_queueobjectuses_tooltip")),
            MyraCheckButton.CreateWithCallback(profile.AutoOpenOwnCorpse, b => profile.AutoOpenOwnCorpse = b,
                TazLang.Get("assistant_autoopenowncorpse"), TazLang.Get("assistant_autoopenowncorpse_tooltip")),
            MyraCheckButton.CreateWithCallback(profile.AutoUnequipForSpellCasting, b => profile.AutoUnequipForSpellCasting = b,
                TazLang.Get("assistant_autounequipforspellcasting"), TazLang.Get("assistant_autounequipforspellcasting_tooltip")),
            MyraCheckButton.CreateWithCallback(profile.AutoUnequipForPotions, b => profile.AutoUnequipForPotions = b,
                TazLang.Get("assistant_autounequipforpotions"), TazLang.Get("assistant_autounequipforpotions_tooltip")),
            MyraCheckButton.CreateWithCallback(profile.DisableWeather, b =>
            {
                profile.DisableWeather = b;
                if (b)
                    World.Instance?.Weather.Reset();
            }, TazLang.Get("assistant_disableweather"), TazLang.Get("assistant_disableweather_tooltip"))
        ];
        var quickHeal = BuildQuickActionCombo(profile.QuickHealAction, a => profile.QuickHealAction = a);
        var quickCure = BuildQuickActionCombo(profile.QuickCureAction, a => profile.QuickCureAction = a);
        var setLastTarget = MyraCheckButton.CreateWithCallback(profile.SingleClickMobileSetsLastTarget,
            b => profile.SingleClickMobileSetsLastTarget = b, TazLang.Get("assistant_singleclicklasttarg"));

        string dragDistanceLabel = TazLang.Get("assistant_mingumpdragdist");
        string scaleLabel = TazLang.Get("assistant_gamescale");
        string delayLabel = TazLang.Get("assistant_objectdelay");
        string healLabel = TazLang.Get("assistant_quickhealspell");
        string cureLabel = TazLang.Get("assistant_quickcurespell");

        if (interaction.IsDesktop)
        {
            var desktop = new VerticalStackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
            var map = DesktopAssistantTheme.Group(TazLang.Get("world_map"),
                DesktopAssistantTheme.ActionButton(TazLang.Get("assistant_open_detached_map"), DetachedWorldMapWindow.Show, primary: true));
            Widget[] reconnect = BuildReconnectRows();
            var startup = DesktopAssistantTheme.Group(TazLang.Get("assistant_loginstartup"),
                new MyraLabel(TazLang.Get("assistant_autorunscript"), MyraLabel.TextStyle.P),
                ScriptExecutorTab.BuildAutorunPicker(),
                OptionTabCommons.StyledHorizontalSeparator(),
                reconnect[0],
                reconnect[1]);
            desktop.Widgets.Add(new DesktopAssistantForm.SectionPair(startup, map));
            var visuals = DesktopAssistantTheme.Group(TazLang.Get("assistant_desktop_visuals"),
                [.. visualToggles, DesktopAssistantForm.Fields(new(dragDistanceLabel, dragDistance), new(scaleLabel, scale)), applyScale]);
            var timing = DesktopAssistantTheme.Group(TazLang.Get("assistant_desktop_object_timing"),
                DesktopAssistantForm.Fields(new DesktopAssistantForm.Field(delayLabel, objectDelay)), checkDelay);
            desktop.Widgets.Add(new DesktopAssistantForm.SectionPair(visuals, timing));
            desktop.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("assistant_desktop_game_interaction"),
                [.. gameplayToggles, DesktopAssistantForm.Fields(new(healLabel, quickHeal), new(cureLabel, quickCure)), setLastTarget]));
            return desktop;
        }

        var game = new HorizontalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        var left = new VerticalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        var right = new VerticalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        game.Widgets.Add(left);
        game.Widgets.Add(right);
        left.Widgets.Add(new MyraLabel(TazLang.Get("assistant_visualconfig"), MyraLabel.TextStyle.H2));
        foreach (Widget toggle in visualToggles)
            left.Widgets.Add(toggle);
        left.Widgets.Add(GameSliderRow(dragDistanceLabel, dragDistance));
        left.Widgets.Add(GameSliderRow(scaleLabel, scale));
        left.Widgets.Add(applyScale);
        right.Widgets.Add(new MyraLabel(TazLang.Get("assistant_delayconfig"), MyraLabel.TextStyle.H2));
        right.Widgets.Add(GameSliderRow(delayLabel, objectDelay));
        right.Widgets.Add(checkDelay);
        right.Widgets.Add(new MyraSpacer(20, 15));
        right.Widgets.Add(new MyraLabel(TazLang.Get("assistant_misc"), MyraLabel.TextStyle.H2));
        foreach (Widget toggle in gameplayToggles)
            right.Widgets.Add(toggle);
        right.Widgets.Add(new MyraLabel(healLabel, MyraLabel.TextStyle.P).PlaceBefore(quickHeal));
        right.Widgets.Add(new MyraLabel(cureLabel, MyraLabel.TextStyle.P).PlaceBefore(quickCure));
        right.Widgets.Add(setLastTarget);
        right.Widgets.Add(new MyraSpacer(20, 15));
        right.Widgets.Add(new MyraLabel(TazLang.Get("assistant_loginstartup"), MyraLabel.TextStyle.H2));
        foreach (Widget row in BuildReconnectRows())
            right.Widgets.Add(row);
        return game;

        void ApplyScale()
        {
            Client.Game.SetScale(gameScale);
            ProfileManager.GlobalSettings.GlobalScale = gameScale;
        }
    }

    /// <summary>
    /// Reconnect toggle and delay. Machine-wide login settings (same as the login screen's), saved immediately.
    /// </summary>
    private static Widget[] BuildReconnectRows()
    {
        Settings settings = Settings.GlobalSettings;

        Widget toggle = MyraCheckButton.CreateWithCallback(settings.Reconnect, value =>
        {
            settings.Reconnect = value;
            settings.Save();
        }, TazLang.Get("assistant_reconnect"), TazLang.Get("autoreconnecttooltip"));

        var delay = new HorizontalStackPanel { Spacing = 4, Margin = new Thickness(RECONNECT_INDENT, 0, 0, 0) };
        delay.Widgets.Add(new LabeledIntegerInput(TazLang.Get("assistant_reconnectwait"), settings.ReconnectTime, value =>
        {
            settings.ReconnectTime = value;
            settings.Save();
        })
        {
            MinValue = Settings.MinReconnectSeconds,
            MaxValue = Settings.MaxReconnectSeconds,
            Tooltip = TazLang.Get("reconnecttooltip")
        });
        delay.Widgets.Add(new MyraLabel(TazLang.Get("assistant_seconds"), MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center });

        return [toggle, delay];
    }

    private static Widget GameSliderRow(string label, LabeledHorizontalSlider slider)
    {
        var row = new HorizontalStackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = MyraStyle.STANDARD_SPACING };
        row.Widgets.Add(slider);
        row.Widgets.Add(new MyraLabel(label, MyraLabel.TextStyle.P));
        return row;
    }

    private static ComboView BuildQuickActionCombo(HealthBarQuickAction current, Action<HealthBarQuickAction> onChanged)
    {
        var combo = new ComboView
        {
            MinWidth = 200,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = TazLang.Get("assistant_quickspelltooltip")
        };

        HealthBarQuickAction[] actions = Enum.GetValues<HealthBarQuickAction>();
        foreach (HealthBarQuickAction action in actions)
            combo.ListView.Widgets.Add(new Label { Text = action.GetDisplayName() });

        int selected = Array.IndexOf(actions, current);
        if (selected >= 0)
            combo.ListView.SelectedIndex = selected;

        combo.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (combo.ListView.SelectedIndex is int index)
                onChanged(actions[index]);
        };
        return combo;
    }
}
