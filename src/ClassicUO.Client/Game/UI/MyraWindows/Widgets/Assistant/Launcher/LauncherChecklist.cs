#nullable enable

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers.Launcher;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Launcher;

/// <summary>
/// "Get started" steps for a profile that can't play yet. Ticks steps off and outlines the fields still empty;
/// hides itself once the profile is ready.
/// </summary>
/// <remarks>Call <see cref="Refresh" /> after a field is committed; it updates in place without rebuilding the page.</remarks>
internal sealed class LauncherChecklist
{
    #region Public accessors

    /// <summary>The checklist box to place on the page.</summary>
    public Widget View => _group;

    #endregion

    #region Private members

    private const string DONE_MARK = "✓";
    private const string TODO_MARK = "•";

    private static readonly SolidBrush _missingBorder = new(new Color(196, 64, 64));
    private static readonly Color _doneColor = new(37, 111, 69);

    private readonly LauncherProfile _profile;
    private readonly Widget _group;
    private readonly List<Step> _steps = [];

    // Each field's own border, put back once its step is done.
    private readonly Dictionary<Widget, (IBrush? Brush, Thickness Thickness)> _originalBorders = [];

    #endregion

    /// <summary>Builds the checklist for <paramref name="profile" />.</summary>
    /// <param name="profile">Profile being set up.</param>
    /// <param name="fields">Fields to outline and focus for each step; a step's first field gets the focus.</param>
    public LauncherChecklist(LauncherProfile profile, IReadOnlyDictionary<LauncherSetupStep, Widget[]> fields)
    {
        _profile = profile;
        var rows = new VerticalStackPanel { Spacing = 4 };
        rows.Widgets.Add(new MyraLabel(TazLang.Get("launcher_getstarted_intro"), MyraLabel.TextStyle.P) { Wrap = true });

        AddStep(rows, LauncherSetupStep.Folder, "launcher_step_folder", fields);
        AddStep(rows, LauncherSetupStep.Address, "launcher_step_address", fields);
        AddStep(rows, LauncherSetupStep.Account, "launcher_step_account", fields);
        AddStep(rows, LauncherSetupStep.Ready, "launcher_step_play", fields);

        _group = DesktopAssistantTheme.Group(TazLang.Get("launcher_getstarted"), rows);
        Refresh();
    }

    #region Public methods

    /// <summary>Re-checks the profile: ticks finished steps, outlines empty fields, hides when ready.</summary>
    public void Refresh()
    {
        LauncherSetupStep missing = ClientLauncher.FindMissingStep(_profile);
        _group.Visible = missing != LauncherSetupStep.Ready;

        foreach (Step step in _steps)
        {
            bool done = step.Key < missing;
            step.Label.Text = $"{(done ? DONE_MARK : TODO_MARK)} {TazLang.Get(step.TextKey)}";
            step.Label.TextColor = done ? _doneColor : DesktopAssistantTheme.Text;

            if (step.GoTo != null)
                step.GoTo.Visible = !done;

            // Outline only the step that's next, so the player isn't faced with every empty box at once.
            foreach (Widget field in step.Fields)
            {
                (IBrush? brush, Thickness thickness) = _originalBorders[field];
                bool outline = step.Key == missing;
                field.Border = outline ? _missingBorder : brush;
                field.BorderThickness = outline ? new Thickness(1) : thickness;
            }
        }
    }

    #endregion

    #region Private methods

    private void AddStep(
        VerticalStackPanel rows,
        LauncherSetupStep key,
        string textKey,
        IReadOnlyDictionary<LauncherSetupStep, Widget[]> fields
    )
    {
        Widget[] stepFields = fields.TryGetValue(key, out Widget[]? found) ? found : [];
        foreach (Widget field in stepFields)
            _originalBorders[field] = (field.Border, field.BorderThickness);

        var row = new HorizontalStackPanel { Spacing = 8 };
        var label = new MyraLabel("", MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };
        row.Widgets.Add(label);

        MyraButton? goTo = null;
        if (stepFields.Length > 0)
        {
            goTo = DesktopAssistantTheme.ActionButton(TazLang.Get("launcher_step_goto"), () => stepFields[0].SetKeyboardFocus());
            row.Widgets.Add(goTo);
        }

        rows.Widgets.Add(row);
        _steps.Add(new Step(key, textKey, label, goTo, stepFields));
    }

    #endregion

    private sealed record Step(LauncherSetupStep Key, string TextKey, MyraLabel Label, MyraButton? GoTo, Widget[] Fields);
}
