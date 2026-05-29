using Xunit;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class TimeoutTests
{
    [Fact]
    public void AgentTaskRequest_TimeoutMs_DefaultIs30000()
    {
        var request = new AgentTaskRequest();

        Assert.Equal(30000, request.TimeoutMs);
    }

    [Fact]
    public void AgentTaskResponse_TimeoutTriggered_DefaultIsFalse()
    {
        var response = new AgentTaskResponse();

        Assert.False(response.TimeoutTriggered);
    }

    [Fact]
    public void AgentTaskResponse_Cancelled_DefaultIsFalse()
    {
        var response = new AgentTaskResponse();

        Assert.False(response.Cancelled);
    }

    [Fact]
    public void AgentTaskRequest_TimeoutMs_SetToZero_MeansNoTimeout()
    {
        var request = new AgentTaskRequest { TimeoutMs = 0 };

        Assert.Equal(0, request.TimeoutMs);
    }

    [Fact]
    public void AgentTaskResponse_Error_CanContainTimeoutMessage()
    {
        var response = new AgentTaskResponse
        {
            Error = "Task timed out after 30000ms",
            TimeoutTriggered = true
        };

        Assert.Contains("timed out", response.Error);
        Assert.True(response.TimeoutTriggered);
    }
}
