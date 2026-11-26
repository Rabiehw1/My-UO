#nullable enable
using System;
using System.Linq;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.LegionScripting;
using Myra.Graphics2D.UI;
using Legion = ClassicUO.LegionScripting.LegionScripting;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

internal sealed class ScriptExecutorTab : VerticalStackPanel
{
    public const string Title = "Script Executor";

    private readonly MyraLabel _status = new("", MyraLabel.TextStyle.P) { Wrap = true };
    private readonly MyraButton _run;
    private readonly MyraButton _stop;

    public ScriptExecutorTab()
    {
        Spacing = 8;
        _run = DesktopAssistantTheme.ActionButton("Run sequence", () =>
        {
            ScriptSequence.Start(Legion.GetSequence());
            Refresh();
        }, primary: true);
        _stop = new MyraButton("Stop sequence", () =>
        {
            ScriptSequence.Stop();
            Refresh();
        });

        Build();
        Refresh();
    }

    internal static Widget BuildAutorun() =>
        DesktopAssistantTheme.Group("Autorun",
            new MyraLabel("Choose one script to start when this character logs in.", MyraLabel.TextStyle.P) { Wrap = true },
            BuildAutorunPicker());

    /// <summary>Drop-down choosing the script that starts when this character logs in; saves on change.</summary>
    /// <returns>The picker widget.</returns>
    internal static Widget BuildAutorunPicker()
    {
        var scripts = Legion.LoadedScripts.OrderBy(script => script.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        string autoRunPath = Legion.AutoRunPath;
        var autorun = Combo("None", scripts);
        int autoIndex = Array.FindIndex(scripts, script => script.RelativePath == autoRunPath);
        if (autoIndex < 0 && autoRunPath.Length > 0)
            autorun.ListView.Widgets.Add(new Label { Text = $"Missing: {autoRunPath}" });

        autorun.ListView.SelectedIndex = autoIndex >= 0 ? autoIndex + 1
            : autoRunPath.Length > 0 ? scripts.Length + 1 : 0;
        autorun.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (autorun.ListView.SelectedIndex is int index && index <= scripts.Length)
                Legion.SetAutoRun(index == 0 ? null : scripts[index - 1]);
        };
        return autorun;
    }

    internal void Refresh()
    {
        _status.Text = ScriptSequence.Status;
        _run.Enabled = !ScriptSequence.IsRunning && Legion.GetSequence().Count > 0;
        _stop.Enabled = ScriptSequence.IsRunning;
    }

    private void Build()
    {
        Widgets.Clear();
        var paths = Legion.GetSequence().ToList();
        ScriptFile[] scripts = Legion.LoadedScripts.OrderBy(script => script.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var queue = new ListView { Height = 200, HorizontalAlignment = HorizontalAlignment.Stretch };
        var addChoices = Combo("Select a script", scripts);
        addChoices.ListView.SelectedIndex = 0;
        var earlier = new MyraButton("Move up");
        var later = new MyraButton("Move down");
        var remove = new MyraButton("Remove step");
        MyraStyle.ApplyButtonDangerStyle(remove);
        var runControls = new HorizontalStackPanel { Spacing = 6 };
        var add = new MyraButton("Add step", () =>
        {
            if (addChoices.ListView.SelectedIndex is not int index || index == 0)
                return;

            paths.Add(scripts[index - 1].RelativePath);
            SaveAndShow(paths.Count - 1);
        });
        add.Enabled = false;
        addChoices.ListView.SelectedIndexChanged += (_, _) => add.Enabled = addChoices.ListView.SelectedIndex > 0;

        earlier.OnClick = () => Move(-1);
        later.OnClick = () => Move(1);
        remove.OnClick = () =>
        {
            if (queue.SelectedIndex is not int index)
                return;

            paths.RemoveAt(index);
            SaveAndShow(Math.Min(index, paths.Count - 1));
        };
        queue.SelectedIndexChanged += (_, _) => RefreshButtons();

        var toolbar = new HorizontalStackPanel { Spacing = 6 };
        toolbar.Widgets.Add(addChoices);
        toolbar.Widgets.Add(add);
        toolbar.Widgets.Add(new MyraButton("Refresh library", () =>
        {
            Legion.LoadScriptsFromFile();
            RemoveReusableWidgets();
            Build();
            Refresh();
        }));

        var actions = new HorizontalStackPanel { Spacing = 5 };
        actions.Widgets.Add(earlier);
        actions.Widgets.Add(later);
        actions.Widgets.Add(remove);

        runControls.Widgets.Add(_run);
        runControls.Widgets.Add(_stop);
        Widgets.Add(DesktopAssistantTheme.Group("Sequence",
            new MyraLabel($"Runs top to bottom. Each step must finish successfully within {ScriptSequence.StepTimeoutSeconds} seconds.", MyraLabel.TextStyle.P) { Wrap = true },
            toolbar, queue, actions, runControls, _status));
        ShowQueue(paths.Count == 0 ? -1 : 0);

        void SaveAndShow(int index)
        {
            Legion.SaveSequence(paths);
            paths = Legion.GetSequence().ToList();
            ShowQueue(Math.Min(index, paths.Count - 1));
            Refresh();
        }

        void ShowQueue(int index)
        {
            queue.Widgets.Clear();
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string missing = scripts.Any(script => script.RelativePath == path) ? "" : " (missing)";
                queue.Widgets.Add(new Label { Text = $"{i + 1}. {path}{missing}" });
            }

            queue.SelectedIndex = index >= 0 ? index : null;
            RefreshButtons();
        }

        void RefreshButtons()
        {
            int index = queue.SelectedIndex ?? -1;
            earlier.Enabled = index > 0;
            later.Enabled = index >= 0 && index + 1 < paths.Count;
            remove.Enabled = index >= 0;
        }

        void Move(int offset)
        {
            if (queue.SelectedIndex is not int index || index + offset < 0 || index + offset >= paths.Count)
                return;

            int destination = index + offset;
            (paths[index], paths[destination]) = (paths[destination], paths[index]);
            SaveAndShow(destination);
        }

        void RemoveReusableWidgets()
        {
            runControls.Widgets.Clear();
            if (_status.Parent is VerticalStackPanel statusParent)
                statusParent.Widgets.Remove(_status);
        }
    }

    internal static ComboView Combo(string prompt, ScriptFile[] scripts)
    {
        var combo = new ComboView { MinWidth = 200 };
        combo.ListView.Widgets.Add(new Label { Text = prompt });
        foreach (ScriptFile script in scripts)
            combo.ListView.Widgets.Add(new Label { Text = script.RelativePath });
        return combo;
    }
}
