#nullable enable

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

/// <summary>
/// Lists the shard spell books. For each book the player picks their own copy, then clicks a spell to place its
/// icon on screen; the icon casts the spell on double-click. Each spell's gump button can be corrected if a book
/// doesn't number its buttons in reading order.
/// </summary>
internal static class ShardSpellBooksTabContent
{
    #region Private members

    private const int SPELLS_PER_ROW = 4;
    private const int BUTTON_INPUT_WIDTH = 40;

    /// <summary>Gap between icons placed one after another, so they don't land on top of each other.</summary>
    private const int ICON_STAGGER = 48;

    private const int ICONS_PER_STAGGER_ROW = 8;

    private static int _placedCount;

    #endregion

    internal static string Title => TazLang.Get("shardspells_title", "Spell Books");

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var root = new VerticalStackPanel { Spacing = MyraStyle.STANDARD_SPACING, Padding = new Thickness(6) };
        root.Widgets.Add(new MyraLabel(TazLang.Get("shardspells_intro"), MyraLabel.TextStyle.P) { Wrap = true, MaxWidth = 560 });
        var styleRow = new HorizontalStackPanel { Spacing = 6 };
        var style = new ComboView { MinWidth = 130 };
        Profile profile = ProfileManager.CurrentProfile;
        // ComboView clones its selected item, so use Myra's cloneable Label.
        style.ListView.Widgets.Add(new Label { Text = TazLang.Get("shardspells_style_symbols", "Pictograms") });
        style.ListView.Widgets.Add(new Label { Text = TazLang.Get("shardspells_style_art", "Illustrations") });
        style.ListView.SelectedIndex = profile.UseIllustratedShardSpellIcons ? 1 : 0;
        style.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (style.ListView.SelectedIndex is int index)
                profile.UseIllustratedShardSpellIcons = index == 1;
        };
        styleRow.Widgets.Add(new MyraLabel(TazLang.Get("shardspells_icon_style", "Icon style"), MyraLabel.TextStyle.P));
        styleRow.Widgets.Add(style);
        root.Widgets.Add(styleRow);

        foreach (ShardSpellBookInfo book in ShardSpellCatalog.Books)
            root.Widgets.Add(BuildBook(book, interaction));

        return root;
    }

    private static Widget BuildBook(ShardSpellBookInfo book, AgentPageInteraction interaction)
    {
        ShardSpellCaster caster = ShardSpellCaster.Instance;
        var bookLabel = new MyraLabel(BookText(book), MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };

        var header = new HorizontalStackPanel { Spacing = 6 };
        header.Widgets.Add(bookLabel);
        header.Widgets.Add(new MyraButton(TazLang.Get("shardspells_pickbook"), () =>
            interaction.SelectTarget(target =>
            {
                if (target is Item item)
                {
                    caster.SetBookSerial(book.Book, item.Serial);
                    bookLabel.Text = BookText(book);
                }
            })
        ) { Tooltip = TazLang.Get("shardspells_pickbook_tooltip") });

        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 4 };

        for (int i = 0; i < book.Spells.Count; i++)
        {
            Widget cell = BuildSpell(book, i, interaction);
            Grid.SetColumn(cell, i % SPELLS_PER_ROW);
            Grid.SetRow(cell, i / SPELLS_PER_ROW);
            grid.Widgets.Add(cell);
        }

        return new VisualContainer(new VisualContainerProps { LabelText = book.Name }, header, grid);
    }

    private static Widget BuildSpell(ShardSpellBookInfo book, int index, AgentPageInteraction interaction)
    {
        ShardSpell spell = book.Spells[index];
        ShardSpellCaster caster = ShardSpellCaster.Instance;

        var cell = new HorizontalStackPanel { Spacing = 2 };
        cell.Widgets.Add(new MyraButton(spell.Name, () => interaction.RunInGame(() => PlaceIcon(book.Book, index)))
        {
            Tooltip = TazLang.Get("shardspells_place_tooltip")
        });

        var buttonInput = new IntegerInputBox(value => caster.SetButton(book.Book, index, value))
        {
            Value = caster.GetButton(book.Book, index),
            MinValue = 0,
            MinWidth = BUTTON_INPUT_WIDTH,
            Tooltip = TazLang.Get("shardspells_button_tooltip")
        };
        cell.Widgets.Add(buttonInput);
        return cell;
    }

    /// <summary>Puts the spell's icon near the middle of the game window, each new one a step further along.</summary>
    private static void PlaceIcon(ShardSpellBook book, int index)
    {
        var icon = new ShardSpellButtonGump(World.Instance, book, index);
        Point center = new(Client.Game.Window.ClientBounds.Width / 2, Client.Game.Window.ClientBounds.Height / 2);

        int slot = _placedCount++ % (ICONS_PER_STAGGER_ROW * ICONS_PER_STAGGER_ROW);
        icon.X = center.X - ICON_STAGGER * ICONS_PER_STAGGER_ROW / 2 + slot % ICONS_PER_STAGGER_ROW * ICON_STAGGER;
        icon.Y = center.Y + slot / ICONS_PER_STAGGER_ROW * ICON_STAGGER;
        UIManager.Add(icon);
    }

    private static string BookText(ShardSpellBookInfo book)
    {
        uint serial = ShardSpellCaster.Instance.GetBookSerial(book.Book);

        return serial == 0
            ? TazLang.Get("shardspells_book_none")
            : TazLang.Get("shardspells_book_picked", ["0x" + serial.ToString("X8")]);
    }

}
