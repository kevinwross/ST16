using ST16.CC;
using Xunit;

namespace ST16.CC.Tests;

/// <summary>Lexer tests: tokens, char/string values, operators, and diagnostics.</summary>
public class LexerTests
{
    private static List<CToken> Lex(string source) => new CLexer(source).Lex();

    [Fact]
    public void CharLiterals_ProduceAsciiValues()
    {
        var t = Lex("'a' 'A' '0' '\\n' '\\x41'");
        Assert.Equal(6, t.Count); // 5 literals + END
        Assert.Equal(97, t[0].Value);   // 'a'
        Assert.Equal(65, t[1].Value);   // 'A'
        Assert.Equal(48, t[2].Value);   // '0'
        Assert.Equal(10, t[3].Value);   // '\n'
        Assert.Equal(65, t[4].Value);   // '\x41'
    }

    [Fact]
    public void MultiCharLiteral_IsAnError()
    {
        var ex = Assert.Throws<CCException>(() => Lex("'ab'"));
        Assert.Contains("multi-character literal", ex.Diagnostic.Message);
    }

    [Fact]
    public void CharLiteralAbove0xFF_IsAnError()
    {
        var ex = Assert.Throws<CCException>(() => Lex("'\\x100'"));
        Assert.Contains("exceeds one byte", ex.Diagnostic.Message);
    }

    [Fact]
    public void StringLiterals_CollectBytes()
    {
        var t = Lex("\"hi\\n\"");
        Assert.Equal(3, t[0].Bytes!.Length);
        Assert.Equal(104, t[0].Bytes[0]); // h
        Assert.Equal(105, t[0].Bytes[1]); // i
        Assert.Equal(10, t[0].Bytes[2]);  // \n
    }

    [Fact]
    public void ThreeCharOperators_AreSingleTokens()
    {
        var t = Lex("<<= >>= ...");
        Assert.Equal(4, t.Count); // 3 ops + END
        Assert.Equal("<<=", t[0].Text);
        Assert.Equal(">>=", t[1].Text);
        Assert.Equal("...", t[2].Text);
    }

    [Fact]
    public void IntLiteral_Above0xFFFF_IsAnError()
    {
        var ex = Assert.Throws<CCException>(() => Lex("70000"));
        Assert.Contains("0xFFFF", ex.Diagnostic.Message);
    }

    [Fact]
    public void IntLiteral_At0xFFFF_IsAccepted()
    {
        var t = Lex("0xFFFF");
        Assert.Equal(0xFFFF, t[0].Value);
    }

    [Fact]
    public void PreprocessorLine_IsAnError()
    {
        var ex = Assert.Throws<CCException>(() => Lex("#include <stdio.h>"));
        Assert.Contains("preprocessor", ex.Diagnostic.Message);
    }

    [Fact]
    public void UnterminatedString_IsAnError()
    {
        var ex = Assert.Throws<CCException>(() => Lex("\"abc"));
        Assert.Contains("unterminated string", ex.Diagnostic.Message);
    }

    [Fact]
    public void LinesAndColumns_AreTracked()
    {
        var t = Lex("int\n  x;");
        Assert.Equal(1, t[0].Line);
        Assert.Equal(1, t[0].Column);
        Assert.Equal(2, t[1].Line);
        Assert.Equal(3, t[1].Column);
    }
}
