#nullable enable
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

public static class SeasonFilterTabContent
{
    private static readonly string[] SeasonNames = { "Spring", "Summer", "Fall", "Winter", "Desolation" };
    private static readonly Season[] AllSeasons =
    {
        Season.Spring,
        Season.Summer,
        Season.Fall,
        Season.Winter,
        Season.Desolation
    };

    // Display options: "None" followed by each season
    private static readonly string[] DisplayOptions;

    static SeasonFilterTabContent()
    {
        DisplayOptions = new string[AllSeasons.Length + 1];
        DisplayOptions[0] = "None";
        for (int j = 0; j < SeasonNames.Length; j++)
            DisplayOptions[j + 1] = SeasonNames[j];
    }

    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var root = new VerticalStackPanel { Spacing = 6 };

        root.Widgets.Add(new MyraLabel(
            "Override seasons sent by the server. For example, if the server sends Winter, you can display Fall instead.",
            MyraLabel.TextStyle.H3) { MaxWidth = 500 });

        // Each row is rebuilt when its selected override changes.
        var rebuildActions = new System.Collections.Generic.List<System.Action>();

        var clear = new MyraButton("Clear All Filters");
        clear.Click += (_, _) => interaction.Confirm(clear, clear.Text, "Remove all season overrides?", () =>
        {
            SeasonFilter.Instance.Clear();
            foreach (System.Action rebuild in rebuildActions) rebuild();
        });
        root.Widgets.Add(MyraStyle.ApplyButtonDangerStyle(clear));

        var grid = new MyraGrid();
        grid.SetupWithHeaders(GridColumnInfo.Auto("When Server Sends"), GridColumnInfo.Auto("Show As"), GridColumnInfo.Auto("Enabled"));

        for (int i = 0; i < AllSeasons.Length; i++)
        {
            Season incoming = AllSeasons[i];
            string incomingName = SeasonNames[i];

            grid.AddWidget(new MyraLabel(incomingName, MyraLabel.TextStyle.P), i + 1, 0);

            var cycleWrapper = new HorizontalStackPanel();
            var enableWrapper = new HorizontalStackPanel();

            void BuildCycleBtn()
            {
                cycleWrapper.Widgets.Clear();
                enableWrapper.Widgets.Clear();

                string currentLabel = "None";
                int currentIdx = 0;
                if (SeasonFilter.Instance.Filters.TryGetValue(incoming, out Season replacement))
                {
                    for (int k = 0; k < AllSeasons.Length; k++)
                    {
                        if (AllSeasons[k] == replacement)
                        {
                            currentIdx = k + 1;
                            currentLabel = SeasonNames[k];
                            break;
                        }
                    }
                }

                cycleWrapper.Widgets.Add(new MyraButton(currentLabel, () =>
                {
                    int nextIdx = (currentIdx + 1) % DisplayOptions.Length;
                    if (nextIdx == 0)
                        SeasonFilter.Instance.RemoveFilter(incoming);
                    else
                        SeasonFilter.Instance.SetFilter(incoming, AllSeasons[nextIdx - 1]);
                    BuildCycleBtn();
                }) { Tooltip = $"Click to cycle season override for {incomingName}" });
                var enabled = MyraCheckButton.CreateWithCallback(currentIdx != 0 && SeasonFilter.Instance.IsEnabled(incoming),
                    value => SeasonFilter.Instance.SetEnabled(incoming, value), tooltip: "Enable or disable this override.");
                enabled.Enabled = currentIdx != 0;
                enableWrapper.Widgets.Add(enabled);
            }

            rebuildActions.Add(BuildCycleBtn);
            BuildCycleBtn();
            grid.AddWidget(cycleWrapper, i + 1, 1);
            grid.AddWidget(enableWrapper, i + 1, 2);
        }

        root.Widgets.Add(grid);
        root.Widgets.Add(new MyraLabel(
            "Click the button to cycle through options. 'None' disables the filter.",
            MyraLabel.TextStyle.P));

        return root;
    }
}
