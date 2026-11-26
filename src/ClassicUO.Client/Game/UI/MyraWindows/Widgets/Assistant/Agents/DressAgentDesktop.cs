#nullable enable
using System;
using System.Globalization;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

public static partial class DressAgentTabContent
{
    private static Widget BuildDesktop(AgentPageInteraction interaction)
    {
        var manager = DressAgentManager.Instance;
        Point gearScroll = Point.Zero;
        return AgentPresetPanel.Build(interaction, TazLang.Get("agent_preset_title"), manager.CurrentPlayerConfigs,
            initial: null, config => config.Name, manager.CreateNewConfig,
            (config, name) => { config.Name = name; manager.Save(); }, copy: null,
            config => { manager.DeleteConfig(config); return true; }, BuildDetails,
            desktopFullHeight: true, rowDetail: config => config.Items.Count.ToString(CultureInfo.CurrentCulture));

        Widget BuildDetails(DressConfig? config, Action<DressConfig?> refresh)
        {
            if (config == null)
                return new MyraLabel(TazLang.Get("dressagent_desktop_no_presets"), MyraLabel.TextStyle.P);

            bool hasGear = config.Items.Count > 0;
            var dress = DesktopAssistantForm.Button(TazLang.Get("dressagent_dress"), () =>
            {
                manager.DressFromConfig(config);
                GameActions.Print(TazLang.Get("dressagent_desktop_dressing", [config.Name]));
            }, primary: true);
            var undress = DesktopAssistantForm.Button(TazLang.Get("dressagent_undress"), () =>
            {
                manager.UndressFromConfig(config);
                GameActions.Print(TazLang.Get("dressagent_desktop_undressing", [config.Name]));
            });
            dress.Enabled = undress.Enabled = hasGear;

            var headingText = new VerticalStackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var presetName = MyraGrid.TableText(config.Name);
            presetName.Font = DesktopAssistantTheme.Font(bold: true);
            headingText.Widgets.Add(presetName);
            string count = config.Items.Count == 1 ? TazLang.Get("dressagent_desktop_one_item")
                : TazLang.Get("dressagent_desktop_item_count", [config.Items.Count.ToString(CultureInfo.CurrentCulture)]);
            headingText.Widgets.Add(new MyraLabel(count, MyraLabel.TextStyle.P) { TextColor = DesktopAssistantTheme.SecondaryText });
            var heading = SectionGrid();
            heading.AddColumn(new Proportion(ProportionType.Fill));
            heading.AddColumn();
            heading.AddWidget(headingText, 0, 0);
            heading.AddWidget(DesktopAssistantForm.Actions(dress, undress), 0, 1);

            var tableView = new GearViewport { ShowHorizontalScrollBar = false };
            var addEquipped = DesktopAssistantForm.Button(TazLang.Get("dressagent_desktop_add_equipped"), () =>
            {
                manager.AddCurrentlyEquippedItems(config);
                RefreshGear();
            }, primary: true);
            var addTarget = DesktopAssistantForm.Button(TazLang.Get("dressagent_desktop_add_target"), () =>
            {
                GameActions.Print(TazLang.Get("dressagent_targetitem"), 82);
                interaction.SelectTarget(target =>
                {
                    if (target is not Item item)
                    {
                        GameActions.Print(TazLang.Get("dressagent_desktop_item_required"), Constants.HUE_WARN);
                        return;
                    }
                    manager.AddItemToConfig(config, item.Serial, item.Name);
                    RefreshGear();
                });
            });
            var clear = DesktopAssistantForm.Button(TazLang.Get("dressagent_clearitems"), destructive: true);
            clear.Enabled = hasGear;
            clear.OnClick = () => interaction.Confirm(clear, TazLang.Get("dressagent_desktop_clear_title"),
                TazLang.Get("dressagent_desktop_clear_question", [config.Name]), () =>
                {
                    manager.ClearConfig(config);
                    RefreshGear();
                }, confirmText: clear.Text, destructive: true);

            var gearHeader = new MyraGrid { ColumnSpacing = 8 };
            gearHeader.AddColumn(new Proportion(ProportionType.Fill));
            gearHeader.AddColumn();
            gearHeader.AddWidget(new MyraLabel(TazLang.Get("dressagent_desktop_gear"), MyraLabel.TextStyle.H3)
                { VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            gearHeader.AddWidget(DesktopAssistantForm.Actions(addEquipped, addTarget), 0, 1);
            var gear = SectionGrid();
            gear.AddColumn(new Proportion(ProportionType.Fill));
            gear.AddRow(count: 3);
            gear.AddWidget(gearHeader, 0, 0);
            gear.AddWidget(tableView, 1, 0);
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            gear.AddWidget(clear, 2, 0);

            if (hasGear)
            {
                var table = new MyraGrid();
                table.SetupWithHeaders(
                    GridColumnInfo.Fill(TazLang.Get("dressagent_desktop_item")),
                    GridColumnInfo.Auto(TazLang.Get("dressagent_desktop_slot")),
                    GridColumnInfo.Auto(TazLang.Get("dressagent_desktop_serial")),
                    GridColumnInfo.Auto(TazLang.Get("dressagent_desktop_actions")));
                int row = 1;
                for (int index = config.Items.Count - 1; index >= 0; index--)
                {
                    DressItem item = config.Items[index];
                    string name = StringHelper.RemoveBaseFontTags(item.Name).Trim();
                    table.AddWidget(MyraGrid.TableText(name.Length > 0 ? name : TazLang.Get("dressagent_desktop_unnamed_item")), row, 0);
                    table.AddWidget(MyraGrid.TableText(EquipSlot(item.Layer)), row, 1);
                    table.AddWidget(MyraGrid.TableText($"{item.Serial:X8}"), row, 2);
                    table.AddWidget(DesktopAssistantForm.Button(TazLang.Get("remove"), () =>
                    {
                        manager.RemoveItemFromConfig(config, item.Serial);
                        RefreshGear();
                    }, destructive: true), row++, 3);
                }
                tableView.Content = table;
                tableView.ScrollPosition = gearScroll;
            }
            else
                tableView.Content = new MyraLabel(TazLang.Get("dressagent_desktop_empty_gear"), MyraLabel.TextStyle.P);
            gearScroll = Point.Zero;

            var fastEquip = MyraCheckButton.CreateWithCallback(config.UseKREquipPacket,
                enabled => { config.UseKREquipPacket = enabled; manager.Save(); },
                TazLang.Get("dressagent_desktop_fast_equip"), TazLang.Get("dressagent_krpacket_tooltip"));
            var bagStatus = new MyraLabel(config.UndressBagSerial == 0 ? TazLang.Get("dressagent_desktop_default_bag")
                : $"0x{config.UndressBagSerial:X8}", MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };
            var setBag = DesktopAssistantForm.Button(TazLang.Get("dressagent_setundressbag"), () =>
            {
                GameActions.Print(TazLang.Get("dressagent_targetcontainer"), 82);
                interaction.SelectTarget(target =>
                {
                    if (target is not Item { ItemData.IsContainer: true } bag)
                    {
                        GameActions.Print(TazLang.Get("dressagent_desktop_container_required"), Constants.HUE_WARN);
                        return;
                    }
                    manager.SetUndressBag(config, bag.Serial);
                    RefreshGear();
                });
            });
            var bagActions = DesktopAssistantForm.Actions(bagStatus, setBag);
            if (config.UndressBagSerial != 0)
                bagActions.Widgets.Add(DesktopAssistantForm.Button(TazLang.Get("dressagent_desktop_reset_bag"), () =>
                {
                    manager.SetUndressBag(config, 0);
                    RefreshGear();
                }));
            var settings = DesktopAssistantTheme.Group(TazLang.Get("dressagent_desktop_equip_undress"), fastEquip,
                new MyraLabel(TazLang.Get("dressagent_desktop_undress_bag"), MyraLabel.TextStyle.P)
                { Tooltip = TazLang.Get("dressagent_desktop_bag_tooltip") }, bagActions);
            var macros = DesktopAssistantTheme.Group(TazLang.Get("dressagent_desktop_macros"),
                new MyraLabel(TazLang.Get("dressagent_desktop_macros_hint"), MyraLabel.TextStyle.P),
                DesktopAssistantForm.Actions(
                    DesktopAssistantForm.Button(TazLang.Get("dressagent_createdressmacro"), () =>
                        interaction.RunInGame(() => manager.CreateDressMacro(config.Name))),
                    DesktopAssistantForm.Button(TazLang.Get("dressagent_createundressmacro"), () =>
                        interaction.RunInGame(() => manager.CreateUndressMacro(config.Name)))));
            var footer = new DesktopAssistantForm.SectionPair(settings, macros);
            return new DressDetails(heading, gear, gearHeader, tableView, clear, footer);

            void RefreshGear()
            {
                gearScroll = tableView.ScrollPosition;
                refresh(config);
            }
        }
    }

    private static string EquipSlot(byte value) => (Layer)value switch
    {
        Layer.OneHanded => TazLang.Get("dressagent_desktop_one_handed"),
        Layer.TwoHanded => TazLang.Get("dressagent_desktop_two_handed"),
        Layer.Invalid => TazLang.Get("dressagent_desktop_unknown_slot"),
        var layer => layer.ToString()
    };

    private static MyraGrid SectionGrid() => new()
    {
        Background = DesktopAssistantTheme.GroupFill, Border = DesktopAssistantTheme.Divider,
        BorderThickness = new Thickness(1), Padding = new Thickness(8), RowSpacing = 6, ColumnSpacing = 8
    };

    private sealed class GearViewport : ScrollViewer
    {
        protected override Point InternalMeasure(Point availableSize)
        {
            var desired = base.InternalMeasure(availableSize);
            desired.X = Math.Min(desired.X, availableSize.X);
            return desired;
        }
    }

    private sealed class DressDetails : VerticalStackPanel
    {
        private readonly Widget _heading, _gearHeader, _clear, _footer;
        private readonly MyraGrid _gear;
        private readonly GearViewport _table;

        internal DressDetails(Widget heading, MyraGrid gear, Widget gearHeader, GearViewport table, Widget clear, Widget footer)
        {
            _heading = heading;
            _gear = gear;
            _gearHeader = gearHeader;
            _table = table;
            _clear = clear;
            _footer = footer;
            Spacing = 8;
            Widgets.Add(heading);
            Widgets.Add(gear);
            Widgets.Add(footer);
        }

        protected override Point InternalMeasure(Point availableSize)
        {
            int surroundingHeight = _heading.Measure(availableSize).Y + _footer.Measure(availableSize).Y + 2 * Spacing;
            Point gearSpace = new(Math.Max(0, availableSize.X - _gear.Padding.Left - _gear.Padding.Right), availableSize.Y);
            surroundingHeight += _gearHeader.Measure(gearSpace).Y + _clear.Measure(gearSpace).Y + 2 * _gear.RowSpacing
                + _gear.Padding.Top + _gear.Padding.Bottom + _gear.BorderThickness.Top + _gear.BorderThickness.Bottom;
            // Keep gear and preset scrolling independent. At small heights the settings pane can also scroll.
            _table.MaxHeight = Math.Max(112, availableSize.Y - surroundingHeight);
            return base.InternalMeasure(availableSize);
        }
    }
}
