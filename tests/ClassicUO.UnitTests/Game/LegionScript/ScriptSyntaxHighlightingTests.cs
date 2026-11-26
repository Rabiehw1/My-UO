using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.LegionScripting;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript;

public sealed class ScriptSyntaxHighlightingTests
{
    [Fact]
    public void Python_ClassifiesMultilineAndByteStringsWithoutReadingCodeFromComments()
    {
        const string source = "# 🐍 API.Print(99)\r\nif value == 0x11:\r\n    API.Print(\"test\")\r\n"
            + "message = \"\"\"first\nsecond\"\"\"\ndata = rb'bytes'\n";

        var spans = ScriptSyntaxHighlighting.Classify(source, ScriptFile.ScriptType.Python);

        Assert.Equal(new[]
        {
            (ScriptSyntaxKind.Comment, "# 🐍 API.Print(99)"), (ScriptSyntaxKind.Keyword, "if"),
            (ScriptSyntaxKind.Number, "0x11"), (ScriptSyntaxKind.Api, "API"), (ScriptSyntaxKind.Api, "Print"),
            (ScriptSyntaxKind.String, "\"test\""), (ScriptSyntaxKind.String, "\"\"\"first\nsecond\"\"\""),
            (ScriptSyntaxKind.String, "rb'bytes'")
        }, ExtractText(source, spans));
    }

    [Fact]
    public void CSharp_ClassifiesMultilineFormsAndTokensInsideInterpolation()
    {
        const string source = "/* 🐍 API.Print\r\nblock */\r\nvar value = @\"two\nlines\";\r\nAPI.Print($\"v={0x11}\");";

        var spans = ScriptSyntaxHighlighting.Classify(source, ScriptFile.ScriptType.CSharp);

        Assert.Equal(new[]
        {
            (ScriptSyntaxKind.Comment, "/* 🐍 API.Print\r\nblock */"), (ScriptSyntaxKind.Keyword, "var"),
            (ScriptSyntaxKind.String, "@\"two\nlines\""), (ScriptSyntaxKind.Api, "API"), (ScriptSyntaxKind.Api, "Print"),
            (ScriptSyntaxKind.String, "$\""), (ScriptSyntaxKind.String, "v="), (ScriptSyntaxKind.Number, "0x11"),
            (ScriptSyntaxKind.String, "\"")
        }, ExtractText(source, spans));
    }

    [Theory]
    [InlineData(ScriptFile.ScriptType.Python)]
    [InlineData(ScriptFile.ScriptType.CSharp)]
    public void UnfinishedSource_PreservesEarlierTokensAndDoesNotRequireACompleteProgram(ScriptFile.ScriptType type)
    {
        const string source = "API.Print(\"unfinished";
        var spans = ScriptSyntaxHighlighting.Classify(source, type);

        Assert.Equal(new[]
        {
            (ScriptSyntaxKind.Api, "API"), (ScriptSyntaxKind.Api, "Print"), (ScriptSyntaxKind.String, "\"unfinished")
        }, ExtractText(source, spans));
        Assert.Empty(ScriptSyntaxHighlighting.Classify(string.Empty, type));
    }

    private static IEnumerable<(ScriptSyntaxKind Kind, string Text)> ExtractText(string source, IReadOnlyList<ScriptSyntaxSpan> spans)
    {
        int lastEnd = 0;
        foreach (ScriptSyntaxSpan span in spans)
        {
            Assert.InRange(span.Start, lastEnd, source.Length - 1);
            Assert.InRange(span.Length, 1, source.Length - span.Start);
            lastEnd = span.Start + span.Length;
        }

        return spans.Select(span => (span.Kind, source.Substring(span.Start, span.Length)));
    }
}
