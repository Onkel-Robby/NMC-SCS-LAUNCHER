using System.Text;

namespace NmcScsLauncher.Infrastructure;

public static class CommandLineArgumentParser
{
    public static IReadOnlyList<string> Parse(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var tokenStarted = false;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];

            if (character == '\\' && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
            {
                current.Append('"');
                tokenStarted = true;
                index++;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                FlushToken(result, current, ref tokenStarted);
                continue;
            }

            current.Append(character);
            tokenStarted = true;
        }

        if (inQuotes)
        {
            throw new ArgumentException("Die zusätzlichen Startparameter enthalten ein nicht geschlossenes Anführungszeichen.", nameof(commandLine));
        }

        FlushToken(result, current, ref tokenStarted);
        return result;
    }

    private static void FlushToken(ICollection<string> result, StringBuilder current, ref bool tokenStarted)
    {
        if (!tokenStarted)
        {
            return;
        }

        result.Add(current.ToString());
        current.Clear();
        tokenStarted = false;
    }
}
