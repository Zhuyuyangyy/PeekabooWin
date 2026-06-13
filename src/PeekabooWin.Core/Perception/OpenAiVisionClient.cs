using System.Net.Http;
using System.Text;
using System.Text.Json;
using PeekabooWin.Core.Infrastructure;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// OpenAI 兼容的多模态视觉客户端 — 通过 chat/completions API 实现图片+文字分析
/// </summary>
public class OpenAiVisionClient : ILlmVisionClient
{
    private const string LogTag = "OpenAiVisionClient";
    private const int MaxRetries = 2;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly string _apiKey;
    private readonly string _model;

    /// <inheritdoc />
    public string ProviderName { get; }

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    /// <summary>
    /// 创建 OpenAI 视觉客户端实例
    /// </summary>
    /// <param name="httpClient">HTTP 客户端（用于连接复用）</param>
    public OpenAiVisionClient(HttpClient httpClient)
    {
        _httpClient = httpClient;

        // 环境变量读取：VISION_* 优先，回退到 LLM_*
        _apiUrl = Environment.GetEnvironmentVariable("VISION_API_URL")
            ?? Environment.GetEnvironmentVariable("LLM_API_URL")
            ?? "https://api.openai.com/v1/chat/completions";

        _apiKey = Environment.GetEnvironmentVariable("VISION_API_KEY")
            ?? Environment.GetEnvironmentVariable("LLM_API_KEY")
            ?? "";

        _model = Environment.GetEnvironmentVariable("VISION_MODEL")
            ?? "gpt-4o";

        ProviderName = _apiUrl.Contains("openai", StringComparison.OrdinalIgnoreCase) ? "openai"
            : _apiUrl.Contains("azure", StringComparison.OrdinalIgnoreCase) ? "azure_openai"
            : "custom_vision";

        PekaLogger.Info(LogTag, $"Initialized: provider={ProviderName}, model={_model}, available={IsAvailable}");
    }

    /// <inheritdoc />
    public async Task<string> ChatWithImageAsync(
        string systemPrompt,
        string userPrompt,
        byte[] imageBytes,
        string mediaType = "image/png",
        CancellationToken ct = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                "Vision API key not configured. Set VISION_API_KEY or LLM_API_KEY environment variable.");

        if (imageBytes is not { Length: > 0 })
            throw new ArgumentException("Image bytes cannot be null or empty.", nameof(imageBytes));

        var base64Image = Convert.ToBase64String(imageBytes);
        var dataUrl = $"data:{mediaType};base64,{base64Image}";

        // 构造 vision 格式请求体
        var requestBody = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = userPrompt },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            temperature = 0.1,
            max_tokens = 4096
        };

        var json = JsonSerializer.Serialize(requestBody);

        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                    PekaLogger.Info(LogTag, $"Retry attempt {attempt}/{MaxRetries}");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(RequestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                PekaLogger.Debug(LogTag,
                    $"Calling {_model} at {_apiUrl} (image: {imageBytes.Length} bytes, attempt {attempt + 1})");

                using var response = await _httpClient.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);

                var content = responseObj
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrEmpty(content))
                    throw new InvalidOperationException("LLM returned empty content in response.");

                PekaLogger.Debug(LogTag, $"Response received: {content.Length} chars");
                return content;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 外部取消，直接抛出，不重试
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // 超时
                lastException = ex;
                PekaLogger.Warn(LogTag, $"Request timed out ({RequestTimeout.TotalSeconds}s) on attempt {attempt + 1}", ex);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                PekaLogger.Warn(LogTag, $"HTTP error on attempt {attempt + 1}: {ex.StatusCode}", ex);

                // 4xx 客户端错误不重试
                if (ex.StatusCode is >= System.Net.HttpStatusCode.BadRequest
                    and < System.Net.HttpStatusCode.InternalServerError)
                    throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                PekaLogger.Error(LogTag, $"Unexpected error on attempt {attempt + 1}", ex);
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Vision API call failed after {MaxRetries + 1} attempts. Last error: {lastException?.Message}",
            lastException);
    }
}
