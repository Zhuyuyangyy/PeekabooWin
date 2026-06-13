using System.Net.Http;
using Xunit;
using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.Trace;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Verification;
using PeekabooWin.Core.Windows;
using PeekabooWin.Core.Capture;

namespace PeekabooWin.Core.Tests;

public class SkillTransferGuardIntegrationTests : IDisposable
{
    private readonly WindowService _windowService;

    public SkillTransferGuardIntegrationTests()
    {
        _windowService = new WindowService();
    }

    private AgentOrchestrator BuildOrchestrator(VisualSkillStore store)
    {
        var vacpSkillIntegration = new VacpSkillIntegration(store);
        var taskParser = new TaskParser();
        var traceLogger = new VacpTraceLogger();
        var tempFiles = new TempFileManager();
        var captureService = new CaptureService(_windowService);
        var ocrService = new OcrService();
        var inputService = new InputService();
        var uiaService = new UIAutomationService(_windowService, inputService);
        var actionVerifier = new ActionVerifier(captureService, ocrService, uiaService, tempFiles);

        // Build PerceptionRouter with local vision fallback for test context
        var perceptionCache = new PerceptionCache();
        var visionClient = new LocalVisionFallback();
        var llmGrounding = new LlmGroundingService(visionClient, captureService, tempFiles, perceptionCache);
        var perceptionRouter = new PerceptionRouter(
            uiaService, llmGrounding, ocrService, captureService,
            inputService, _windowService, tempFiles, perceptionCache);

        return new AgentOrchestrator(
            taskParser,
            new ActionExecutor(_windowService, captureService, inputService, ocrService, uiaService, tempFiles, perceptionRouter),
            vacpSkillIntegration,
            traceLogger,
            new ActionRiskGate(),
            new RecoveryPlanner(),
            actionVerifier,
            new ElementCandidateRanker(),
            captureService,
            ocrService,
            uiaService,
            _windowService,
            tempFiles,
            new SkillTransferController());
    }

    [Fact]
    public async Task RunAsync_ClickStep_TransferDecisionFieldExistsInStepTrace()
    {
        var store = new VisualSkillStore();
        var orchestrator = BuildOrchestrator(store);

        var request = new AgentTaskRequest
        {
            Task = "click the button",
            DryRun = true,
            TimeoutMs = 5000
        };

        var response = await orchestrator.RunAsync(request, CancellationToken.None);

        Assert.NotNull(response.Trace);
        var clickSteps = response.Trace.StepTraces
            .Where(s => s.Action == "click" || s.Action == "click-element-guess")
            .ToList();
        Assert.NotEmpty(clickSteps);
        Assert.All(clickSteps, step => Assert.NotNull(step.TransferDecision));
    }

    [Fact]
    public async Task RunAsync_ScreenshotStep_TransferDecisionNotSet()
    {
        var store = new VisualSkillStore();
        var orchestrator = BuildOrchestrator(store);

        var request = new AgentTaskRequest
        {
            Task = "screenshot",
            DryRun = true,
            TimeoutMs = 5000
        };

        var response = await orchestrator.RunAsync(request, CancellationToken.None);

        Assert.NotNull(response.Trace);
        Assert.DoesNotContain(response.Trace.StepTraces,
            s => s.TransferDecision != null);
    }

    [Fact]
    public async Task RunAsync_InspectStep_TransferDecisionNotSet()
    {
        var store = new VisualSkillStore();
        var orchestrator = BuildOrchestrator(store);

        var request = new AgentTaskRequest
        {
            Task = "inspect window",
            DryRun = true,
            TimeoutMs = 5000
        };

        var response = await orchestrator.RunAsync(request, CancellationToken.None);

        Assert.NotNull(response.Trace);
        Assert.DoesNotContain(response.Trace.StepTraces,
            s => s.TransferDecision != null);
    }

    public void Dispose()
    {
    }
}
