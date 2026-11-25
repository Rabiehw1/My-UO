#nullable enable

using System;
using System.Linq;
using ClassicUO.Game.Managers.ShardSpells;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers.ShardSpells;

public sealed class ShardSpellCatalogTests
{
    [Theory]
    [InlineData(ShardSpellBook.Necro, 15)]
    [InlineData(ShardSpellBook.Earth, 16)]
    [InlineData(ShardSpellBook.Holy, 16)]
    [InlineData(ShardSpellBook.Songs, 16)]
    [InlineData(ShardSpellBook.Archaic, 9)]
    public void Books_HaveEverySpellFromTheirBook(ShardSpellBook book, int count)
    {
        ShardSpellCatalog.Get(book).Spells.Should().HaveCount(count);
    }

    [Fact]
    public void Books_AreIndexedByEnumValue()
    {
        foreach (ShardSpellBook book in Enum.GetValues<ShardSpellBook>())
            ShardSpellCatalog.Get(book).Book.Should().Be(book);
    }

    [Fact]
    public void Buttons_FollowReadingOrder_SongOfLifeIsSeven()
    {
        ShardSpellBookInfo songs = ShardSpellCatalog.Get(ShardSpellBook.Songs);
        int index = songs.Spells.ToList().FindIndex(s => s.Name == "Song of Life");

        songs.Spells[index].DefaultButton.Should().Be(7);
    }

    [Theory]
    [InlineData(ShardSpellBook.Songs, 6, "SL")]
    [InlineData(ShardSpellBook.Necro, 3, "ST")]
    [InlineData(ShardSpellBook.Necro, 12, "Ki")]
    public void Abbreviations_UseMeaningfulInitials(ShardSpellBook book, int index, string expected)
    {
        ShardSpellCatalog.GetSpell(book, index)!.Abbreviation.Should().Be(expected);
    }

    [Fact]
    public void GetSpell_OutOfRange_ReturnsNull()
    {
        ShardSpellCatalog.GetSpell(ShardSpellBook.Archaic, 9).Should().BeNull();
        ShardSpellCatalog.GetSpell((ShardSpellBook)99, 0).Should().BeNull();
    }
}
