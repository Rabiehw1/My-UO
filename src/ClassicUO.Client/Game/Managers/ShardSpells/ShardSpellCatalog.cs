#nullable enable

using System.Collections.Generic;
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
public sealed record ShardSpell(string Name, string Abbreviation, int DefaultButton);

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
            "Control Undead", "Darkness", "Decaying Ray", "Spectre's Touch", "Abyssal Flame", "Animate Dead",
            "Sacrifice", "Wraith's Breath", "Sorcerer's Bane", "Summon Spirit", "Wraithform", "Wyvern Strike", "Kill",
            "Plague", "Spellbind"),
        Book(ShardSpellBook.Earth, "Earth Book", new Color(46, 120, 56),
            "Antidote", "Owl Sight", "Shifting Earth", "Summon Mammals", "Call Lightning", "Earth Blessing",
            "Earth Portal", "Nature's Touch", "Gust of Air", "Rising Fire", "Shapeshift", "Ice Strike", "Earth Spirit",
            "Flame Spirit", "Storm Spirit", "Water Spirit"),
        Book(ShardSpellBook.Holy, "Holy Book", new Color(215, 221, 230),
            "Grand Feast", "Turn Undead", "Light of Day", "Angelic Aura", "Holy Bolt", "Seraphim's Will",
            "Angelic Gate", "Remove Curse", "Wrath of God", "Divine Fury", "Astral Storm", "Enlightenment", "Revive",
            "Sanctuary", "Summon Guardian", "Apocalypse"),
        Book(ShardSpellBook.Songs, "Book of Songs", new Color(40, 100, 170),
            "Song of Light", "Song of Haste", "Song of Defense", "Song of Glory", "Song of Cloaking", "Song of Remedy",
            "Song of Life", "Song of Dismissal", "Song of Sirens", "Song of Earth", "Song of Air", "Song of Fire",
            "Song of Water", "Song of Beckon", "Song of Fright", "Song of Salvation"),
        Book(ShardSpellBook.Archaic, "Archaic Verses", new Color(150, 90, 40),
            "Sonic Disturbance", "Lesser Healing", "Shadows", "Corpse Distention", "Spirit Flock", "Dragon Skin",
            "Life Balance", "Bardic Boulders", "Beastal Bond")
    ];

    #endregion

    #region Public methods

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

    #endregion

    #region Private methods

    private static ShardSpellBookInfo Book(ShardSpellBook book, string name, Color color, params string[] spellNames)
    {
        var spells = new List<ShardSpell>(spellNames.Length);
        int firstButton = book == ShardSpellBook.Necro ? 101 : 1;

        for (int i = 0; i < spellNames.Length; i++)
            spells.Add(new ShardSpell(spellNames[i], Abbreviate(spellNames[i]), firstButton + i));

        return new ShardSpellBookInfo(book, name, color, spells);
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
