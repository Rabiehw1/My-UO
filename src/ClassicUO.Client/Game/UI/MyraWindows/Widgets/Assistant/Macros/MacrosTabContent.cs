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
        Macro? selectedMacro = null;
        string filterText = "";

        // ── Panel references ─────────────────────────────────────────────────
        var macroListPanel = new VerticalStackPanel { Spacing = 1 };
        var editorPanel    = new VerticalStackPanel { Spacing = 4 };
        var hotkeyRow      = new HorizontalStackPanel { Spacing = 4 };
        var actionsPanel   = new VerticalStackPanel { Spacing = 2 };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false, Wrap = true };

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
            macroListPanel.Widgets.Clear();

            List<Macro> allMacros = manager.GetAllMacros();

            List<Macro> display = string.IsNullOrWhiteSpace(filterText)
                ? allMacros
                : allMacros.Where(m =>
                    m.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                    m.GetBinding().Describe().Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (display.Count == 0)
            {
                macroListPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_nomacros"), MyraLabel.TextStyle.P));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(
                GridColumnInfo.Fill(TazLang.Get("macrostab_col_name")),
                GridColumnInfo.Auto(TazLang.Get("macrostab_col_hotkey")),
                GridColumnInfo.Auto(TazLang.Get(interaction.IsDesktop ? "agent_col_actions" : "macrostab_col_edit"))
            );

            int dataRow = 1;
            foreach (Macro macro in display)
            {
                Macro captured = macro;

                grid.AddWidget(MyraGrid.TableText(macro.Name), dataRow, 0);
                grid.AddWidget(MyraGrid.TableText(macro.GetBinding().Describe()), dataRow, 1);
                grid.AddWidget(new MyraButton(TazLang.Get("macrostab_col_edit"), () =>
                {
                    selectedMacro = captured;
                    BuildMacroList();
                    BuildEditor();
                }), dataRow, 2);
                dataRow++;
            }

            macroListPanel.Widgets.Add(grid);
        }

        // ── BuildEditor ───────────────────────────────────────────────────────
        void BuildEditor()
        {
            editorPanel.Widgets.Clear();

            if (selectedMacro == null)
            {
                editorPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_selectmacro"), MyraLabel.TextStyle.H3));
                return;
            }

            Macro macro = selectedMacro;

            // Name row
            var nameRow = new HorizontalStackPanel { Spacing = 2 };
            nameRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_macroname"), MyraLabel.TextStyle.P));
            var nameBox = new MyraInputBox { Text = macro.Name, Width = 200 };
            nameBox.TextChangedByUser += (_, _) =>
            {
                macro.Name = nameBox.Text ?? "";
                MarkDirty();
                if (interaction.IsDesktop)
                    BuildMacroList();
            };
            nameRow.Widgets.Add(nameBox);
            editorPanel.Widgets.Add(nameRow);
            editorPanel.Widgets.Add(new MyraSpacer(10, 2));

            // Hotkey row (rebuilt dynamically)
            BuildHotkeyRow();
            editorPanel.Widgets.Add(hotkeyRow);
            editorPanel.Widgets.Add(new MyraSpacer(10, 2));

            // Journal triggers row
            var journalRow = new HorizontalStackPanel { Spacing = 2 };
            journalRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_journaltriggers"), MyraLabel.TextStyle.P)
            {
                Tooltip = TazLang.Get("macrostab_journaltriggers_tooltip")
            });
            var journalBox = new MyraInputBox { Text = macro.JournalTriggers ?? "", Width = 260, Tooltip = TazLang.Get("macrostab_journaltriggers_tooltip") };
            journalBox.TextChangedByUser += (_, _) =>
            {
                macro.JournalTriggers = journalBox.Text ?? "";
                MarkDirty();
            };
            journalRow.Widgets.Add(journalBox);
            editorPanel.Widgets.Add(journalRow);
            editorPanel.Widgets.Add(new MyraSpacer(10, 2));

            // Create Macro Button / Button Editor
            var macroButtonRow = new HorizontalStackPanel { Spacing = 2 };
            macroButtonRow.Widgets.Add(new MyraButton(TazLang.Get("macrostab_createbutton"), () => interaction.RunInGame(() =>
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
            })) { Tooltip = TazLang.Get("macrostab_createbutton_tooltip") });

            macroButtonRow.Widgets.Add(new MyraButton(TazLang.Get("macrostab_buttoneditor"), () =>
            {
                interaction.RunInGame(() => MacroButtonEditorWindow.Show(macro));
            }) { Tooltip = TazLang.Get("macrostab_buttoneditor_tooltip") });

            editorPanel.Widgets.Add(macroButtonRow);

            editorPanel.Widgets.Add(new MyraSpacer(10, 2));

            editorPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_actions"), MyraLabel.TextStyle.P));

            BuildActionsPanel();
            editorPanel.Widgets.Add(interaction.IsDesktop ? actionsPanel : new ScrollViewer { MaxHeight = 250, Content = actionsPanel });

            editorPanel.Widgets.Add(new MyraSpacer(10, 1));

            var bottomRow = new HorizontalStackPanel { Spacing = 2 };
            bottomRow.Widgets.Add(new MyraButton(TazLang.Get("macrostab_addaction"), () =>
            {
                MacroObject newAction = Macro.Create(MacroType.Say);
                var scanAction = (MacroObject)macro.Items;
                MacroObject? lastAction = null;
                while (scanAction != null && scanAction.Code != MacroType.None)
                {
                    lastAction = scanAction;
                    scanAction = (MacroObject)scanAction.Next;
                }
                if (lastAction != null)
                    macro.Insert(lastAction, newAction);
                else
                {
                    macro.Items = newAction;
                    newAction.Next = new MacroObject(MacroType.None, MacroSubType.MSC_NONE);
                }
                MarkDirty();
                BuildActionsPanel();
            }));

            bottomRow.Widgets.Add(new MyraButton(TazLang.Get("macrostab_addloop"), () =>
            {
                MacroLoopContainer loopContainer = Macro.CreateLoopContainer(loopCount: 1, delayBetweenIterations: 500);
                var scanAction = (MacroObject)macro.Items;
                MacroObject? lastAction = null;
                while (scanAction != null && scanAction.Code != MacroType.None)
                {
                    lastAction = scanAction;
                    scanAction = (MacroObject)scanAction.Next;
                }
                if (lastAction != null)
                    macro.Insert(lastAction, loopContainer);
                else
                {
                    macro.Items = loopContainer;
                    loopContainer.Next = new MacroObject(MacroType.None, MacroSubType.MSC_NONE);
                }
                MarkDirty();
                BuildActionsPanel();
            }));

            var delete = MyraStyle.ApplyButtonDangerStyle(new MyraButton(TazLang.Get("macrostab_deletemacro"))
                { Tooltip = TazLang.Get("macrostab_deletemacro_tooltip") });
            delete.Click += (_, _) =>
            {
                interaction.Confirm(delete, TazLang.Get("macrostab_delete_title", [macro.Name]),
                    TazLang.Get("macrostab_delete_confirm", [macro.Name]), () =>
                    {
                        manager.Remove(macro);
                        selectedMacro = null;
                        MarkDirty();
                        BuildMacroList();
                        BuildEditor();
                    }, confirmText: TazLang.Get("delete"), destructive: true);
            };
            bottomRow.Widgets.Add(delete);

            editorPanel.Widgets.Add(bottomRow);
        }

        // ── BuildHotkeyRow ────────────────────────────────────────────────────
        void BuildHotkeyRow()
        {
            hotkeyRow.Widgets.Clear();

            if (selectedMacro == null) return;
            Macro macro = selectedMacro;

            if (interaction.IsDesktop)
            {
                var set = new MyraButton(TazLang.Get("mog_kw_set", "Set"));
                set.Click += (_, _) => HotkeysTabContent.CaptureMacroBinding(interaction, set, manager, macro, RefreshBinding);
                hotkeyRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_hotkey"), MyraLabel.TextStyle.P));
                hotkeyRow.Widgets.Add(new MyraLabel(macro.GetBinding().Describe(), MyraLabel.TextStyle.P));
                hotkeyRow.Widgets.Add(set);
                var clear = new MyraButton(TazLang.Get("mog_kw_clear", "Clear")) { Enabled = !macro.GetBinding().IsEmpty };
                clear.Click += (_, _) =>
                {
                    macro.ApplyBinding(new HotkeyBinding());
                    MarkDirty();
                    RefreshBinding();
                };
                hotkeyRow.Widgets.Add(clear);
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

                    HorizontalStackPanel row = BuildActionRow(
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

            if (actionIndex == 0)
                actionsPanel.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_noactions"), MyraLabel.TextStyle.H3));
            if (hasCastSpell)
                actionsPanel.Widgets.Insert(0, new MyraLabel(
                    TazLang.Get("macrostab_set_spell_book", "You must set your spell book in the Spell Books tab."),
                    MyraLabel.TextStyle.P) { Wrap = true });
        }

        HorizontalStackPanel BuildActionRow(
            MacroObject action,
            string indexLabel,
            Action<MacroObject> onTypeReplace,   // combo changed -> swap + rebuild
            Action<MacroObject> onTextReplace,   // text typed on non-string action -> swap silently
            Action onRemove)
        {
            var row = new HorizontalStackPanel { Spacing = 2 };
            row.Widgets.Add(new MyraLabel(indexLabel, MyraLabel.TextStyle.P));

            MacroObject capturedAction = action;

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
            row.Widgets.Add(MyraStyle.ApplyButtonDangerStyle(new MyraButton(TazLang.Get("macrostab_removeaction"), () =>
            {
                onRemove();
                MarkDirty();
                BuildActionsPanel();
            })
            { Tooltip = TazLang.Get("macrostab_removeaction_tooltip") }));

            return row;
        }

        void AddSpellSelectors(HorizontalStackPanel row, MacroObject action, Action<MacroObject> replace)
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
            var loopHeaderRow = new HorizontalStackPanel { Spacing = 4 };
            loopHeaderRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_loop", [(loopIndex + 1).ToString()]), MyraLabel.TextStyle.P));

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

            loopHeaderRow.Widgets.Add(new MyraLabel(TazLang.Get("macrostab_loop_delay"), MyraLabel.TextStyle.P));

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

            loopHeaderRow.Widgets.Add(MyraStyle.ApplyButtonDangerStyle(new MyraButton(TazLang.Get("macrostab_removeloop"), () =>
            {
                macro.Remove(loopContainer);
                MarkDirty();
                BuildActionsPanel();
            })));

            actionsPanel.Widgets.Add(loopHeaderRow);

            // Inner actions list (indented)
            var loopActionsPanel = new VerticalStackPanel { Spacing = 1 };
            LinkedListNode<MacroObject>? innerAction = loopContainer.Items.First;
            int innerIndex = 0;

            while (innerAction != null)
            {
                MacroObject capturedInnerAction = innerAction.Value;
                int capturedInnerIndex = innerIndex;

                HorizontalStackPanel row = BuildActionRow(
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
            loopActionsPanel.Widgets.Add(new MyraButton(TazLang.Get("macrostab_addactiontoloop"), () =>
            {
                MacroObject newAction = Macro.Create(MacroType.Say);
                loopContainer.Items.AddLast(newAction);
                MarkDirty();
                BuildActionsPanel();
            }));

            actionsPanel.Widgets.Add(loopActionsPanel);
        }

        // ── Toolbar ───────────────────────────────────────────────────────────
        Container toolbar = interaction.IsDesktop
            ? new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 }
            : new HorizontalStackPanel { Spacing = 2 };

        var addMacroButton = new MyraButton(TazLang.Get("macrostab_add"), () =>
        {
            string baseName = TazLang.Get("macrostab_newmacro");
            string macroName = baseName;
            int counter = 1;
            while (manager.GetAllMacros().Any(m => m.Name == macroName))
                macroName = $"{baseName} {counter++}";

            var newMacro = new Macro(macroName);
            newMacro.Items = new MacroObject(MacroType.None, MacroSubType.MSC_NONE);
            manager.PushToBack(newMacro);
            selectedMacro = newMacro;
            MarkDirty();
            BuildMacroList();
            BuildEditor();
        });
        if (interaction.IsDesktop)
            DesktopAssistantTheme.ApplyActionButtonStyle(addMacroButton, primary: true);
        toolbar.Widgets.Add(addMacroButton);

        toolbar.Widgets.Add(new MyraButton(TazLang.Get("macrostab_moveup"), () =>
        {
            if (selectedMacro == null) return;
            manager.MoveMacroUp(selectedMacro);
            MarkDirty();
            BuildMacroList();
        }));

        toolbar.Widgets.Add(new MyraButton(TazLang.Get("macrostab_movedown"), () =>
        {
            if (selectedMacro == null) return;
            manager.MoveMacroDown(selectedMacro);
            MarkDirty();
            BuildMacroList();
        }));

        toolbar.Widgets.Add(new MyraButton(TazLang.Get("macrostab_import"), () =>
        {
            string? xml = Clipboard.GetClipboardText();
            if (xml.NotNullNotEmpty() && manager.ImportFromXml(xml))
            {
                status.Visible = false;
                BuildMacroList();
                return;
            }
            ReportStatus(TazLang.Get("macrostab_import_error"), Constants.HUE_ERROR);
        }) { Tooltip = TazLang.Get("macrostab_import_tooltip") });

        toolbar.Widgets.Add(new MyraButton(TazLang.Get("macrostab_export"), () =>
        {
            manager.GetXmlExport()?.CopyToClipboard();
            int count = manager.GetAllMacros().Count;
            ReportStatus(TazLang.Get("macrostab_export_success", [count.ToString()]), Constants.HUE_SUCCESS);
        }) { Tooltip = TazLang.Get("macrostab_export_tooltip") });

        var filterBox = new MyraInputBox { HintText = TazLang.Get("macrostab_filter"), Width = 150 };
        filterBox.TextChangedByUser += (_, _) =>
        {
            filterText = filterBox.Text ?? "";
            BuildMacroList();
        };
        toolbar.Widgets.Add(filterBox);

        // ── Main layout ───────────────────────────────────────────────────────
        StackPanel mainArea = interaction.IsDesktop
            ? new VerticalStackPanel { Spacing = 4 }
            : new HorizontalStackPanel { Spacing = 4 };

        var listScroll = new ScrollViewer { MaxHeight = interaction.IsDesktop ? 160 : 450, Content = macroListPanel };
        mainArea.Widgets.Add(listScroll);
        mainArea.Widgets.Add(editorPanel);

        BuildMacroList();
        BuildEditor();

        var root = new VerticalStackPanel { Spacing = 2 };
        root.Widgets.Add(toolbar);
        root.Widgets.Add(status);
        root.Widgets.Add(mainArea);
        return root;
    }
}
