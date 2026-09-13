using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class CommandLineArgumentParserTests
{
    [Fact]
    public void ParsesQuotedArgumentsWithoutShellConcatenation()
    {
        var arguments = CommandLineArgumentParser.Parse("-nointro -some \"value with spaces\"");

        Assert.Equal(new[] { "-nointro", "-some", "value with spaces" }, arguments);
    }

    [Fact]
    public void RejectsUnclosedQuote()
    {
        Assert.Throws<ArgumentException>(() => CommandLineArgumentParser.Parse("-foo \"bar"));
    }
}
