using System.IO;
using System.Text;
using System.Xml;
using ClassicUO.Common.Enums;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.ShardSpells;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers.ShardSpells;

public class ShardSpellMacroTests
{
    [Fact]
    public void EverySpellRoundTripsThroughTheMacroSelection()
    {
        foreach (ShardSpellBookInfo book in ShardSpellCatalog.Books)
        {
            for (int index = 0; index < book.Spells.Count; index++)
            {
                string selection = ShardSpellCatalog.FormatMacroSelection(book.Book, index);
                Assert.True(ShardSpellCatalog.TryParseMacroSelection(selection, out var loadedBook, out int loadedIndex));
                Assert.Equal(book.Book, loadedBook);
                Assert.Equal(index, loadedIndex);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown:0")]
    [InlineData("Necro:15")]
    [InlineData("Songs:-2")]
    public void InvalidSelectionsAreRejected(string value) =>
        Assert.False(ShardSpellCatalog.TryParseMacroSelection(value, out _, out _));

    [Fact]
    public void MacroXmlRetainsLegacyAndShardCastsIncludingInsideLoops()
    {
        var original = new Macro("Cast spells");
        original.PushToBack(new MacroObject(MacroType.CastSpell, MacroSubType.Heal));
        original.PushToBack(Macro.CreateShardSpellAction("Necro:0"));
        var loop = new MacroLoopContainer();
        loop.Items.AddLast(Macro.CreateShardSpellAction("Songs:6"));
        original.PushToBack(loop);

        var builder = new StringBuilder();
        using (var writer = new XmlTextWriter(new StringWriter(builder)))
            original.Save(writer);
        var xml = new XmlDocument();
        xml.LoadXml(builder.ToString());
        var loaded = new Macro(original.Name);
        loaded.Load(xml.DocumentElement);

        var legacy = Assert.IsType<MacroObject>(loaded.Items);
        Assert.Equal(MacroSubType.Heal, legacy.SubCode);
        var shardCast = Assert.IsType<MacroObjectString>(legacy.Next);
        Assert.Equal(MacroType.CastSpell, shardCast.Code);
        Assert.Equal("Necro:0", shardCast.Text);
        var loadedLoop = Assert.IsType<MacroLoopContainer>(shardCast.Next);
        Assert.Equal("Songs:6", Assert.IsType<MacroObjectString>(Assert.Single(loadedLoop.Items)).Text);
    }
}
