namespace PeekabooWin.Core.Perception;

/// <summary>
/// 多模态 LLM 视觉客户端接口 — 支持发送图片+文字进行视觉分析
/// </summary>
public interface ILlmVisionClient
{
    /// <summary>
    /// 发送图片+提示词给多模态 LLM，返回文本响应
    /// </summary>
    Task<string> ChatWithImageAsync(
        string systemPrompt,
        string userPrompt,
        byte[] imageBytes,
        string mediaType = "image/png",
        CancellationToken ct = default);

    /// <summary>
    /// 提供商标识
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 是否可用（API Key 已配置）
    /// </summary>
    bool IsAvailable { get; }
}
