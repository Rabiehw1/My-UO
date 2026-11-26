#nullable enable
using System;
using System.IO;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.LegionScripting;
using Myra.Graphics2D.UI;
using Legion = ClassicUO.LegionScripting.LegionScripting;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

internal static class ScriptsTabContent
{
    public const string Title = "Scripts";

    public static Widget Build(AgentPageInteraction interaction)
    {
        var tabs = new MyraTabControl();
        tabs.AddTab("Library", () => new ScriptLibraryTab(interaction), rebuildOnSelect: true);
        tabs.AddTab(ScriptExecutorTab.Title, () => new ScriptExecutorTab(), rebuildOnSelect: true);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            interaction.CancelDialog();
            foreach (var item in tabs.Items)
            {
                var label = tabs.FindChild<Widget>(widget => ReferenceEquals(widget.Tag, item))?.FindChild<Label>();
                if (label == null)
                    continue;

                bool selected = ReferenceEquals(tabs.SelectedItem, item);
                label.TextColor = selected ? DesktopAssistantTheme.Accent : DesktopAssistantTheme.Text;
                label.Font = DesktopAssistantTheme.Font(bold: selected);
            }
        };
        tabs.SelectFirst();
        return tabs;
    }
}

internal sealed class ScriptLibraryTab : VerticalStackPanel
{
    private readonly AgentPageInteraction _interaction;
    private readonly VerticalStackPanel _active = new() { Spacing = 3 };
    private readonly ListView _files = new() { Height = 240, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly VerticalStackPanel _details = new() { Spacing = 6 };
    private readonly MyraInputBox _search = new() { HintText = "Find a script" };
    private readonly MyraLabel _actionStatus = new("", MyraLabel.TextStyle.P) { Wrap = true };
    private (ScriptFile Script, bool Stopping)[] _running = [];
    private ScriptFile? _selected;
    private bool _loading;

    public ScriptLibraryTab(AgentPageInteraction interaction)
    {
        _interaction = interaction;
        Spacing = 8;
        var grid = new MyraGrid { ColumnSpacing = 10, MaxWidth = 1030 };
        grid.AddColumn(new Proportion(ProportionType.Pixels, 285));
        grid.AddColumn(new Proportion(ProportionType.Fill));

        var sidebar = new VerticalStackPanel { Spacing = 8 };
        sidebar.Widgets.Add(DesktopAssistantTheme.Group("Running Scripts", _active));
        var tools = new HorizontalStackPanel { Spacing = 3 };
        var create = DesktopAssistantTheme.ActionButton(TazLang.Get("scriptmanager_newscript"), primary: true);
        create.OnClick = () => ShowCreate(create, group: false);
        var addGroup = new MyraButton("New Group");
        addGroup.OnClick = () => ShowCreate(addGroup, group: true);
        tools.Widgets.Add(create);
        tools.Widgets.Add(addGroup);
        tools.Widgets.Add(new MyraButton("Refresh", () => Reload()));
        sidebar.Widgets.Add(DesktopAssistantTheme.Group("Script library", tools, _search, _files));
        grid.AddWidget(sidebar, 0, 0);
        grid.AddWidget(_details, 0, 1);
        Widgets.Add(grid);
        Widgets.Add(_actionStatus);

        _files.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _files.SelectedItem?.Tag is ScriptFile script)
                Select(script);
        };
        _search.TextChangedByUser += (_, _) => ShowFiles();
        ShowFiles();
        RefreshRunning();
    }

    internal void RefreshRunning()
    {
        var running = Legion.LoadedScripts.Concat(Legion.RunningScripts).Distinct()
            .Where(s => s.IsPlaying)
            .Select(s => (s, Stopping: !Legion.RunningScripts.Contains(s)))
            .ToArray();
        if (_active.Widgets.Count > 0 && running.SequenceEqual(_running))
            return;

        _running = running;
        _active.Widgets.Clear();
        if (running.Length == 0)
            _active.Widgets.Add(new MyraLabel("No scripts running.", MyraLabel.TextStyle.P));

        foreach (var (script, stopping) in running)
        {
            var row = new HorizontalStackPanel { Spacing = 5 };
            var stop = DesktopAssistantTheme.ActivityButton("Run", stopping ? "Stopping…" : "Stop", () => script.IsPlaying, () =>
            {
                Legion.StopScript(script);
                RefreshRunning();
            });
            stop.Enabled = !stopping;
            row.Widgets.Add(stop);
            row.Widgets.Add(new MyraLabel(script.FileName, MyraLabel.TextStyle.P) { Tooltip = script.RelativePath });
            _active.Widgets.Add(row);
        }

        ShowFiles();
    }

    private void Select(ScriptFile script)
    {
        _selected = script;
        _actionStatus.Text = "";
        ShowDetails();
    }

    private void ShowFiles()
    {
        string query = _search.Text?.Trim() ?? "";
        var scripts = Legion.LoadedScripts
            .Where(script => script.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(script => script.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        string? selected = _selected?.RelativePath;
        _loading = true;
        _files.Widgets.Clear();
        foreach (var script in scripts)
        {
            string state = script.IsPlaying ? " · Running" : "";
            _files.Widgets.Add(new Label { Text = script.RelativePath + state, Tag = script });
        }

        _selected = scripts.FirstOrDefault(script => script.RelativePath == selected) ?? scripts.FirstOrDefault();
        int index = Array.IndexOf(scripts, _selected);
        _files.SelectedIndex = index >= 0 ? index : null;
        _loading = false;
        ShowDetails();
    }

    private void ShowDetails()
    {
        _details.Widgets.Clear();
        if (_selected is not { } script)
        {
            _details.Widgets.Add(new MyraLabel("No matching scripts.", MyraLabel.TextStyle.P));
            return;
        }

        var actions = new HorizontalStackPanel { Spacing = 4 };
        actions.Widgets.Add(DesktopAssistantTheme.ActivityButton("Run script", "Stop script", () => script.IsPlaying, () =>
        {
            if (script.IsPlaying)
                Legion.StopScript(script);
            else
                Legion.PlayScript(script);
            RefreshRunning();
        }));

        bool autorun = Legion.AutoRunPath == script.RelativePath;
        actions.Widgets.Add(new MyraButton(autorun ? "Clear Autorun" : "Set as Autorun", () =>
        {
            Legion.SetAutoRun(autorun ? null : script);
            _actionStatus.Text = autorun ? "Autorun cleared." : $"{script.FileName} will run at login.";
            ShowDetails();
        }));
        var hotkey = new MyraButton(ScriptHotkeysManager.GetBinding(script).IsEmpty
            ? "Set hotkey" : ScriptHotkeysManager.GetBinding(script).Describe());
        hotkey.OnClick = () => HotkeysTabContent.CaptureScriptBinding(_interaction, hotkey, script, ShowDetails);
        actions.Widgets.Add(hotkey);
        _details.Widgets.Add(DesktopAssistantTheme.Group(script.FileName,
            new MyraLabel(script.RelativePath, MyraLabel.TextStyle.P) { Wrap = true }, actions));

        var preview = new MyraLabel(string.Join("\n", (script.FileContents ?? []).Take(12)), MyraLabel.TextStyle.P) { Wrap = true };
        var edit = new MyraButton("Edit source");
        edit.OnClick = () =>
        {
            var source = new ScriptSourceEditor(string.Join("\n", script.ReadFromFile()), script.Type);
            _interaction.ShowDialog(edit, script.FileName, source, saved =>
            {
                if (!saved)
                    return;

                script.OverrideFileContents(source.Source);
                script.ReadFromFile();
                ShowDetails();
            }, confirmText: TazLang.Get("mog_kw_save"), preferredWidth: 820);
        };
        _details.Widgets.Add(DesktopAssistantTheme.Group("Source preview", edit, preview));
    }

    private void Reload()
    {
        Legion.LoadScriptsFromFile();
        _running = [];
        _active.Widgets.Clear();
        RefreshRunning();
    }

    private void ShowCreate(Widget anchor, bool group)
    {
        var name = new MyraInputBox { Width = 280, HintText = group ? "Group name" : "Script name" };
        var body = new VerticalStackPanel { Spacing = 6 };
        var error = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true };
        body.Widgets.Add(name);
        var directory = new ComboView { MinWidth = 225 };
        if (!group)
        {
            directory.ListView.Widgets.Add(new Label { Text = "Ungrouped", Tag = "" });
            foreach (string path in Directory.EnumerateDirectories(Legion.ScriptPath).Order())
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                    directory.ListView.Widgets.Add(new Label { Text = Path.GetFileName(path), Tag = Path.GetFileName(path) });
            }
            directory.ListView.SelectedIndex = 0;
            body.Widgets.Add(directory);
        }
        body.Widgets.Add(error);

        _interaction.ShowDialog(anchor, group ? "New Group" : "New Script", body, confirmed =>
        {
            if (confirmed)
                Reload();
        }, () =>
        {
            string leaf = name.Text?.Trim() ?? "";
            if (leaf.Length == 0 || leaf.EndsWith('.') || leaf.StartsWith('_') ||
                leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || leaf.IndexOfAny(['/', '\\', ':']) >= 0)
            {
                error.Text = "Enter a valid name.";
                return false;
            }

            try
            {
                if (group)
                {
                    string target = Path.Combine(Legion.ScriptPath, leaf);
                    if (Directory.Exists(target))
                    {
                        error.Text = "This group already exists.";
                        return false;
                    }
                    Directory.CreateDirectory(target);
                }
                else
                {
                    if (!leaf.EndsWith(".py", StringComparison.Ordinal) && !leaf.EndsWith(".cs", StringComparison.Ordinal))
                        leaf += ".py";

                    if (leaf == "API.py")
                    {
                        error.Text = "API.py is reserved.";
                        return false;
                    }

                    string target = Path.Combine(Legion.ScriptPath, directory.ListView.SelectedItem?.Tag as string ?? "", leaf);
                    using var file = File.Open(target, FileMode.CreateNew, FileAccess.Write);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                error.Text = ex.Message;
                return false;
            }
        }, confirmText: TazLang.Get("create"));
    }
}
