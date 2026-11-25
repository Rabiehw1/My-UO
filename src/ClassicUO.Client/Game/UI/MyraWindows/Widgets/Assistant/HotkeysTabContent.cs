#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
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
        var root = new VerticalStackPanel { Spacing = 6 };
        if (!interaction.IsDesktop)
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

            var grid = new MyraGrid { MaxWidth = interaction.IsDesktop ? null : 800 };
            grid.SetupWithHeaders(
                GridColumnInfo.Auto("Category"),
                GridColumnInfo.Auto("Name"),
                GridColumnInfo.Auto("Binding"),
                GridColumnInfo.Auto("Conflicts"),
                GridColumnInfo.Auto(""),
                GridColumnInfo.Auto(""),
                GridColumnInfo.Auto("On")
            );

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

                grid.AddWidget(new MyraLabel(r.Category ?? string.Empty, MyraLabel.TextStyle.P), row, 0);
                grid.AddWidget(new MyraLabel(r.Name, MyraLabel.TextStyle.P), row, 1);

                grid.AddWidget(new MyraLabel(description, MyraLabel.TextStyle.P), row, 2);

                var conflictLabel = new MyraLabel(conflicts.Count > 0 ? "Yes" : string.Empty, MyraLabel.TextStyle.P);
                if (conflicts.Count > 0)
                    conflictLabel.Tooltip = "Conflicts with: " + string.Join(", ", conflicts);
                grid.AddWidget(conflictLabel, row, 3);

                var setButton = new MyraButton(TazLang.Get("mog_kw_set", "Set"));
                setButton.Click += (_, _) => StartCapture(setButton, localRow);
                grid.AddWidget(setButton, row, 4);

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
                });
                grid.AddWidget(menuButton, row, 5);

                if (localRow.SupportsEnable)
                {
                    grid.AddWidget(MyraCheckButton.CreateWithCallback(
                        localRow.GetEnabled(),
                        b =>
                        {
                            localRow.SetEnabled(b);
                            if (interaction.IsDesktop)
                                HotKeys.Save();
                        },
                        null,
                        "Enable or disable this hotkey"), row, 6);
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
            if (interaction.IsDesktop)
                HotKeys.Save();
            BuildList();
        }

        void StartCapture(Widget anchor, HotkeyRow row)
        {
            if (interaction.IsDesktop)
            {
                CaptureDesktopBinding(interaction, anchor, row, BuildList);
                return;
            }

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
        resetAll.Click += (_, _) =>
        {
            if (interaction.IsDesktop)
                interaction.Confirm(resetAll, resetAll.Text,
                    TazLang.Get("hotkeys_reset_confirm", "Restore all registered hotkeys to their defaults?"), ResetAll);
            else
                ResetAll();
        };
        topBar.Widgets.Add(resetAll);
        root.Widgets.Add(topBar);

        root.Widgets.Add(interaction.IsDesktop ? listPanel : new ScrollViewer { Height = 360, Content = listPanel });

        return root;

        void ResetAll()
        {
            foreach (HotKeyEntry entry in HotKeys.AllRegistered())
                entry.ResetToDefault();
            SaveAndRefresh();
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
        Macro macro, Action refresh) => CaptureDesktopBinding(interaction, anchor, BuildMacroRow(manager, macro), refresh);

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
        }, refresh);
    }

    private static void CaptureDesktopBinding(AgentPageInteraction interaction, Widget anchor, HotkeyRow row, Action refresh)
    {
        HotkeyBinding original = row.GetBinding().Clone();
        HotkeyBinding pending = original;
        HotkeyRow[] conflicts = [];
        var dialog = new Window { Title = TazLang.Get("hotkeycapture_title", "Set Hotkey") };
        var content = new VerticalStackPanel { Spacing = 8, Padding = new Thickness(8), Width = 400 };
        var bindingLabel = new MyraLabel("", MyraLabel.TextStyle.H3);
        var conflictLabel = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true };
        var buttons = new HorizontalStackPanel { Spacing = 4 };
        var save = new MyraButton(TazLang.Get("hotkeycapture_save", "Save"), Commit);
        var replace = new MyraButton(TazLang.Get("hotkeys_replace", "Replace binding"), Commit);
        buttons.Widgets.Add(save);
        buttons.Widgets.Add(replace);
        buttons.Widgets.Add(new MyraButton(TazLang.Get("hotkeycapture_clear", "Clear"), () => UpdateBinding(new HotkeyBinding())));
        buttons.Widgets.Add(new MyraButton(TazLang.Get("hotkeycapture_reset", "Reset"), () => UpdateBinding(original)));
        buttons.Widgets.Add(new MyraButton(TazLang.Get("hotkeycapture_cancel", "Cancel"), dialog.Close));

        content.Widgets.Add(new MyraLabel(row.Name, MyraLabel.TextStyle.P) { Wrap = true });
        content.Widgets.Add(new MyraLabel(TazLang.Get("hotkeycapture_instructions",
            "Press a key, side mouse button, wheel or controller button."), MyraLabel.TextStyle.P) { Wrap = true });
        content.Widgets.Add(bindingLabel);
        content.Widgets.Add(conflictLabel);
        content.Widgets.Add(buttons);
        dialog.Content = content;
        UpdateBinding(original);
        interaction.ShowDesktopDialog(anchor, dialog, UpdateBinding);

        void UpdateBinding(HotkeyBinding binding)
        {
            pending = binding;
            bindingLabel.Text = pending.Describe();
            conflicts = row.CheckConflicts && !binding.IsEmpty
                ? BuildRows().Where(other => !ReferenceEquals(other.Source, row.Source)
                    && other.CheckConflicts && binding.Matches(other.GetBinding())).ToArray()
                : [];
            bool invalidScriptBinding = row.Category == "Legion Scripts" && !binding.IsEmpty && !binding.IsTriggerable;
            bool modifierOnlyMacro = row.Source is Macro && !binding.IsEmpty && !binding.IsTriggerable && !binding.WheelScroll;
            conflictLabel.Text = invalidScriptBinding
                ? "Use a keyboard, mouse button, or controller button combination."
                : modifierOnlyMacro
                ? TazLang.Get("macrostab_hotkey_modifiersonly", "Macros cannot be bound to modifiers alone.")
                : conflicts.Length == 0
                    ? ""
                    : TazLang.GetEx("hotkeys_assigned_to", "Already assigned to {0}. Replacing will clear those bindings.",
                        [string.Join(", ", conflicts.Select(other => $"{other.Category} · {other.Name}"))]);
            conflictLabel.Visible = conflictLabel.Text.Length > 0;
            replace.Visible = !invalidScriptBinding && !modifierOnlyMacro && conflicts.Length > 0;
            save.Visible = !replace.Visible;
            save.Enabled = !invalidScriptBinding && !modifierOnlyMacro;
        }

        void Commit()
        {
            dialog.Close();
            foreach (HotkeyRow conflict in conflicts)
                conflict.ClearBinding();
            row.Apply(pending);
            if (row.Source is HotKeyEntry || conflicts.Any(conflict => conflict.Source is HotKeyEntry))
                HotKeys.Save();
            refresh();
        }
    }
}
