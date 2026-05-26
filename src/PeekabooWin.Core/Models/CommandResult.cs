using System.Text.Json.Serialization;
using PeekabooWin.Core.Infrastructure;

namespace PeekabooWin.Core.Models;

public class CommandResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }

    [JsonPropertyName("trace_id")]
    public string TraceId { get; set; } = TraceIdProvider.Current;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static CommandResult Ok(string command, object? data = null) =>
        new() { Success = true, Command = command, Data = data };

    public static CommandResult Fail(string command, string error, string? errorCode = null, string? hint = null) =>
        new() { Success = false, Command = command, Error = error, ErrorCode = errorCode, Hint = hint };

    public static CommandResult FailFromException(PeekabooWin.Core.Exceptions.PeekabooException ex) =>
        new() { Success = false, Command = "", Error = ex.Message, ErrorCode = ex.ErrorCode, Hint = ex.Hint };
}
