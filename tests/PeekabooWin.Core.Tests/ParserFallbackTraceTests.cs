using Xunit;
using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class ParserFallbackTraceTests
{
    private readonly TaskParser _parser = new();

    [Fact]
    public void ParseTaskMetadata_HasFallbackReasonProperty()
    {
        var meta = new ParseTaskMetadata();

        Assert.NotNull(meta.FallbackReason);
        Assert.Equal("", meta.FallbackReason);
    }

    [Fact]
    public void ParseTaskMetadata_HasLlmEnabledProperty()
    {
        var meta = new ParseTaskMetadata();

        Assert.False(meta.LlmEnabled);
    }

    [Fact]
    public void ParseTaskMetadata_HasParserModeProperty()
    {
        var meta = new ParseTaskMetadata();

        Assert.NotNull(meta.ParserMode);
        Assert.Equal("none", meta.ParserMode);
    }

    [Fact]
    public void ParseTaskMetadata_HasLlmErrorCodeProperty()
    {
        var meta = new ParseTaskMetadata();

        Assert.NotNull(meta.LlmErrorCode);
        Assert.Equal("", meta.LlmErrorCode);
    }

    [Fact]
    public async Task LlmErrorCode_IsMissingApiKey_WhenNoApiKey()
    {
        var originalKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
        Environment.SetEnvironmentVariable("MINIMAX_API_KEY", null);

        try
        {
            await _parser.ParseTaskAsync("do something completely unknown xyz");

            var meta = _parser.GetLastParseMetadata();
            Assert.Equal("MISSING_API_KEY", meta.LlmErrorCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", originalKey);
        }
    }

    [Fact]
    public async Task ParserMode_IsRuleBased_ForSimpleCommands()
    {
        await _parser.ParseTaskAsync("click 100 200");

        var meta = _parser.GetLastParseMetadata();
        Assert.Equal("rule_based", meta.ParserMode);
    }

    [Fact]
    public void AgentTaskResponse_HasParserModeField()
    {
        var response = new AgentTaskResponse();

        Assert.Equal("none", response.ParserMode);
    }

    [Fact]
    public void AgentTaskResponse_HasFallbackReasonField()
    {
        var response = new AgentTaskResponse();

        Assert.NotNull(response.FallbackReason);
        Assert.Equal("", response.FallbackReason);
    }

    [Fact]
    public void AgentTaskResponse_HasLlmErrorCodeField()
    {
        var response = new AgentTaskResponse();

        Assert.NotNull(response.LlmErrorCode);
        Assert.Equal("", response.LlmErrorCode);
    }
}
