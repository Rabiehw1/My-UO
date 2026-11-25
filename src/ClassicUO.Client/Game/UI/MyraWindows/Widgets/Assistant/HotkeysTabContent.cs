#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Hotkeys;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

/// <summary>
/// Central place to view and edit every hotkey in one table. Registered hotkeys persist to
/// hotkeys.json; macro hotkeys are shown in the same table and, when edited here, are saved back
/// to the macro itself (macros.xml) so the macro system stays the owner of its bindings.
/// Conflicts across both systems are surfaced in the Conflicts column (hover for details).
/// </summary>
public static class HotkeysTabContent
{
    internal static string Title => TazLang.Get("assistantwindow_tab_hotkeys", "Hotkeys");

    /// <summary>
    /// A unified editable row over either a registered <see cref="HotKeyEntry"/> or a <see cref="Macro"/>.
    /// <see cref="Source"/> is the stable backing object, used to keep "currently capturing" identity
    /// across table rebuilds.
    /// </summary>
    private sealed class HotkeyRow
    {
        public string Name = string.Empty;
        public string Category = string.Empty;
        public object Source = null!;
        public Func<HotkeyBinding> GetBinding = () => new HotkeyBinding();
        public Action<HotkeyBinding> Apply = _ => { };
        public Action ClearBinding = () => { };
        public Action? ResetBinding;
        public bool SupportsEnable;
        public Func<bool> GetEnabled = () => true;
        public Action<bool> SetEnabled = _ => { };
        public bool CheckConflicts = true;
    }

    private static HotkeyCaptureWindow? _captureWindow;

    /// <summary>Close any open capture window; called from AssistantWindow.Dispose so a capture
    /// session doesn't outlive the window that spawned it.</summary>
    public static void Cleanup()
    {
        if (_captureWindow is { IsDisposed: false })
            _captureWindow.Dispose();

        _captureWindow = null;
    }

    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        if (interaction.IsDesktop)
            return BuildDesktop(interaction);

        var root = new VerticalStackPanel { Spacing = 6 };
        root.Widgets.Add(new MyraLabel(Title, MyraLabel.TextStyle.H2));

        var listPanel = new VerticalStackPanel { Spacing = 1 };
        var filter = new MyraInputBox { HintText = TazLang.Get("hotkeys_filter", "Search hotkeys"), Width = 220 };

        void BuildList()
        {
            listPanel.Widgets.Clear();

            List<HotkeyRow> rows = BuildRows();
            if (rows.Count == 0)
            {
                listPanel.Widgets.Add(new MyraLabel("No hotkeys registered.", MyraLabel.TextStyle.P));
                return;
            }

            // Snapshot every binding up front so conflicts are detected across the whole table
            // (registered hotkeys and macros alike), not just within one system.
            List<(HotkeyRow Row, HotkeyBinding Binding)> snapshots =
                rows.Select(r => (Row: r, Binding: r.GetBinding())).ToList();

            var grid = new MyraGrid { MaxWidth = 800 };
            string categoryTitle = TazLang.Get("assistant_hotkeys_category");
            string nameTitle = TazLang.Get("mog_kw_name");
            string bindingTitle = TazLang.Get("assistant_hotkeys_binding");
            string conflictTitle = TazLang.Get("assistant_hotkeys_conflicts");
            grid.SetupWithHeaders(
                GridColumnInfo.Auto(categoryTitle),
                GridColumnInfo.Auto(nameTitle),
                GridColumnInfo.Auto(bindingTitle),
                GridColumnInfo.Auto(conflictTitle),
                GridColumnInfo.Auto(""),
                GridColumnInfo.Auto(""),
                GridColumnInfo.Auto("On"));

            int row = 1;
            string query = filter.Text?.Trim() ?? "";
            foreach ((HotkeyRow r, HotkeyBinding binding) in snapshots)
            {
                string description = binding.Describe();
                if (!r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !r.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;

                HotkeyRow localRow = r;

                List<string> conflicts = (binding.IsEmpty || !r.CheckConflicts)
                    ? new List<string>()
                    : snapshots
                        .Where(s => !ReferenceEquals(s.Row, r) && s.Row.CheckConflicts && binding.Matches(s.Binding))
                        .Select(s => s.Row.Name)
                        .ToList();

                grid.AddWidget(MyraGrid.TableText(r.Category ?? string.Empty), row, 0);
                grid.AddWidget(MyraGrid.TableText(r.Name), row, 1);
                grid.AddWidget(MyraGrid.TableText(description), row, 2);

                var conflictLabel = new MyraLabel(conflicts.Count > 0 ? "Yes" : string.Empty, MyraLabel.TextStyle.P);
                if (conflicts.Count > 0)
                    conflictLabel.Tooltip = "Conflicts with: " + string.Join(", ", conflicts);
                grid.AddWidget(conflictLabel, row, 3);

                var setButton = new MyraButton(TazLang.Get("mog_kw_set", "Set"));
                setButton.Click += (_, _) => StartCapture(setButton, localRow);

                MyraButton menuButton = null!;
                menuButton = new MyraButton("...", () =>
                {
                    var items = new List<(string Label, Action Action)>
                    {
                        (TazLang.Get("mog_kw_clear", "Clear"), () => { localRow.ClearBinding(); SaveAndRefresh(); })
                    };
                    if (localRow.ResetBinding != null)
                    {
                        Action reset = localRow.ResetBinding;
                        items.Add((TazLang.Get("hotkeycapture_reset", "Reset"), () => { reset(); SaveAndRefresh(); }));
                    }
                    ShowRowMenu(menuButton, items.ToArray());
                }) { Tooltip = TazLang.Get("assistant_hotkeys_actions_tooltip") };
                grid.AddWidget(setButton, row, 4);
                grid.AddWidget(menuButton, row, 5);

                if (localRow.SupportsEnable)
                {
                    var enabled = MyraCheckButton.CreateWithCallback(
                        localRow.GetEnabled(),
                        localRow.SetEnabled,
                        null,
                        "Enable or disable this hotkey");
                    grid.AddWidget(enabled, row, 6);
                }
                else
                {
                    grid.AddWidget(new MyraLabel(string.Empty, MyraLabel.TextStyle.P), row, 6);
                }

                row++;
            }

            listPanel.Widgets.Add(row > 1 ? grid : new MyraLabel(
                TazLang.Get("hotkeys_no_matches", "No matching hotkeys."), MyraLabel.TextStyle.P));
        }

        void SaveAndRefresh()
        {
            BuildList();
        }

        void StartCapture(Widget anchor, HotkeyRow row)
        {
            if (_captureWindow is { IsDisposed: false })
            {
                _captureWindow.BringOnTop();
                return;
            }

            _captureWindow = new HotkeyCaptureWindow(
                prompt: row.Name,
                existing: row.GetBinding(),
                onSaved: binding =>
                {
                    row.Apply(binding);
                    BuildList();
                },
                capturesMouseEvents: true);
        }

        BuildList();

        var topBar = new HorizontalStackPanel { Spacing = 4 };
        filter.TextChangedByUser += (_, _) => BuildList();
        topBar.Widgets.Add(filter);
        var resetAll = new MyraButton(TazLang.Get("hotkeys_reset_all", "Reset all"));
        resetAll.Click += (_, _) => ResetAll();
        topBar.Widgets.Add(resetAll);
        root.Widgets.Add(topBar);

        root.Widgets.Add(new ScrollViewer { Height = 360, Content = listPanel });

        return root;

        void ResetAll()
        {
            foreach (HotKeyEntry entry in HotKeys.AllRegistered())
                entry.ResetToDefault();
            SaveAndRefresh();
        }
    }

    private static Widget BuildDesktop(AgentPageInteraction interaction)
    {
        var search = new MyraInputBox { HintText = TazLang.Get("hotkeys_search") };
        var categories = new ListView();
        var results = new ListView
        {
            BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        results.ScrollViewer.ShowHorizontalScrollBar = false;
        DesktopAssistantForm.StyleListSelection(results);
        var inspector = new VerticalStackPanel
        {
            Spacing = 3, Padding = new Thickness(6), Background = DesktopAssistantTheme.Sidebar,
            Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(1)
        };
        var allRows = BuildRows();
        string?[] categoryNames = new string?[] { null }.Concat(allRows.Select(row => row.Category).Distinct()).ToArray();
        foreach (string? name in categoryNames)
        {
            int count = name == null ? allRows.Count : allRows.Count(row => row.Category == name);
            categories.Widgets.Add(DesktopAssistantForm.ListRow(CategoryName(name), MyraGrid.TableText(count.ToString(), alignRight: true)));
        }
        categories.SelectedIndex = 0;

        object? selection = null;
        bool rebuilding = false;
        var bindingFilter = new ComboView { MinWidth = 130 };
        foreach (string key in new[] { "hotkeys_all_bindings", "hotkeys_assigned", "hotkeys_unassigned" })
            bindingFilter.ListView.Widgets.Add(new Label { Text = TazLang.Get(key) });
        bindingFilter.ListView.SelectedIndex = 0;
        GridColumnInfo[] columns =
        [
            GridColumnInfo.Fill(TazLang.Get("hotkeys_action"), 5),
            GridColumnInfo.Fill(TazLang.Get("assistant_hotkeys_binding"), 2),
            GridColumnInfo.Fill(TazLang.Get("assistant_hotkeys_conflicts"), 2)
        ];
        var heading = new MyraGrid();
        heading.SetupWithHeaders(columns);
        var resultTitle = new MyraLabel("", MyraLabel.TextStyle.H3);
        var resultCount = new MyraLabel("", MyraLabel.TextStyle.P) { TextColor = DesktopAssistantTheme.SecondaryText };
        var title = new MyraGrid { Background = DesktopAssistantTheme.Sidebar };
        title.AddColumn(new Proportion(ProportionType.Fill));
        title.AddColumn();
        title.AddWidget(resultTitle, 0, 0);
        title.AddWidget(resultCount, 0, 1);
        var empty = new MyraLabel(TazLang.Get("hotkeys_no_matches"), MyraLabel.TextStyle.P)
        {
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4)
        };
        var rows = new Panel();
        rows.Widgets.Add(results);
        rows.Widgets.Add(empty);
        var bindings = new MyraGrid { RowSpacing = 0, Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(1) };
        bindings.AddRow();
        bindings.AddRow();
        bindings.AddRow(new Proportion(ProportionType.Fill));
        bindings.AddWidget(title, 0, 0);
        bindings.AddWidget(heading, 1, 0);
        bindings.AddWidget(rows, 2, 0);

        var add = DesktopAssistantForm.Button(TazLang.Get("hotkeys_add"), primary: true);
        add.OnClick = () =>
        {
            var available = BuildRows().Where(row => row.GetBinding().IsEmpty).ToArray();
            if (available.Length == 0)
            {
                interaction.ShowDialog(add, add.Text, new MyraLabel(TazLang.Get("hotkeys_all_assigned"), MyraLabel.TextStyle.P), _ => { });
                return;
            }

            CaptureDesktopBinding(interaction, add, null, assigned =>
            {
                search.Text = string.Empty;
                bindingFilter.ListView.SelectedIndex = 0;
                categories.SelectedIndex = Math.Max(0, Array.IndexOf(categoryNames, assigned.Category));
                selection = assigned.Source;
                Refresh(resetScroll: true, revealSelection: true);
            }, available);
        };
        var reset = DesktopAssistantForm.Button(TazLang.Get("hotkeys_reset_all"));
        reset.Click += (_, _) => interaction.Confirm(reset, reset.Text, TazLang.Get("hotkeys_reset_confirm"), () =>
        {
            foreach (HotKeyEntry entry in HotKeys.AllRegistered())
                entry.ResetToDefault();
            Save();
        });
        bindingFilter.ListView.SelectedIndexChanged += (_, _) => Refresh(resetScroll: true);
        search.TextChangedByUser += (_, _) => Refresh(resetScroll: true);
        categories.SelectedIndexChanged += (_, _) => Refresh(resetScroll: true);
        results.SelectedIndexChanged += (_, _) =>
        {
            if (!rebuilding)
                ShowSelection();
        };
        var toolbar = new MyraGrid { ColumnSpacing = 6 };
        toolbar.AddColumn(new Proportion(ProportionType.Fill));
        toolbar.AddColumn();
        toolbar.AddWidget(search, 0, 0);
        toolbar.AddWidget(bindingFilter, 0, 1);

        var editor = new MyraGrid { RowSpacing = 6 };
        editor.AddRow();
        editor.AddRow(new Proportion(ProportionType.Fill));
        editor.AddRow();
        editor.AddWidget(toolbar, 0, 0);
        editor.AddWidget(bindings, 1, 0);
        editor.AddWidget(inspector, 2, 0);

        const int sidebarWidth = DesktopAssistantForm.ListSidebarWidth;
        var layout = new DesktopAssistantForm.FixedSidebarLayout(editor, sidebarWidth) { ColumnSpacing = 10 };
        layout.AddColumn(new Proportion(ProportionType.Pixels, sidebarWidth));
        layout.AddColumn(new Proportion(ProportionType.Fill));
        layout.AddRow(new Proportion(ProportionType.Fill));
        layout.AddWidget(DesktopAssistantForm.ListSidebar(TazLang.Get("hotkeys_categories"), categories, [add, reset]), 0, 0);
        layout.AddWidget(editor, 0, 1);

        Refresh();
        return layout;

        void Save()
        {
            HotKeys.Save();
            Refresh();
        }

        void Refresh(bool resetScroll = false, bool revealSelection = false)
        {
            string? category = categoryNames[categories.SelectedIndex ?? 0];
            string query = search.Text?.Trim() ?? "";
            var all = BuildRows().Select(row => (Row: row, Binding: row.GetBinding())).ToList();
            int mode = bindingFilter.ListView.SelectedIndex ?? 0;
            var visible = all.Where(entry => (category == null || category == entry.Row.Category)
                && (mode == 0 || entry.Binding.IsEmpty == (mode == 2))
                && (entry.Row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Row.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Binding.Describe().Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            int selectedIndex = Math.Max(0, visible.FindIndex(entry => ReferenceEquals(entry.Row.Source, selection)));
            Point scrollPosition = resetScroll ? Point.Zero : results.ScrollViewer.ScrollPosition;
            rebuilding = true;
            results.Widgets.Clear();
            foreach (var (row, binding) in visible)
            {
                var conflicts = all.Where(other => !ReferenceEquals(row, other.Row) && row.CheckConflicts
                    && other.Row.CheckConflicts && !binding.IsEmpty && binding.Matches(other.Binding))
                    .Select(other => other.Row.Name).ToArray();
                var item = new MyraGrid
                {
                    Tag = row, MinHeight = 24, ColumnSpacing = 4,
                    Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(0, 0, 0, 1)
                };
                item.AddTableColumns(columns);
                item.AddWidget(MyraGrid.TableText(row.Name), 0, 0);
                item.AddWidget(MyraGrid.TableText(DescribeBinding(row, binding)), 0, 1);
                item.AddWidget(MyraGrid.TableText(conflicts.Length == 0 ? "—" : string.Join(", ", conflicts)), 0, 2);
                results.Widgets.Add(item);
            }

            resultTitle.Text = category == null ? TazLang.Get("hotkeys_all_title") : CategoryName(category);
            resultCount.Text = visible.Count == 1 ? TazLang.Get("hotkeys_one_action")
                : TazLang.Get("hotkeys_action_count", [visible.Count.ToString()]);
            empty.Visible = visible.Count == 0;
            results.SelectedIndex = visible.Count > 0 ? selectedIndex : null;
            results.ScrollViewer.ScrollPosition = scrollPosition;
            rebuilding = false;
            ShowSelection();
            if (revealSelection)
                DesktopAssistantForm.RevealSelection(results);
        }

        void ShowSelection()
        {
            var selected = results.SelectedItem?.Tag as HotkeyRow;
            selection = selected?.Source;
            inspector.Widgets.Clear();
            inspector.Visible = selected != null;
            if (selected == null)
                return;

            var bindingValue = selected.GetBinding();
            inspector.Widgets.Add(new MyraLabel(selected.Name, MyraLabel.TextStyle.H3));
            inspector.Widgets.Add(DesktopAssistantForm.Fields(
                new(TazLang.Get("assistant_hotkeys_binding"), MyraGrid.TableText(DescribeBinding(selected, bindingValue))),
                new(TazLang.Get("assistant_hotkeys_category"), MyraGrid.TableText(CategoryName(selected.Category)))));
            var set = DesktopAssistantForm.Button(TazLang.Get("hotkeys_set_shortcut"), primary: true);
            set.OnClick = () => CaptureDesktopBinding(interaction, set, selected, _ => Refresh());
            var clear = DesktopAssistantForm.Button(TazLang.Get("mog_kw_clear"), () => { selected.ClearBinding(); Save(); });
            clear.Enabled = !bindingValue.IsEmpty;
            var actions = DesktopAssistantForm.Actions(set, clear);
            if (selected.ResetBinding is Action restore)
                actions.Widgets.Add(DesktopAssistantForm.Button(TazLang.Get("hotkeycapture_reset"), () => { restore(); Save(); }));
            if (selected.SupportsEnable && !bindingValue.IsEmpty)
                actions.Widgets.Add(MyraCheckButton.CreateWithCallback(selected.GetEnabled(),
                    enabled => { selected.SetEnabled(enabled); Save(); }, TazLang.Get("hotkeys_enable_shortcut")));
            inspector.Widgets.Add(actions);
        }

        static string CategoryName(string? category) => category switch
        {
            null => TazLang.Get("hotkeys_all_categories"),
            "" => TazLang.Get("hotkeys_uncategorized"),
            _ => category
        };

        static string DescribeBinding(HotkeyRow row, HotkeyBinding binding)
        {
            if (binding.IsEmpty)
                return TazLang.Get("hotkeys_unassigned");
            string description = binding.Describe();
            return row.SupportsEnable && !row.GetEnabled() ? TazLang.Get("hotkeys_disabled_binding", [description]) : description;
        }
    }

    private static void ShowRowMenu(Widget anchor, params (string Label, Action Action)[] items)
    {
        var menu = new VerticalMenu();
        foreach ((string label, Action action) in items)
        {
            var item = new MenuItem { Text = label };
            if (action != null)
            {
                Action captured = action;
                item.Selected += (_, _) => captured();
            }
            menu.Items.Add(item);
        }
        anchor.Desktop?.ShowContextMenu(menu, anchor.ToGlobal(new Point(0, anchor.Bounds.Height)));
    }

    private static List<HotkeyRow> BuildRows()
    {
        var rows = new List<HotkeyRow>();

        foreach (HotKeyEntry entry in HotKeys.AllRegistered()
                     .OrderBy(e => e.Category ?? string.Empty)
                     .ThenBy(e => e.Name))
        {
            HotKeyEntry e = entry;
            rows.Add(new HotkeyRow
            {
                Name = e.Name,
                Category = e.Category ?? string.Empty,
                Source = e,
                GetBinding = () => e.Binding ?? new HotkeyBinding(),
                Apply = b => e.Binding = b,
                ClearBinding = () => e.Binding?.Clear(),
                ResetBinding = e.ResetToDefault,
                SupportsEnable = true,
                GetEnabled = () => e.Enabled,
                SetEnabled = v => e.Enabled = v,
                CheckConflicts = e.CheckConflicts,
            });
        }

        MacroManager? manager = World.Instance?.Macros;
        if (manager != null)
        {
            MacroManager macros = manager;
            foreach (Macro m in macros.GetAllMacros().OrderBy(x => x.Name))
            {
                rows.Add(BuildMacroRow(macros, m));
            }
        }

        return rows;
    }

    private static HotkeyRow BuildMacroRow(MacroManager macros, Macro macro) => new()
    {
        Name = macro.Name,
        Category = TazLang.Get("hotkeys_macro_category", "Macro"),
        Source = macro,
        GetBinding = macro.GetBinding,
        Apply = binding =>
        {
            macro.ApplyBinding(binding);
            macros.Save();
        },
        ClearBinding = () =>
        {
            macro.ApplyBinding(new HotkeyBinding());
            macros.Save();
        }
    };

    internal static void CaptureMacroBinding(AgentPageInteraction interaction, Widget anchor, MacroManager manager,
        Macro macro, Action refresh) => CaptureDesktopBinding(interaction, anchor, BuildMacroRow(manager, macro), _ => refresh());

    internal static void CaptureScriptBinding(AgentPageInteraction interaction, Widget anchor,
        ClassicUO.LegionScripting.ScriptFile script, Action refresh)
    {
        var source = HotKeys.Get("lscript:" + script.RelativePath);
        CaptureDesktopBinding(interaction, anchor, new HotkeyRow
        {
            Name = script.FileName,
            Category = "Legion Scripts",
            Source = (object?)source ?? script,
            GetBinding = () => ClassicUO.LegionScripting.ScriptHotkeysManager.GetBinding(script),
            Apply = binding =>
            {
                ClassicUO.LegionScripting.ScriptHotkeysManager.SetBinding(script, binding);
                HotKeys.Save();
            }
        }, _ => refresh());
    }

    private static void CaptureDesktopBinding(AgentPageInteraction interaction, Widget anchor, HotkeyRow? row,
        Action<HotkeyRow> refresh, IReadOnlyList<HotkeyRow>? available = null)
    {
        HotkeyBinding original = row?.GetBinding().Clone() ?? new HotkeyBinding();
        HotkeyBinding pending = original;
        HotkeyRow[] conflicts = [];
        var content = new VerticalStackPanel { Spacing = 8 };
        var bindingLabel = new MyraLabel("", MyraLabel.TextStyle.H3);
        var conflictLabel = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true };
        var save = new MyraButton(TazLang.Get("hotkeycapture_save"));
        var replace = new MyraButton(TazLang.Get("hotkeys_replace", "Replace binding"));
        var clear = DesktopAssistantForm.Button(TazLang.Get("hotkeycapture_clear"), () => UpdateBinding(new HotkeyBinding()));
        var reset = DesktopAssistantForm.Button(TazLang.Get("hotkeycapture_reset"), () => UpdateBinding(original));
        if (available != null)
        {
            var actionPicker = new ComboView { MinWidth = 220 };
            actionPicker.ListView.Widgets.Add(new Label { Text = TazLang.Get("hotkeys_choose_action") });
            foreach (HotkeyRow choice in available)
                actionPicker.ListView.Widgets.Add(new Label { Text = $"{choice.Category} · {choice.Name}" });
            actionPicker.ListView.SelectedIndex = 0;
            actionPicker.ListView.SelectedIndexChanged += (_, _) =>
            {
                int index = actionPicker.ListView.SelectedIndex ?? 0;
                row = index > 0 ? available[index - 1] : null;
                UpdateBinding(original);
            };
            content.Widgets.Add(new MyraLabel(TazLang.Get("hotkeys_action"), MyraLabel.TextStyle.P));
            content.Widgets.Add(actionPicker);
        }
        else if (row != null)
            content.Widgets.Add(new MyraLabel(row.Name, MyraLabel.TextStyle.P) { Wrap = true });
        content.Widgets.Add(new MyraLabel(TazLang.Get("hotkeycapture_instructions",
            "Press a key, side mouse button, wheel or controller button."), MyraLabel.TextStyle.P) { Wrap = true });
        content.Widgets.Add(bindingLabel);
        content.Widgets.Add(conflictLabel);
        var dialog = AgentPageInteraction.CreateDesktopDialog(TazLang.Get(available != null ? "hotkeys_add" : "hotkeycapture_title"),
            content, [save, replace], available == null ? [clear, reset] : null);
        save.OnClick = Commit;
        replace.OnClick = Commit;
        MyraStyle.ApplyButtonDangerStyle(replace);
        UpdateBinding(original);
        interaction.ShowDesktopDialog(anchor, dialog, UpdateBinding,
            captureReady: available == null ? null : () => row != null && anchor.Desktop?.ContextMenu == null);

        void UpdateBinding(HotkeyBinding binding)
        {
            pending = binding;
            bindingLabel.Text = pending.IsEmpty && available != null ? TazLang.Get("hotkeys_unassigned") : pending.Describe();
            conflicts = row is { CheckConflicts: true } && !binding.IsEmpty
                ? BuildRows().Where(other => !ReferenceEquals(other.Source, row.Source)
                    && other.CheckConflicts && binding.Matches(other.GetBinding())).ToArray()
                : [];
            bool requiresTrigger = row?.Category == "Legion Scripts" || available != null && row?.Source is HotKeyEntry { OnPressed: not null };
            bool invalidActionBinding = requiresTrigger && !binding.IsEmpty && !binding.IsTriggerable;
            bool modifierOnlyMacro = row?.Source is Macro && !binding.IsEmpty && !binding.IsTriggerable && !binding.WheelScroll;
            conflictLabel.Text = invalidActionBinding
                ? TazLang.Get("hotkeys_input_required")
                : modifierOnlyMacro
                ? TazLang.Get("macrostab_hotkey_modifiersonly", "Macros cannot be bound to modifiers alone.")
                : conflicts.Length == 0
                    ? ""
                    : TazLang.GetEx("hotkeys_assigned_to", "Already assigned to {0}. Replacing will clear those bindings.",
                        [string.Join(", ", conflicts.Select(other => $"{other.Category} · {other.Name}"))]);
            conflictLabel.Visible = conflictLabel.Text.Length > 0;
            replace.Visible = !invalidActionBinding && !modifierOnlyMacro && conflicts.Length > 0;
            save.Visible = !replace.Visible;
            save.Enabled = row != null && (available == null || !binding.IsEmpty) && !invalidActionBinding && !modifierOnlyMacro;
        }

        void Commit()
        {
            if (row == null)
                return;

            dialog.Close();
            foreach (HotkeyRow conflict in conflicts)
                conflict.ClearBinding();
            row.Apply(pending);
            if (row.Source is HotKeyEntry || conflicts.Any(conflict => conflict.Source is HotKeyEntry))
                HotKeys.Save();
            refresh(row);
        }
    }
}
