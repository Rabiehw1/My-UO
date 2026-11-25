#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

/// <summary>
/// Shopping Assistant's Buy and Sell tabs: named lists in a sidebar, one active at a time, each with its own item rules.
/// </summary>
internal static class ShopListsTabContent
{
    #region Private members

    private const int ART_SIZE = 24;
    private const int NUMBER_WIDTH = 70;

    #endregion

    /// <summary>Builds the Buy or Sell tab.</summary>
    /// <param name="interaction">Page services.</param>
    /// <param name="transactionSettings">The Transaction settings box shown under the selected list.</param>
    /// <param name="buying">Buy tab when true, Sell tab when false.</param>
    /// <returns>The page.</returns>
    internal static Widget Build(AgentPageInteraction interaction, Widget transactionSettings, bool buying)
    {
        ShopListSave file = BuySellAgent.Instance.ShopLists;
        ShopListGroup save = buying ? file.Buy : file.Sell;

        Widget presets = AgentPresetPanel.Build(
            interaction, TazLang.Get(buying ? "shoplists_buy_title" : "shoplists_sell_title"), save.Presets, save.GetActive(),
            preset => preset.Name,
            name =>
            {
                var created = new ShopListPreset { Name = name };
                save.Presets.Add(created);
                file.Save();
                return created;
            },
            (preset, name) =>
            {
                preset.Name = name;
                file.Save();
            },
            preset =>
            {
                ShopListPreset copy = preset.Duplicate(TazLang.Get("shoplists_copy_name", [preset.Name]));
                save.Presets.Add(copy);
                file.Save();
                return copy;
            },
            preset =>
            {
                if (save.ActiveId == preset.Id)
                    save.ActiveId = null;
                bool removed = save.Presets.Remove(preset);
                file.Save();
                return removed;
            },
            (preset, refresh) => BuildDetails(new Context(file, save, interaction, buying), preset, refresh),
            minimumCount: 1,
            isRunning: preset => save.ActiveId == preset.Id,
            desktopOptions: interaction.IsDesktop ? transactionSettings : null,
            runningText: TazLang.Get("shoplists_active"));

        if (interaction.IsDesktop)
            return presets;

        var root = new VerticalStackPanel { Spacing = 8 };
        root.Widgets.Add(transactionSettings);
        root.Widgets.Add(presets);
        return root;
    }

    #region Private methods

    private static Widget BuildDetails(Context ctx, ShopListPreset? preset, Action<ShopListPreset?> refresh)
    {
        var details = new VerticalStackPanel { Spacing = 8 };
        if (preset == null)
            return details;

        bool active = ctx.Group.ActiveId == preset.Id;
        details.Widgets.Add(BuildHeader(ctx, preset, active, refresh));

        if (!active)
            details.Widgets.Add(new MyraLabel(TazLang.Get(ctx.Buying ? "shoplists_inactive_buy" : "shoplists_inactive_sell"), MyraLabel.TextStyle.P)
            {
                Wrap = true, TextColor = DesktopAssistantTheme.SecondaryText
            });

        details.Widgets.Add(DesktopAssistantForm.Fields(
            new(TazLang.Get("shopping_item_list"), BuildListPicker(ctx, preset, refresh),
                Button(TazLang.Get("autoloot_manage_lists"), ctx.Interaction.OpenItemLists, ctx.Interaction))));

        details.Widgets.Add(MyraCheckButton.CreateWithCallback(preset.TakeAll, value =>
        {
            preset.TakeAll = value;
            ctx.File.Save();
        }, TazLang.Get(ctx.Buying ? "shoplists_buy_all" : "shoplists_sell_all"),
            TazLang.Get(ctx.Buying ? "shoplists_buy_all_tooltip" : "shoplists_sell_all_tooltip")));

        details.Widgets.Add(ItemListsTabContent.Section(ctx.Interaction,
            TazLang.Get(ctx.Buying ? "shopping_buy_rules" : "shopping_sell_rules"), BuildRules(ctx, preset)));
        return details;
    }

    /// <summary>Name on the left; one button on the right that makes this the active list or stops using it.</summary>
    private static Widget BuildHeader(Context ctx, ShopListPreset preset, bool active, Action<ShopListPreset?> refresh)
    {
        var title = MyraGrid.TableText(preset.Name);
        title.Font = DesktopAssistantTheme.Font(1, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;

        MyraButton toggle = DesktopAssistantTheme.ActionButton(
            TazLang.Get(active ? "shoplists_stop_using" : "shoplists_use"),
            () =>
            {
                ctx.Group.ActiveId = active ? null : preset.Id;
                ctx.File.Save();
                refresh(preset);
            },
            primary: !active);
        toggle.Tooltip = TazLang.Get("shoplists_use_tooltip");

        var header = new MyraGrid { ColumnSpacing = 8 };
        header.AddColumn(new Proportion(ProportionType.Fill));
        header.AddColumn();
        header.AddWidget(title, 0, 0);
        header.AddWidget(toggle, 0, 1);
        return header;
    }

    private static Widget BuildListPicker(Context ctx, ShopListPreset preset, Action<ShopListPreset?> refresh)
    {
        List<SharedItemList> lists = ItemListLibrary.Instance.Lists;
        var combo = new ComboView { MinWidth = 200 };

        // ComboView clones its selected item, so use Myra's cloneable Label.
        combo.ListView.Widgets.Add(new Label { Text = TazLang.Get("shoplists_no_list") });
        foreach (SharedItemList list in lists)
            combo.ListView.Widgets.Add(new Label { Text = list.Name });

        int index = lists.FindIndex(list => list.Id == preset.ItemListId);
        combo.ListView.SelectedIndex = index + 1;
        combo.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (combo.ListView.SelectedIndex is not int selected)
                return;

            preset.ItemListId = selected > 0 && selected <= lists.Count ? lists[selected - 1].Id : null;
            preset.SyncRules();
            ctx.File.Save();
            refresh(preset);
        };
        return combo;
    }

    private static Widget BuildRules(Context ctx, ShopListPreset preset)
    {
        List<BuySellItemConfig> rules = preset.SyncRules();
        SharedItemList? list = ItemListLibrary.Resolve(preset.ItemListId);
        if (list == null || rules.Count == 0)
            return new MyraLabel(TazLang.Get(list == null ? "shoplists_pick_list" : "shoplists_empty_list"), MyraLabel.TextStyle.P) { Wrap = true };

        var grid = new MyraGrid();
        grid.SetupWithHeaders(
        [
            GridColumnInfo.Auto(TazLang.Get("agent_col_art")),
            GridColumnInfo.Fill(TazLang.Get("shoplists_col_item")),
            GridColumnInfo.Auto(TazLang.Get("agent_col_maxamount")),
            GridColumnInfo.Auto(TazLang.Get(ctx.Buying ? "autobuy_col_restockupto" : "autosell_col_minonhand")),
            ..ctx.Buying ? new[] { GridColumnInfo.Auto(TazLang.Get("autobuy_col_maxprice")) } : [],
            GridColumnInfo.Auto(TazLang.Get("agent_col_enabled"))
        ]);

        for (int i = 0; i < rules.Count; i++)
        {
            BuySellItemConfig rule = rules[i];
            int row = i + 1;
            grid.AddWidget(new MyraArtTexture(rule.Graphic, maxSize: ART_SIZE)
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }, row, 0);

            string name = list.Find(rule.Graphic, rule.Hue)?.Name is { Length: > 0 } listName ? listName : $"0x{rule.Graphic:X4}";
            MyraLabel label = MyraGrid.TableText(name);
            label.VerticalAlignment = VerticalAlignment.Center;
            grid.AddWidget(label, row, 1);

            grid.AddWidget(Number(rule.MaxAmount == ushort.MaxValue ? 0 : rule.MaxAmount, "shoplists_max_tooltip", ctx, value =>
                rule.MaxAmount = value == 0 ? ushort.MaxValue : (ushort)Math.Min(value, ushort.MaxValue)), row, 2);
            grid.AddWidget(Number(rule.RestockUpTo, ctx.Buying ? "autobuy_restockupto_tooltip" : "autosell_minonhand_tooltip", ctx, value =>
                rule.RestockUpTo = (ushort)Math.Min(value, ushort.MaxValue)), row, 3);

            int column = 4;
            if (ctx.Buying)
                grid.AddWidget(Number(rule.MaxPrice, "autobuy_maxprice_tooltip", ctx, value => rule.MaxPrice = (uint)Math.Min(value, uint.MaxValue)), row, column++);

            var enabled = MyraCheckButton.CreateWithCallback(rule.Enabled, value =>
            {
                rule.Enabled = value;
                ctx.File.Save();
            });
            enabled.HorizontalAlignment = HorizontalAlignment.Center;
            grid.AddWidget(enabled, row, column);
        }

        return grid;
    }

    /// <summary>A whole-number box that commits as you type and saves when it loses focus.</summary>
    private static MyraInputBox Number(long value, string tooltipKey, Context ctx, Action<long> commit)
    {
        var box = new MyraInputBox
        {
            Text = value.ToString(), MinWidth = NUMBER_WIDTH, Tooltip = TazLang.Get(tooltipKey),
            InputFilter = MyraInputBox.DigitInputFilter
        };
        box.TextChangedByUser += (_, _) =>
        {
            if (long.TryParse(box.Text, out long parsed))
                commit(parsed);
            else if (string.IsNullOrEmpty(box.Text))
                commit(0);
        };
        box.LostFocus = ctx.File.Save;
        return box;
    }

    private static MyraButton Button(string text, Action action, AgentPageInteraction interaction) =>
        interaction.IsDesktop ? DesktopAssistantTheme.ActionButton(text, action) : new MyraButton(text, action);

    #endregion

    /// <summary>What every part of one tab needs: the file to save, the side's lists, page services and which side.</summary>
    private sealed record Context(ShopListSave File, ShopListGroup Group, AgentPageInteraction Interaction, bool Buying);
}
