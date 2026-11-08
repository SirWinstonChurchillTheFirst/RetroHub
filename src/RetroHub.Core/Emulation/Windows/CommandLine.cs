using System.Text;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>
/// Builds Windows command lines from argv lists so that CommandLineToArgvW / the MSVCRT parser give back exactly
/// the same list. Same rules as .NET's internal PasteArguments (used by ProcessStartInfo.ArgumentList):
/// arguments without whitespace or quotes are passed as-is; others are quoted, a quote becomes \",
/// backslashes are doubled only when they precede a quote (or the closing quote), an empty argument becomes "".
/// </summary>
public static class CommandLine
{
    /// <summary>Longest command line CreateProcessW accepts (including the terminating null).</summary>
    public const int MaxLength = 32767;

    /// <summary>The full command line: the quoted program path (argv[0]) followed by the quoted arguments.</summary>
    public static string Build(string fileName, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var sb = new StringBuilder();
        // argv[0] follows different rules (no backslash escapes, ends at the next quote): always quote it.
        // Quotes cannot appear in Windows paths, so any surrounding quotes are simply dropped.
        sb.Append('"').Append(fileName.Trim().Trim('"')).Append('"');
        foreach (var a in arguments) AppendArgument(sb, a ?? "");
        return sb.ToString();
    }

    /// <summary>Only the arguments (for ProcessStartInfo.Arguments / ShellExecute parameters).</summary>
    public static string JoinArguments(IEnumerable<string> arguments)
    {
        var sb = new StringBuilder();
        foreach (var a in arguments) AppendArgument(sb, a ?? "");
        return sb.ToString();
    }

    /// <summary>Appends one argument (preceded by a space if the builder is not empty).</summary>
    public static void AppendArgument(StringBuilder sb, string argument)
    {
        const char quote = '"', backslash = '\\';
        if (sb.Length != 0) sb.Append(' ');

        if (argument.Length != 0 && !NeedsQuotes(argument))
        {
            sb.Append(argument);
            return;
        }

        sb.Append(quote);
        int i = 0;
        while (i < argument.Length)
        {
            char c = argument[i++];
            if (c == backslash)
            {
                int count = 1;
                while (i < argument.Length && argument[i] == backslash)
                {
                    i++;
                    count++;
                }
                if (i == argument.Length)
                {
                    // trailing backslashes: doubled, so the closing quote is not escaped
                    sb.Append(backslash, count * 2);
                }
                else if (argument[i] == quote)
                {
                    // backslashes before a quote: doubled, plus one to escape the quote itself
                    sb.Append(backslash, count * 2 + 1).Append(quote);
                    i++;
                }
                else
                {
                    // backslashes elsewhere are literal
                    sb.Append(backslash, count);
                }
                continue;
            }
            if (c == quote)
            {
                sb.Append(backslash).Append(quote);
                continue;
            }
            sb.Append(c);
        }
        sb.Append(quote);
    }

    private static bool NeedsQuotes(string s)
    {
        foreach (char c in s)
            if (char.IsWhiteSpace(c) || c == '"') return true;
        return false;
    }
}
