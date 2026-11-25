#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.TextureAtlases;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

/// <summary>
/// Lists shard spell books and lets players bind their own copies, place spell icons, and edit spell button IDs.
/// </summary>
internal static class ShardSpellBooksTabContent
{
    #region Private members

    private const int PREVIEW_SIZE = 24;
    private const int BUTTON_INPUT_WIDTH = 78;
    private const string COLLAPSED_GLYPH = "⮞";
    private const string EXPANDED_GLYPH = "⮟";

    /// <summary>Gap between icons placed one after another, so they don't land on top of each other.</summary>
    private const int ICON_STAGGER = 48;

    private const int ICONS_PER_STAGGER_ROW = 8;

    private static int _placedCount;

    #endregion

    internal static string Title => TazLang.Get("shardspells_title");

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var root = new VerticalStackPanel { Spacing = 5, Padding = new Thickness(0, 0, 0, 6) };
        var styleRow = new HorizontalStackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        var style = new ComboView { Id = "spell-icon-style", MinWidth = 130 };
        Profile profile = ProfileManager.CurrentProfile;
        var previews = new List<SpellPreview>();
        // ComboView clones its selected item, so use Myra's cloneable Label.
        style.ListView.Widgets.Add(new Label { Text = TazLang.Get("shardspells_style_symbols") });
        style.ListView.Widgets.Add(new Label { Text = TazLang.Get("shardspells_style_art") });
        style.ListView.SelectedIndex = profile.UseIllustratedShardSpellIcons ? 1 : 0;
        style.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (style.ListView.SelectedIndex is not int index)
                return;

            profile.UseIllustratedShardSpellIcons = index == 1;
            foreach (SpellPreview preview in previews)
                preview.ShowStyle(profile.UseIllustratedShardSpellIcons);
        };
        styleRow.Widgets.Add(new MyraLabel(TazLang.Get("shardspells_icon_style"), MyraLabel.TextStyle.P)
        {
            VerticalAlignment = VerticalAlignment.Center
        });
        styleRow.Widgets.Add(style);
        root.Widgets.Add(styleRow);

        ShardSpellCaster caster = ShardSpellCaster.Instance;
        ShardSpellBookInfo firstBook = ShardSpellCatalog.Books.FirstOrDefault(book => caster.GetBookSerial(book.Book) != 0)
            ?? ShardSpellCatalog.Books[0];
        Action<bool>? expandOpenBook = null;
        foreach (ShardSpellBookInfo book in ShardSpellCatalog.Books)
        {
            var (section, disclosure, setExpanded) = BuildBook(book, interaction, previews, profile.UseIllustratedShardSpellIcons);
            disclosure.Click += (_, _) =>
            {
                bool opening = expandOpenBook != setExpanded;
                expandOpenBook?.Invoke(false);
                expandOpenBook = opening ? setExpanded : null;
                expandOpenBook?.Invoke(true);
            };
            if (book.Book == firstBook.Book)
            {
                expandOpenBook = setExpanded;
                setExpanded(true);
            }
            root.Widgets.Add(section);
        }

        return root;
    }

    private static (VerticalStackPanel Section, MyraButton Disclosure, Action<bool> SetExpanded) BuildBook(
        ShardSpellBookInfo book, AgentPageInteraction interaction, List<SpellPreview> previews, bool illustrated)
    {
        ShardSpellCaster caster = ShardSpellCaster.Instance;
        var (table, buttonIds, placeButtons) = BuildSpellTable(book, interaction, previews, illustrated);
        var disclosure = DesktopAssistantTheme.ActionButton(book.Name);
        disclosure.Id = $"spell-book-{book.Book}";
        disclosure.Background = null;
        disclosure.Border = null;
        disclosure.Padding = new Thickness(4, 3);
        var caption = disclosure.Content;
        var disclosureLabel = new HorizontalStackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var glyph = MyraLabel.Symbol(COLLAPSED_GLYPH, 13, DesktopAssistantTheme.Text);
        disclosure.Content = disclosureLabel;
        disclosureLabel.Widgets.Add(glyph);
        disclosureLabel.Widgets.Add(caption);
        var bookStatus = new MyraLabel("", MyraLabel.TextStyle.P)
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextColor = DesktopAssistantTheme.SecondaryText
        };

        var editIds = DesktopAssistantTheme.ActionButton(TazLang.Get("shardspells_edit_ids"));
        editIds.Id = $"spell-edit-ids-{book.Book}";
        editIds.Tooltip = TazLang.Get("shardspells_button_id_tooltip");
        editIds.Click += (_, _) =>
        {
            bool show = !buttonIds[0].Visible;
            foreach (Widget buttonId in buttonIds)
                buttonId.Visible = show;
            editIds.Text = TazLang.Get(show ? "shardspells_hide_ids" : "shardspells_edit_ids");
        };
        var pickBook = DesktopAssistantTheme.ActionButton(TazLang.Get("shardspells_set_book"));
        pickBook.Id = $"spell-pick-book-{book.Book}";
        pickBook.Tooltip = TazLang.Get("shardspells_set_book_tooltip");
        pickBook.Click += (_, _) => interaction.SelectTarget(target =>
        {
            if (target is Item item)
            {
                caster.SetBookSerial(book.Book, item.Serial);
                UpdateBinding();
            }
        });

        var actions = DesktopAssistantForm.Actions(editIds, pickBook);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Visible = false;
        table.Visible = false;

        var header = new MyraGrid
        {
            Padding = new Thickness(6, 3), ColumnSpacing = 7, Background = DesktopAssistantTheme.Sidebar
        };
        header.AddColumn();
        header.AddColumn(new Proportion(ProportionType.Fill));
        header.AddColumn();
        header.AddWidget(disclosure, 0, 0);
        header.AddWidget(bookStatus, 0, 1);
        header.AddWidget(actions, 0, 2);

        var section = new VerticalStackPanel { Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(1) };
        section.Widgets.Add(header);
        section.Widgets.Add(table);
        UpdateBinding();
        return (section, disclosure, SetExpanded);

        void SetExpanded(bool expanded)
        {
            glyph.Text = expanded ? EXPANDED_GLYPH : COLLAPSED_GLYPH;
            actions.Visible = table.Visible = expanded;
        }

        void UpdateBinding()
        {
            uint serial = caster.GetBookSerial(book.Book);
            bookStatus.Text = serial == 0
                ? TazLang.Get("shardspells_book_unbound")
                : TazLang.Get("shardspells_book_bound", [$"0x{serial:X8}"]);
            pickBook.Text = TazLang.Get(serial == 0 ? "shardspells_set_book" : "shardspells_change_book");
            foreach (MyraButton place in placeButtons)
            {
                place.Enabled = serial != 0;
                place.Tooltip = TazLang.Get(serial == 0 ? "shardspells_place_unavailable" : "shardspells_place_icon_tooltip");
            }
        }
    }

    private static (MyraGrid Table, Widget[] ButtonIds, MyraButton[] PlaceButtons) BuildSpellTable(
        ShardSpellBookInfo book, AgentPageInteraction interaction, List<SpellPreview> previews, bool illustrated)
    {
        ShardSpellCaster caster = ShardSpellCaster.Instance;
        var table = new MyraGrid { Id = $"spell-table-{book.Book}" };
        Widget[] headers = table.SetupWithHeaders(
            GridColumnInfo.Fill(TazLang.Get("shardspells_spell")),
            GridColumnInfo.Auto(TazLang.Get("shardspells_button_id")),
            GridColumnInfo.Auto(TazLang.Get("shardspells_game_icon"), alignRight: true));
        table.ShowGridLines = false;
        table.BorderThickness = new Thickness(0);
        table.RowSpacing = 0;
        var buttonIds = new Widget[book.Spells.Count + 1];
        buttonIds[0] = headers[1];
        headers[1].Visible = false;
        var placeButtons = new MyraButton[book.Spells.Count];

        for (int i = 0; i < book.Spells.Count; i++)
        {
            int row = i + 1;
            int spellIndex = i;
            ShardSpell spell = book.Spells[spellIndex];
            table.AddWidget(new Panel { Border = DesktopAssistantTheme.Divider, BorderThickness = new Thickness(0, 1, 0, 0) }, row, 0, colspan: 3);
            var name = new HorizontalStackPanel
            {
                Spacing = 7, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center
            };
            name.Widgets.Add(BuildPreview(book.Book, spellIndex, spell, previews, illustrated));
            MyraLabel spellLabel = MyraGrid.TableText(spell.Name);
            spellLabel.VerticalAlignment = VerticalAlignment.Center;
            name.Widgets.Add(spellLabel);
            table.AddWidget(name, row, 0);

            var buttonId = new IntegerInputBox(value => caster.SetButton(book.Book, spellIndex, value))
            {
                Id = $"spell-id-{book.Book}-{spellIndex}",
                Value = caster.GetButton(book.Book, spellIndex),
                MinValue = 0,
                MinWidth = BUTTON_INPUT_WIDTH,
                Visible = false,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Tooltip = TazLang.Get("shardspells_button_id_tooltip")
            };
            table.AddWidget(buttonId, row, 1);
            buttonIds[row] = buttonId;

            var place = DesktopAssistantTheme.ActionButton(TazLang.Get("shardspells_place"), primary: true);
            place.Id = $"spell-place-{book.Book}-{spellIndex}";
            place.Margin = new Thickness(6, 3);
            place.HorizontalAlignment = HorizontalAlignment.Right;
            place.Click += (_, _) => interaction.RunInGame(() => PlaceIcon(book.Book, spellIndex));
            table.AddWidget(place, row, 2);
            placeButtons[spellIndex] = place;
        }

        return (table, buttonIds, placeButtons);
    }

    private static Widget BuildPreview(ShardSpellBook book, int index, ShardSpell spell, List<SpellPreview> previews, bool illustrated)
    {
        var (pictogram, illustration, bounds) = ShardSpellButtonGump.LoadIconArt(book, index);
        if (pictogram == null && illustration == null)
            return new MyraLabel(spell.Abbreviation, MyraLabel.TextStyle.P) { MinWidth = PREVIEW_SIZE, Tooltip = spell.Name };

        var image = new Image
        {
            MaxWidth = PREVIEW_SIZE, MaxHeight = PREVIEW_SIZE, VerticalAlignment = VerticalAlignment.Center
        };
        var preview = new SpellPreview(image,
            pictogram == null ? null : new TextureRegion(pictogram, bounds),
            illustration == null ? null : new TextureRegion(illustration, bounds));
        preview.ShowStyle(illustrated);
        previews.Add(preview);
        return image;
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

    private sealed record SpellPreview(Image Image, TextureRegion? Pictogram, TextureRegion? Illustration)
    {
        internal void ShowStyle(bool illustrated) => Image.Renderable = illustrated ? Illustration ?? Pictogram : Pictogram ?? Illustration;
    }

}
