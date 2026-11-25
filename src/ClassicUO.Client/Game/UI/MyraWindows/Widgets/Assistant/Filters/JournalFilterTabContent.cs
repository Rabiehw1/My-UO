#nullable enable
using System.Collections.Generic;
using System.Linq;
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
        var root = new VerticalStackPanel { Spacing = 6 };

        root.Widgets.Add(new MyraLabel(
            "Hide journal messages containing the filter text.", MyraLabel.TextStyle.P));

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
                filtersPanel.Widgets.Add(new MyraLabel("No filters configured.", MyraLabel.TextStyle.H3));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(GridColumnInfo.Auto("Enabled"), GridColumnInfo.Fill("Filter Text"), GridColumnInfo.Auto("Actions"));

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

                grid.AddWidget(MyraStyle.ApplyButtonDangerStyle(new MyraButton("Delete", () =>
                {
                    manager.RemoveFilter(filter);
                    manager.Save(false);
                    BuildFilterList();
                }) { Tooltip = "Delete this filter" }), dataRow, 2);

                dataRow++;
            }

            filtersPanel.Widgets.Add(grid);
        }

        var addConfirmRow = new HorizontalStackPanel { Spacing = 4 };
        addConfirmRow.Widgets.Add(new MyraButton("Add", () =>
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
        addConfirmRow.Widgets.Add(new MyraButton("Cancel", () =>
        {
            addFilterPanel.Visible = false;
            newFilterBox.Text = "";
        }));

        var addFieldRow = new HorizontalStackPanel { Spacing = 4 };
        addFieldRow.Widgets.Add(new MyraLabel("Filter Text:", MyraLabel.TextStyle.P)
            { Tooltip = "Matches part of a journal entry, regardless of capitalization." });
        addFieldRow.Widgets.Add(newFilterBox);

        addFilterPanel.Widgets.Add(new MyraLabel("Add New Filter:", MyraLabel.TextStyle.H3));
        addFilterPanel.Widgets.Add(addFieldRow);
        addFilterPanel.Widgets.Add(addConfirmRow);

        var actionRow = new HorizontalStackPanel { Spacing = 4 };
        actionRow.Widgets.Add(new MyraButton("Add Filter Entry", () => addFilterPanel.Visible = !addFilterPanel.Visible));
        actionRow.Widgets.Add(new MyraButton("Import", () =>
        {
            string? json = Clipboard.GetClipboardText();
            if (json.NotNullNotEmpty() && manager.ImportFromJson(json, message => Report(message, Constants.HUE_SUCCESS)))
            {
                BuildFilterList();
                return;
            }
            Report("The clipboard does not contain a filter list.", Constants.HUE_ERROR);
        }) { Tooltip = "Import from your clipboard, must have a valid export copied." });
        actionRow.Widgets.Add(new MyraButton("Export", () =>
        {
            manager.GetJsonExport()?.CopyToClipboard();
            Report("Copied enabled journal filters.", Constants.HUE_SUCCESS);
        }) { Tooltip = "Copy enabled filters to the clipboard." });

        root.Widgets.Add(actionRow);
        root.Widgets.Add(status);
        root.Widgets.Add(addFilterPanel);
        root.Widgets.Add(new MyraLabel("Current Journal Filters:", MyraLabel.TextStyle.H3));
        BuildFilterList();
        root.Widgets.Add(interaction.IsDesktop ? filtersPanel : new ScrollViewer { Height = 250, Content = filtersPanel });

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
