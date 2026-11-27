#nullable enable

using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Trainer;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Trainer;

/// <summary>The Trainer page: tick skills, grouped by class, and train them in turn.</summary>
internal static class TrainerTabContent
{
    #region Public accessors

    /// <summary>Page and tab title.</summary>
    public static string Title => TazLang.Get("trainer_title");

    #endregion

    #region Public methods

    /// <summary>Builds the page for the current character.</summary>
    /// <param name="interaction">Page services, used to pick targets.</param>
    /// <returns>The page.</returns>
    public static Widget Build(AgentPageInteraction interaction)
    {
        if (ProfileManager.CurrentProfile is not { } profile)
            return Text("trainer_connect");

        var root = new MyraGrid { RowSpacing = 8 };
        root.AddColumn(new Proportion(ProportionType.Fill));
        root.AddRow();
        root.AddRow(new Proportion(ProportionType.Fill));
        var controlsHost = new VerticalStackPanel();
        void RefreshControls()
        {
            controlsHost.Widgets.Clear();
            controlsHost.Widgets.Add(BuildControls(profile));
        }

        root.AddWidget(controlsHost, 0, 0);
        RefreshControls();
        var details = new VerticalStackPanel { Spacing = 8 };
        var order = Text("trainer_order_empty");
        var picker = new ListView();
        var classes = System.Enum.GetValues<TrainerClass>();
        foreach (TrainerClass group in classes)
        {
            var row = new MyraGrid { ColumnSpacing = 6, MinHeight = 28 };
            row.AddColumn(new Proportion(ProportionType.Pixels, 22));
            row.AddColumn(new Proportion(ProportionType.Fill));
            row.AddColumn();
            row.AddWidget(new MyraArtTexture(ClassGraphic(group), maxSize: 20)
                { VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            row.AddWidget(MyraGrid.TableText(ClassName(group)), 0, 1);
            row.AddWidget(MyraGrid.TableText(TrainerCatalog.Skills.Count(s => s.Class == group).ToString()), 0, 2);
            picker.Widgets.Add(row);
        }

        void RefreshOrder()
        {
            var names = profile.Trainer.Selected.Select(TrainerCatalog.Find).OfType<TrainerSkill>()
                .Select((skill, i) => $"{i + 1}. {skill.Name}");
            order.Text = profile.Trainer.Selected.Count == 0 ? TazLang.Get("trainer_order_empty") : string.Join("     ", names);
        }

        void ShowClass()
        {
            if (picker.SelectedIndex is not int index)
                return;
            _selectedClass = classes[index];
            details.Widgets.Clear();
            var table = new MyraGrid();
            table.SetupWithHeaders(GridColumnInfo.Auto(TazLang.Get("trainer_column_skill")),
                GridColumnInfo.Fill(TazLang.Get("trainer_column_use")), GridColumnInfo.Pixels(TazLang.Get("trainer_column_setup"), 172));
            int row = 1;
            foreach (TrainerSkill skill in TrainerCatalog.Skills.Where(s => s.Class == _selectedClass))
                AddSkillRow(table, row++, profile, skill, interaction, () => { RefreshOrder(); RefreshControls(); }, ShowClass);
            details.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("trainer_class_skills", [ClassName(_selectedClass)]), table));
            order = Text("trainer_order_empty");
            RefreshOrder();
            details.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("trainer_order"), order, Text("trainer_order_help")));
        }

        var layout = new DesktopAssistantForm.FixedSidebarLayout(details, DesktopAssistantForm.ListSidebarWidth) { ColumnSpacing = 10 };
        layout.AddColumn(new Proportion(ProportionType.Pixels, DesktopAssistantForm.ListSidebarWidth));
        layout.AddColumn(new Proportion(ProportionType.Fill));
        layout.AddRow(new Proportion(ProportionType.Fill));
        layout.AddWidget(DesktopAssistantForm.ListSidebar(TazLang.Get("trainer_classes"), picker, []), 0, 0);
        layout.AddWidget(new ScrollViewer { Content = details, ShowHorizontalScrollBar = false }, 0, 1);
        root.AddWidget(layout, 1, 0);
        picker.SelectedIndexChanged += (_, _) => ShowClass();
        picker.SelectedIndex = System.Array.IndexOf(classes, _selectedClass);
        return root;
    }

    #endregion

    #region Private methods

    private static TrainerClass _selectedClass;
    private static string ClassName(TrainerClass group) => TazLang.Get("trainer_class_" + group.ToString().ToLowerInvariant());

    // Native item art: sword, wand, lute, bow, key, hammer and sextant.
    private static uint ClassGraphic(TrainerClass group) => group switch
    {
        TrainerClass.Warrior => 0xF61,
        TrainerClass.Mage => 0xDF2,
        TrainerClass.Bard => 0xEB3,
        TrainerClass.Ranger => 0x13B2,
        TrainerClass.Thief => 0x100E,
        TrainerClass.Crafter => 0x13E3,
        _ => 0x1057
    };

    /// <summary>Start/stop, the shared wait and what is running.</summary>
    private static Widget BuildControls(Profile profile)
    {
        TrainerManager trainer = TrainerManager.Instance;
        var status = new MyraLabel(StatusText(profile), MyraLabel.TextStyle.P) { Wrap = true };
        var toggle = DesktopAssistantTheme.ActivityButton(
            TazLang.Get("trainer_start"), TazLang.Get("trainer_stop"), () => trainer.IsRunning,
            () =>
            {
                if (trainer.IsRunning)
                    trainer.Stop();
                else
                    trainer.Start();
                DesktopAgentsWindow.RequestRefresh();
            });

        var wait = new SpinButton
        {
            Integer = true,
            Value = profile.Trainer.WaitSeconds,
            Minimum = TrainerManager.MIN_WAIT_SECONDS,
            Maximum = TrainerManager.MAX_WAIT_SECONDS,
            MinWidth = 64,
            Tooltip = TazLang.Get("trainer_wait_tooltip")
        };
        // Saved with the profile on logout; a spinner tick is not a structural edit.
        wait.ValueChangedByUser += (_, _) =>
        {
            profile.Trainer.WaitSeconds = (int)(wait.Value ?? TrainerManager.DEFAULT_WAIT_SECONDS);
            status.Text = StatusText(profile);
        };

        toggle.Enabled = trainer.IsRunning || profile.Trainer.Selected.Count > 0;
        return DesktopAssistantTheme.Group(TazLang.Get("trainer_controls"),
            DesktopAssistantForm.Actions(status, Text("trainer_wait"), wait, toggle));
    }

    private static string StatusText(Profile profile)
    {
        int count = profile.Trainer.Selected.Count;
        if (!TrainerManager.Instance.IsRunning)
            return TazLang.Get("trainer_status_stopped", [count.ToString()]);

        return TazLang.Get("trainer_status_running", [count.ToString(), profile.Trainer.WaitSeconds.ToString()]);
    }

    /// <summary>One skill: tick box, what it does, and the picked target with a pick button when it needs one.</summary>
    private static void AddSkillRow(MyraGrid table, int row, Profile profile, TrainerSkill skill, AgentPageInteraction interaction, System.Action changed, System.Action refresh)
    {
        TrainerSettings settings = profile.Trainer;
        table.AddWidget(MyraCheckButton.CreateWithCallback(
            settings.Selected.Contains(skill.Id),
            ticked => { SetSelected(profile, skill, ticked); changed(); },
            skill.Name,
            TazLang.Get($"trainer_skill_{skill.Id}_help")
        ), row, 0);

        var info = new MyraLabel(DetailsText(settings, skill), MyraLabel.TextStyle.P)
        {
            Wrap = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        table.AddWidget(info, row, 1);

        var setup = new VerticalStackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        void AddPicker(string key, uint serial, System.Action<object> select)
        {
            string caption = TazLang.Get(key);
            var button = DesktopAssistantForm.Button(serial == 0 ? caption : "✓  " + caption,
                () => interaction.SelectTarget(select));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (serial != 0)
            {
                DesktopAssistantTheme.ApplyPickedButtonStyle(button);
                if (serial > 1) button.Tooltip = $"0x{serial:X8}";
            }
            setup.Widgets.Add(button);
        }

        void Equipment(string key, uint serial, System.Action<uint> save)
        {
            AddPicker(key, serial, picked =>
            {
                if (picked is not Item item) return;
                if (key == "trainer_pick_smelt_tool" && !BlacksmithTrainer.IsTongs(item.Graphic))
                {
                    GameActions.Print(TazLang.Get("trainer_smith_tongs"), Constants.HUE_WARN);
                    return;
                }
                save(item.Serial);
                profile.Save();
                refresh();
            });
        }

        if (skill.Target != TrainerTargetKind.None)
        {
            string key = skill.Id switch
            {
                "blacksmithy" => "trainer_pick_hammer",
                "tailoring" => "trainer_pick_kit",
                "arms_lore" => "trainer_pick_examine",
                "lumberjacking" => "trainer_pick_axe",
                "enticement" => "trainer_pick_animal",
                "mining" => "trainer_pick_mining_tool",
                "carpentry" => "trainer_pick_carpentry_tool",
                "bowcraft" => "trainer_pick_fletching_tool",
                "alchemy" => "trainer_pick_mortar",
                "item_identification" => "trainer_pick_examine",
                "lockpicking" => "trainer_pick_chest",
                "healing" or "veterinary" => "trainer_pick_patient",
                "inscription" => "trainer_pick_scrolls",
                "fishing" => "trainer_pick_pole",
                _ => skill.Target == TrainerTargetKind.Item ? "trainer_choose_item" : "trainer_choose_creature"
            };
            settings.Targets.TryGetValue(skill.Id, out var saved);
            AddPicker(key, saved?.Serial ?? 0, picked =>
            {
                if (!IsValidTarget(skill, picked, out var entity)) return;
                if (skill.Use == TrainerUse.Blacksmith && !BlacksmithTrainer.IsHammer(entity!.Graphic))
                {
                    GameActions.Print(TazLang.Get("trainer_smith_tool"), Constants.HUE_WARN);
                    return;
                }
                settings.Targets[skill.Id] = new TrainerTarget { Serial = entity!.Serial, Name = entity.Name ?? "" };
                profile.Save();
                refresh();
            });
        }

        switch (skill.Use)
        {
            case TrainerUse.Unlock:
                Equipment("trainer_pick_lockpicks", settings.Lockpicks, value => settings.Lockpicks = value);
                break;
            case TrainerUse.Gather:
                AddPicker("trainer_pick_mining_ground", settings.MiningGround == null ? 0u : 1u, picked =>
                {
                    if (picked is not GameObject tile || tile is not (Land or Static)) return;
                    settings.MiningGround = new(World.Instance.MapIndex, tile.X, tile.Y, tile.Graphic, tile is Land);
                    profile.Save();
                    refresh();
                });
                break;
            case TrainerUse.Craft when skill.Id == "alchemy":
                foreach (string family in PracticeTrainer.PotionFamilies)
                    setup.Widgets.Add(MyraCheckButton.CreateWithCallback(
                        settings.AlchemyFamilies.Contains(family), enabled =>
                        {
                            if (enabled) settings.AlchemyFamilies.Add(family);
                            else settings.AlchemyFamilies.Remove(family);
                            profile.Save();
                            refresh();
                        }, TazLang.Get("trainer_potion_" + family)));
                break;
            case TrainerUse.Craft when skill.Id == "bowcraft":
                setup.Widgets.Add(Text("trainer_fletching_reserve"));
                var reserve = new SpinButton { Integer = true, Minimum = 0, Maximum = 60000, Value = settings.FletchingLogReserve };
                reserve.ValueChangedByUser += (_, _) =>
                {
                    settings.FletchingLogReserve = (int)(reserve.Value ?? 0);
                    profile.Save();
                };
                setup.Widgets.Add(reserve);
                break;
            case TrainerUse.Entice:
                Equipment("trainer_herd_tool", settings.HerdingTool, value => settings.HerdingTool = value);
                break;
            case TrainerUse.Tailor:
                Equipment("trainer_tailor_unstitch", settings.UnstitchTool, value => settings.UnstitchTool = value);
                break;
            case TrainerUse.Blacksmith:
                Equipment("trainer_pick_smelt_tool", settings.SmeltTool, value => settings.SmeltTool = value);
                Equipment("trainer_pick_forge", settings.Forge, value => settings.Forge = value);
                break;
            case TrainerUse.Begging:
                var vendors = settings.BeggingVendors;
                AddPicker("trainer_beg_add", vendors.FirstOrDefault()?.Serial ?? 0, target =>
                {
                    if (target is not Mobile mobile || mobile == World.Instance?.Player ||
                        vendors.Any(v => v.Serial == mobile.Serial)) return;
                    vendors.Add(new TrainerTarget { Serial = mobile.Serial, Name = mobile.Name ?? "" });
                    profile.Save();
                    refresh();
                });
                foreach (var vendor in vendors)
                {
                    setup.Widgets.Add(MyraGrid.TableText(string.IsNullOrEmpty(vendor.Name) ? $"0x{vendor.Serial:X8}" : vendor.Name));
                    var remove = DesktopAssistantForm.Button(TazLang.Get("trainer_beg_remove"), () =>
                    {
                        vendors.Remove(vendor);
                        profile.Save();
                        refresh();
                    }, destructive: true);
                    remove.HorizontalAlignment = HorizontalAlignment.Stretch;
                    setup.Widgets.Add(remove);
                }
                break;
        }
        if (setup.Widgets.Count > 0)
            table.AddWidget(setup, row, 2);
    }

    private static bool IsValidTarget(TrainerSkill skill, object? picked, out Entity? entity)
    {
        entity = skill.Target switch
        {
            TrainerTargetKind.Item => picked as Item,
            TrainerTargetKind.Mobile => picked as Mobile,
            _ => null
        };

        if (entity == null)
            GameActions.Print(TazLang.Get(
                skill.Target == TrainerTargetKind.Item ? "trainer_pick_item" : "trainer_pick_creature",
                [skill.Name]
            ), Constants.HUE_WARN);

        return entity != null;
    }

    private static string DetailsText(TrainerSettings settings, TrainerSkill skill)
    {
        string help = TazLang.Get($"trainer_skill_{skill.Id}_help");
        if (skill.Target == TrainerTargetKind.None)
            return help;

        return settings.Targets.TryGetValue(skill.Id, out TrainerTarget? target) && target.Serial != 0
            ? TazLang.Get("trainer_target_picked", [help, target.Name.Length > 0 ? target.Name : $"0x{target.Serial:X8}"])
            : TazLang.Get("trainer_target_none", [help]);
    }

    /// <summary>Ticks or unticks a skill, keeping the list in page order so the training turn order matches.</summary>
    private static void SetSelected(Profile profile, TrainerSkill skill, bool ticked)
    {
        List<string> selected = profile.Trainer.Selected;
        selected.Remove(skill.Id);
        if (ticked)
        {
            var ordered = new List<string>(selected.Count + 1);
            foreach (TrainerSkill candidate in TrainerCatalog.Skills)
                if (candidate == skill || selected.Contains(candidate.Id))
                    ordered.Add(candidate.Id);

            selected.Clear();
            selected.AddRange(ordered);
        }

        profile.Save();
    }

    private static MyraLabel Text(string key) => new(TazLang.Get(key), MyraLabel.TextStyle.P) { Wrap = true };

    #endregion
}
