#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal static partial class HealingAssistantTabContent
{
    internal sealed class PlannerView(Action refreshStatus) : VerticalStackPanel
    {
        private long _nextRefresh;

        internal void RefreshStatus(bool force = false)
        {
            long now = Time.Ticks;
            if (!force && now < _nextRefresh)
                return;
            _nextRefresh = now + 250;
            refreshStatus();
        }
    }

    private sealed class RangeBar : Container
    {
        protected override Point InternalMeasure(Point availableSize) =>
            new(availableSize.X, Widgets.Count == 0 ? 0 : Widgets.Max(cell => cell.Measure(availableSize).Y));

        protected override void InternalArrange()
        {
            Rectangle bounds = ActualBounds;
            int width = Math.Max(0, bounds.Width - Math.Max(0, Widgets.Count - 1));
            int covered = 0;
            int x = bounds.X;
            foreach (Widget cell in Widgets)
            {
                int start = covered * width / 100;
                covered += (int)cell.Tag;
                int size = covered * width / 100 - start;
                cell.Arrange(new Rectangle(x, bounds.Y, size, bounds.Height));
                x += size + 1;
            }
        }
    }

    private static PlannerView BuildPlanner(AgentPageInteraction interaction, Profile profile,
        MethodSetting[] methods, Func<HealingMethod, (int Min, int Max)> allowedRange)
    {
        var status = new MyraLabel("", MyraLabel.TextStyle.P);
        var segments = new List<(HealingMethod? Method, Widget Cell)>();
        var root = new PlannerView(UpdateStatus) { Spacing = 8 };
        var validation = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var first = new ComboView { MinWidth = 140, MaxWidth = 195, VerticalAlignment = VerticalAlignment.Center };
        foreach (MethodSetting method in methods)
            first.ListView.Widgets.Add(new Label { Text = method.Name });
        first.ListView.SelectedIndex = (int)profile.HealingAssistantFirstMethod;
        first.Tooltip = TazLang.Get("healingassistant_planner_order_tip");
        var controls = DesktopAssistantForm.Actions(
            MyraCheckButton.CreateWithCallback(profile.EnableHealingAssistant, value =>
            {
                profile.EnableHealingAssistant = value;
                root.RefreshStatus(force: true);
            }, TazLang.Get("healingassistant_enable")),
            new MyraLabel(TazLang.Get("healingassistant_highest_range"), MyraLabel.TextStyle.P) { Tooltip = first.Tooltip },
            first, status);
        controls.Background = DesktopAssistantTheme.Sidebar;
        controls.Padding = new Thickness(6);
        root.Widgets.Add(controls);
        root.Widgets.Add(validation);

        var band = new RangeBar { Background = DesktopAssistantTheme.Divider };
        root.Widgets.Add(band);
        var table = new MyraGrid();
        table.SetupWithHeaders(
            GridColumnInfo.Auto(TazLang.Get("agent_col_enabled")),
            GridColumnInfo.Fill(TazLang.Get("hotkeys_action"), 2),
            GridColumnInfo.Auto(TazLang.Get("healingassistant_start_at")),
            GridColumnInfo.Auto(TazLang.Get("healingassistant_effective_range")),
            GridColumnInfo.Fill(TazLang.Get("healingassistant_setup")));

        var selected = Enum.GetValues<HealingMethod>()
            .FirstOrDefault(method => HealingAssistantManager.IsEnabled(profile, method));
        var names = new MyraButton[methods.Length];
        var fills = new Panel[methods.Length];
        var cutoffs = new MyraInputBox[methods.Length];
        var ranges = new MyraLabel[methods.Length];
        var setup = new MyraLabel[methods.Length];
        var details = new VerticalStackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        var bandageOptions = new Panel { Visible = false };

        foreach (HealingMethod method in Enum.GetValues<HealingMethod>())
        {
            int index = (int)method;
            int row = index + 1;
            var fill = new Panel { Visible = false, Background = DesktopAssistantTheme.Selected };
            fills[index] = fill;
            table.AddWidget(fill, row, 0, colspan: 5);
            var enabled = MyraCheckButton.CreateWithCallback(HealingAssistantManager.IsEnabled(profile, method), value =>
            {
                methods[index].SetEnabled(value);
                if (value)
                {
                    var (min, max) = allowedRange(method);
                    if (min <= max)
                        HealingAssistantManager.SetThreshold(profile, method,
                            Math.Clamp(HealingAssistantManager.GetThreshold(profile, method), min, max));
                    else
                        HealingAssistantManager.FitEnabledThresholds(profile);
                }
                validation.Visible = false;
                RefreshPlan();
            });
            enabled.Tooltip = methods[index].ToggleLabel;
            enabled.HorizontalAlignment = HorizontalAlignment.Center;
            table.AddWidget(enabled, row, 0);

            var name = DesktopAssistantTheme.NavigationButton(methods[index].Name);
            name.Background = DesktopAssistantTheme.Canvas;
            name.AcceptsKeyboardFocus = true;
            name.Tooltip = methods[index].Name;
            name.Click += (_, _) => Select(method);
            names[index] = name;
            table.AddWidget(name, row, 1);

            var cutoff = new MyraInputBox { MinWidth = 48, InputFilter = MyraInputBox.DigitInputFilter };
            cutoffs[index] = cutoff;
            cutoff.LostFocus = Commit;
            cutoff.KeyDown += (_, e) =>
            {
                if (e.Data == Keys.Enter)
                    Commit();
            };
            table.AddWidget(cutoff, row, 2);
            ranges[index] = MyraGrid.TableText("—");
            setup[index] = MyraGrid.TableText("—");
            table.AddWidget(ranges[index], row, 3);
            table.AddWidget(setup[index], row, 4);

            void Commit()
            {
                int original = UpperHp(method);
                if (!int.TryParse(cutoff.Text, out int requested) || requested == original)
                {
                    cutoff.Text = original.ToString();
                    return;
                }

                var (min, max) = HealingAssistantManager.IsEnabled(profile, method) ? allowedRange(method) : (1, 99);
                if (min <= max)
                    HealingAssistantManager.SetThreshold(profile, method, 100 - Math.Clamp(requested, 100 - max, 100 - min));
                validation.Visible = min > max || requested != UpperHp(method);
                if (validation.Visible)
                    validation.Text = CutoffHint(min, max);
                RefreshPlan();
            }
        }

        const int setupWidth = 265;
        var editor = new DesktopAssistantForm.FixedSidebarLayout(table, setupWidth) { ColumnSpacing = 8 };
        editor.AddColumn(new Proportion(ProportionType.Fill));
        editor.AddColumn(new Proportion(ProportionType.Pixels, setupWidth));
        editor.AddWidget(table, 0, 0);
        editor.AddWidget(details, 0, 1);
        root.Widgets.Add(editor);
        root.Widgets.Add(bandageOptions);

        first.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (first.ListView.SelectedIndex is int index)
            {
                HealingAssistantManager.ChangeFirstMethod(profile, (HealingMethod)index);
                validation.Visible = false;
                RefreshPlan();
            }
        };
        Select(selected);
        RefreshPlan();
        return root;

        int UpperHp(HealingMethod method) => 100 - HealingAssistantManager.GetThreshold(profile, method);

        void Select(HealingMethod method)
        {
            selected = method;
            for (int i = 0; i < names.Length; i++)
            {
                bool active = i == (int)method;
                fills[i].Visible = active;
                DesktopAssistantTheme.SelectNavigation(names[i], active);
            }
            details.Widgets.Clear();
            bandageOptions.Visible = false;
            bandageOptions.Widgets.Clear();
            switch (method)
            {
                case HealingMethod.Bandages:
                    var advanced = DesktopAssistantForm.Button(TazLang.Get("healingassistant_bandage_options"), () =>
                    {
                        if (bandageOptions.Widgets.Count == 0)
                            bandageOptions.Widgets.Add(DesktopAssistantTheme.Group(TazLang.Get("healingassistant_bandage_options"),
                                BandageAgentTabContent.BuildOptions(profile)));
                        bandageOptions.Visible = !bandageOptions.Visible;
                    });
                    details.Widgets.Add(DesktopAssistantTheme.Group(methods[(int)method].Name, advanced));
                    break;
                case HealingMethod.GreaterHeal:
                    details.Widgets.Add(DesktopAssistantTheme.Group(methods[(int)method].Name,
                        StackField("healingassistant_greater_stack", profile.HealingAssistantGreaterHealSerial, value => profile.HealingAssistantGreaterHealSerial = value)));
                    AddCooldown("healingassistant_planner_potion_delay", "healingassistant_planner_potion_shared",
                        profile.HealingAssistantPotionCooldownMs, HealingAssistantManager.MinimumCooldownMs, value => profile.HealingAssistantPotionCooldownMs = value);
                    break;
                case HealingMethod.TamalaHeal:
                    details.Widgets.Add(DesktopAssistantTheme.Group(methods[(int)method].Name,
                        StackField("healingassistant_full_stack", profile.HealingAssistantFullTamalaSerial, value => profile.HealingAssistantFullTamalaSerial = value),
                        StackField("healingassistant_half_stack", profile.HealingAssistantHalfTamalaSerial, value => profile.HealingAssistantHalfTamalaSerial = value)));
                    AddCooldown("healingassistant_planner_potion_delay", "healingassistant_planner_potion_shared",
                        profile.HealingAssistantPotionCooldownMs, HealingAssistantManager.MinimumCooldownMs, value => profile.HealingAssistantPotionCooldownMs = value);
                    break;
                case HealingMethod.MagicFish:
                    details.Widgets.Add(DesktopAssistantTheme.Group(methods[(int)method].Name,
                        StackField("healingassistant_fish_stack", profile.HealingAssistantMagicFishSerial, value => profile.HealingAssistantMagicFishSerial = value)));
                    AddCooldown("healingassistant_planner_fish_delay", null, profile.HealingAssistantFishCooldownMs, HealingAssistantManager.MinimumCooldownMs,
                        value => profile.HealingAssistantFishCooldownMs = value);
                    break;
                default:
                    if (RequiredBook(method) is { } book)
                    {
                        var note = new MyraLabel(TazLang.Get(BookIsSet(book) ? "healingassistant_book_set" : "healingassistant_book_needed",
                            [ShardSpellCatalog.Get(book).Name]), MyraLabel.TextStyle.P);
                        var open = DesktopAssistantForm.Button(TazLang.Get("healingassistant_open_books"),
                            () => DesktopAgentsWindow.Show(ShardSpellBooksTabContent.Title));
                        details.Widgets.Add(DesktopAssistantTheme.Group(methods[(int)method].Name, note, open));
                    }
                    else
                        details.Widgets.Add(DesktopAssistantTheme.Group(methods[(int)method].Name,
                            new MyraLabel(TazLang.Get("healingassistant_magery"), MyraLabel.TextStyle.P)));
                    AddCooldown("healingassistant_planner_spell_delay", "healingassistant_planner_spell_shared",
                        profile.HealingAssistantSpellCooldownMs, HealingAssistantManager.MinimumSpellCooldownMs, value => profile.HealingAssistantSpellCooldownMs = value);
                    break;
            }
        }

        VerticalStackPanel StackField(string key, uint serial, Action<uint> save)
        {
            var field = BuildSerialField(interaction, TazLang.Get(key), serial, value =>
            {
                save(value);
                RefreshSetup();
                root.RefreshStatus(force: true);
            });
            var stack = new VerticalStackPanel { Spacing = 3 };
            stack.Widgets.Add(new MyraLabel(field.Label, MyraLabel.TextStyle.P));
            stack.Widgets.Add(DesktopAssistantForm.Actions(field.Control, field.Actions!));
            return stack;
        }

        void AddCooldown(string key, string? description, int milliseconds, int minimum, Action<int> save)
        {
            var seconds = new MyraInputBox { MinWidth = 66, InputFilter = c => char.IsDigit(c) || c == '.' };
            seconds.Text = Format();
            seconds.LostFocus = Commit;
            seconds.KeyDown += (_, e) =>
            {
                if (e.Data == Keys.Enter)
                    Commit();
            };
            var input = DesktopAssistantForm.Actions(seconds, new MyraLabel(TazLang.Get("healingassistant_seconds"), MyraLabel.TextStyle.P));
            details.Widgets.Add(description == null ? DesktopAssistantTheme.Group(TazLang.Get(key), input)
                : DesktopAssistantTheme.Group(TazLang.Get(key), input, new MyraLabel(TazLang.Get(description), MyraLabel.TextStyle.P)));

            string Format() => (milliseconds / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

            void Commit()
            {
                if (decimal.TryParse(seconds.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
                {
                    value = Math.Clamp(value, minimum / 1000m, int.MaxValue / 1000m);
                    milliseconds = (int)Math.Round(value * 1000);
                    save(milliseconds);
                    root.RefreshStatus(force: true);
                }
                seconds.Text = Format();
            }
        }

        void RefreshPlan()
        {
            band.Widgets.Clear();
            segments.Clear();
            foreach (HealingMethod method in Enum.GetValues<HealingMethod>())
            {
                int index = (int)method;
                cutoffs[index].Text = UpperHp(method).ToString();
                var (min, max) = HealingAssistantManager.IsEnabled(profile, method) ? allowedRange(method) : (1, 99);
                cutoffs[index].Tooltip = CutoffHint(min, max);
                ranges[index].Text = "—";
                ranges[index].Tooltip = null;
            }
            int upper = 100;
            HealingMethod? owner = null;
            // The evaluator gives the later enum value precedence when legacy cutoffs are equal.
            var enabled = Enum.GetValues<HealingMethod>().Where(method => HealingAssistantManager.IsEnabled(profile, method))
                .OrderByDescending(UpperHp).ThenBy(method => method);
            foreach (HealingMethod method in enabled)
            {
                int lower = UpperHp(method);
                AddSegment(owner, lower, upper);
                upper = lower;
                owner = method;
            }
            AddSegment(owner, 0, upper);
            RefreshSetup();
            root.RefreshStatus(force: true);
        }

        void AddSegment(HealingMethod? method, int lower, int upper)
        {
            if (upper <= lower)
                return;
            string range = lower == 0 ? $"≤{upper}%" : $">{lower}–{upper}%";
            string caption = method is { } owner ? methods[(int)owner].Name : TazLang.Get("healingassistant_no_hp_heal");
            var cell = new VerticalStackPanel
            {
                Tag = upper - lower, Padding = new Thickness(4, 3), ClipToBounds = true, Tooltip = $"{range} · {caption}"
            };
            var label = MyraGrid.TableText(range);
            label.Font = DesktopAssistantTheme.Font(bold: true);
            cell.Widgets.Add(label);
            cell.Widgets.Add(MyraGrid.TableText(caption));
            band.Widgets.Add(cell);
            segments.Add((method, cell));
            if (method is { } kind)
            {
                ranges[(int)kind].Text = range;
                ranges[(int)kind].Tooltip = lower == 0
                    ? TazLang.Get("healingassistant_lowest_range", [upper.ToString()])
                    : TazLang.Get("healingassistant_active_range", [lower.ToString(), upper.ToString()]);
            }
        }

        void RefreshSetup()
        {
            foreach (HealingMethod method in Enum.GetValues<HealingMethod>())
            {
                string caption = RequiredBook(method) is { } book
                    ? TazLang.Get(BookIsSet(book) ? "healingassistant_setup_book" : "healingassistant_setup_missing_book")
                    : method is HealingMethod.MagicFish or HealingMethod.TamalaHeal or HealingMethod.GreaterHeal
                        ? TazLang.Get(HasStack(method) ? "healingassistant_setup_stack" : "healingassistant_setup_missing_stack")
                        : "—";
                setup[(int)method].Text = caption;
                setup[(int)method].Tooltip = caption;
            }
        }

        void UpdateStatus()
        {
            PlayerMobile? player = World.Instance?.Player;
            bool hasHealth = player is { HitsMax: > 0, IsDead: false };
            var current = hasHealth ? HealingAssistantManager.GetMethodForHealth(profile, player!.Hits, player.HitsMax) : null;
            for (int i = 0; i < segments.Count; i++)
            {
                var (method, cell) = segments[i];
                cell.Background = profile.EnableHealingAssistant && hasHealth && method == current
                    ? DesktopAssistantTheme.Selected : i % 2 == 0 ? DesktopAssistantTheme.Sidebar : DesktopAssistantTheme.TabStrip;
            }
            string message;
            if (!profile.EnableHealingAssistant)
                message = TazLang.Get("healingassistant_status_off");
            else if (!hasHealth)
                message = TazLang.Get("healingassistant_status_unavailable");
            else if (BandageManager.Instance.IsBandaging)
                message = TazLang.Get("healingassistant_status_bandaging");
            else if (current is not { } method)
                message = TazLang.Get("healingassistant_no_hp_heal");
            else
            {
                string name = methods[(int)method].Name;
                long delay = BandageManager.Instance.GetHealingCooldownRemaining(profile, method, Time.Ticks);
                if (!HasStack(method))
                    message = TazLang.Get("healingassistant_status_missing_stack", [name]);
                else if (RequiredBook(method) is { } book && !BookIsSet(book))
                    message = TazLang.Get("healingassistant_status_missing_book", [ShardSpellCatalog.Get(book).Name]);
                else if (delay > 0)
                    message = TazLang.Get("healingassistant_status_cooldown", [name, ((delay + 999) / 1000).ToString()]);
                else
                    message = TazLang.Get("healingassistant_status_in_range", [name]);
            }
            if (hasHealth)
                message = $"{100d * player!.Hits / player.HitsMax:0.#}% HP · {message}";
            status.Text = message;
        }

        bool HasStack(HealingMethod method) => method switch
        {
            HealingMethod.MagicFish => SerialHelper.IsItem(profile.HealingAssistantMagicFishSerial),
            HealingMethod.GreaterHeal => SerialHelper.IsItem(profile.HealingAssistantGreaterHealSerial),
            HealingMethod.TamalaHeal => SerialHelper.IsItem(profile.HealingAssistantFullTamalaSerial)
                || SerialHelper.IsItem(profile.HealingAssistantHalfTamalaSerial),
            _ => true
        };

        static bool BookIsSet(ShardSpellBook book) => ShardSpellCaster.Instance.GetBookSerial(book) != 0;

        static ShardSpellBook? RequiredBook(HealingMethod method) => method switch
        {
            HealingMethod.NaturesTouch => ShardSpellBook.Earth,
            HealingMethod.AngelicAura => ShardSpellBook.Holy,
            HealingMethod.SongOfLife => ShardSpellBook.Songs,
            _ => null
        };

        static string CutoffHint(int min, int max) => min > max ? TazLang.Get("healingassistant_no_threshold_range")
            : TazLang.Get("healingassistant_hp_threshold_range", [(100 - max).ToString(), (100 - min).ToString()]);
    }
}
