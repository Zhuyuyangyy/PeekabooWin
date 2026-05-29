using System.Text.Json;
using Xunit;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Trace;

namespace PeekabooWin.Core.Tests;

public class AgentRuntimeApiModelTests
{
    [Fact]
    public void AgentTaskResponse_Trace_PropertyIsExecutionTraceNullable()
    {
        var response = new AgentTaskResponse();

        Assert.Null(response.Trace);
    }

    [Fact]
    public void AgentTaskResponse_Trace_DefaultIsNull()
    {
        var response = new AgentTaskResponse();

        Assert.Null(response.Trace);
        Assert.False(response.Success);
        Assert.Equal("", response.Task);
    }

    [Fact]
    public void AgentTaskResponse_Trace_CanBeSetWithExecutionTrace()
    {
        var trace = new ExecutionTrace
        {
            TraceId = "rt-001",
            Task = "click OK",
            Success = true,
            Decision = "ALLOW",
            RiskLevel = "L0",
            TotalSteps = 2,
            SuccessfulSteps = 2
        };

        var response = new AgentTaskResponse
        {
            Task = "click OK",
            Success = true,
            Trace = trace
        };

        Assert.NotNull(response.Trace);
        Assert.Equal("rt-001", response.Trace!.TraceId);
        Assert.Equal("click OK", response.Trace.Task);
        Assert.True(response.Trace.Success);
        Assert.Equal("ALLOW", response.Trace.Decision);
        Assert.Equal("L0", response.Trace.RiskLevel);
        Assert.Equal(2, response.Trace.TotalSteps);
    }

    [Fact]
    public void AgentTaskResponse_WithTrace_SerializesToJsonWithTraceField()
    {
        var response = new AgentTaskResponse
        {
            Task = "type hello",
            Success = true,
            Trace = new ExecutionTrace
            {
                TraceId = "rt-002",
                Decision = "ALLOW",
                RiskLevel = "L0",
                GroundingScore = 0.85
            }
        };

        var json = JsonSerializer.Serialize(response);

        Assert.Contains("\"trace\"", json);
        Assert.Contains("\"TraceId\":\"rt-002\"", json);
        Assert.Contains("\"Decision\":\"ALLOW\"", json);
        Assert.Contains("\"RiskLevel\":\"L0\"", json);
        Assert.Contains("\"GroundingScore\"", json);
    }

    [Fact]
    public void AgentTaskRequest_TimeoutMs_DefaultIs30000()
    {
        var request = new AgentTaskRequest();

        Assert.Equal(30000, request.TimeoutMs);
    }

    [Fact]
    public void AgentTaskResponse_WithoutTrace_SerializesWithoutTraceData()
    {
        var response = new AgentTaskResponse
        {
            Task = "screenshot",
            Success = true
        };

        var json = JsonSerializer.Serialize(response);

        Assert.Contains("\"trace\":null", json);
    }

    [Fact]
    public void AgentTaskResponse_TraceWithStepTraces_RoundTripsViaJson()
    {
        var response = new AgentTaskResponse
        {
            Task = "multi-step task",
            Success = false,
            Trace = new ExecutionTrace
            {
                TraceId = "rt-003",
                TotalSteps = 3,
                SuccessfulSteps = 2,
                FailedSteps = 1,
                StepTraces = new List<StepTrace>
                {
                    new() { StepIndex = 0, Action = "focus-window", Success = true },
                    new() { StepIndex = 1, Action = "click", Success = true },
                    new() { StepIndex = 2, Action = "type", Success = false, Error = "Element not found" }
                }
            }
        };

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<AgentTaskResponse>(json);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.Trace);
        Assert.Equal("rt-003", deserialized.Trace!.TraceId);
        Assert.Equal(3, deserialized.Trace.StepTraces.Count);
        Assert.Equal("focus-window", deserialized.Trace.StepTraces[0].Action);
        Assert.Equal("type", deserialized.Trace.StepTraces[2].Action);
        Assert.Equal("Element not found", deserialized.Trace.StepTraces[2].Error);
    }
}
