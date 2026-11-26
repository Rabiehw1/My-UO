#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers.ShardSpells;

/// <summary>Zulu Hotel spell books that cast through their own gump (the regular spellbook is handled elsewhere).</summary>
public enum ShardSpellBook
{
    Necro,
    Earth,
    Holy,
    Songs,
    Archaic
}

/// <summary>One spell in a shard book.</summary>
/// <param name="Name">Name as written in the book.</param>
/// <param name="Abbreviation">Up to three letters drawn on the icon.</param>
/// <param name="DefaultButton">Gump button that casts it; books number their buttons in reading order.</param>
/// <param name="PowerWords">Words the caster says, as the shard's spell config lists them; null if none.</param>
public sealed record ShardSpell(string Name, string Abbreviation, int DefaultButton, string? PowerWords);

/// <summary>A shard book: its spells in reading order and the colour its icons are drawn in.</summary>
public sealed record ShardSpellBookInfo(ShardSpellBook Book, string Name, Color IconColor, IReadOnlyList<ShardSpell> Spells);

/// <summary>
/// The custom spell books and their spells. Button numbers follow the order spells appear in each book (the
/// songbook's "Song of Life", 7th, is button 7). Adding a spell: append it, since saved icons refer to positions.
/// </summary>
internal static class ShardSpellCatalog
{
    #region Public accessors

    /// <summary>All books, in menu order.</summary>
    public static IReadOnlyList<ShardSpellBookInfo> Books { get; } =
    [
        Book(ShardSpellBook.Necro, "Necro Book", new Color(154, 32, 42),
            ("Control Undead", "Nutu Magistri Supplicare"),
            ("Darkness", "In Caligne Abditus"),
            ("Decaying Ray", "Umbra Aufero Vita"),
            ("Spectre's Touch", "Enervare"),
            ("Abyssal Flame", "Orinundus Barathrum Erado Hostes Hostium"),
            ("Animate Dead", "Corpus Sine Nomine Expergefaceret"),
            ("Sacrifice", "Animus Ex Corporis Resolveretur"),
            ("Wraith's Breath", "Manes Sollicti Mi Compellere"),
            ("Sorcerer's Bane", "Fluctus Perturbo Magus Navitas"),
            ("Summon Spirit", "Manes Turbidi Sollictique Resolverent"),
            ("Wraithform", "Manes Sollicti Mihi Infundite"),
            ("Wyvern Strike", "Umbrae Tenebrae Venarent"),
            ("Kill", "Ulties Manum Necarent"),
            ("Plague", "Fluctus Puter Se Aresceret"),
            ("Spellbind", "Nutu Magistri Se Compellere")),
        Book(ShardSpellBook.Earth, "Earth Book", new Color(46, 120, 56),
            ("Antidote", "Puissante Terre Traite Ce Patient"),
            ("Owl Sight", "Vista Da Noite"),
            ("Shifting Earth", "Esmagamento Con Pedra"),
            ("Summon Mammals", "Chame O Mamifero Agora"),
            ("Call Lightning", "Batida Do Deus"),
            ("Earth Blessing", "Foria Da Terra"),
            ("Earth Portal", "Destraves Limites Da Natureza"),
            ("Nature's Touch", "Guerissez Par Terre"),
            ("Gust of Air", "Gust Do Ar"),
            ("Rising Fire", "Batida Do Fogo"),
            ("Shapeshift", "Mude Minha Forma"),
            ("Ice Strike", "Geada Com Inverno"),
            ("Earth Spirit", "Chame A Terra Elemental"),
            ("Flame Spirit", "Chame O Fogo Elemental"),
            ("Storm Spirit", "Chame O Ar Elemental"),
            ("Water Spirit", "Chame A Agua Elemental")),
        Book(ShardSpellBook.Holy, "Holy Book", new Color(215, 221, 230),
            ("Grand Feast", "Pluris De Epulum"),
            ("Turn Undead", "Verto Phasma Terreo"),
            ("Light of Day", "Lucis Diei"),
            ("Angelic Aura", "Tactum Angelus"),
            ("Holy Bolt", "Sanctus Utor Telum"),
            ("Seraphim's Will", "Pium Fortuno"),
            ("Angelic Gate", "Angelus De Porta"),
            ("Remove Curse", "Aufero Pestis"),
            ("Wrath of God", "Judicium Incursus"),
            ("Divine Fury", "Divinitus Ira"),
            ("Astral Storm", "Caelum Tempestas"),
            ("Enlightenment", "Invenio"),
            ("Revive", "Novo Sano"),
            ("Sanctuary", "Aufero Venarent"),
            ("Summon Guardian", "Chame O Lucis Spiritus"),
            ("Apocalypse", "Mortis De Fatum")),
        Book(ShardSpellBook.Songs, "Book of Songs", new Color(40, 100, 170),
            ("Song of Light", "Emissio Illic Existo Lucescere"),
            ("Song of Haste", "Adfor Adiudico Festinatio Adsimulo Volo"),
            ("Song of Defense", "Impertio Cutis Adsimulo Scrupeus"),
            ("Song of Glory", "Obtestor Tribuo Dominatus Fortuno"),
            ("Song of Cloaking", "Velo Ipse Attritus Iniquus"),
            ("Song of Remedy", "Deporto Eiusmodi Impurus Veneno"),
            ("Song of Life", "Votum Spiritus Medicor Plaga"),
            ("Song of Dismissal", "Deporto Eiusmodi Impurus Veneficium"),
            ("Song of Sirens", "Glacio Iniquus Abutor Reverentia"),
            ("Song of Earth", "Terra Caelesits Impertio Istic Furor"),
            ("Song of Air", "Aetherius Caelesits Impertio Istic Furor"),
            ("Song of Fire", "Ignis Caelesits Impertio Istic Furor"),
            ("Song of Water", "Aqua Caelesits Impertio Istic Furor"),
            ("Song of Beckon", "Genero Coortus Malus Custos"),
            ("Song of Fright", "Refugio Iniquus Abutor Reverentia"),
            ("Song of Salvation", "Misericordia Dono Animo Exanimalis")),
        // Verses are played, not spoken: the shard shows an emote instead of words.
        Book(ShardSpellBook.Archaic, "Archaic Verses", new Color(150, 90, 40),
            ("Sonic Disturbance", null),
            ("Lesser Healing", null),
            ("Shadows", null),
            ("Corpse Distention", null),
            ("Spirit Flock", null),
            ("Dragon Skin", null),
            ("Life Balance", null),
            ("Bardic Boulders", null),
            ("Beastal Bond", null))
    ];

    #endregion

    #region Private members

    // Spells the shard has that no book here lists, so their words are only known for muting: Liche (necro).
    private static readonly string[] ExtraPowerWords = ["Umbrae Tenebrae Miserere Animi Non Digna Ferentis"];

    // Declared after Books: static fields initialise in order.
    private static readonly HashSet<string> _powerWords = BuildPowerWords();

    #endregion

    #region Public methods

    /// <summary>Whether <paramref name="text"/> is exactly one of the shard books' power words, ignoring case.</summary>
    /// <param name="text">Spoken text, already trimmed.</param>
    /// <returns>True for a known incantation.</returns>
    public static bool IsPowerWords(string text) => _powerWords.Contains(text);

    /// <summary>Looks up a book.</summary>
    /// <param name="book">Book to find.</param>
    /// <returns>The book's info.</returns>
    public static ShardSpellBookInfo Get(ShardSpellBook book) => Books[(int)book];

    /// <summary>Looks up a spell by position, for restoring saved icons.</summary>
    /// <param name="book">Book holding the spell.</param>
    /// <param name="index">Position in the book.</param>
    /// <returns>The spell, or null if the position no longer exists.</returns>
    public static ShardSpell? GetSpell(ShardSpellBook book, int index)
    {
        if ((int)book < 0 || (int)book >= Books.Count)
            return null;

        IReadOnlyList<ShardSpell> spells = Get(book).Spells;
        return index >= 0 && index < spells.Count ? spells[index] : null;
    }

    internal static string FormatMacroSelection(ShardSpellBook book, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{book}:{index}");

    internal static bool TryParseMacroSelection(string? text, out ShardSpellBook book, out int index)
    {
        book = default;
        index = -1;
        if (string.IsNullOrEmpty(text))
            return false;

        ReadOnlySpan<char> value = text.AsSpan();
        int delimiter = value.IndexOf(':');
        return delimiter > 0 && Enum.TryParse(value[..delimiter], out book)
            && (uint)book < Books.Count
            && int.TryParse(value[(delimiter + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
            && (index == -1 || GetSpell(book, index) != null);
    }

    #endregion

    #region Private methods

    private static ShardSpellBookInfo Book(
        ShardSpellBook book,
        string name,
        Color color,
        params (string Name, string? PowerWords)[] entries
    )
    {
        var spells = new List<ShardSpell>(entries.Length);
        int firstButton = book == ShardSpellBook.Necro ? 101 : 1;

        for (int i = 0; i < entries.Length; i++)
        {
            (string spellName, string? words) = entries[i];
            spells.Add(new ShardSpell(spellName, Abbreviate(spellName), firstButton + i, words));
        }

        return new ShardSpellBookInfo(book, name, color, spells);
    }

    private static HashSet<string> BuildPowerWords()
    {
        var words = new HashSet<string>(ExtraPowerWords, StringComparer.OrdinalIgnoreCase);

        foreach (ShardSpellBookInfo info in Books)
            foreach (ShardSpell spell in info.Spells)
                if (!string.IsNullOrEmpty(spell.PowerWords))
                    words.Add(spell.PowerWords);

        return words;
    }

    /// <summary>
    /// Initials of the words that matter: "Song of Life" → "SL", "Spectre's Touch" → "ST", "Kill" → "Ki".
    /// </summary>
    private static string Abbreviate(string name)
    {
        var letters = new System.Text.StringBuilder(3);

        foreach (string word in name.Split(' '))
        {
            if (word.Length == 0 || word == "of" || word == "the")
                continue;

            letters.Append(char.ToUpperInvariant(word[0]));

            if (letters.Length == 3)
                break;
        }

        return letters.Length > 1 ? letters.ToString() : name.Length > 1 ? name[..2] : name;
    }

    #endregion
}
