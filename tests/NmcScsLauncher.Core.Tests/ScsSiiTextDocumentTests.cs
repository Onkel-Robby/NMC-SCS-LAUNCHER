using NmcScsLauncher.Core;

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
