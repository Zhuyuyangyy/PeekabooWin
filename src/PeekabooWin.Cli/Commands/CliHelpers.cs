using System.Text.Encodings.Web;
using System.Text.Json;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Cli.Commands;

public static class CliHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string? GetFlag(string[] args, string name, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    var parts = new List<string>();
                    int j = i + 1;
                    while (j < args.Length && !args[j].StartsWith("--"))
                    {
                        parts.Add(args[j]);
                        j++;
                    }
                    return string.Join(" ", parts);
                }
            }
        }
        return null;
    }

    public static bool HasFlag(string[] args, string name, string shortName)
    {
        return args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                             a.Equals(shortName, StringComparison.OrdinalIgnoreCase));
    }

    public static void PrintJson(object obj)
    {
        Console.WriteLine(JsonSerializer.Serialize(obj, JsonOptions));
    }

    public static void PrintError(string command, string message, string? errorCode = null, string? hint = null)
    {
        var r = CommandResult.Fail(command, message, errorCode, hint);
        PekaLogger.Error(command, message);
        Console.Error.WriteLine(JsonSerializer.Serialize(r, JsonOptions));
    }
}
