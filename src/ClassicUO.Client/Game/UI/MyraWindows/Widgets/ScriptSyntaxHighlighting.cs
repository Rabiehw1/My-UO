#nullable enable
using System;
using System.Collections.Generic;
using ClassicUO.LegionScripting;
using IronPython.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

namespace ClassicUO.Game.UI.MyraWindows.Widgets;

internal enum ScriptSyntaxKind
{
    Comment,
    String,
    Number,
    Keyword,
    Api
}

internal readonly record struct ScriptSyntaxSpan(int Start, int Length, ScriptSyntaxKind Kind);

internal static class ScriptSyntaxHighlighting
{
    private static readonly Lazy<ScriptEngine> PythonEngine = new(() => Python.CreateEngine());
    private static readonly CSharpParseOptions CSharpOptions = CSharpParseOptions.Default.WithKind(Microsoft.CodeAnalysis.SourceCodeKind.Script);

    internal static IReadOnlyList<ScriptSyntaxSpan> Classify(string source, ScriptFile.ScriptType type)
    {
        if (string.IsNullOrEmpty(source))
            return [];

        var spans = new List<ScriptSyntaxSpan>();
        if (type == ScriptFile.ScriptType.CSharp)
            ClassifyCSharp(source, spans);
        else
            ClassifyPython(source, spans);

        spans.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return spans;
    }

    private static void ClassifyCSharp(string source, List<ScriptSyntaxSpan> spans)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source, CSharpOptions).GetRoot();
        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            if (trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
                or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia
                or SyntaxKind.ShebangDirectiveTrivia)
            {
                spans.Add(new ScriptSyntaxSpan(trivia.SpanStart, trivia.Span.Length, ScriptSyntaxKind.Comment));
            }
        }

        foreach (SyntaxToken token in root.DescendantTokens())
        {
            if (token.Span.IsEmpty)
                continue;

            SyntaxKind kind = token.Kind();
            ScriptSyntaxKind? category = kind switch
            {
                SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.Utf8StringLiteralToken
                    or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
                    or SyntaxKind.Utf8SingleLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken
                    or SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedVerbatimStringStartToken
                    or SyntaxKind.InterpolatedSingleLineRawStringStartToken or SyntaxKind.InterpolatedMultiLineRawStringStartToken
                    or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.InterpolatedStringEndToken
                    or SyntaxKind.InterpolatedRawStringEndToken => ScriptSyntaxKind.String,
                SyntaxKind.NumericLiteralToken => ScriptSyntaxKind.Number,
                _ when SyntaxFacts.IsKeywordKind(kind) => ScriptSyntaxKind.Keyword,
                SyntaxKind.IdentifierToken when token.Parent is IdentifierNameSyntax { IsVar: true } name
                    && name.Parent is VariableDeclarationSyntax declaration && declaration.Type == name => ScriptSyntaxKind.Keyword,
                SyntaxKind.IdentifierToken when IsApiIdentifier(source, token.SpanStart, token.Span.Length) => ScriptSyntaxKind.Api,
                _ => null
            };

            if (category is { } value)
                spans.Add(new ScriptSyntaxSpan(token.SpanStart, token.Span.Length, value));
        }
    }

    private static void ClassifyPython(string source, List<ScriptSyntaxSpan> spans)
    {
        ScriptEngine engine = PythonEngine.Value;
        TokenCategorizer tokenizer = engine.GetService<TokenCategorizer>();
        tokenizer.Initialize(null, engine.CreateScriptSourceFromString(source), SourceLocation.MinValue);
        foreach (TokenInfo token in tokenizer.ReadTokens(source.Length))
        {
            int start = token.SourceSpan.Start.Index;
            int length = token.SourceSpan.End.Index - start;
            if (start < 0 || length <= 0 || start > source.Length - length)
                continue;

            ScriptSyntaxKind? category = token.Category switch
            {
                TokenCategory.Comment or TokenCategory.LineComment or TokenCategory.DocComment => ScriptSyntaxKind.Comment,
                TokenCategory.StringLiteral or TokenCategory.CharacterLiteral => ScriptSyntaxKind.String,
                // IronPython categorizes byte literals as numeric constants.
                TokenCategory.NumericLiteral when IsPythonByteLiteral(source.AsSpan(start, length)) => ScriptSyntaxKind.String,
                TokenCategory.NumericLiteral => ScriptSyntaxKind.Number,
                TokenCategory.Keyword => ScriptSyntaxKind.Keyword,
                TokenCategory.Identifier when IsApiIdentifier(source, start, length) => ScriptSyntaxKind.Api,
                _ => null
            };

            if (category is { } value)
                spans.Add(new ScriptSyntaxSpan(start, length, value));
        }
    }

    private static bool IsPythonByteLiteral(ReadOnlySpan<char> token)
    {
        int prefix = token.Length > 0 && token[0] is 'r' or 'R' ? 1 : 0;
        if (prefix >= token.Length || token[prefix] is not ('b' or 'B'))
            return false;

        int quote = prefix + 1;
        if (prefix == 0 && quote < token.Length && token[quote] is 'r' or 'R')
            ++quote;

        return quote < token.Length && token[quote] is '\'' or '"';
    }

    private static bool IsApiIdentifier(string source, int start, int length)
    {
        if (source.AsSpan(start, length).SequenceEqual("API"))
            return true;

        int dot = start - 1;
        while (dot >= 0 && source[dot] is ' ' or '\t')
            --dot;
        if (dot < 0 || source[dot] != '.')
            return false;

        int end = dot - 1;
        while (end >= 0 && source[end] is ' ' or '\t')
            --end;
        int apiStart = end - 2;
        return apiStart >= 0 && source.AsSpan(apiStart, 3).SequenceEqual("API")
            && (apiStart == 0 || !char.IsLetterOrDigit(source[apiStart - 1]) && source[apiStart - 1] != '_');
    }
}
