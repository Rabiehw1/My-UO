#nullable enable
using ClassicUO.Common.Enums;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Hotkeys;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Search;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Macros;

public static class MacrosTabContent
{
    internal static string Title => TazLang.Get("assistantwindow_tab_macros", "Macros");

    private static readonly HashSet<MacroType> _filteredMacroTypes = [MacroType.INVALID, MacroType.REMOVED, MacroType.REMOVED2, MacroType.REMOVED3];

    /// <summary>Macro types whose sub-type dropdown is a list of available gumps (shared sub-type range).</summary>
    private static readonly HashSet<MacroType> _gumpListMacroTypes = [MacroType.Open, MacroType.Close, MacroType.Minimize, MacroType.Maximize, MacroType.ToggleGump];
    private static readonly string[] _sortedMacroTypeNames;
    private static readonly MacroType[] _sortedMacroTypeValues;
    private static readonly Dictionary<MacroType, int> _macroTypeToDisplayIndex;

    static MacrosTabContent()
    {
        MacroType[] macroTypes = Enum.GetValues<MacroType>()
            .Where(t => !_filteredMacroTypes.Contains(t))
            .OrderBy(t => t == MacroType.None ? "" : t.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _sortedMacroTypeNames = macroTypes
            .Select(t => StringHelper.AddSpaceBeforeCapital(t.ToString()))
            .ToArray();
        _sortedMacroTypeValues = macroTypes;

        _macroTypeToDisplayIndex = new Dictionary<MacroType, int>();
        for (int i = 0; i < macroTypes.Length; i++)
            _macroTypeToDisplayIndex[macroTypes[i]] = i;
    }

    public static Widget Build(MyraControl owner) => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        Profile? profile = ProfileManager.CurrentProfile;
        MacroManager? manager = World.Instance?.Macros;
        if (profile == null || manager == null)
            return new MyraLabel(TazLang.Get("macrostab_noprofile"), MyraLabel.TextStyle.P);

        // ── State ────────────────────────────────────────────────────────────
        Macro? selectedMacro = interaction.IsDesktop ? manager.GetAllMacros().FirstOrDefault() : null;
        bool refreshingPicker = false;
        MyraButton[] selectionActions = [];

        // ── Panel references ─────────────────────────────────────────────────
        var macroListPanel = new VerticalStackPanel { Spacing = 1 };
        var macroPicker = interaction.IsDesktop ? new ListView() : null;
        var filterBox = new MyraInputBox
        {
            HintText = TazLang.Get(interaction.IsDesktop ? "macrostab_search" : "macrostab_filter")
        };
        var editorPanel    = new VerticalStackPanel { Spacing = 4 };
        var editorLayout = new MyraGrid { RowSpacing = 4 };
        editorLayout.AddRow();
        editorLayout.AddRow(new Proportion(ProportionType.Fill));
        var hotkeyRow      = new HorizontalStackPanel { Spacing = 4 };
        var actionsPanel   = new VerticalStackPanel { Spacing = 2 };
        var actionScroll = new ScrollViewer();
        if (interaction.IsDesktop)
        {
            actionScroll.Content = actionsPanel;
            actionScroll.ShowHorizontalScrollBar = false;
        }
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false, Wrap = true };
        var actionCount = new MyraLabel("", MyraLabel.TextStyle.P) { TextColor = DesktopAssistantTheme.SecondaryText };

        // ── Helpers ──────────────────────────────────────────────────────────
        void MarkDirty() => manager.Save();

        void ReportStatus(string text, ushort hue)
        {
            if (interaction.IsDesktop)
            {
                status.Text = text;
                status.Visible = true;
            }
            else
                GameActions.Print(text, hue);
        }

        // ── BuildMacroList ────────────────────────────────────────────────────
        void BuildMacroList()
        {
            string filterText = filterBox.Text?.Trim() ?? "";
            List<Macro> allMacros = manager.GetAllMacros();

            List<Macro> display = string.IsNullOrWhiteSpace(filterText)
                ? allMacros
                : allMacros.Where(m =>
                    m.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                    m.GetBinding().Describe().Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (macroPicker != null)
            {
                refreshingPicker = true;
                Point scrollPosition = macroPicker.ScrollViewer.ScrollPosition;
                macroPicker.Widgets.Clear();
                foreach (Macro macro in display)
                {
                    var binding = macro.GetBinding();
                    var item = DesktopAssistantForm.ListRow(macro.Name,
                        binding.IsEmpty ? null : MyraGrid.TableText(binding.Describe(), alignRight: true));
                    item.Tag = macro;
                    macroPicker.Widgets.Add(item);
                }

                if (display.Count == 0)
                    macroPicker.Widgets.Add(new MyraLabel(TazLang.Get(allMacros.Count == 0 ? "macrostab_nomacros" : "macrostab_no_matches"),
                        MyraLabel.TextStyle.P));
                int selectedIndex = display.FindIndex(macro => ReferenceEquals(macro, selectedMacro));
                macroPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : null;
                macroPicker.ScrollViewer.ScrollPosition = scrollPosition;
                refreshingPicker = false;
                return;
            }

            macroListPanel.Widgets.Clear();
            if (display.Count == 0)
            {
                macroListPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_nomacros"), MyraLabel.TextStyle.P));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(
                GridColumnInfo.Fill(TazLang.Get("macrostab_col_name")),
                GridColumnInfo.Auto(TazLang.Get("macrostab_col_hotkey")),
                GridColumnInfo.Auto(TazLang.Get("macrostab_col_edit"))
            );

            int dataRow = 1;
            foreach (Macro macro in display)
            {
                Macro captured = macro;

                grid.AddWidget(MyraGrid.TableText(macro.Name), dataRow, 0);
                grid.AddWidget(MyraGrid.TableText(macro.GetBinding().Describe()), dataRow, 1);
                grid.AddWidget(new MyraButton(TazLang.Get("macrostab_col_edit"), () => SelectMacro(captured)), dataRow, 2);
                dataRow++;
            }

            macroListPanel.Widgets.Add(grid);
        }

        void SelectMacro(Macro? macro)
        {
            selectedMacro = macro;
            foreach (MyraButton action in selectionActions)
                action.Enabled = macro != null;
            status.Visible = false;
            actionScroll.ResetScroll();
            if (!interaction.IsDesktop)
                BuildMacroList();
            BuildEditor();
        }

        MyraButton Button(string key, Action? click = null, bool primary = false, bool destructive = false)
        {
            string caption = TazLang.Get(key);
            if (interaction.IsDesktop)
                return DesktopAssistantForm.Button(caption, click, primary, destructive);

            var button = new MyraButton(caption, click);
            if (destructive)
                MyraStyle.ApplyButtonDangerStyle(button);
            return button;
        }

        // ── BuildEditor ───────────────────────────────────────────────────────
        void BuildEditor()
        {
            editorPanel.Widgets.Clear();
            editorLayout.Widgets.Clear();

            if (selectedMacro == null)
            {
                editorPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_selectmacro"), MyraLabel.TextStyle.H3));
                if (interaction.IsDesktop)
                {
                    editorPanel.Widgets.Add(status);
                    editorLayout.AddWidget(editorPanel, 0, 0);
                }
                return;
            }

            Macro macro = selectedMacro;

            if (interaction.IsDesktop)
            {
                var heading = MyraGrid.TableText(macro.Name);
                heading.Font = DesktopAssistantTheme.Font(2, bold: true);
                editorPanel.Widgets.Add(heading);
                editorPanel.Widgets.Add(status);
            }
            else
            {
                var nameRow = new HorizontalStackPanel { Spacing = 2 };
                nameRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_macroname"), MyraLabel.TextStyle.P));
                var nameBox = new MyraInputBox { Text = macro.Name, Width = 200 };
                nameBox.TextChangedByUser += (_, _) =>
                {
                    macro.Name = nameBox.Text ?? "";
                    MarkDirty();
                };
                nameRow.Widgets.Add(nameBox);
                editorPanel.Widgets.Add(nameRow);
                editorPanel.Widgets.Add(new MyraSpacer(10, 2));
            }
            BuildHotkeyRow();

            var journalBox = new MyraInputBox
            {
                Text = macro.JournalTriggers ?? "", Width = interaction.IsDesktop ? null : 260,
                HintText = interaction.IsDesktop ? TazLang.Get("macrostab_journal_hint") : "",
                Tooltip = TazLang.Get("macrostab_journaltriggers_tooltip")
            };
            journalBox.TextChangedByUser += (_, _) =>
            {
                macro.JournalTriggers = journalBox.Text ?? "";
                MarkDirty();
            };
            var createButton = Button("macrostab_createbutton", () => interaction.RunInGame(() =>
            {
                foreach (IGui? gump in UIManager.Gumps)
                    if (gump is MacroButtonGump mbg && mbg.TheMacro == macro)
                    {
                        mbg.Dispose();
                        break;
                    }
                var macroButtonGump = new MacroButtonGump(World.Instance, macro, 0, 0);
                macroButtonGump.CenterXInViewPort();
                macroButtonGump.CenterYInViewPort();
                UIManager.Add(macroButtonGump);
            }));
            createButton.Tooltip = TazLang.Get("macrostab_createbutton_tooltip");
            var buttonEditor = Button("macrostab_buttoneditor", () => interaction.RunInGame(() => MacroButtonEditorWindow.Show(macro)));
            buttonEditor.Tooltip = TazLang.Get("macrostab_buttoneditor_tooltip");

            BuildActionsPanel();
            var addAction = Button("macrostab_addaction", () => AppendAction(macro, Macro.Create(MacroType.Say)), primary: true);
            var addLoop = Button("macrostab_addloop", () => AppendAction(macro,
                Macro.CreateLoopContainer(loopCount: 1, delayBetweenIterations: 500)));

            if (interaction.IsDesktop)
            {
                var triggers = DesktopAssistantForm.Fields(
                    new(TazLang.Get("macrostab_hotkey_label"), hotkeyRow),
                    new(TazLang.Get("macrostab_journal_label"), journalBox));
                journalBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                editorPanel.Widgets.Add(new DesktopAssistantForm.SectionPair(
                    DesktopAssistantTheme.Group(TazLang.Get("macrostab_triggers"), triggers),
                    DesktopAssistantTheme.Group(TazLang.Get("macrostab_ingame_button"), DesktopAssistantForm.Actions(createButton, buttonEditor)),
                    firstWeight: 7, secondWeight: 4));

                var actionHeader = new MyraGrid { Background = DesktopAssistantTheme.Sidebar, Padding = new Thickness(6, 3) };
                actionHeader.AddColumn(new Proportion(ProportionType.Fill));
                actionHeader.AddColumn();
                actionHeader.AddWidget(new MyraLabel(TazLang.Get("macrostab_actions_heading"), MyraLabel.TextStyle.H3), 0, 0);
                actionHeader.AddWidget(actionCount, 0, 1);
                var actionTools = DesktopAssistantForm.Actions(addAction, addLoop);
                actionTools.Background = DesktopAssistantTheme.Sidebar;
                actionTools.Border = DesktopAssistantTheme.Divider;
                actionTools.BorderThickness = new Thickness(0, 1, 0, 0);
                actionTools.Padding = new Thickness(6);
                var editorActions = new MyraGrid
                {
                    RowSpacing = 0, Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(1)
                };
                editorActions.AddRow();
                editorActions.AddRow(new Proportion(ProportionType.Fill));
                editorActions.AddRow();
                editorActions.AddWidget(actionHeader, 0, 0);
                editorActions.AddWidget(actionScroll, 1, 0);
                editorActions.AddWidget(actionTools, 2, 0);

                editorLayout.AddWidget(editorPanel, 0, 0);
                editorLayout.AddWidget(editorActions, 1, 0);
            }
            else
            {
                editorPanel.Widgets.Add(hotkeyRow);
                editorPanel.Widgets.Add(new MyraSpacer(10, 2));
                var journalRow = new HorizontalStackPanel { Spacing = 2 };
                journalRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_journaltriggers"), MyraLabel.TextStyle.P)
                {
                    Tooltip = journalBox.Tooltip
                });
                journalRow.Widgets.Add(journalBox);
                editorPanel.Widgets.Add(journalRow);
                editorPanel.Widgets.Add(new MyraSpacer(10, 2));
                var macroButtons = new HorizontalStackPanel { Spacing = 2 };
                macroButtons.Widgets.Add(createButton);
                macroButtons.Widgets.Add(buttonEditor);
                editorPanel.Widgets.Add(macroButtons);
                editorPanel.Widgets.Add(new MyraSpacer(10, 2));
                editorPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_actions"), MyraLabel.TextStyle.P));
                editorPanel.Widgets.Add(new ScrollViewer { MaxHeight = 250, Content = actionsPanel });

                var delete = Button("macrostab_deletemacro", destructive: true);
                delete.Tooltip = TazLang.Get("macrostab_deletemacro_tooltip");
                delete.OnClick = () => ConfirmDelete(delete, macro);
                var bottomRow = new HorizontalStackPanel { Spacing = 2 };
                bottomRow.Widgets.Add(addAction);
                bottomRow.Widgets.Add(addLoop);
                bottomRow.Widgets.Add(delete);
                editorPanel.Widgets.Add(bottomRow);
            }
        }

        void AppendAction(Macro macro, MacroObject action)
        {
            MacroObject? last = null;
            for (var current = (MacroObject?)macro.Items; current != null && current.Code != MacroType.None; current = (MacroObject?)current.Next)
                last = current;

            if (last != null)
                macro.Insert(last, action);
            else
            {
                macro.Items = action;
                action.Next = new MacroObject(MacroType.None, MacroSubType.MSC_NONE);
            }
            MarkDirty();
            BuildActionsPanel();
            if (interaction.IsDesktop)
            {
                actionScroll.UpdateArrange();
                actionScroll.ScrollPosition = new Point(0, actionScroll.ScrollMaximum.Y);
            }
        }

        void ConfirmDelete(MyraButton anchor, Macro macro)
        {
            interaction.Confirm(anchor, TazLang.Get("macrostab_delete_title", [macro.Name]),
                TazLang.Get("macrostab_delete_confirm", [macro.Name]), () =>
                {
                    var next = macro.Next as Macro ?? macro.Previous as Macro;
                    manager.Remove(macro);
                    MarkDirty();
                    SelectMacro(interaction.IsDesktop ? next : null);
                    if (interaction.IsDesktop)
                        BuildMacroList();
                }, confirmText: TazLang.Get("delete"), destructive: true);
        }

        // ── BuildHotkeyRow ────────────────────────────────────────────────────
        void BuildHotkeyRow()
        {
            hotkeyRow.Widgets.Clear();

            if (selectedMacro == null) return;
            Macro macro = selectedMacro;

            if (interaction.IsDesktop)
            {
                var set = Button("hotkeys_set_shortcut");
                set.OnClick = () => HotkeysTabContent.CaptureMacroBinding(interaction, set, manager, macro, RefreshBinding);
                var binding = macro.GetBinding();
                hotkeyRow.Widgets.Add(MyraGrid.TableText(binding.IsEmpty ? TazLang.Get("hotkeys_unassigned") : binding.Describe()));
                hotkeyRow.Widgets.Add(set);
                return;
            }

            var hotkeyInput = new HotkeyInput.HotkeyInput(
                TazLang.Get("macrostab_hotkey"),
                macro.GetBinding(),
                e =>
                {
                    macro.ApplyBinding(e.NewValue);
                    MarkDirty();
                    BuildMacroList();
                },
                true,
                newBinding =>
                {
                    // Macro dispatch looks up a key, mouse button, wheel direction or controller combo;
                    // a modifier-only binding would be saved but could never fire.
                    if (newBinding is { HasKey: false, HasMouseButton: false, WheelScroll: false, HasController: false })
                    {
                        GameActions.Print(World.Instance, TazLang.Get("macrostab_hotkey_modifiersonly"), Constants.HUE_ERROR);
                        return false;
                    }

                    Macro? conflict = World.Instance?.Macros?.GetAllMacros()
                        .FirstOrDefault(m => m != macro && m.GetBinding().Matches(newBinding));

                    if (conflict == null)
                        return true;

                    GameActions.Print(World.Instance, TazLang.Get("macrostab_hotkey_inuse", [conflict.Name]), Constants.HUE_ERROR);
                    return false;
                }
            );

            hotkeyRow.Widgets.Add(hotkeyInput);

            void RefreshBinding()
            {
                BuildHotkeyRow();
                BuildMacroList();
            }
        }

        // ── BuildActionsPanel ─────────────────────────────────────────────────
        void BuildActionsPanel()
        {
            actionsPanel.Widgets.Clear();

            if (selectedMacro == null) return;
            Macro macro = selectedMacro;

            var action = (MacroObject)macro.Items;
            int actionIndex = 0;
            bool hasCastSpell = false;

            while (action != null && action.Code != MacroType.None)
            {
                if (action is MacroLoopContainer loopContainer)
                {
                    hasCastSpell |= loopContainer.Items.Any(item => item.Code == MacroType.CastSpell);
                    BuildLoopContainerWidget(loopContainer, macro, actionIndex);
                    actionIndex++;
                }
                else
                {
                    hasCastSpell |= action.Code == MacroType.CastSpell;
                    MacroObject capturedAction = action;
                    int capturedIndex = actionIndex;

                    Container row = BuildActionRow(
                        capturedAction,
                        $"{capturedIndex + 1}.",
                        onTypeReplace: newAction =>
                        {
                            macro.Insert(capturedAction, newAction);
                            macro.Remove(capturedAction);
                        },
                        onTextReplace: strAction =>
                        {
                            strAction.Next = capturedAction.Next;
                            var scan = (MacroObject)macro.Items;
                            MacroObject? prev = null;
                            while (scan != null && scan != capturedAction)
                            {
                                prev = scan;
                                scan = (MacroObject)scan.Next;
                            }
                            if (prev != null) prev.Next = strAction;
                            else macro.Items = strAction;
                        },
                        onRemove: () => macro.Remove(capturedAction)
                    );

                    actionsPanel.Widgets.Add(row);
                    actionIndex++;
                }

                action = (MacroObject)action.Next;
            }

            if (interaction.IsDesktop)
                actionCount.Text = actionIndex == 1 ? TazLang.Get("macrostab_one_step")
                    : TazLang.Get("macrostab_step_count", [actionIndex.ToString()]);
            if (actionIndex == 0)
                actionsPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_noactions"), MyraLabel.TextStyle.H3));
            if (hasCastSpell)
                actionsPanel.Widgets.Insert(0, new MyraLabel(
                    TazLang.Get("macrostab_set_spell_book", "You must set your spell book in the Spell Books tab."),
                    MyraLabel.TextStyle.P) { Wrap = true });
        }

        Container BuildActionRow(
            MacroObject action,
            string indexLabel,
            Action<MacroObject> onTypeReplace,   // combo changed -> swap + rebuild
            Action<MacroObject> onTextReplace,   // text typed on non-string action -> swap silently
            Action onRemove)
        {
            Container row = interaction.IsDesktop
                ? new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 3, UniformSizing = false }
                : new HorizontalStackPanel { Spacing = 2 };
            if (!interaction.IsDesktop)
                row.Widgets.Add(new MyraLabel(indexLabel, MyraLabel.TextStyle.P));

            MacroObject capturedAction = action;
            MyraInputBox? textEditor = null;

            var typeCombo = new ContainsLevenshteinComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                TooltipSelector = s => s
            };

            MyraStyle.ApplySearchComboBoxPopupBorder(typeCombo);

            foreach (string typeName in _sortedMacroTypeNames)
                typeCombo.Items.Add(typeName);

            int displayIdx = _macroTypeToDisplayIndex.GetValueOrDefault(capturedAction.Code, 0);
            typeCombo.SelectedIndex = displayIdx;
            typeCombo.SelectedItemChanged += (_, _) =>
            {
                if (typeCombo.SelectedIndex == null) return;
                MacroType newType = _sortedMacroTypeValues[typeCombo.SelectedIndex.Value];
                if (newType == capturedAction.Code) return;

                MacroObject newAction = newType == MacroType.CastSpell
                    ? Macro.CreateShardSpellAction()
                    : Macro.Create(newType);
                onTypeReplace(newAction);
                MarkDirty();

                // ContainsLevenshteinComboBox hides its popup before raising SelectedItemChanged,
                // so a synchronous rebuild here is safe (unlike the old obsolete ComboBox).
                BuildActionsPanel();
            };
            row.Widgets.Add(typeCombo);

            // Sub-type dropdown
            if (capturedAction.Code == MacroType.CastSpell)
            {
                AddSpellSelectors(row, capturedAction, onTypeReplace);
            }
            else if (capturedAction.SubMenuType == 1)
            {
                int subCount = 0, subOffset = 0;
                Macro.GetBoundByCode(capturedAction.Code, ref subCount, ref subOffset);

                var subValues = new MacroSubType[subCount];
                string[] subNames = new string[subCount];
                for (int si = 0; si < subCount; si++)
                {
                    subValues[si] = (MacroSubType)(si + subOffset);
                    subNames[si] = Macro.GetSubTypeName(capturedAction.Code, subValues[si]);
                }

                // The gump-list macro types (Open/Close/Minimize/Maximize/ToggleGump) show a list
                // of available gumps. Sort that list the same way as the main macro list and add
                // spacing after capitals for legibility; other sub-type lists keep their natural order.
                if (_gumpListMacroTypes.Contains(capturedAction.Code))
                {
                    int[] order = Enumerable.Range(0, subCount)
                        .OrderBy(i => subNames[i], StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    subValues = order.Select(i => subValues[i]).ToArray();
                    subNames = order.Select(i => StringHelper.AddSpaceBeforeCapital(subNames[i])).ToArray();
                }

                int curSubIdx = Array.IndexOf(subValues, capturedAction.SubCode);
                if (curSubIdx < 0) curSubIdx = 0;

                var subCombo = new ContainsLevenshteinComboBox { VerticalAlignment = VerticalAlignment.Center };
                MyraStyle.ApplySearchComboBoxPopupBorder(subCombo);

                foreach (string subName in subNames)
                    subCombo.Items.Add(subName);

                subCombo.SelectedIndex = curSubIdx;
                subCombo.SelectedItemChanged += (_, _) =>
                {
                    if (subCombo.SelectedIndex == null) return;
                    capturedAction.SubCode = subValues[subCombo.SelectedIndex.Value];
                    MarkDirty();
                };
                row.Widgets.Add(subCombo);
            }
            // Text input
            else if (capturedAction.SubMenuType == 2)
            {
                string currentText = capturedAction.HasString()
                    ? ((MacroObjectString)capturedAction).Text
                    : "";
                var textBox = new MyraInputBox { Text = currentText, Width = 180 };
                textEditor = textBox;
                textBox.TextChangedByUser += (_, _) =>
                {
                    string newText = textBox.Text ?? "";
                    if (capturedAction.HasString())
                    {
                        ((MacroObjectString)capturedAction).Text = newText;
                    }
                    else
                    {
                        var strAction = new MacroObjectString(capturedAction.Code, capturedAction.SubCode, newText);
                        onTextReplace(strAction);
                        // no BuildActionsPanel() here
                    }
                    MarkDirty();
                };
                row.Widgets.Add(textBox);
            }

            // Remove button
            var remove = Button("macrostab_removeaction", () =>
            {
                onRemove();
                MarkDirty();
                BuildActionsPanel();
            }, destructive: true);
            remove.Tooltip = TazLang.Get("macrostab_removeaction_tooltip");
            if (row is WrapPanel fields)
                return new DesktopActionRow(indexLabel, fields, typeCombo, textEditor, remove);

            row.Widgets.Add(remove);
            return row;
        }

        void AddSpellSelectors(Container row, MacroObject action, Action<MacroObject> replace)
        {
            string selectSpell = TazLang.Get("macrostab_select_spell", "Select spell");
            var bookPicker = new ContainsLevenshteinComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                PlaceholderText = TazLang.Get("macrostab_select_book", "Select book")
            };
            var spellPicker = new ContainsLevenshteinComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                PlaceholderText = action is MacroObjectString
                    ? selectSpell
                    : $"Saved spell · {Macro.GetSubTypeName(action.Code, action.SubCode)}",
                Enabled = false
            };
            MyraStyle.ApplySearchComboBoxPopupBorder(bookPicker);
            MyraStyle.ApplySearchComboBoxPopupBorder(spellPicker);
            foreach (ShardSpellBookInfo entry in ShardSpellCatalog.Books)
                bookPicker.Items.Add(entry.Name);

            if (action is MacroObjectString saved
                && ShardSpellCatalog.TryParseMacroSelection(saved.Text, out ShardSpellBook selectedBook, out int selectedSpell))
            {
                bookPicker.SelectedIndex = (int)selectedBook;
                FillSpells(selectedBook, selectedSpell);
            }

            bookPicker.SelectedItemChanged += (_, _) =>
            {
                if (bookPicker.SelectedIndex is not int bookIndex)
                    return;

                var book = (ShardSpellBook)bookIndex;
                SaveSelection(book, -1);
                if (action is MacroObjectString)
                    FillSpells(book, -1);
            };
            spellPicker.SelectedItemChanged += (_, _) =>
            {
                if (bookPicker.SelectedIndex is int bookIndex && spellPicker.SelectedIndex is int spellIndex)
                    SaveSelection((ShardSpellBook)bookIndex, spellIndex);
            };
            row.Widgets.Add(bookPicker);
            row.Widgets.Add(spellPicker);

            void FillSpells(ShardSpellBook book, int index)
            {
                spellPicker.Items.Clear();
                spellPicker.PlaceholderText = selectSpell;
                foreach (ShardSpell spell in ShardSpellCatalog.Get(book).Spells)
                    spellPicker.Items.Add(spell.Name);

                spellPicker.SelectedIndex = index >= 0 ? index : null;
                spellPicker.Enabled = true;
            }

            void SaveSelection(ShardSpellBook book, int index)
            {
                string selection = ShardSpellCatalog.FormatMacroSelection(book, index);
                if (action is MacroObjectString custom)
                {
                    custom.Text = selection;
                    MarkDirty();
                    return;
                }

                replace(Macro.CreateShardSpellAction(selection));
                MarkDirty();
                BuildActionsPanel();
            }
        }

        void BuildLoopContainerWidget(MacroLoopContainer loopContainer, Macro macro, int loopIndex)
        {
            // Header row: Loop info and Edit/Remove buttons
            Container loopHeaderRow = interaction.IsDesktop
                ? DesktopAssistantForm.Actions()
                : new HorizontalStackPanel { Spacing = 4 };
            string loopTitle = TazLang.Get("macrostab_loop", [(loopIndex + 1).ToString()]);
            if (!interaction.IsDesktop)
                loopHeaderRow.Widgets.Add(new MyraLabel(loopTitle, MyraLabel.TextStyle.P));
            else
                loopHeaderRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_loop_repeat"), MyraLabel.TextStyle.P));

            var loopCountBox = new MyraInputBox { Text = loopContainer.LoopCount.ToString(), Width = 50 };
            loopCountBox.TextChangedByUser += (_, _) =>
            {
                if (int.TryParse(loopCountBox.Text, out int val) && val > 0)
                {
                    loopContainer.LoopCount = val;
                    MarkDirty();
                }
            };
            loopHeaderRow.Widgets.Add(loopCountBox);

            loopHeaderRow.Widgets.Add(new MyraLabel(TazLang.Get(interaction.IsDesktop ? "macrostab_loop_delay_label" : "macrostab_loop_delay"),
                MyraLabel.TextStyle.P));

            var delayBox = new MyraInputBox { Text = loopContainer.DelayBetweenIterations.ToString(), Width = 70 };
            delayBox.TextChangedByUser += (_, _) =>
            {
                if (int.TryParse(delayBox.Text, out int val) && val >= 0)
                {
                    loopContainer.DelayBetweenIterations = val;
                    MarkDirty();
                }
            };
            loopHeaderRow.Widgets.Add(delayBox);

            loopHeaderRow.Widgets.Add(Button("macrostab_removeloop", () =>
            {
                macro.Remove(loopContainer);
                MarkDirty();
                BuildActionsPanel();
            }, destructive: true));

            // Inner actions list (indented)
            var loopActionsPanel = new VerticalStackPanel { Spacing = 1 };
            LinkedListNode<MacroObject>? innerAction = loopContainer.Items.First;
            int innerIndex = 0;

            while (innerAction != null)
            {
                MacroObject capturedInnerAction = innerAction.Value;
                int capturedInnerIndex = innerIndex;

                Container row = BuildActionRow(
                    capturedInnerAction,
                    $"  {loopIndex + 1}.{capturedInnerIndex + 1}.",
                    onTypeReplace: newAction =>
                    {
                        LinkedListNode<MacroObject>? node = loopContainer.Items.Find(capturedInnerAction);
                        if (node != null)
                        {
                            loopContainer.Items.AddBefore(node, newAction);
                            loopContainer.Items.Remove(node);
                        }
                    },
                    onTextReplace: strAction =>
                    {
                        LinkedListNode<MacroObject>? node = loopContainer.Items.Find(capturedInnerAction);
                        if (node != null)
                        {
                            loopContainer.Items.AddBefore(node, strAction);
                            loopContainer.Items.Remove(node);
                        }
                    },
                    onRemove: () => loopContainer.Items.Remove(capturedInnerAction)
                );

                loopActionsPanel.Widgets.Add(row);

                innerAction = innerAction.Next;
                innerIndex++;
            }

            // Add to loop button
            loopActionsPanel.Widgets.Add(Button("macrostab_addactiontoloop", () =>
            {
                MacroObject newAction = Macro.Create(MacroType.Say);
                loopContainer.Items.AddLast(newAction);
                MarkDirty();
                BuildActionsPanel();
            }));

            if (interaction.IsDesktop)
                actionsPanel.Widgets.Add(DesktopAssistantTheme.Group(loopTitle, loopHeaderRow, loopActionsPanel));
            else
            {
                actionsPanel.Widgets.Add(loopHeaderRow);
                actionsPanel.Widgets.Add(loopActionsPanel);
            }
        }

        string AvailableName(string baseName)
        {
            var usedNames = new HashSet<string>(manager.GetAllMacros().Select(macro => macro.Name), StringComparer.OrdinalIgnoreCase);
            string name = baseName;
            for (int suffix = 1; usedNames.Contains(name); suffix++)
                name = $"{baseName} {suffix}";
            return name;
        }

        Macro NewMacro(string name) => new(name) { Items = new MacroObject(MacroType.None, MacroSubType.MSC_NONE) };

        void SaveAndSelect(Macro macro)
        {
            MarkDirty();
            if (macroPicker != null)
                filterBox.Text = string.Empty;
            SelectMacro(macro);
            if (macroPicker != null)
            {
                BuildMacroList();
                DesktopAssistantForm.RevealSelection(macroPicker);
            }
        }

        void InsertMacro(Macro macro)
        {
            manager.PushToBack(macro);
            SaveAndSelect(macro);
        }

        void MoveSelected(bool up)
        {
            if (selectedMacro is not { } macro || !(up ? manager.MoveMacroUp(macro) : manager.MoveMacroDown(macro)))
                return;
            MarkDirty();
            BuildMacroList();
            if (macroPicker != null)
                DesktopAssistantForm.RevealSelection(macroPicker);
        }

        void Import()
        {
            string? xml = Clipboard.GetClipboardText();
            if (xml.NotNullNotEmpty() && manager.ImportFromXml(xml))
            {
                status.Visible = false;
                if (interaction.IsDesktop && selectedMacro == null)
                    SelectMacro(manager.GetAllMacros().FirstOrDefault());
                BuildMacroList();
                return;
            }
            ReportStatus(TazLang.Get("macrostab_import_error"), Constants.HUE_ERROR);
        }

        void Export()
        {
            manager.GetXmlExport()?.CopyToClipboard();
            int count = manager.GetAllMacros().Count;
            ReportStatus(TazLang.Get("macrostab_export_success", [count.ToString()]), Constants.HUE_SUCCESS);
        }

        void PromptForName(MyraButton anchor, string title, string initial, string confirm, Action<string> apply, Macro? editing = null)
        {
            var name = new MyraInputBox { Text = initial, HintText = TazLang.Get("macrostab_name_hint"), MinWidth = 220 };
            var error = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
            var body = new VerticalStackPanel { Spacing = 4 };
            body.Widgets.Add(DesktopAssistantForm.Fields(new DesktopAssistantForm.Field(TazLang.Get("macrostab_name_hint"), name)));
            body.Widgets.Add(error);
            name.TextChangedByUser += (_, _) => error.Visible = false;
            interaction.ShowDialog(anchor, title, body, confirmed =>
            {
                if (confirmed)
                    apply(name.Text.Trim());
            }, Validate, confirmText: confirm);

            bool Validate()
            {
                string value = name.Text?.Trim() ?? "";
                string? message = value.Length == 0 ? TazLang.Get("macrostab_name_required") : null;
                if (message == null && manager.GetAllMacros().Any(macro => !ReferenceEquals(macro, editing)
                    && string.Equals(macro.Name, value, StringComparison.OrdinalIgnoreCase)))
                    message = TazLang.Get("macrostab_name_in_use");
                error.Text = message ?? "";
                error.Visible = message != null;
                return message == null;
            }
        }

        filterBox.TextChangedByUser += (_, _) => BuildMacroList();
        BuildMacroList();
        BuildEditor();

        if (macroPicker != null)
        {
            var add = Button("agent_preset_new", primary: true);
            add.OnClick = () => PromptForName(add, TazLang.Get("macrostab_newmacro"), AvailableName(TazLang.Get("macrostab_newmacro")),
                TazLang.Get("macrostab_create"), name => InsertMacro(NewMacro(name)));
            var rename = Button("assistant_dialog_rename");
            rename.OnClick = () =>
            {
                if (selectedMacro is { } macro)
                    PromptForName(rename, rename.Text, macro.Name, rename.Text, name =>
                    {
                        macro.Name = name;
                        SaveAndSelect(macro);
                    }, macro);
            };
            var copy = Button("agent_preset_copy");
            copy.OnClick = () =>
            {
                if (selectedMacro is { } macro)
                    PromptForName(copy, TazLang.Get("macrostab_copy"), AvailableName(TazLang.Get("macrostab_copy_name", [macro.Name])),
                        copy.Text, name => InsertMacro(macro.Duplicate(name)));
            };
            var delete = Button("delete", destructive: true);
            delete.Tooltip = TazLang.Get("macrostab_deletemacro_tooltip");
            delete.OnClick = () =>
            {
                if (selectedMacro is { } macro)
                    ConfirmDelete(delete, macro);
            };
            selectionActions = [rename, copy, delete];
            foreach (MyraButton action in selectionActions)
                action.Enabled = selectedMacro != null;
            macroPicker.SelectedIndexChanged += (_, _) =>
            {
                if (!refreshingPicker && macroPicker.SelectedItem?.Tag is Macro macro)
                    SelectMacro(macro);
            };
            var listActions = Button("macrostab_list_actions");
            listActions.HorizontalAlignment = HorizontalAlignment.Stretch;
            listActions.OnClick = () =>
            {
                var menu = new VerticalMenu();
                AddAction("macrostab_moveup", () => MoveSelected(up: true), selectedMacro?.Previous != null);
                AddAction("macrostab_movedown", () => MoveSelected(up: false), selectedMacro?.Next != null);
                AddAction("macrostab_import", Import);
                AddAction("macrostab_export", Export);
                listActions.Desktop?.ShowContextMenu(menu, listActions.ToGlobal(new Point(0, listActions.Bounds.Height)));

                void AddAction(string key, Action action, bool enabled = true)
                {
                    var item = new MenuItem { Text = TazLang.Get(key), Enabled = enabled };
                    item.Selected += (_, _) => action();
                    menu.Items.Add(item);
                }
            };

            const int sidebarWidth = DesktopAssistantForm.ListSidebarWidth;
            var layout = new DesktopAssistantForm.FixedSidebarLayout(editorLayout, sidebarWidth) { ColumnSpacing = 10 };
            layout.AddColumn(new Proportion(ProportionType.Pixels, sidebarWidth));
            layout.AddColumn(new Proportion(ProportionType.Fill));
            layout.AddRow(new Proportion(ProportionType.Fill));
            layout.AddWidget(DesktopAssistantForm.ListSidebar(TazLang.Get("macrostab_saved_macros"), macroPicker, [add, rename, copy, delete],
                filterBox, listActions), 0, 0);
            layout.AddWidget(editorLayout, 0, 1);
            return layout;
        }

        var toolbar = new HorizontalStackPanel { Spacing = 2 };
        toolbar.Widgets.Add(Button("macrostab_add", () => InsertMacro(NewMacro(AvailableName(TazLang.Get("macrostab_newmacro"))))));
        toolbar.Widgets.Add(Button("macrostab_moveup", () => MoveSelected(up: true)));
        toolbar.Widgets.Add(Button("macrostab_movedown", () => MoveSelected(up: false)));
        var import = Button("macrostab_import", Import);
        import.Tooltip = TazLang.Get("macrostab_import_tooltip");
        toolbar.Widgets.Add(import);
        var export = Button("macrostab_export", Export);
        export.Tooltip = TazLang.Get("macrostab_export_tooltip");
        toolbar.Widgets.Add(export);
        filterBox.Width = 150;
        toolbar.Widgets.Add(filterBox);
        var mainArea = new HorizontalStackPanel { Spacing = 4 };
        mainArea.Widgets.Add(new ScrollViewer { MaxHeight = 450, Content = macroListPanel });
        mainArea.Widgets.Add(editorPanel);
        var root = new VerticalStackPanel { Spacing = 2 };
        root.Widgets.Add(toolbar);
        root.Widgets.Add(status);
        root.Widgets.Add(mainArea);
        return root;
    }

    private sealed class DesktopActionRow : MyraGrid
    {
        private readonly MyraLabel _number;
        private readonly MyraButton _remove;
        private readonly WrapPanel _fields;
        private readonly Widget _typePicker;
        private readonly MyraInputBox? _textEditor;

        internal DesktopActionRow(string index, WrapPanel fields, Widget typePicker, MyraInputBox? textEditor, MyraButton remove)
        {
            _number = new MyraLabel(index, MyraLabel.TextStyle.P) { MinWidth = 24 };
            _fields = fields;
            _typePicker = typePicker;
            _textEditor = textEditor;
            _remove = remove;
            ColumnSpacing = 5;
            Padding = new Thickness(5, 3);
            Border = DesktopAssistantTheme.Divider;
            BorderThickness = new Thickness(0, 0, 0, 1);
            AddColumn();
            AddColumn(new Proportion(ProportionType.Fill));
            AddColumn();
            AddWidget(_number, 0, 0);
            AddWidget(_fields, 0, 1);
            AddWidget(_remove, 0, 2);
        }

        protected override Point InternalMeasure(Point availableSize)
        {
            int fieldWidth = Math.Max(1, availableSize.X - _number.Measure(availableSize).X
                - _remove.Measure(availableSize).X - 2 * ColumnSpacing);
            _fields.MaxWidth = fieldWidth;
            _typePicker.MaxWidth = Math.Max(1, fieldWidth / 2);
            if (_textEditor != null)
                _textEditor.Width = Math.Clamp(fieldWidth - _typePicker.Measure(availableSize).X - _fields.HorizontalSpacing, 1, 180);
            return base.InternalMeasure(availableSize);
        }
    }
}
