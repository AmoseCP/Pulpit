using Pulpit.Core.Parsing;
using Xunit;

namespace Pulpit.Core.Tests;

public sealed class TextNormalizerTests
{
    // ---------------- NormalizeAlias：SCHEMA.md 规定的完整规则 ----------------

    [Theory]
    [InlineData("约", "约")]
    [InlineData("ＪＯＨＮ", "john")]        // 全角字母 → NFKC → 半角 → 小写
    [InlineData("John", "john")]
    [InlineData("1 Sa", "1sa")]            // 去空格
    [InlineData("1.sa", "1sa")]            // 去 .
    [InlineData("1-sa", "1sa")]            // 去 -
    [InlineData("1_sa", "1sa")]            // 去 _
    [InlineData("撒·上", "撒上")]            // 去 ·
    [InlineData("约　翰", "约翰")]            // U+3000 全角空格（敬空用的那个）
    [InlineData("", "")]
    public void NormalizeAliasFollowsSchemaRules(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeAlias(input));

    [Fact]
    public void NormalizeAliasHandlesNull()
        => Assert.Equal(string.Empty, TextNormalizer.NormalizeAlias(null));

    // ---------------- NormalizeInput：整串，刻意保留连字符 ----------------

    /// <summary>
    /// 这一条是本文件存在的主要理由：整串归一化若照抄别名规则去掉 <c>-</c>，
    /// <c>诗23:1-3</c> 会变成 <c>诗23:13</c>——静默地查错节，没有任何报错。
    /// </summary>
    [Fact]
    public void NormalizeInputKeepsHyphenSoRangesSurvive()
    {
        Assert.Equal("诗23:1-3", TextNormalizer.NormalizeInput("诗23:1-3"));
        Assert.Equal("诗23:1-3", TextNormalizer.NormalizeInput("诗 23 : 1 - 3"));
    }

    [Theory]
    [InlineData("约３：１６", "约3:16")]      // 全角数字与全角冒号
    [InlineData("罗 8 : 28", "罗8:28")]     // 去空格
    [InlineData("　约3:16　", "约3:16")]     // 全角空格
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeInputFoldsFullWidthAndStripsWhitespace(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeInput(input));

    [Fact]
    public void NormalizeInputHandlesNull()
        => Assert.Equal(string.Empty, TextNormalizer.NormalizeInput(null));
}
