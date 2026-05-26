using System.Text.Json;

namespace PeekabooWin.Core.Infrastructure;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public static class PekaLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeekabooWin", "logs");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object LockObj = new();

    public static void Log(LogLevel level, string source, string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var entry = new
            {
                ts = DateTime.UtcNow.ToString("O"),
                level = level.ToString(),
                trace_id = TraceIdProvider.Current,
                source,
                message,
                exception = exception?.Message
            };
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            var logFile = Path.Combine(LogDir, $"peekaboo-win-{DateTime.UtcNow:yyyyMMdd}.log");
            lock (LockObj)
            {
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
        }
        catch { }
    }

    public static void Debug(string source, string message) => Log(LogLevel.Debug, source, message);
    public static void Info(string source, string message) => Log(LogLevel.Information, source, message);
    public static void Warn(string source, string message, Exception? ex = null) => Log(LogLevel.Warning, source, message, ex);
    public static void Error(string source, string message, Exception? ex = null) => Log(LogLevel.Error, source, message, ex);
}
