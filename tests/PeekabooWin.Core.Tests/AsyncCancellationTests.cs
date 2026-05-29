using Xunit;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class AsyncCancellationTests
{
    [Fact]
    public void CancellationTokenSource_CanBeCreatedAndCancelled()
    {
        using var cts = new CancellationTokenSource();

        Assert.False(cts.IsCancellationRequested);

        cts.Cancel();

        Assert.True(cts.IsCancellationRequested);
        Assert.True(cts.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task OperationCanceledException_ThrownWhenTokenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await Task.Delay(5000, cts.Token);
        });
    }

    [Fact]
    public async Task TaskDelay_RespectsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await Task.Delay(10000, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public void AgentTaskRequest_TimeoutMs_DefaultIs30000()
    {
        var request = new AgentTaskRequest();

        Assert.Equal(30000, request.TimeoutMs);
    }

    [Fact]
    public void AgentTaskResponse_CancelledAndTimeoutTriggered_DefaultToFalse()
    {
        var response = new AgentTaskResponse();

        Assert.False(response.Cancelled);
        Assert.False(response.TimeoutTriggered);
    }
}
