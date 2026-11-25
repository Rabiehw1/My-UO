#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal static class ShoppingAssistantTabContent
{
    internal static string Title => TazLang.Get("assistant_agent_tab_shopping");

    internal static Widget Build(AgentPageInteraction interaction)
    {
        if (ProfileManager.CurrentProfile is not { } profile)
            return new MyraLabel(TazLang.Get("shopping_profilenotloaded"), MyraLabel.TextStyle.P);

        var tabs = new MyraTabControl();
        if (interaction.IsDesktop)
        {
            tabs.HorizontalAlignment = HorizontalAlignment.Stretch;
            tabs.VerticalAlignment = VerticalAlignment.Stretch;
        }

        tabs.AddTab(TazLang.Get("shopping_tab_buy"), () => BuildMode(profile, interaction, AgentType.Buy), rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("shopping_tab_sell"), () => BuildMode(profile, interaction, AgentType.Sell), rebuildOnSelect: true);
        tabs.SelectFirst();
        return tabs;
    }

    private static Widget BuildMode(Profile profile, AgentPageInteraction interaction, AgentType mode)
    {
        bool buying = mode == AgentType.Buy;
        BuySellAgent agent = BuySellAgent.Instance;
        Func<BuySellItemConfig> createRule = buying ? agent.NewBuyConfig : agent.NewSellConfig;
        string ruleTitle = TazLang.Get(buying ? "shopping_buy_rules" : "shopping_sell_rules");
        string quantityLabel = TazLang.Get(buying ? "autobuy_col_restockupto" : "autosell_col_minonhand");
        string quantityTip = TazLang.Get(buying ? "autobuy_restockupto_tooltip" : "autosell_minonhand_tooltip");
        string priceTip = TazLang.Get("autobuy_maxprice_tooltip");

        var entries = new Panel();
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        MyraButton? clear = null;
        var add = Button("agent_addmanualentry", primary: true);
        add.OnClick = () => ShowAddDialog(add, interaction, buying, quantityLabel, quantityTip, createRule, RefreshEntries);
        var target = Button("agent_addfromtarget", () =>
        {
            GameActions.Print(Client.Game.UO.World, TazLang.Get(buying ? "autobuy_targetprompt" : "autosell_targetprompt"));
            interaction.SelectTarget(selected =>
            {
                if (selected is not Item item)
                    return;
                if (!buying && agent.TryGetSellConfig(item.Graphic, item.Hue, out _))
                    return;

                AddTarget(item);
                RefreshEntries();
            });
        });
        target.Tooltip = TazLang.Get(buying ? "autobuy_addfromtarget_tooltip" : "autosell_addfromtarget_tooltip");
        var actions = DesktopAssistantForm.Actions(add, target);

        if (!buying)
        {
            var container = Button("autosell_addfromcontainer", () =>
            {
                GameActions.Print(Client.Game.UO.World, TazLang.Get("autosell_addfromcontainer_prompt"));
                interaction.SelectTarget(selected =>
                {
                    if (selected is not Item source)
                        return;

                    int added = 0;
                    for (LinkedObject item = source.Items; item != null; item = item.Next)
                    {
                        if (item is Item content && !agent.TryGetSellConfig(content.Graphic, content.Hue, out _))
                        {
                            AddTarget(content);
                            added++;
                        }
                    }
                    RefreshEntries();
                    Report(TazLang.Get("autosell_addedfromcontainer", [added.ToString()]), Constants.HUE_SUCCESS);
                });
            });
            container.Tooltip = TazLang.Get("autosell_addfromcontainer_tooltip");
            actions.Widgets.Add(container);

            clear = Button("autosell_clearall");
            clear.Tooltip = TazLang.Get("autosell_clearall_tooltip");
            clear.OnClick = () => interaction.Confirm(clear, TazLang.Get("autosell_clearall"),
                TazLang.Get("shopping_clear_sell_confirm"), () =>
                {
                    agent.SellConfigs?.Clear();
                    RefreshEntries();
                }, confirmText: TazLang.Get("autosell_clearall"), destructive: true);
            MyraStyle.ApplyButtonDangerStyle(clear);
        }

        var import = Button("agent_import", () =>
        {
            string? json = Clipboard.GetClipboardText();
            if (json.NotNullNotEmpty() && BuySellAgent.ImportFromJson(json, mode))
            {
                RefreshEntries();
                Report(TazLang.Get(buying ? "autobuy_imported" : "autosell_imported"), Constants.HUE_SUCCESS);
            }
            else
                Report(TazLang.Get("agent_invalidimport"), Constants.HUE_ERROR);
        });
        import.Tooltip = TazLang.Get("agent_import_tooltip");
        actions.Widgets.Add(import);

        var export = Button("agent_export", () =>
        {
            if (BuySellAgent.GetJsonExport(mode) is { } json)
            {
                json.CopyToClipboard();
                Report(TazLang.Get(buying ? "autobuy_exported" : "autosell_exported"), Constants.HUE_SUCCESS);
            }
        });
        export.Tooltip = TazLang.Get("agent_export_tooltip");
        actions.Widgets.Add(export);
        if (clear != null)
            actions.Widgets.Add(clear);

        var sharedMax = new IntegerInputBox(value =>
        {
            if (buying)
                profile.AutoBuySharedItemMaxAmount = (ushort)value;
            else
                profile.AutoSellSharedItemMaxAmount = (ushort)value;
        })
        {
            MinValue = 0, MaxValue = ushort.MaxValue, MinWidth = 78,
            Value = buying ? profile.AutoBuySharedItemMaxAmount : profile.AutoSellSharedItemMaxAmount,
            Tooltip = TazLang.Get("shopping_shared_max_tooltip")
        };
        var lists = SharedItemListPicker.Build(interaction, TazLang.Get(buying ? "assistant_agent_tab_autobuy" : "assistant_agent_tab_autosell"),
            buying ? profile.AutoBuySharedItemListId : profile.AutoSellSharedItemListId,
            id =>
            {
                if (buying)
                    profile.AutoBuySharedItemListId = id;
                else
                    profile.AutoSellSharedItemListId = id;
            },
            () => (Rules() ?? []).Select(rule => new SharedItem(rule.Graphic, rule.Hue, "")),
            item =>
            {
                if (Rules()?.Any(rule => rule.Graphic == item.Graphic && rule.Hue == item.Hue) == true)
                    return false;

                BuySellItemConfig rule = createRule();
                rule.Graphic = item.Graphic;
                rule.Hue = item.Hue;
                rule.MaxAmount = buying ? profile.AutoBuySharedItemMaxAmount : profile.AutoSellSharedItemMaxAmount;
                return true;
            }, RefreshEntries, DesktopAssistantForm.Fields(new DesktopAssistantForm.Field(TazLang.Get("shopping_shared_max"), sharedMax)));

        var root = new VerticalStackPanel { Spacing = 8 };
        Widget settings = BuildSettings(profile, interaction, buying);
        root.Widgets.Add(interaction.IsDesktop ? new DesktopAssistantForm.SectionPair(settings, lists) : settings);
        if (!interaction.IsDesktop)
            root.Widgets.Add(lists);
        root.Widgets.Add(ItemListsTabContent.Section(interaction, ruleTitle, actions, status,
            interaction.IsDesktop ? entries : new ScrollViewer { MaxHeight = 300, Content = entries }));
        RefreshEntries();
        return interaction.IsDesktop ? new ScrollViewer { Content = root } : root;

        List<BuySellItemConfig>? Rules() => buying ? agent.BuyConfigs : agent.SellConfigs;

        void AddTarget(Item item)
        {
            BuySellItemConfig rule = createRule();
            rule.Graphic = item.Graphic;
            rule.Hue = item.Hue;
        }

        MyraButton Button(string key, Action? action = null, bool primary = false)
        {
            var button = new MyraButton(TazLang.Get(key), action);
            if (interaction.IsDesktop)
                DesktopAssistantTheme.ApplyActionButtonStyle(button, primary);
            return button;
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

        void RefreshEntries()
        {
            entries.Widgets.Clear();
            status.Visible = false;
            List<BuySellItemConfig>? rules = Rules();
            if (clear != null)
                clear.Enabled = rules is { Count: > 0 };
            if (rules is not { Count: > 0 })
            {
                entries.Widgets.Add(new MyraLabel(TazLang.Get("shopping_no_rules"), MyraLabel.TextStyle.P));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(
            [
                GridColumnInfo.Auto(TazLang.Get("agent_col_art")),
                GridColumnInfo.Fill(TazLang.Get("agent_col_graphic")),
                GridColumnInfo.Fill(TazLang.Get("agent_col_hue")),
                GridColumnInfo.Fill(TazLang.Get("agent_col_maxamount")),
                GridColumnInfo.Fill(quantityLabel),
                ..buying ? new[] { GridColumnInfo.Fill(TazLang.Get("autobuy_col_maxprice")) } : [],
                GridColumnInfo.Auto(TazLang.Get("agent_col_enabled")),
                GridColumnInfo.Auto(TazLang.Get("agent_col_actions"))
            ]);

            for (int index = rules.Count - 1, row = 1; index >= 0; index--, row++)
            {
                BuySellItemConfig rule = rules[index];
                if (rule.Graphic > 0)
                    grid.AddWidget(new MyraArtTexture(rule.Graphic, maxSize: interaction.IsDesktop ? 24 : 36)
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, row, 0);

                var graphic = new MyraInputBox { Text = rule.Graphic.ToString(), Tooltip = TazLang.Get("shopping_graphic_tooltip") };
                graphic.TextChangedByUser += (_, _) =>
                {
                    if (TryParseGraphic(graphic.Text, out ushort value))
                        rule.Graphic = value;
                };
                grid.AddWidget(graphic, row, 1);

                var hue = MyraInputBox.Hue(rule.Hue, width: null, tooltip: TazLang.Get("shopping_hue_tooltip"));
                hue.TextChangedByUser += (_, _) =>
                {
                    if (MyraInputBox.TryParseHue(hue.Text, out ushort value))
                        rule.Hue = value;
                };
                grid.AddWidget(hue, row, 2);

                grid.AddWidget(Number(rule.MaxAmount == ushort.MaxValue ? 0u : rule.MaxAmount, TazLang.Get("agent_maxamount_tooltip"), value =>
                {
                    if (ushort.TryParse(value, out ushort amount))
                        rule.MaxAmount = amount == 0 ? ushort.MaxValue : amount;
                }), row, 3);
                grid.AddWidget(Number(rule.RestockUpTo, quantityTip, value =>
                {
                    if (ushort.TryParse(value, out ushort amount))
                        rule.RestockUpTo = amount;
                }), row, 4);

                int column = 5;
                if (buying)
                    grid.AddWidget(Number(rule.MaxPrice, priceTip, value =>
                    {
                        if (uint.TryParse(value, out uint price))
                            rule.MaxPrice = price;
                    }), row, column++);

                var enabled = MyraCheckButton.CreateWithCallback(rule.Enabled, value => rule.Enabled = value);
                enabled.HorizontalAlignment = HorizontalAlignment.Center;
                grid.AddWidget(enabled, row, column++);
                grid.AddWidget(MyraStyle.ApplyButtonDangerStyle(Button("remove", () =>
                {
                    agent.DeleteConfig(rule);
                    RefreshEntries();
                })), row, column);
            }
            entries.Widgets.Add(grid);
        }
    }

    private static Widget BuildSettings(Profile profile, AgentPageInteraction interaction, bool buying)
    {
        var enabled = MyraCheckButton.CreateWithCallback(buying ? profile.BuyAgentEnabled : profile.SellAgentEnabled, value =>
        {
            if (buying)
                profile.BuyAgentEnabled = value;
            else
                profile.SellAgentEnabled = value;
        }, TazLang.Get(buying ? "autobuy_enable" : "autosell_enable"));
        var help = new MyraLabel(TazLang.Get(buying ? "shopping_buy_help" : "shopping_sell_help"), MyraLabel.TextStyle.P)
        {
            Wrap = true
        };
        var total = LabeledHorizontalSlider.CreateSliderWithCallback(0, 1000, buying ? profile.BuyAgentMaxItems : profile.SellAgentMaxItems, value =>
        {
            if (buying)
                profile.BuyAgentMaxItems = (int)value;
            else
                profile.SellAgentMaxItems = (int)value;
        });
        var uniques = LabeledHorizontalSlider.CreateSliderWithCallback(0, 100, buying ? profile.BuyAgentMaxUniques : profile.SellAgentMaxUniques, value =>
        {
            if (buying)
                profile.BuyAgentMaxUniques = (int)value;
            else
                profile.SellAgentMaxUniques = (int)value;
        });
        total.Tooltip = uniques.Tooltip = TazLang.Get("shopping_limit_tooltip");
        var subcontainers = MyraCheckButton.CreateWithCallback(profile.BuyAgentSubContainers, value => profile.BuyAgentSubContainers = value,
            TazLang.Get("shopping_subcontainers"), TazLang.Get("shopping_subcontainers_tooltip"));
        return ItemListsTabContent.Section(interaction, TazLang.Get("shopping_settings"), enabled, help,
            DesktopAssistantForm.Fields(
                new(TazLang.Get("shopping_max_items"), total), new(TazLang.Get("shopping_max_types"), uniques)),
            subcontainers);
    }

    private static void ShowAddDialog(MyraButton anchor, AgentPageInteraction interaction, bool buying,
        string quantityLabel, string quantityTip, Func<BuySellItemConfig> create, Action refresh)
    {
        var graphic = new MyraInputBox { Tooltip = TazLang.Get("shopping_graphic_tooltip") };
        var hue = MyraInputBox.Hue(ushort.MaxValue, width: null, tooltip: TazLang.Get("shopping_hue_tooltip"));
        var maxAmount = Number(0, TazLang.Get("agent_maxamount_tooltip"));
        var quantity = Number(0, quantityTip);
        var price = buying ? Number(0, TazLang.Get("autobuy_maxprice_tooltip")) : null;
        var fields = new List<DesktopAssistantForm.Field>
        {
            new(TazLang.Get("agent_col_graphic"), graphic),
            new(TazLang.Get("agent_col_hue"), hue),
            new(TazLang.Get("agent_col_maxamount"), maxAmount),
            new(quantityLabel, quantity)
        };
        if (price != null)
            fields.Add(new(TazLang.Get("autobuy_col_maxprice"), price));
        foreach (var field in fields)
            field.Control.MinWidth = 96;

        var error = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var body = new VerticalStackPanel { Spacing = 6 };
        body.Widgets.Add(DesktopAssistantForm.Fields(fields.ToArray()));
        body.Widgets.Add(error);
        interaction.ShowDialog(anchor, TazLang.Get(buying ? "shopping_add_buy" : "shopping_add_sell"), body,
            confirmed =>
            {
                if (confirmed)
                    refresh();
            }, Save, confirmText: TazLang.Get("agent_add"), preferredWidth: 365);

        bool Save()
        {
            if (!TryParseGraphic(graphic.Text, out ushort graphicId))
                return Error("shopping_invalid_graphic");
            if (!MyraInputBox.TryParseHue(hue.Text, out ushort hueId))
                return Error("shopping_invalid_hue");
            if (!TryParseNumber(maxAmount, out ushort max) || !TryParseNumber(quantity, out ushort restock))
                return Error("shopping_invalid_quantity");
            uint maxPrice = 0;
            if (price != null && !uint.TryParse(price.Text, out maxPrice) && !string.IsNullOrWhiteSpace(price.Text))
                return Error("shopping_invalid_price");

            BuySellItemConfig rule = create();
            rule.Graphic = graphicId;
            rule.Hue = hueId;
            rule.MaxAmount = max == 0 ? ushort.MaxValue : max;
            rule.RestockUpTo = restock;
            rule.MaxPrice = maxPrice;
            return true;
        }

        bool Error(string key)
        {
            error.Text = TazLang.Get(key);
            error.Visible = true;
            return false;
        }

        static bool TryParseNumber(MyraInputBox box, out ushort value) =>
            ushort.TryParse(box.Text, out value) || string.IsNullOrWhiteSpace(box.Text);
    }

    private static MyraInputBox Number(uint value, string tooltip, Action<string>? update = null)
    {
        var field = new MyraInputBox { Text = value.ToString(), Tooltip = tooltip, InputFilter = MyraInputBox.DigitInputFilter };
        if (update != null)
            field.TextChangedByUser += (_, _) => update(field.Text);
        return field;
    }

    private static bool TryParseGraphic(string text, out ushort graphic)
    {
        if (StringHelper.TryParseInt(text, out int value) && value is > 0 and <= ushort.MaxValue)
        {
            graphic = (ushort)value;
            return true;
        }
        graphic = 0;
        return false;
    }
}
