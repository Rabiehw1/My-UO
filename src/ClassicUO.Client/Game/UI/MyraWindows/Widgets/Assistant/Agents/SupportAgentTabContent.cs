#nullable enable

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.Managers.Support;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Utility;
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
    internal sealed class Overview(Action refresh) : VerticalStackPanel
    {
        private long _nextRefresh;

        internal void RefreshStatus()
        {
            long now = Time.Ticks;
            if (now < _nextRefresh)
                return;
            _nextRefresh = now + 250;
            refresh();
        }
    }

    #endregion

    #region Public methods

    /// <summary>Builds the page.</summary>
    /// <param name="interaction">Target picking for the in-game or desktop window.</param>
    /// <returns>The page's root widget.</returns>
    public static Widget Build(AgentPageInteraction interaction)
    {
        SupportAgentManager manager = SupportAgentManager.Instance;
        SupportAgentSave settings = manager.Settings;
        var status = Label("");
        MyraLabel? petState = null;
        var classPanel = new VerticalStackPanel { Spacing = 6 };
        var title = Row();
        var toggle = interaction.IsDesktop
            ? DesktopAssistantTheme.ActivityButton(TazLang.Get("supportagent_start"), TazLang.Get("supportagent_stop"),
                () => settings.Enabled)
            : Button(interaction, "", primary: true);
        var root = new Overview(UpdateStatus) { Spacing = 8 };
        toggle.OnClick = Toggle;
        void Toggle()
        {
            settings.Enabled = !settings.Enabled;
            settings.Save();
            UpdateStatus();
        }
        title.Widgets.Add(new MyraLabel(TazLang.Get("supportagent_title"), MyraLabel.TextStyle.H2));
        title.Widgets.Add(toggle);

        void UpdateStatus()
        {
            if (!interaction.IsDesktop)
                toggle.Text = TazLang.Get(settings.Enabled ? "supportagent_stop" : "supportagent_start");
            status.Text = manager.OverviewStatus();
            if (petState != null)
                petState.Text = PetText(manager, settings);
        }

        void RefreshClass()
        {
            bool canUseAbilities = settings.Class != SupportClass.Ranger;
            classPanel.Widgets.Clear();
            petState = null;
            var actions = new VerticalStackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
            if (canUseAbilities)
            {
                actions.Widgets.Add(BuildHealingSection(settings));
                var abilitiesPanel = new VerticalStackPanel { Spacing = 4 };
                void RefreshAbilities() => FillAbilities(abilitiesPanel, settings, interaction, RefreshAbilities);
                RefreshAbilities();
                actions.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("supportagent_abilities") }, abilitiesPanel));
            }
            else
            {
                petState = Label("");
                actions.Widgets.Add(BuildPetSection(manager, settings, interaction, petState, UpdateStatus));
            }

            if (SupportClassPresets.CanAttack(settings.Class))
                actions.Widgets.Add(BuildCombatSection(settings.ActiveCombat));

            var positioning = new VerticalStackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
            positioning.Widgets.Add(BuildFollowSection(settings.ActiveFollow));
            positioning.Widgets.Add(new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("supportagent_survival") },
                BuildEmergencyRow(settings.Emergency, settings.Class)));
            positioning.Widgets.Add(new Expander(Label(TazLang.Get("supportagent_setup_commands")),
                MyraCheckButton.CreateWithCallback(settings.ShowStatusOverhead, value => settings.ShowStatusOverhead = value,
                    TazLang.Get("supportagent_showstatus"), TazLang.Get("supportagent_showstatus_tooltip")),
                BuildCommandsSection(settings, canUseAbilities)) { IsExpanded = false, IndentWidth = 0 });
            classPanel.Widgets.Add(new DesktopAssistantForm.SectionPair(actions, positioning));
            UpdateStatus();
        }

        root.Widgets.Add(title);
        root.Widgets.Add(BuildHelperSection(settings, manager, interaction, status, RefreshClass, UpdateStatus));
        root.Widgets.Add(classPanel);

        RefreshClass();
        return root;
    }

    #endregion

    #region Private methods

    private static Widget BuildHelperSection(
        SupportAgentSave settings,
        SupportAgentManager manager,
        AgentPageInteraction interaction,
        Widget status,
        Action refreshClass,
        Action refreshStatus
    )
    {
        var classCombo = new ComboView { MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };

        foreach (SupportClass supportClass in Enum.GetValues<SupportClass>())
            classCombo.ListView.Widgets.Add(new Label { Text = TazLang.Get("supportagent_class_" + supportClass.ToString().ToLowerInvariant()) });

        classCombo.ListView.SelectedIndex = (int)settings.Class;
        classCombo.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (classCombo.ListView.SelectedIndex is not int index)
                return;

            manager.SetClass((SupportClass)index);
            refreshClass();
            settings.Save();
        };

        var leaderLabel = new MyraLabel(LeaderText(settings), MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };
        var setup = Row();
        setup.Widgets.Add(Label(TazLang.Get("supportagent_class")));
        setup.Widgets.Add(classCombo);
        setup.Widgets.Add(leaderLabel);
        var setLeader = Button(interaction, TazLang.Get("supportagent_setleader"), () =>
            interaction.SelectTarget(target =>
            {
                if (target is Mobile mobile && mobile != World.Instance.Player)
                {
                    manager.SetLeader(mobile);
                    leaderLabel.Text = LeaderText(settings);
                    refreshStatus();
                }
            }));
        setLeader.Tooltip = TazLang.Get("supportagent_setleader_tooltip");
        setup.Widgets.Add(setLeader);
        setup.Widgets.Add(Button(interaction, TazLang.Get("supportagent_clearleader"), () =>
        {
            settings.LeaderSerial = 0;
            settings.LeaderName = "";
            settings.Save();
            leaderLabel.Text = LeaderText(settings);
            refreshStatus();
        }));
        setup.Widgets.Add(status);
        return new VisualContainer(new VisualContainerProps(), setup);
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

        return new VisualContainer(
            new VisualContainerProps { LabelText = TazLang.Get("supportagent_healing") },
            values
        );
    }

    private static Widget BuildPetSection(SupportAgentManager manager, SupportAgentSave settings,
        AgentPageInteraction interaction, Widget status, Action refresh)
    {
        var choose = Button(interaction, TazLang.Get("supportagent_pet_choose"), () => interaction.SelectTarget(target =>
        {
            if (target is Mobile pet && manager.SetRangerPet(pet))
                refresh();
            else
                GameActions.Print(TazLang.Get("supportagent_pet_invalid"));
        }));
        var selection = Row();
        selection.Widgets.Add(status);
        selection.Widgets.Add(choose);
        return new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("supportagent_pet_commands") },
            MyraCheckButton.CreateWithCallback(settings.RangerPetEnabled, value =>
            {
                settings.RangerPetEnabled = value;
                settings.Save();
                refresh();
            }, TazLang.Get("supportagent_pet_enable"), TazLang.Get("supportagent_pet_tooltip")), selection);
    }

    private static string PetText(SupportAgentManager manager, SupportAgentSave settings)
    {
        if (manager.RangerPet is { } pet)
            return pet.HitsMax > 0
                ? TazLang.Get("supportagent_pet_health", [pet.Name, (pet.Hits * 100 / pet.HitsMax).ToString()])
                : pet.Name;
        return settings.RangerPetSerial == 0 ? TazLang.Get("supportagent_pet_none")
            : TazLang.Get("supportagent_pet_away", [settings.RangerPetName]);
    }

    private static Widget BuildEmergencyRow(SupportEmergencySettings emergency, SupportClass supportClass)
    {
        string tooltipKey = supportClass switch
        {
            SupportClass.Bard => "supportagent_bard_emergency_tooltip",
            SupportClass.Ranger => "supportagent_ranger_emergency_tooltip",
            _ => "supportagent_emergency_enable_tooltip"
        };
        var emergencyRow = Row();
        emergencyRow.Widgets.Add(MyraCheckButton.CreateWithCallback(
            emergency.Enabled,
            v => emergency.Enabled = v,
            TazLang.Get("supportagent_emergency_enable"),
            TazLang.Get(tooltipKey)
        ));
        emergencyRow.Widgets.Add(Number(TazLang.Get("supportagent_emergencyhealth"), emergency.EmergencyHealth, v => emergency.EmergencyHealth = Math.Clamp(v, 1, 99)));
        emergencyRow.Widgets.Add(Number(TazLang.Get("supportagent_recoveryhealth"), emergency.RecoveryHealth, v => emergency.RecoveryHealth = Math.Clamp(v, 1, 100)));

        return emergencyRow;
    }

    private static Widget BuildFollowSection(SupportFollowSettings follow)
    {
        var row = Row();
        row.Widgets.Add(MyraCheckButton.CreateWithCallback(follow.Enabled, v => follow.Enabled = v, TazLang.Get("supportagent_follow_enable")));
        row.Widgets.Add(Number(TazLang.Get("supportagent_followdistance"), follow.FollowDistance, v => follow.FollowDistance = Math.Max(1, v)));
        row.Widgets.Add(Number(TazLang.Get("supportagent_maxleaderdistance"), follow.MaxLeaderDistance, v => follow.MaxLeaderDistance = Math.Max(1, v)));

        return new VisualContainer(new VisualContainerProps { LabelText = TazLang.Get("supportagent_following") }, row);
    }

    private static Widget BuildCombatSection(SupportCombatSettings combat)
    {
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

    private static Widget BuildCommandsSection(SupportAgentSave settings, bool canUseAbilities)
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
        if (canUseAbilities)
            row.Widgets.Add(Text(TazLang.Get("supportagent_healcommand"), commands.HealCommand, v => commands.HealCommand = v));
        if (SupportClassPresets.CanUseRole(settings.Class, SupportRole.Bless))
            row.Widgets.Add(Text(TazLang.Get("supportagent_blesscommand"), commands.BlessCommand, v => commands.BlessCommand = v));
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
        {
            if (!SupportClassPresets.CanUseRole(settings.Class, ability.Role))
                continue;

            var row = Row();
            string tooltip = settings.Class == SupportClass.Bard
                ? TazLang.Get("supportagent_bard_songbook_tip") : RoleText(ability.Role);
            row.Widgets.Add(MyraCheckButton.CreateWithCallback(ability.Enabled, v => ability.Enabled = v, ability.DisplayName, tooltip));
            if (settings.Class != SupportClass.Bard)
                row.Widgets.Add(BuildAbilitySource(ability, settings, interaction, refresh));
            panel.Widgets.Add(row);

            var timing = Row();
            timing.Widgets.Add(Number(TazLang.Get("supportagent_casttime"), ability.CastTimeMs, v => ability.CastTimeMs = Math.Max(0, v)));
            timing.Widgets.Add(Number(TazLang.Get("supportagent_recovery"), ability.RecoveryMs, v => ability.RecoveryMs = Math.Max(0, v)));
            timing.Widgets.Add(Number(TazLang.Get("supportagent_cooldown"), ability.CooldownMs, v => ability.CooldownMs = Math.Max(0, v)));
            panel.Widgets.Add(new Expander(Label(TazLang.Get("supportagent_ability_timing")), timing) { IsExpanded = false, IndentWidth = 0 });
        }

        var reset = Button(interaction, TazLang.Get("supportagent_resetabilities"), () =>
        {
            classSettings.Abilities = SupportClassPresets.Create(settings.Class);
            settings.Save();
            refresh();
        });
        reset.Tooltip = TazLang.Get("supportagent_resetabilities_tooltip");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Widgets.Add(reset);
    }

    /// <summary>The field saying what the ability uses: a spell number, a book spell, a picked item or text.</summary>
    private static Widget BuildAbilitySource(
        SupportAbility ability,
        SupportAgentSave settings,
        AgentPageInteraction interaction,
        Action refresh
    )
    {
        var cell = new HorizontalStackPanel { Spacing = ROW_SPACING };

        switch (ability.Action)
        {
            case SupportActionType.CastSpell:
                cell.Widgets.Add(Number(TazLang.Get("supportagent_spellnumber"), ability.SpellId, v => ability.SpellId = Math.Max(0, v)));
                break;

            case SupportActionType.UseItemAndGumpButton:
                AddItemPicker(cell, ability, settings, interaction, refresh);
                cell.Widgets.Add(Number(TazLang.Get("supportagent_gumpbutton"), ability.GumpButton, v => ability.GumpButton = Math.Max(0, v)));
                break;

            case SupportActionType.UseItem:
                AddItemPicker(cell, ability, settings, interaction, refresh);
                break;

            case SupportActionType.CastShardSpell:
                ShardSpellBookInfo book = ShardSpellCatalog.Get(ability.ShardBook);
                var spells = new ComboView { MinWidth = 125, VerticalAlignment = VerticalAlignment.Center };
                foreach (ShardSpell spell in book.Spells)
                    spells.ListView.Widgets.Add(new Label { Text = spell.Name });
                if (ShardSpellCatalog.GetSpell(ability.ShardBook, ability.SpellId) != null)
                    spells.ListView.SelectedIndex = ability.SpellId;
                spells.ListView.SelectedIndexChanged += (_, _) =>
                {
                    if (spells.ListView.SelectedIndex is int index)
                        ability.SpellId = index;
                };
                cell.Widgets.Add(spells);
                cell.Widgets.Add(Label(book.Name));
                break;

            case SupportActionType.Say:
                cell.Widgets.Add(Text(TazLang.Get("supportagent_saytext"), ability.Text, v => ability.Text = v));
                break;
        }

        return cell;
    }

    private static MyraButton Button(AgentPageInteraction interaction, string label, Action? action = null, bool primary = false) =>
        interaction.IsDesktop ? DesktopAssistantTheme.ActionButton(label, action, primary) : new MyraButton(label, action);

    /// <summary>Shows the picked item and a button to pick it; item abilities only ever use this exact item.</summary>
    private static void AddItemPicker(
        HorizontalStackPanel row,
        SupportAbility ability,
        SupportAgentSave settings,
        AgentPageInteraction interaction,
        Action refresh
    )
    {
        row.Widgets.Add(Label(ability.ItemSerial != 0
            ? TazLang.Get("supportagent_item_picked", [HEX_PREFIX + ability.ItemSerial.ToString("X8")])
            : TazLang.Get("supportagent_item_none")));

        var pick = Button(interaction, TazLang.Get("supportagent_pickitem"), () =>
            interaction.SelectTarget(target =>
            {
                if (target is Item item)
                {
                    ability.ItemSerial = item.Serial;
                    ability.ItemGraphic = item.Graphic;
                    settings.Save();
                    refresh();
                }
            }));
        pick.Tooltip = TazLang.Get("supportagent_pickitem_tooltip");
        row.Widgets.Add(pick);
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

    #endregion
}
