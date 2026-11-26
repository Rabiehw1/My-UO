#nullable enable

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Trainer;
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
        var root = new VerticalStackPanel { Spacing = 10 };
        if (ProfileManager.CurrentProfile is not { } profile)
        {
            root.Widgets.Add(Text("trainer_connect"));
            return root;
        }

        root.Widgets.Add(Text("trainer_help"));
        root.Widgets.Add(BuildControls(profile));

        foreach (TrainerClass trainerClass in System.Enum.GetValues<TrainerClass>())
        {
            var rows = new List<Widget>();
            foreach (TrainerSkill skill in TrainerCatalog.Skills)
                if (skill.Class == trainerClass)
                    rows.Add(BuildSkillRow(profile, skill, interaction));

            if (rows.Count > 0)
                root.Widgets.Add(DesktopAssistantTheme.Group(
                    TazLang.Get("trainer_class_" + trainerClass.ToString().ToLowerInvariant()),
                    rows.ToArray()
                ));
        }

        return root;
    }

    #endregion

    #region Private methods

    /// <summary>Start/stop, the shared wait and what is running.</summary>
    private static Widget BuildControls(Profile profile)
    {
        TrainerManager trainer = TrainerManager.Instance;
        var status = new MyraLabel(StatusText(profile), MyraLabel.TextStyle.P) { Wrap = true };
        var toggle = DesktopAssistantForm.Button(
            TazLang.Get(trainer.IsRunning ? "trainer_stop" : "trainer_start"),
            () =>
            {
                if (trainer.IsRunning)
                    trainer.Stop();
                else
                    trainer.Start();
                DesktopAgentsWindow.RequestRefresh();
            },
            primary: !trainer.IsRunning
        );

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
        wait.ValueChangedByUser += (_, _) => profile.Trainer.WaitSeconds = (int)(wait.Value ?? TrainerManager.DEFAULT_WAIT_SECONDS);

        return DesktopAssistantTheme.Group(
            TazLang.Get("trainer_controls"),
            status,
            DesktopAssistantForm.Fields(new DesktopAssistantForm.Field(TazLang.Get("trainer_wait"), wait)),
            DesktopAssistantForm.Actions(toggle)
        );
    }

    private static string StatusText(Profile profile)
    {
        int count = profile.Trainer.Selected.Count;
        if (!TrainerManager.Instance.IsRunning)
            return TazLang.Get("trainer_status_stopped", [count.ToString()]);

        return TazLang.Get("trainer_status_running", [count.ToString(), profile.Trainer.WaitSeconds.ToString()]);
    }

    /// <summary>One skill: tick box, what it does, and the picked target with a pick button when it needs one.</summary>
    private static Widget BuildSkillRow(Profile profile, TrainerSkill skill, AgentPageInteraction interaction)
    {
        TrainerSettings settings = profile.Trainer;
        var row = new VerticalStackPanel { Spacing = 2 };
        row.Widgets.Add(MyraCheckButton.CreateWithCallback(
            settings.Selected.Contains(skill.Id),
            ticked => SetSelected(profile, skill, ticked),
            skill.Name,
            TazLang.Get($"trainer_skill_{skill.Id}_help")
        ));

        var details = new HorizontalStackPanel { Spacing = 6 };
        var info = new MyraLabel(DetailsText(settings, skill), MyraLabel.TextStyle.P)
        {
            Wrap = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        details.Widgets.Add(info);

        if (skill.Target != TrainerTargetKind.None)
            details.Widgets.Add(DesktopAssistantForm.Button(TazLang.Get("trainer_pick"), () =>
                interaction.SelectTarget(picked =>
                {
                    if (!IsValidTarget(skill, picked, out Entity? entity))
                        return;

                    settings.Targets[skill.Id] = new TrainerTarget { Serial = entity!.Serial, Name = entity.Name ?? "" };
                    profile.Save();
                    info.Text = DetailsText(settings, skill);
                })));

        row.Widgets.Add(details);
        return row;
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
