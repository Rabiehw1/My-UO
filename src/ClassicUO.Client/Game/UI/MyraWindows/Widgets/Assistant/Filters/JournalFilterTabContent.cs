#nullable enable
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

public static class JournalFilterTabContent
{
    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        MyraButton Button(string text, System.Action? action = null, bool primary = false, bool destructive = false, string? tooltip = null) =>
            FilterPageStyle.Button(interaction, text, action, primary, destructive, tooltip);

        var root = new VerticalStackPanel { Spacing = 6 };

        root.Widgets.Add(new MyraLabel(
            TazLang.Get("assistant_filters_journal_help"), MyraLabel.TextStyle.P) { Wrap = true });

        var addFilterPanel = new VerticalStackPanel { Visible = false, Spacing = 4 };
        var newFilterBox = new MyraInputBox { HintText = "Text to hide", Width = 260 };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false, Wrap = true };
        JournalFilterManager manager = JournalFilterManager.Instance;

        var filtersPanel = new VerticalStackPanel { Spacing = 2 };

        void BuildFilterList()
        {
            filtersPanel.Widgets.Clear();
            List<string> filters = manager.Filters.ToList();

            if (filters.Count == 0)
            {
                filtersPanel.Widgets.Add(new MyraLabel(TazLang.Get("assistant_filters_empty"), MyraLabel.TextStyle.P));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(GridColumnInfo.Auto(TazLang.Get("agent_col_enabled")),
                GridColumnInfo.Fill(TazLang.Get("assistant_filters_text")), GridColumnInfo.Auto(TazLang.Get("agent_col_actions")));

            int dataRow = 1;
            for (int i = filters.Count - 1; i >= 0; i--)
            {
                string filter = filters[i];
                var filterBox = new MyraInputBox { Text = filter };
                filterBox.LostFocus = () =>
                {
                    string edited = filterBox.Text ?? "";
                    if (edited != filter && manager.ReplaceFilter(filter, edited))
                    {
                        filter = edited;
                        manager.Save(false);
                    }
                    filterBox.Text = filter;
                };
                grid.AddWidget(MyraCheckButton.CreateWithCallback(manager.IsEnabled(filter), enabled =>
                {
                    manager.SetEnabled(filter, enabled);
                    manager.Save(false);
                }, tooltip: "Enable or disable this filter."), dataRow, 0);
                grid.AddWidget(filterBox, dataRow, 1);

                grid.AddWidget(Button(TazLang.Get("remove"), () =>
                {
                    manager.RemoveFilter(filter);
                    manager.Save(false);
                    BuildFilterList();
                }, destructive: true), dataRow, 2);

                dataRow++;
            }

            filtersPanel.Widgets.Add(grid);
        }

        var addConfirmRow = DesktopAssistantForm.Actions();
        addConfirmRow.Widgets.Add(Button(TazLang.Get("add"), () =>
        {
            string text = newFilterBox.Text ?? "";
            if (!string.IsNullOrWhiteSpace(text))
            {
                manager.AddFilter(text);
                manager.Save(false);
                newFilterBox.Text = "";
                addFilterPanel.Visible = false;
                BuildFilterList();
            }
        }));
        addConfirmRow.Widgets.Add(Button(TazLang.Get("cancel"), () =>
        {
            addFilterPanel.Visible = false;
            newFilterBox.Text = "";
        }));

        var addFieldRow = DesktopAssistantForm.Actions();
        addFieldRow.Widgets.Add(new MyraLabel("Filter Text:", MyraLabel.TextStyle.P)
            { Tooltip = "Matches part of a journal entry, regardless of capitalization." });
        addFieldRow.Widgets.Add(newFilterBox);

        addFilterPanel.Widgets.Add(new MyraLabel("Add New Filter:", MyraLabel.TextStyle.H3));
        addFilterPanel.Widgets.Add(addFieldRow);
        addFilterPanel.Widgets.Add(addConfirmRow);

        var actionRow = DesktopAssistantForm.Actions();
        var addEntry = Button(TazLang.Get("assistant_filters_add_entry"), () => addFilterPanel.Visible = !addFilterPanel.Visible, primary: true);
        actionRow.Widgets.Add(addEntry);
        actionRow.Widgets.Add(Button(TazLang.Get("assistant_filters_import"), () =>
        {
            string? json = Clipboard.GetClipboardText();
            if (json.NotNullNotEmpty() && manager.ImportFromJson(json, message => Report(message, Constants.HUE_SUCCESS)))
            {
                BuildFilterList();
                return;
            }
            Report("The clipboard does not contain a filter list.", Constants.HUE_ERROR);
        }, tooltip: "Import from your clipboard, must have a valid export copied."));
        actionRow.Widgets.Add(Button(TazLang.Get("assistant_filters_export"), () =>
        {
            manager.GetJsonExport()?.CopyToClipboard();
            Report("Copied enabled journal filters.", Constants.HUE_SUCCESS);
        }, tooltip: "Copy enabled filters to the clipboard."));

        BuildFilterList();
        root.Widgets.Add(FilterPageStyle.Group(interaction, TazLang.Get("assistant_filters_journal_rules"),
            actionRow, status, addFilterPanel,
            interaction.IsDesktop ? filtersPanel : new ScrollViewer { Height = 250, Content = filtersPanel }));

        return root;

        void Report(string message, ushort hue)
        {
            if (interaction.IsDesktop)
            {
                status.Text = message;
                status.Visible = true;
            }
            else
                GameActions.Print(message, hue);
        }
    }
}
