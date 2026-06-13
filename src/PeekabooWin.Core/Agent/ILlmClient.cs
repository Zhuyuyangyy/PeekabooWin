namespace PeekabooWin.Core.Agent;

public interface ILlmClient
{
    Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
    string ProviderName { get; }
    bool IsAvailable { get; }
}
