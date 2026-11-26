#nullable enable
using System;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

internal static class OnScreenMessageTabContent
{
    internal static Widget Build()
    {
        var root = new VerticalStackPanel { Spacing = 10 };

        if (ProfileManager.CurrentProfile is not { } profile)
        {
            root.Widgets.Add(Text("assistant_filters_connect"));
            return root;
        }

        var phrases = profile.OnScreenMessagePhrases ??= new();
        var rules = new VerticalStackPanel();
        var input = new MyraInputBox { HintText = TazLang.Get("assistant_filters_phrase_hint") };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true, Visible = false };
        var add = DesktopAssistantForm.Button(TazLang.Get("assistant_filters_add_phrase"), AddPhrase, primary: true);

        root.Widgets.Add(Text("assistant_filters_on_screen_help"));
        root.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("assistant_filters_repeated"),
            Text("assistant_filters_repeat_help"),
            MyraCheckButton.CreateWithCallback(profile.ReduceSpellAbsorptionMessages, value =>
            {
                profile.ReduceSpellAbsorptionMessages = value;
                profile.Save();
            }, TazLang.Get("assistant_filters_absorption")),
            MyraCheckButton.CreateWithCallback(profile.ReduceItemCooldownMessages, value =>
            {
                profile.ReduceItemCooldownMessages = value;
                profile.Save();
            }, TazLang.Get("assistant_filters_cooldown"))));
        root.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("assistant_filters_phrases"),
            Text("assistant_filters_phrases_help"), input, DesktopAssistantForm.Actions(add), status, rules));

        BuildRules();
        return root;

        void AddPhrase()
        {
            string phrase = input.Text?.Trim() ?? "";
            if (!Validate(phrase))
                return;

            phrases.Add(phrase, true);
            profile.Save();
            input.Text = "";
            BuildRules();
        }

        bool Validate(string phrase, string? original = null)
        {
            string? error = phrase.Length == 0 ? "assistant_filters_phrase_required" : null;
            if (error == null && phrases.Keys.Any(key => key != original && string.Equals(key, phrase, StringComparison.OrdinalIgnoreCase)))
                error = "assistant_filters_phrase_duplicate";

            status.Visible = error != null;
            status.Text = error == null ? "" : TazLang.Get(error);
            return error == null;
        }

        void BuildRules()
        {
            rules.Widgets.Clear();
            if (phrases.Count == 0)
            {
                rules.Widgets.Add(Text("assistant_filters_phrases_empty"));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(
                GridColumnInfo.Auto(TazLang.Get("agent_col_enabled")),
                GridColumnInfo.Fill(TazLang.Get("assistant_filters_text")),
                GridColumnInfo.Auto(TazLang.Get("agent_col_actions")));
            int row = 1;
            foreach ((string phrase, bool enabled) in phrases)
            {
                string key = phrase;
                var field = new MyraInputBox { Text = key };
                field.LostFocus = () =>
                {
                    string edited = field.Text?.Trim() ?? "";
                    if (edited != key && Validate(edited, key) && phrases.Remove(key, out bool wasEnabled))
                    {
                        phrases.Add(edited, wasEnabled);
                        key = edited;
                        profile.Save();
                    }
                    field.Text = key;
                };

                grid.AddWidget(MyraCheckButton.CreateWithCallback(enabled, value =>
                {
                    if (phrases.ContainsKey(key))
                    {
                        phrases[key] = value;
                        profile.Save();
                    }
                }), row, 0);
                grid.AddWidget(field, row, 1);
                grid.AddWidget(DesktopAssistantForm.Button(TazLang.Get("remove"), () =>
                {
                    if (phrases.Remove(key))
                        profile.Save();
                    status.Visible = false;
                    BuildRules();
                }, destructive: true), row++, 2);
            }
            rules.Widgets.Add(grid);
        }
    }

    private static MyraLabel Text(string key) => new(TazLang.Get(key), MyraLabel.TextStyle.P) { Wrap = true };
}
