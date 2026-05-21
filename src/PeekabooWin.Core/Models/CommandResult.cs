using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

/// <summary>
/// 所有 CLI 命令的统一返回格式
/// </summary>
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

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static CommandResult Ok(string command, object? data = null) =>
        new() { Success = true, Command = command, Data = data };

    public static CommandResult Fail(string command, string error) =>
        new() { Success = false, Command = command, Error = error };
}
