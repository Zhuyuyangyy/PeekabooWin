using System.Text.Json;
using Xunit;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class ApiResponseModelTests
{
    [Fact]
    public void AgentTaskResponse_HasAllV12Fields()
    {
        var response = new AgentTaskResponse();

        Assert.Equal(0, response.TimeoutMs);
        Assert.False(response.Cancelled);
        Assert.False(response.TimeoutTriggered);
        Assert.Equal("none", response.ParserMode);
        Assert.True(response.LlmEnabled);
        Assert.Equal("", response.FallbackReason);
        Assert.Equal("", response.LlmErrorCode);
    }

    [Fact]
    public void AgentTaskResponse_JsonSerialization_IncludesAllV12Fields()
    {
        var response = new AgentTaskResponse
        {
            Task = "test",
            TimeoutMs = 5000,
            Cancelled = true,
            TimeoutTriggered = true,
            ParserMode = "rule_based",
            LlmEnabled = false,
            FallbackReason = "MISSING_API_KEY",
            LlmErrorCode = "MISSING_API_KEY"
        };

        var json = JsonSerializer.Serialize(response);

        Assert.Contains("\"timeout_ms\":5000", json);
        Assert.Contains("\"cancelled\":true", json);
        Assert.Contains("\"timeout_triggered\":true", json);
        Assert.Contains("\"parser_mode\":\"rule_based\"", json);
        Assert.Contains("\"llm_enabled\":false", json);
        Assert.Contains("\"fallback_reason\":\"MISSING_API_KEY\"", json);
        Assert.Contains("\"llm_error_code\":\"MISSING_API_KEY\"", json);
    }

    [Fact]
    public void OcrResult_HasErrorCodeField()
    {
        var result = new OcrResult { ErrorCode = "OCR_ENGINE_FAIL" };

        Assert.Equal("OCR_ENGINE_FAIL", result.ErrorCode);
    }

    [Fact]
    public void OcrResult_Success_IsTrue_WhenErrorIsNull()
    {
        var result = new OcrResult();

        Assert.Null(result.Error);
        Assert.True(result.Success);
    }

    [Fact]
    public void OcrResult_Success_IsFalse_WhenErrorIsSet()
    {
        var result = new OcrResult { Error = "OCR engine failed" };

        Assert.False(result.Success);
    }

    [Fact]
    public void AgentTaskRequest_TimeoutMs_DefaultIs30000()
    {
        var request = new AgentTaskRequest();

        Assert.Equal(30000, request.TimeoutMs);
    }

    [Fact]
    public void AgentTaskRequest_JsonSerialization_IncludesTimeoutMs()
    {
        var request = new AgentTaskRequest { Task = "test", TimeoutMs = 10000 };

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"timeout_ms\":10000", json);
    }
}
