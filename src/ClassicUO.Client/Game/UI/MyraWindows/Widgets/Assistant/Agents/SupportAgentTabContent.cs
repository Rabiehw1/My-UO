#nullable enable

using System;
using System.Globalization;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Support;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

/// <summary>
/// The Support agent page: makes the logged-in character a helper of a chosen class, and edits which abilities it
/// uses and how it follows, survives, fights and listens. Settings belong to <see cref="SupportAgentManager" />;
/// structural edits (class, leader, item picks, resets) save at once, values save when the session ends.
/// </summary>
internal static class SupportAgentTabContent
{
    #region Private members

    private const int SMALL_INPUT_WIDTH = 70;
    private const int TEXT_INPUT_WIDTH = 160;
    private const string HEX_PREFIX = "0x";
    private const int ROW_SPACING = 8;

    #endregion

    #region Public methods

    /// <summary>Builds the page.</summary>
    /// <param name="interaction">Target picking for the in-game or desktop window.</param>
    /// <returns>The page's root widget.</returns>
    public static Widget Build(AgentPageInteraction interaction)
    {
        SupportAgentManager manager = SupportAgentManager.Instance;
        SupportAgentSave settings = manager.Settings;
        var root = new VerticalStackPanel { Spacing = 6 };
        var abilitiesPanel = new VerticalStackPanel { Spacing = 4 };

        void RefreshAbilities() => FillAbilities(abilitiesPanel, settings, interaction, RefreshAbilities);

        root.Widgets.Add(new MyraLabel(TazLang.Get("supportagent_intro"), MyraLabel.TextStyle.P) { Wrap = true });
        root.Widgets.Add(BuildHelperSection(settings, manager, interaction, RefreshAbilities));
        root.Widgets.Add(BuildHealingSection(settings));
        root.Widgets.Add(new VisualContainer(
            new VisualContainerProps { LabelText = TazLang.Get("supportagent_abilities") },
            abilitiesPanel
        ));
        root.Widgets.Add(BuildFollowSection(settings));
        root.Widgets.Add(BuildCombatSection(settings));
        root.Widgets.Add(BuildCommandsSection(settings));

        RefreshAbilities();
        return root;
    }

    #endregion

    #region Private methods

    private static Widget BuildHelperSection(
        SupportAgentSave settings,
        SupportAgentManager manager,
        AgentPageInteraction interaction,
        Action refreshAbilities
    )
    {
        var enabledRow = Row();
        enabledRow.Widgets.Add(MyraCheckButton.CreateWithCallback(
            settings.Enabled,
            value =>
            {
                settings.Enabled = value;
                settings.Save();
            },
            TazLang.Get("supportagent_enable"),
            TazLang.Get("supportagent_enable_tooltip")
        ));

        var classCombo = new ComboView { MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };

        foreach (SupportClass supportClass in Enum.GetValues<SupportClass>())
            classCombo.ListView.Widgets.Add(new Label { Text = TazLang.Get("supportagent_class_" + supportClass.ToString().ToLowerInvariant()) });

        classCombo.ListView.SelectedIndex = (int)settings.Class;
        classCombo.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (classCombo.ListView.SelectedIndex is not int index)
                return;

            settings.Class = (SupportClass)index;
            settings.Save();
            refreshAbilities();
        };

        var classRow = Row();
        classRow.Widgets.Add(Label(TazLang.Get("supportagent_class")));
        classRow.Widgets.Add(classCombo);

        var leaderLabel = new MyraLabel(LeaderText(settings), MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };
        var leaderRow = Row();
        leaderRow.Widgets.Add(leaderLabel);
        leaderRow.Widgets.Add(new MyraButton(TazLang.Get("supportagent_setleader"), () =>
            interaction.SelectTarget(target =>
            {
                if (target is Mobile mobile && mobile != World.Instance.Player)
                {
                    manager.SetLeader(mobile);
                    leaderLabel.Text = LeaderText(settings);
                }
            })
        ) { Tooltip = TazLang.Get("supportagent_setleader_tooltip") });
        leaderRow.Widgets.Add(new MyraButton(TazLang.Get("supportagent_clearleader"), () =>
        {
            settings.LeaderSerial = 0;
            settings.LeaderName = "";
            settings.Save();
            leaderLabel.Text = LeaderText(settings);
        }));

        return new VisualContainer(
            new VisualContainerProps { LabelText = TazLang.Get("supportagent_helper") },
            enabledRow,
            classRow,
            leaderRow
        );
    }

    private static Widget BuildHealingSection(SupportAgentSave settings)
    {
        var values = Row();
        values.Widgets.Add(Number(TazLang.Get("supportagent_healthreshold"), settings.HealThreshold, v => settings.HealThreshold = Math.Clamp(v, 1, 100)));
        values.Widgets.Add(Number(TazLang.Get("supportagent_healrange"), settings.HealRange, v => settings.HealRange = Math.Max(1, v)));
        values.Widgets.Add(MyraCheckButton.CreateWithCallback(
            settings.HealFriends,
            v => settings.HealFriends = v,
            TazLang.Get("supportagent_healfriends"),
            TazLang.Get("supportagent_healfriends_tooltip")
        ));

        SupportEmergencySettings emergency = settings.Emergency;
        var emergencyRow = Row();
        emergencyRow.Widgets.Add(MyraCheckButton.CreateWithCallback(
            emergency.Enabled,
            v => emergency.Enabled = v,
            TazLang.Get("supportagent_emergency_enable"),
            TazLang.Get("supportagent_emergency_enable_tooltip")
        ));
        emergencyRow.Widgets.Add(Number(TazLang.Get("supportagent_emergencyhealth"), emergency.EmergencyHealth, v => emergency.EmergencyHealth = Math.Clamp(v, 1, 99)));
        emergencyRow.Widgets.Add(Number(TazLang.Get("supportagent_recoveryhealth"), emergency.RecoveryHealth, v => emergency.RecoveryHealth = Math.Clamp(v, 1, 100)));

        return new VisualContainer(
            new VisualContainerProps { LabelText = TazLang.Get("supportagent_healing") },
            values,
            emergencyRow
        );
    }

    private static Widget BuildFollowSection(SupportAgentSave settings)
    {
        SupportFollowSettings follow = settings.Follow;
        var row = Row();
        row.Widgets.Add(MyraCheckButton.CreateWithCallback(follow.Enabled, v => follow.Enabled = v, TazLang.Get("supportagent_follow_enable")));
        row.Widgets.Add(Number(TazLang.Get("supportagent_followdistance"), follow.FollowDistance, v => follow.FollowDistance = Math.Max(1, v)));
        row.Widgets.Add(Number(TazLang.Get("supportagent_maxleaderdistance"), follow.MaxLeaderDistance, v => follow.MaxLeaderDistance = Math.Max(1, v)));

        return new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("supportagent_following") }, row);
    }

    private static Widget BuildCombatSection(SupportAgentSave settings)
    {
        SupportCombatSettings combat = settings.Combat;
        var mainRow = Row();
        mainRow.Widgets.Add(MyraCheckButton.CreateWithCallback(combat.Enabled, v => combat.Enabled = v, TazLang.Get("supportagent_combat_enable")));
        mainRow.Widgets.Add(Number(TazLang.Get("supportagent_attackrange"), combat.AttackRange, v => combat.AttackRange = Math.Max(1, v)));
        mainRow.Widgets.Add(Number(TazLang.Get("supportagent_attackinterval"), combat.AttackIntervalMs, v => combat.AttackIntervalMs = Math.Max(250, v)));
        mainRow.Widgets.Add(MyraCheckButton.CreateWithCallback(combat.AttackGray, v => combat.AttackGray = v, TazLang.Get("supportagent_attackgray")));
        mainRow.Widgets.Add(MyraCheckButton.CreateWithCallback(combat.AttackRed, v => combat.AttackRed = v, TazLang.Get("supportagent_attackred")));

        var rangedRow = Row();
        rangedRow.Widgets.Add(MyraCheckButton.CreateWithCallback(
            combat.KeepDistance,
            v => combat.KeepDistance = v,
            TazLang.Get("supportagent_keepdistance"),
            TazLang.Get("supportagent_keepdistance_tooltip")
        ));
        rangedRow.Widgets.Add(Number(TazLang.Get("supportagent_meleedanger"), combat.MeleeDangerRange, v => combat.MeleeDangerRange = Math.Max(1, v)));
        rangedRow.Widgets.Add(Number(TazLang.Get("supportagent_backoffrange"), combat.PreferredMinRange, v => combat.PreferredMinRange = Math.Max(1, v)));

        return new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("supportagent_combat") }, mainRow, rangedRow);
    }

    private static Widget BuildCommandsSection(SupportAgentSave settings)
    {
        SupportCommandSettings commands = settings.Commands;
        var row = Row();
        row.Widgets.Add(MyraCheckButton.CreateWithCallback(
            commands.Enabled,
            v => commands.Enabled = v,
            TazLang.Get("supportagent_commands_enable"),
            TazLang.Get("supportagent_commands_enable_tooltip")
        ));
        row.Widgets.Add(Text(TazLang.Get("supportagent_followcommand"), commands.FollowCommand, v => commands.FollowCommand = v));
        row.Widgets.Add(Text(TazLang.Get("supportagent_healcommand"), commands.HealCommand, v => commands.HealCommand = v));
        row.Widgets.Add(Number(TazLang.Get("supportagent_followoverride"), commands.FollowOverrideMs, v => commands.FollowOverrideMs = Math.Max(0, v)));

        return new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("supportagent_commands") }, row);
    }

    /// <summary>Lists the current class's abilities with the fields their action type uses.</summary>
    private static void FillAbilities(
        VerticalStackPanel panel,
        SupportAgentSave settings,
        AgentPageInteraction interaction,
        Action refresh
    )
    {
        panel.Widgets.Clear();
        SupportClassSettings classSettings = settings.GetClassSettings(settings.Class);

        foreach (SupportAbility ability in classSettings.Abilities)
            panel.Widgets.Add(BuildAbilityRow(ability, settings, interaction, refresh));

        panel.Widgets.Add(new MyraButton(TazLang.Get("supportagent_resetabilities"), () =>
        {
            classSettings.Abilities = SupportClassPresets.Create(settings.Class);
            settings.Save();
            refresh();
        }) { Tooltip = TazLang.Get("supportagent_resetabilities_tooltip") });
    }

    private static Widget BuildAbilityRow(
        SupportAbility ability,
        SupportAgentSave settings,
        AgentPageInteraction interaction,
        Action refresh
    )
    {
        var row = Row();
        row.Widgets.Add(MyraCheckButton.CreateWithCallback(ability.Enabled, v => ability.Enabled = v, ability.Name, RoleText(ability.Role)));

        switch (ability.Action)
        {
            case SupportActionType.CastSpell:
                row.Widgets.Add(Number(TazLang.Get("supportagent_spellnumber"), ability.SpellId, v => ability.SpellId = Math.Max(0, v)));
                break;

            case SupportActionType.UseItemAndGumpButton:
                row.Widgets.Add(Hex(TazLang.Get("supportagent_itemgraphic"), ability.ItemGraphic, v => ability.ItemGraphic = v));
                row.Widgets.Add(Number(TazLang.Get("supportagent_gumpbutton"), ability.GumpButton, v => ability.GumpButton = Math.Max(0, v)));
                break;

            case SupportActionType.UseItem:
                row.Widgets.Add(Label(ability.ItemSerial != 0
                    ? TazLang.Get("supportagent_item_picked", [HEX_PREFIX + ability.ItemSerial.ToString("X8")])
                    : TazLang.Get("supportagent_item_none")));
                row.Widgets.Add(new MyraButton(TazLang.Get("supportagent_pickitem"), () =>
                    interaction.SelectTarget(target =>
                    {
                        if (target is Item item)
                        {
                            ability.ItemSerial = item.Serial;
                            ability.ItemGraphic = item.Graphic;
                            settings.Save();
                            refresh();
                        }
                    })
                ));
                break;

            case SupportActionType.Say:
                row.Widgets.Add(Text(TazLang.Get("supportagent_saytext"), ability.Text, v => ability.Text = v));
                break;
        }

        row.Widgets.Add(Number(TazLang.Get("supportagent_casttime"), ability.CastTimeMs, v => ability.CastTimeMs = Math.Max(0, v)));
        row.Widgets.Add(Number(TazLang.Get("supportagent_recovery"), ability.RecoveryMs, v => ability.RecoveryMs = Math.Max(0, v)));
        row.Widgets.Add(Number(TazLang.Get("supportagent_cooldown"), ability.CooldownMs, v => ability.CooldownMs = Math.Max(0, v)));
        return row;
    }

    private static string LeaderText(SupportAgentSave settings) =>
        settings.LeaderSerial == 0
            ? TazLang.Get("supportagent_leader_none")
            : TazLang.Get("supportagent_leader", [settings.LeaderName, HEX_PREFIX + settings.LeaderSerial.ToString("X8")]);

    private static string RoleText(SupportRole role) => TazLang.Get("supportagent_role_" + role.ToString().ToLowerInvariant());

    /// <summary>A horizontal row that wraps onto more lines when the window is narrow.</summary>
    private static WrapPanel Row() =>
        new() { Orientation = Orientation.Horizontal, HorizontalSpacing = ROW_SPACING, VerticalSpacing = ROW_SPACING / 2 };

    private static MyraLabel Label(string text) =>
        new(text, MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };

    private static Widget Number(string label, int value, Action<int> onChanged) =>
        new LabeledIntegerInput(label, value, onChanged) { InputBoxMinWidth = SMALL_INPUT_WIDTH };

    private static Widget Text(string label, string value, Action<string> onChanged)
    {
        var box = new MyraInputBox { Text = value, MinWidth = TEXT_INPUT_WIDTH };
        box.TextChangedByUser += (_, _) => onChanged(box.Text ?? "");

        var row = new HorizontalStackPanel { Spacing = 4 };
        row.Widgets.Add(Label(label));
        row.Widgets.Add(box);
        return row;
    }

    /// <summary>Hex item-graphic input ("0x1C13"); only valid values are stored.</summary>
    private static Widget Hex(string label, ushort value, Action<ushort> onChanged)
    {
        var box = new MyraInputBox { Text = HEX_PREFIX + value.ToString("X4"), MinWidth = SMALL_INPUT_WIDTH };
        box.TextChangedByUser += (_, _) =>
        {
            string text = (box.Text ?? "").Trim();

            if (text.StartsWith(HEX_PREFIX, StringComparison.OrdinalIgnoreCase))
                text = text[HEX_PREFIX.Length..];

            if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort parsed))
                onChanged(parsed);
        };

        var row = new HorizontalStackPanel { Spacing = 4 };
        row.Widgets.Add(Label(label));
        row.Widgets.Add(box);
        return row;
    }

    #endregion
}
