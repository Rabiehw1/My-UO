#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

public static class SoundFilterTabContent
{
    public static Widget Build() => Build(AgentPageInteraction.InGame, isMusic: false);

    internal static Widget Build(AgentPageInteraction interaction, bool isMusic)
    {
        MyraButton Button(string text, System.Action? action = null, bool primary = false, bool destructive = false, string? tooltip = null) =>
            FilterPageStyle.Button(interaction, text, action, primary, destructive, tooltip);

        SoundFilterManager manager = SoundFilterManager.Instance;
        var filters = isMusic ? manager.FilteredMusic : manager.FilteredSounds;
        var audio = Client.Game.Audio;
        string idName = isMusic ? "Music ID" : "Sound ID";
        int maxId = isMusic ? 149 : 65535;

        var root = new VerticalStackPanel { Spacing = 6 };
        var rules = new VerticalStackPanel { Spacing = 3 };
        var history = new VerticalStackPanel { Spacing = 3 };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false, Wrap = true };
        var idBox = new MyraInputBox { HintText = $"{idName} (0–{maxId})", Width = 170 };
        var toolbar = DesktopAssistantForm.Actions();
        var clear = Button(TazLang.Get("assistant_filters_clear"), destructive: true);
        toolbar.Widgets.Add(idBox);
        var addFilter = Button(TazLang.Get("assistant_filters_add_entry"), () =>
        {
            if (!TryReadId(idBox.Text, out int id))
                return;

            manager.AddFilter(id, isMusic);
            idBox.Text = "";
            status.Visible = false;
            BuildRules();
        }, primary: true);
        toolbar.Widgets.Add(addFilter);
        toolbar.Widgets.Add(Button("Test Play", () =>
        {
            if (TryReadId(idBox.Text, out int id))
                Play(id, preview: true);
        }));
        toolbar.Widgets.Add(Button(TazLang.Get("assistant_filters_import"), Import));
        toolbar.Widgets.Add(Button(TazLang.Get("assistant_filters_export"), Export, tooltip: "Copy enabled filters to the clipboard."));

        clear.Click += (_, _) => interaction.Confirm(clear, clear.Text,
            isMusic ? "Remove all music filters?" : "Remove all sound filters?", () =>
            {
                manager.Clear(isMusic);
                BuildRules();
            }, confirmText: TazLang.Get("remove"), destructive: true);
        toolbar.Widgets.Add(clear);

        root.Widgets.Add(FilterPageStyle.Group(interaction, TazLang.Get("assistant_filters_audio_rules"),
            toolbar, status, interaction.IsDesktop ? rules : new ScrollViewer { Height = 250, Content = rules }));
        root.Widgets.Add(FilterPageStyle.Group(interaction, TazLang.Get("assistant_filters_recent_audio"),
            DesktopAssistantForm.Actions(Button(TazLang.Get("assistant_filters_refresh"), BuildHistory)),
            interaction.IsDesktop ? history : new ScrollViewer { MaxHeight = 200, Content = history }));
        BuildRules();
        BuildHistory();
        return root;

        void BuildRules()
        {
            rules.Widgets.Clear();
            clear.Enabled = filters.Count > 0;
            if (filters.Count == 0)
            {
                rules.Widgets.Add(new MyraLabel(isMusic ? "No music filters." : "No sound filters.", MyraLabel.TextStyle.P));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(GridColumnInfo.Auto(TazLang.Get("agent_col_enabled")), GridColumnInfo.Fill(idName),
                GridColumnInfo.Auto(TazLang.Get("agent_col_actions")));
            int row = 1;
            foreach (int savedId in filters.OrderBy(id => id))
            {
                int id = savedId;
                var field = new MyraInputBox { Text = id.ToString(), Width = 100 };
                field.LostFocus = () =>
                {
                    if (TryReadId(field.Text, out int replacement) && replacement != id)
                    {
                        if (manager.ReplaceFilter(id, replacement, isMusic))
                            id = replacement;
                        else
                            Report($"A filter for {idName.ToLowerInvariant()} {replacement} already exists.", Constants.HUE_ERROR);
                    }
                    field.Text = id.ToString();
                };
                grid.AddWidget(MyraCheckButton.CreateWithCallback(manager.IsSoundFiltered(id, isMusic),
                    enabled => manager.SetEnabled(id, enabled, isMusic), tooltip: "Enable or disable this filter."), row, 0);
                grid.AddWidget(field, row, 1);

                var actions = DesktopAssistantForm.Actions();
                actions.Widgets.Add(Button("Play", () => Play(id, preview: true), tooltip: "Play once while bypassing this filter."));
                actions.Widgets.Add(Button(TazLang.Get("remove"), () =>
                {
                    manager.RemoveFilter(id, isMusic);
                    BuildRules();
                }, destructive: true));
                grid.AddWidget(actions, row++, 2);
            }

            rules.Widgets.Add(grid);
        }

        void BuildHistory()
        {
            history.Widgets.Clear();
            var recent = isMusic ? audio.LastPlayedMusic.GetItems() : audio.LastPlayedSounds.GetItems().Reverse();
            foreach ((int id, string name) in recent)
            {
                var row = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 2 };
                row.Widgets.Add(new MyraLabel($"{idName} {id} ({name})", MyraLabel.TextStyle.P));
                row.Widgets.Add(Button("Add Filter", () =>
                {
                    manager.AddFilter(id, isMusic);
                    BuildRules();
                }));
                row.Widgets.Add(Button("Play Again", () => Play(id, preview: !isMusic)));
                history.Widgets.Add(row);
            }

            if (history.Widgets.Count == 0)
                history.Widgets.Add(new MyraLabel("Nothing played yet.", MyraLabel.TextStyle.P));
        }

        void Play(int id, bool preview)
        {
            if (isMusic)
            {
                audio.StopMusic();
                audio.PlayMusic(id, skipIgnore: preview);
            }
            else
                audio.PlaySound(id, skipFilter: preview);
        }

        bool TryReadId(string? text, out int id)
        {
            if (int.TryParse(text, out id) && id >= 0 && id <= maxId)
                return true;

            Report($"Enter a {idName.ToLowerInvariant()} between 0 and {maxId}.", Constants.HUE_ERROR);
            return false;
        }

        void Import()
        {
            try
            {
                var data = Clipboard.GetClipboardText();
                if (string.IsNullOrWhiteSpace(data))
                {
                    Report("Clipboard is empty.", Constants.HUE_ERROR);
                    return;
                }

                var imported = JsonSerializer.Deserialize(data, HashSetIntContext.Default.HashSetInt32);
                if (imported == null)
                {
                    Report("The clipboard does not contain a filter list.", Constants.HUE_ERROR);
                    return;
                }

                int previous = filters.Count;
                foreach (int id in imported)
                    manager.AddFilter(Math.Clamp(id, 0, maxId), isMusic);
                BuildRules();
                Report($"Added {filters.Count - previous} filters from the clipboard.", Constants.HUE_SUCCESS);
            }
            catch (JsonException)
            {
                Report("The clipboard does not contain a filter list.", Constants.HUE_ERROR);
            }
        }

        void Export()
        {
            var enabled = filters.Where(id => manager.IsSoundFiltered(id, isMusic)).ToHashSet();
            JsonSerializer.Serialize(enabled, HashSetIntContext.Default.HashSetInt32).CopyToClipboard();
            Report($"Copied {enabled.Count} enabled filters.", Constants.HUE_SUCCESS);
        }

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
