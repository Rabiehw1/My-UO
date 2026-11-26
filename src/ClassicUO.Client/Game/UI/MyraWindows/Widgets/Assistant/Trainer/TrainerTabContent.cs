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
                GridColumnInfo.Fill(TazLang.Get("trainer_column_use")), GridColumnInfo.Auto(TazLang.Get("trainer_column_target")));
            int row = 1;
            foreach (TrainerSkill skill in TrainerCatalog.Skills.Where(s => s.Class == _selectedClass))
                AddSkillRow(table, row++, profile, skill, interaction, () => { RefreshOrder(); RefreshControls(); });
            details.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("trainer_class_skills", [ClassName(_selectedClass)]), table));
            if (_selectedClass == TrainerClass.Bard)
            {
                var route = new VerticalStackPanel { Spacing = 4 };
                var vendors = profile.Trainer.BeggingVendors;
                route.Widgets.Add(DesktopAssistantForm.Button(TazLang.Get("trainer_beg_add"), () =>
                    interaction.SelectTarget(target =>
                    {
                        if (target is not Mobile mobile || mobile == World.Instance?.Player ||
                            vendors.Any(v => v.Serial == mobile.Serial))
                            return;
                        vendors.Add(new TrainerTarget { Serial = mobile.Serial, Name = mobile.Name ?? "" });
                        profile.Save();
                        ShowClass();
                    }), primary: true));
                foreach (var vendor in vendors)
                    route.Widgets.Add(DesktopAssistantForm.Actions(MyraGrid.TableText($"{vendor.Name} · 0x{vendor.Serial:X8}"),
                        DesktopAssistantForm.Button(TazLang.Get("trainer_beg_remove"), () =>
                        {
                            vendors.Remove(vendor);
                            profile.Save();
                            ShowClass();
                        }, destructive: true)));
                details.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("trainer_beg_route"), route));
            }
            if (_selectedClass == TrainerClass.Crafter)
            {
                MyraButton Pick(string key, System.Func<uint> get, System.Action<uint> set)
                {
                    string Caption() => TazLang.Get(key) + (get() == 0 ? "" : $" · 0x{get():X8}");
                    var button = DesktopAssistantForm.Button(Caption());
                    button.OnClick = () => interaction.SelectTarget(target =>
                    {
                        if (target is not Item item)
                            return;
                        set(item.Serial);
                        profile.Save();
                        button.Text = Caption();
                    });
                    return button;
                }
                details.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("trainer_smith_equipment"),
                    DesktopAssistantForm.Actions(
                        Pick("trainer_pick_smelt_tool", () => profile.Trainer.SmeltTool, value => profile.Trainer.SmeltTool = value),
                        Pick("trainer_pick_forge", () => profile.Trainer.Forge, value => profile.Trainer.Forge = value))));
                details.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("trainer_tailor_equipment"),
                    DesktopAssistantForm.Actions(Pick("trainer_tailor_unstitch", () => profile.Trainer.UnstitchTool,
                        value => profile.Trainer.UnstitchTool = value))));
            }
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
    private static void AddSkillRow(MyraGrid table, int row, Profile profile, TrainerSkill skill, AgentPageInteraction interaction, System.Action changed)
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

        if (skill.Target != TrainerTargetKind.None)
            table.AddWidget(DesktopAssistantForm.Button(TazLang.Get(skill.Target == TrainerTargetKind.Item ? "trainer_choose_item" : "trainer_choose_creature"), () =>
                interaction.SelectTarget(picked =>
                {
                    if (!IsValidTarget(skill, picked, out Entity? entity))
                        return;

                    settings.Targets[skill.Id] = new TrainerTarget { Serial = entity!.Serial, Name = entity.Name ?? "" };
                    profile.Save();
                    info.Text = DetailsText(settings, skill);
                })), row, 2);
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
