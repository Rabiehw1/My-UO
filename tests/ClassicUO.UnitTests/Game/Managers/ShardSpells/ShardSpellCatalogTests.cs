#nullable enable

using System;
using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Game.Managers.ShardSpells;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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

    [Fact]
    public void NecroButtons_RunFrom101Through115()
    {
        ShardSpellCatalog.Get(ShardSpellBook.Necro).Spells.Select(spell => spell.DefaultButton)
            .Should().Equal(Enumerable.Range(101, 15));
    }

    [Fact]
    public void BookGump_RecognizesTheBookSerialOrIdentifiableBookText()
    {
        const uint bookSerial = 0x40123456;
        const uint otherSender = 0x123456;
        ShardSpellBookInfo book = ShardSpellCatalog.Get(ShardSpellBook.Earth);
        ShardSpell spell = book.Spells.Single(s => s.Name == "Nature's Touch");

        ShardSpellCaster.MatchesBookGump(bookSerial, bookSerial, book, spell, []).Should().BeTrue();
        ShardSpellCaster.MatchesBookGump(bookSerial, otherSender, book, spell,
            [$"<b>{book.Name}</b>", "<center>Nature&#8217;s Touch</center>"]).Should().BeTrue();
        ShardSpellCaster.MatchesBookGump(bookSerial, otherSender, book, spell,
            ["Nature's Touch", "Earth Portal"]).Should().BeTrue();
        ShardSpellCaster.MatchesBookGump(bookSerial, otherSender, book, spell,
            ["Vendor", "Nature's Touch"]).Should().BeFalse();
        ShardSpellCaster.MatchesBookGump(bookSerial, otherSender, book, spell, []).Should().BeFalse();
    }

    [Theory]
    [InlineData("ShardSpellIcons.png")]
    [InlineData("ShardSpellIllustrations.png")]
    public void EmbeddedAtlas_HasArtForEverySpell(string fileName)
    {
        const int tileSize = 64;
        var assembly = typeof(ExternalImageLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.gumpartassets.{fileName}");
        Assert.NotNull(stream);
        using var image = Image.Load<Rgba32>(stream);

        foreach (ShardSpellBookInfo book in ShardSpellCatalog.Books)
        {
            for (int i = 0; i < book.Spells.Count; i++)
            {
                int x = i * tileSize + tileSize / 2;
                int y = (int)book.Book * tileSize + tileSize / 2;
                Assert.True(x < image.Width && y < image.Height && image[x, y].A != 0, book.Spells[i].Name);
            }
        }
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
