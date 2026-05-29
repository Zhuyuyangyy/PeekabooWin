using System.IO;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.Verification;

namespace PeekabooWin.Core.Planning;

/// <summary>
/// VACP: Vision-Action Closed-loop Planner
/// Visual Action Closed-loop Planner
/// 
/// Pipeline:
/// Screen Perception -> Element Grounding -> Action Proposal -> Risk Gating -> Feedback Verification
/// </summary>
public class VacpPlanner
{
    private readonly ElementGroundingScore _grounder;
    private readonly ActionRiskGate _riskGate;
    private readonly BeforeAfterVerifier _verifier;
    private readonly ActionRanker _ranker;
    private readonly StableTyper _stableTyper;
    private readonly InputService _inputService;

    public VacpPlanner(
        ElementGroundingScore grounder,
        ActionRiskGate riskGate,
        BeforeAfterVerifier verifier,
        ActionRanker ranker,
        StableTyper stableTyper,
        InputService inputService)
    {
        _grounder = grounder;
        _riskGate = riskGate;
        _verifier = verifier;
        _ranker = ranker;
        _stableTyper = stableTyper;
        _inputService = inputService;
    }

    public async Task<VacpResult> Execute闭环(VacpRequest request)
    {
        var result = new VacpResult { Task = request.Task };

        try
        {
            // Step 1: Capture screenshot (to temp file, then read as bytes)
            var tempPath = Path.Combine(Path.GetTempPath(), $"vacp_{Guid.NewGuid():N}.png");
            request.ScreenCaptureService.CaptureScreen(tempPath);
            result.ScreenshotBefore = File.Exists(tempPath) ? File.ReadAllBytes(tempPath) : Array.Empty<byte>();

            // Step 2: Vision perceive -> Screen State Graph
            var graph = await request.VisionClient.PerceiveScreen(result.ScreenshotBefore, request.Task);
            result.ScreenGraph = graph;

            // Step 3: Build action candidates from graph
            var candidates = BuildCandidatesFromGraph(graph, request.Task);
            result.ActionCandidates = candidates;

            // Step 4: Element grounding score
            var groundedCandidates = candidates.Select(c =>
            {
                if (c.TargetElement != null)
                {
                    var query = new GroundingQuery
                    {
                        TargetText = c.TargetElement.Label ?? c.TargetElement.Name,
                        ExpectedType = c.TargetElement.Type,
                    };
                    c.GroundingScore = _grounder.Score(c.TargetElement, query);
                }
                return c;
            }).ToList();

            // Step 5: Rank candidates
            var context = new RankingContext { Goal = request.Task };
            var rankedCandidates = _ranker.Rank(groundedCandidates, context);
            result.RankedCandidates = rankedCandidates;

            if (rankedCandidates.Count == 0)
            {
                result.Success = false;
                result.FinalMessage = "No executable candidates found";
                return result;
            }

            var bestAction = rankedCandidates[0];
            result.SelectedAction = bestAction;

            // Step 6: Risk gating
            var riskContext = new ActionRiskContext
            {
                ActionType = bestAction.ActionType,
                TargetLabel = bestAction.TargetElement?.Label,
                InputText = bestAction.InputText,
                PageType = graph.ScreenType,
                TargetElement = bestAction.TargetElement,
                GroundingScore = bestAction.GroundingScore,
            };
            var riskDecision = _riskGate.Evaluate(riskContext);
            result.RiskDecision = riskDecision;

            if (riskDecision.Decision == RiskLevel.Block)
            {
                result.Success = false;
                result.Blocked = true;
                result.FinalMessage = "Action blocked by risk gate: " + riskDecision.BlockReason;
                return result;
            }

            if (riskDecision.Decision == RiskLevel.Confirm)
            {
                result.NeedsConfirmation = true;
                result.ConfirmationMessage = riskDecision.RequiredConfirmation;
            }

            // Step 7: Execute
            result.ExecutionResult = await ExecuteAction(bestAction, request);

            if (!result.ExecutionResult.Success)
            {
                result.Success = false;
                result.FinalMessage = "Execution failed: " + result.ExecutionResult.Error;
                return result;
            }

            // Step 8: Verification with after-screenshot
            var afterPath = Path.Combine(Path.GetTempPath(), $"vacp_{Guid.NewGuid():N}.png");
            request.ScreenCaptureService.CaptureScreen(afterPath);
            result.ScreenshotAfter = File.Exists(afterPath) ? File.ReadAllBytes(afterPath) : Array.Empty<byte>();

            var verifyContext = new VerificationContext
            {
                ActionType = bestAction.ActionType,
                TargetElement = bestAction.TargetElement,
                ExpectedText = bestAction.InputText,
                InputText = bestAction.InputText,
            };
            var verifyResult = _verifier.Verify(result.ScreenshotBefore, result.ScreenshotAfter, verifyContext);
            result.VerificationResult = verifyResult;

            if (verifyResult.Outcome == BeforeAfterVerificationOutcome.Success)
            {
                result.Success = true;
                result.FinalMessage = "Task completed (score: " + verifyResult.VerificationScore.ToString("F2") + ")";
            }
            else
            {
                // Retry once
                result.RetryCount = 1;
                result.RetrySuggestion = verifyResult.RecoverySuggestion;

                result.ExecutionResult = await ExecuteAction(bestAction, request);
                if (result.ExecutionResult.Success)
                {
                    var retryAfterPath = Path.Combine(Path.GetTempPath(), $"vacp_{Guid.NewGuid():N}.png");
                    request.ScreenCaptureService.CaptureScreen(retryAfterPath);
                    result.ScreenshotAfter = File.Exists(retryAfterPath) ? File.ReadAllBytes(retryAfterPath) : Array.Empty<byte>();
                    result.Success = true;
                    result.FinalMessage = "Completed after retry (attempts: 2)";
                }
                else
                {
                    result.Success = false;
                    result.FinalMessage = "Still failed after retry: " + result.ExecutionResult.Error;
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.FinalMessage = "VACP exception: " + ex.Message;
        }

        return result;
    }

    private List<ActionCandidate> BuildCandidatesFromGraph(ScreenStateGraph graph, string task)
    {
        var candidates = new List<ActionCandidate>();

        foreach (var element in graph.Elements)
        {
            if (element.IsEnabled && element.State != "disabled")
            {
                candidates.Add(new ActionCandidate
                {
                    ActionType = "click",
                    TargetElement = element,
                    Description = "Click [" + element.Label + "]",
                    GroundingScore = element.Confidence,
                    ModelScore = element.Confidence,
                });
            }
        }

        var taskLower = task.ToLower();
        if (taskLower.Contains("input") || taskLower.Contains("type") || taskLower.Contains("fill"))
        {
            var emptyInput = graph.Elements
                .Where(e => e.Type == "input" && e.State == "empty")
                .FirstOrDefault();

            if (emptyInput != null)
            {
                candidates.Insert(0, new ActionCandidate
                {
                    ActionType = "type",
                    TargetElement = emptyInput,
                    InputText = ExtractInputText(task),
                    Description = "Type in [" + emptyInput.Label + "]",
                    GroundingScore = emptyInput.Confidence,
                });
            }
        }

        return candidates;
    }

    private string? ExtractInputText(string task)
    {
        var parts = task.Split(new[] { "input", "输入", "type" }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1) return parts[1].Trim();
        return null;
    }

    private async Task<ExecutionResult> ExecuteAction(ActionCandidate action, VacpRequest request)
    {
        var execResult = new ExecutionResult();

        try
        {
            switch (action.ActionType)
            {
                case "click":
                    var elem = action.TargetElement;
                    if (elem != null)
                    {
                        var x = elem.BBox.CenterX;
                        var y = elem.BBox.CenterY;
                        request.InputService.Click(x, y);
                        execResult.Success = true;
                        execResult.Message = "Clicked at (" + x + ", " + y + ")";
                    }
                    break;

                case "type":
                    if (!string.IsNullOrEmpty(action.InputText))
                    {
                        await _stableTyper.TypeSlowly(action.InputText);
                        execResult.Success = true;
                        execResult.Message = "Typed: " + action.InputText;
                    }
                    break;

                default:
                    execResult.Success = false;
                    execResult.Error = "Unsupported action type: " + action.ActionType;
                    break;
            }
        }
        catch (Exception ex)
        {
            execResult.Success = false;
            execResult.Error = ex.Message;
        }

        return execResult;
    }
}

public class VacpRequest
{
    public string Task { get; set; } = "";
    public CaptureService ScreenCaptureService { get; set; } = null!;
    public IVisionClient VisionClient { get; set; } = null!;
    public InputService InputService { get; set; } = null!;

    // V0.8 Skill-Guided Execution: hint from matched skill influences VACP candidate ranking
    public SkillHint? SkillHint { get; set; }
}

public class VacpResult
{
    public string Task { get; set; } = "";
    public bool Success { get; set; }
    public bool Blocked { get; set; }
    public bool NeedsConfirmation { get; set; }
    public string? ConfirmationMessage { get; set; }
    public string FinalMessage { get; set; } = "";
    public string? Error { get; set; }

    public byte[]? ScreenshotBefore { get; set; }
    public byte[]? ScreenshotAfter { get; set; }
    public ScreenStateGraph? ScreenGraph { get; set; }
    public List<ActionCandidate> ActionCandidates { get; set; } = new();
    public List<ActionCandidate> RankedCandidates { get; set; } = new();
    public ActionCandidate? SelectedAction { get; set; }
    public RiskDecision? RiskDecision { get; set; }
    public ExecutionResult? ExecutionResult { get; set; }
    public BeforeAfterVerificationResult? VerificationResult { get; set; }
    public int RetryCount { get; set; }
    public string? RetrySuggestion { get; set; }
}

public class ExecutionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? Error { get; set; }
}

public interface IScreenCaptureService
{
    byte[] CaptureScreen();
    byte[] CaptureWindow(IntPtr hwnd);
}

public interface IVisionClient
{
    Task<ScreenStateGraph> PerceiveScreen(byte[] screenshot, string task);
}