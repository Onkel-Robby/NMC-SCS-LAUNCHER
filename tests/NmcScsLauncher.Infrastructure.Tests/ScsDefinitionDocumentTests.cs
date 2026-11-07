using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsDefinitionDocumentTests
{
    [Fact]
    public void ReadsProfileStringsTokensArraysAndUnrelatedTuples()
    {
        var document = ScsDefinitionDocument.Parse("""
            SiiNunit
            {
            profile : _nameless.12.ab {
             profile_name: "My Profile"
             save_time: 12345678
             enabled: true
             position: (1.5, &3f800000, -3)
             placement: (1, 2, 3) (1; 0, 0, 0)
             active_mods: 2
             active_mods[0]: "mod.first"
             active_mods[1]: "mod.second"
             empty: 0
            }
            package_version_info : .universal {
             package_name: "universal"
             compatible_versions[]: "1.59.*"
             compatible_versions[]: "1.60.*"
            }
            }
            """);

        Assert.Equal(2, document.Units.Count);
        var profile = document.Units[0];
        Assert.Equal("profile", profile.Type);
        Assert.Equal("_nameless.12.ab", profile.Id);
        Assert.Equal("My Profile", profile.GetString("profile_name"));
        Assert.Equal("12345678", profile.GetToken("save_time"));
        Assert.Equal("true", profile.GetToken("enabled"));
        Assert.Equal(new[] { "mod.first", "mod.second" }, profile.GetStrings("active_mods"));
        Assert.Empty(profile.GetStrings("empty"));
        Assert.Equal(ScsDefinitionValueKind.Tuple, profile.GetValue("position")!.Kind);
        Assert.Equal("&3f800000", profile.GetValue("position")!.Elements[1].Text);
        Assert.Equal(ScsDefinitionValueKind.Placement, profile.GetValue("placement")!.Kind);
        Assert.Equal(new[] { "1.59.*", "1.60.*" }, document.Units[1].GetStrings("compatible_versions"));
        Assert.Contains("placement", profile.AttributeNames);
        Assert.True(profile.HasAttribute("empty"));
        Assert.False(profile.HasAttribute("missing"));
        Assert.Null(profile.GetString("missing"));
        Assert.Null(profile.GetToken("missing"));
        Assert.Empty(profile.GetValues("missing"));
    }

    [Fact]
    public void DecodesSiiByteEscapesAsUtf8AndPreservesQuotedPunctuation()
    {
        var unit = Single("""
            name: "Gr\xc3\xbc\xc3\x9fe \xe2\x82\xac"
            literal: "http://host/#name /*not comment*/ @include {} : []"
            escaped: "one\n\"two\"\\three\tend\r"
            """);

        Assert.Equal("Grüße €", unit.GetString("name"));
        Assert.Equal("http://host/#name /*not comment*/ @include {} : []", unit.GetString("literal"));
        Assert.Equal("one\n\"two\"\\three\tend\r", unit.GetString("escaped"));
    }

    [Fact]
    public void ParsesCommentsBetweenTokensWithoutMergingThem()
    {
        var unit = ScsDefinitionDocument.Parse("\uFEFFSiiNunit/*root*/{#line\npkg/*c*/:/*c*/.one{/*c*/name/*c*/:/*c*/\"ok\"//tail\nvalues[]: token\n/*end*/}} ").Units.Single();
        Assert.Equal("ok", unit.GetString("name"));
        Assert.Equal(new[] { "token" }, unit.GetTokens("values"));
    }

    [Fact]
    public void ReadsContiguousIndexedArraysWithOrWithoutCountAndTupleArrays()
    {
        var unit = Single("""
            values[1]: &40000000
            values[0]: 1
            names[0]: "first"
            names: 1
            curve[]: (1000, 1)
            curve[]: (2000, .75)
            """);
        Assert.Equal(new[] { "1", "&40000000" }, unit.GetTokens("values"));
        Assert.Equal(new[] { "first" }, unit.GetStrings("names"));
        Assert.Equal("2000", unit.GetValues("curve")[1].Elements[0].Text);
    }

    [Fact]
    public void PreservesExplicitArrayCountsWithoutCoercingArrayToScalar()
    {
        var unit = Single("declared: 1 declared[0]: \"one\" indexed[0]: \"one\" append[]: \"one\" empty: 0");
        Assert.Equal(1, unit.GetDeclaredArrayCount("declared"));
        Assert.Equal(0, unit.GetDeclaredArrayCount("empty"));
        Assert.Null(unit.GetDeclaredArrayCount("indexed"));
        Assert.Null(unit.GetDeclaredArrayCount("append"));
        Assert.Null(unit.GetDeclaredArrayCount("absent"));
        Assert.Throws<InvalidDataException>(() => unit.GetToken("declared"));
    }

    [Theory]
    [InlineData("sounds: 2 sounds[]: \"engine|example.bank\" sounds[]: \"exhaust|example.bank\"")]
    [InlineData("sounds[]: \"engine|example.bank\" sounds[]: \"exhaust|example.bank\" sounds: 2")]
    public void ReadsCountedAppendArraysUsedByOriginalSoundIncludes(string attributes)
    {
        var unit = Single(attributes);
        Assert.Equal(2, unit.GetDeclaredArrayCount("sounds"));
        Assert.Equal(new[] { "engine|example.bank", "exhaust|example.bank" }, unit.GetStrings("sounds"));
        Assert.Throws<InvalidDataException>(() => unit.GetToken("sounds"));
    }

    [Fact]
    public void DoesNotCoerceScalarAndArrayValueTypes()
    {
        var unit = Single("text: \"123\" number: 123 list[]: \"a\" vector: (1, 2) words[]: one");
        Assert.Throws<InvalidDataException>(() => unit.GetToken("text"));
        Assert.Throws<InvalidDataException>(() => unit.GetString("number"));
        Assert.Throws<InvalidDataException>(() => unit.GetString("list"));
        Assert.Throws<InvalidDataException>(() => unit.GetToken("vector"));
        Assert.Throws<InvalidDataException>(() => unit.GetTokens("list"));
        Assert.Throws<InvalidDataException>(() => unit.GetStrings("words"));
        Assert.Throws<InvalidDataException>(() => unit.GetStrings("text"));
        Assert.Throws<InvalidDataException>(() => unit.GetTokens("number"));
    }

    [Theory]
    [InlineData("a: 1 a: 2")]
    [InlineData("a[]: one a[0]: two")]
    [InlineData("a: 2 a[]: one")]
    [InlineData("a: 0 a[]: one")]
    [InlineData("a: -1 a[]: one")]
    [InlineData("a: \"1\" a[]: one")]
    [InlineData("a: 2 a[]: one a[1]: two")]
    [InlineData("a: 2 a[0]: one")]
    [InlineData("a: 1 a[0]: one a[1]: two")]
    [InlineData("a[0]: one a[0]: two")]
    [InlineData("a[1]: one")]
    [InlineData("a[0]: one a[2]: two")]
    [InlineData("a: -1 a[0]: one")]
    [InlineData("a: \"1\" a[0]: one")]
    [InlineData("a[-1]: one")]
    [InlineData("a[1.0]: one")]
    [InlineData("a[2147483648]: one")]
    [InlineData("a[99999999]: one")]
    public void RejectsAmbiguousOrMalformedArrays(string attributes) => Assert.Throws<InvalidDataException>(() => Single(attributes));

    [Theory]
    [InlineData("\"SiiNunit\" { p : .a {} }")]
    [InlineData("SiiNunit \"{\" p : .a {} }")]
    [InlineData("SiiNunit { p \":\" .a {} }")]
    [InlineData("SiiNunit { p : \".a\" {} }")]
    [InlineData("SiiNunit { p : .a \"{\" } }")]
    [InlineData("SiiNunit { p : .a { key \":\" value } }")]
    [InlineData("SiiNunit { p : .a {} \"}\"")]
    public void QuotedGrammarMarkersAreNeverSyntax(string content)
    {
        Assert.Throws<InvalidDataException>(() => ScsDefinitionDocument.Parse(content));
    }

    [Theory]
    [InlineData("SiiNunit { p : .a { key: value }")]
    [InlineData("SiiNunit { p : .a { key: { child: 1 } } }")]
    [InlineData("SiiNunit { p : .a { key: (1, 2 } }")]
    [InlineData("SiiNunit { p : .a { key: (1,,2) } }")]
    [InlineData("SiiNunit { p : .a { key: () } }")]
    [InlineData("SiiNunit { p : .a { key: \"missing } }")]
    [InlineData("SiiNunit { p : .a { /* missing }")]
    [InlineData("SiiNunit { @include \"part.sui\" }")]
    [InlineData("SiiNunit { p : .a { key: @include \"part.sui\" } }")]
    [InlineData("SiiNunit { p : .a {} } trailing")]
    [InlineData("SiiNunit { p : .a { key: one two } }")]
    [InlineData("SiiNunit { p : .a { key: 1\u0000 } }")]
    public void RejectsUnresolvedDirectivesAndMalformedGrammar(string content) =>
        Assert.Throws<InvalidDataException>(() => ScsDefinitionDocument.Parse(content));

    [Theory]
    [InlineData("\\q")]
    [InlineData("\\x1")]
    [InlineData("\\xGG")]
    [InlineData("\\xff")]
    [InlineData("\\xc3x")]
    public void RejectsUnsupportedOrInvalidByteEscapes(string text) => Assert.Throws<InvalidDataException>(() => Single("name: \"" + text + "\""));

    [Fact]
    public void RejectsDuplicateUnitIds()
    {
        Assert.Throws<InvalidDataException>(() => ScsDefinitionDocument.Parse("SiiNunit { p : .same {} q : .same {} }"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("one..two")]
    [InlineData("one.")]
    public void RejectsEmptyUnitNameParts(string id) =>
        Assert.Throws<InvalidDataException>(() => ScsDefinitionDocument.Parse("SiiNunit { p : " + id + " {} }"));

    [Fact]
    public void RejectsExcessiveInputAndNesting()
    {
        Assert.Throws<InvalidDataException>(() => ScsDefinitionDocument.Parse(new string(' ', 8 * 1024 * 1024 + 1)));
        Assert.Throws<InvalidDataException>(() => Single("value: " + new string('(', 20) + "1" + new string(')', 20)));
    }

    private static ScsDefinitionUnit Single(string attributes) => ScsDefinitionDocument.Parse("SiiNunit { p : .one { " + attributes + " } }").Units.Single();
}
