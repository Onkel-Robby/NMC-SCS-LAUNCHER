using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class ScsSiiTextDocumentTests
{
    [Fact]
    public void SetScalar_ReplacesOnlyExactScalarAndPreservesLineEnding()
    {
        const string source =
            "SiiNunit\r\n{\r\n profile_name: \"Old Name\"\r\n experience_points: 12\r\n}\r\n";

        var document = ScsSiiTextDocument.Parse(source);
        var updated = document.SetScalar("profile_name", "\"NMC Test\"");

        Assert.Contains(" profile_name: \"NMC Test\"\r\n", updated.ToText());
        Assert.Contains(" experience_points: 12\r\n", updated.ToText());
        Assert.DoesNotContain("\"Old Name\"", updated.ToText());
    }


    [Fact]
    public void SetScalar_AcceptsScalarWithoutLeadingIndent()
    {
        const string source =
            "SiiNunit\n{\nmoney_account: 10\n experience_points: 20\n}\n";

        var updated = ScsSiiTextDocument.Parse(source)
            .SetScalar("money_account", "99")
            .ToText();

        Assert.Contains("money_account: 99\n", updated);
        Assert.Contains(" experience_points: 20\n", updated);
    }

    [Fact]
    public void SetScalar_RejectsAmbiguousScalar()
    {
        const string source =
            "SiiNunit\n{\n money_account: 10\n\tmoney_account: 20\n}\n";

        var document = ScsSiiTextDocument.Parse(source);

        var exception = Assert.Throws<ScsSaveEditException>(() =>
            document.SetScalar("money_account", "99"));

        Assert.Contains("mehrfach", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetScalar_RejectsLineInjection()
    {
        var document = ScsSiiTextDocument.Parse(
            "SiiNunit\n{\n profile_name: \"Old\"\n}\n");

        Assert.Throws<ArgumentException>(() =>
            document.SetScalar("profile_name", "\"New\"\n money_account: 999999"));
    }

    [Fact]
    public void Parse_RejectsNonSiiText()
    {
        Assert.Throws<ScsSaveEditException>(() =>
            ScsSiiTextDocument.Parse("not a save"));
    }
}
