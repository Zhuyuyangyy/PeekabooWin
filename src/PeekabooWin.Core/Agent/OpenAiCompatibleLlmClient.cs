using System.Net.Http;
using System.Text;
using System.Text.Json;
using PeekabooWin.Core.Infrastructure;

namespace PeekabooWin.Core.Agent;

public class OpenAiCompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly string _apiKey;
    private readonly string _model;

    public string ProviderName { get; }
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    public OpenAiCompatibleLlmClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _apiUrl = Environment.GetEnvironmentVariable("LLM_API_URL")
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_URL")
            ?? "https://api.deepseek.com/v1/chat/completions";
        _apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY")
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? Environment.GetEnvironmentVariable("MINIMAX_API_KEY")
            ?? "";
        _model = Environment.GetEnvironmentVariable("LLM_MODEL")
            ?? "deepseek-chat";

        ProviderName = _apiUrl.Contains("deepseek") ? "deepseek"
            : _apiUrl.Contains("minimax") ? "minimax"
            : "custom";
    }

    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("LLM API key not set. Set LLM_API_KEY or DEEPSEEK_API_KEY environment variable.");

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = 1024
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = content;

        PekaLogger.Debug("LlmClient", $"Calling {_model} at {_apiUrl}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);

        return responseObj.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";
    }
}
