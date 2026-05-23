using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Planning;

/// <summary>
/// VACP trace logger — persists VacpTraceRecord to disk for audit/replay.
/// </summary>
public class VacpTraceLogger
{
    private readonly string _baseTraceDir;
    private readonly JsonSerializerOptions _jsonOptions;

    public VacpTraceLogger(string? baseDir = null)
    {
        _baseTraceDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PeekabooWin", "traces");

        Directory.CreateDirectory(_baseTraceDir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public VacpTaskTrace BeginTaskTrace(string taskId, string taskDescription)
    {
        var trace = new VacpTaskTrace
        {
            TaskId = taskId,
            TaskDescription = taskDescription
        };

        var taskDir = Path.Combine(_baseTraceDir, taskId);
        Directory.CreateDirectory(taskDir);
        Directory.CreateDirectory(Path.Combine(taskDir, "screenshots"));
        return trace;
    }

    public void RecordStep(VacpTaskTrace taskTrace, VacpTraceRecord stepRecord)
    {
        taskTrace.StepTraces.Add(stepRecord);
        taskTrace.TotalSteps = taskTrace.StepTraces.Count;

        if (stepRecord.StepSuccess)
            taskTrace.SuccessfulSteps++;
        else if (stepRecord.RiskGateDecision == "BLOCK")
            taskTrace.BlockedSteps++;
        else
            taskTrace.FailedSteps++;

        taskTrace.OverallSuccess = taskTrace.FailedSteps == 0 && taskTrace.BlockedSteps == 0;

        var stepFile = Path.Combine(_baseTraceDir, taskTrace.TaskId, $"step_{stepRecord.StepIndex:D3}.json");
        var json = JsonSerializer.Serialize(stepRecord, _jsonOptions);
        File.WriteAllText(stepFile, json);
    }

    public string? SaveScreenshot(string taskId, int stepIndex, byte[] imageBytes, string suffix)
    {
        try
        {
            var dir = Path.Combine(_baseTraceDir, taskId, "screenshots");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, $"step_{stepIndex:D3}_{suffix}.png");
            File.WriteAllBytes(filePath, imageBytes);
            return filePath;
        }
        catch { return null; }
    }

    public void FinishTaskTrace(VacpTaskTrace taskTrace)
    {
        var summaryPath = Path.Combine(_baseTraceDir, taskTrace.TaskId, "task_summary.json");
        var json = JsonSerializer.Serialize(taskTrace, _jsonOptions);
        File.WriteAllText(summaryPath, json);
    }

    public VacpTaskTrace? LoadTaskTrace(string taskId)
    {
        var path = Path.Combine(_baseTraceDir, taskId, "task_summary.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<VacpTaskTrace>(json, _jsonOptions);
    }

    public List<string> ListTaskTraces()
    {
        if (!Directory.Exists(_baseTraceDir)) return new List<string>();
        return Directory.GetDirectories(_baseTraceDir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .OrderByDescending(n => n)
            .ToList();
    }

    public string GenerateBenchmarkReport(string reportName, IEnumerable<VacpTaskTrace> taskTraces)
    {
        var traces = taskTraces.ToList();
        var allSteps = traces.SelectMany(t => t.StepTraces).ToList();

        var blockedCount = allSteps.Count(s => s.RiskGateDecision == "BLOCK");
        var highRiskCount = allSteps.Count(s => s.RiskScore >= 0.6);
        var groundedCount = allSteps.Count(s => s.GroundingScore >= 0.75);
        var successCount = allSteps.Count(s => s.StepSuccess);
        var retrySuccessCount = traces.Count(t => t.StepTraces.Any(s => s.WasRetried && s.StepSuccess));
        var stepWithVerif = allSteps.Where(s => s.VerificationScore > 0).ToList();

        var metrics = new BenchmarkMetrics
        {
            ReportName = reportName,
            GeneratedAt = DateTime.UtcNow,
            TotalTasks = traces.Count,
            GroundingPassRate = allSteps.Count == 0 ? 0.0 : (double)groundedCount / allSteps.Count,
            StepSuccessRate = allSteps.Count == 0 ? 0.0 : (double)successCount / allSteps.Count,
            HighRiskBlockRate = highRiskCount == 0 ? 0.0 : (double)blockedCount / highRiskCount,
            VerificationRecoveryRate = traces.Count == 0 ? 0.0 : (double)retrySuccessCount / traces.Count,
            AverageRiskScore = allSteps.Count == 0 ? 0.0 : allSteps.Average(s => s.RiskScore),
            AverageVerificationScore = stepWithVerif.Count == 0 ? 0.0 : stepWithVerif.Average(s => s.VerificationScore),
            TaskTraces = traces,
        };

        var reportPath = Path.Combine(_baseTraceDir, $"benchmark_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(metrics, _jsonOptions);
        File.WriteAllText(reportPath, json);
        return reportPath;
    }
}

public class BenchmarkMetrics
{
    [JsonPropertyName("report_name")]
    public string ReportName { get; set; } = "";

    [JsonPropertyName("generated_at")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("total_tasks")]
    public int TotalTasks { get; set; }

    [JsonPropertyName("grounding_pass_rate")]
    public double GroundingPassRate { get; set; }

    [JsonPropertyName("step_success_rate")]
    public double StepSuccessRate { get; set; }

    [JsonPropertyName("high_risk_block_rate")]
    public double HighRiskBlockRate { get; set; }

    [JsonPropertyName("verification_recovery_rate")]
    public double VerificationRecoveryRate { get; set; }

    [JsonPropertyName("average_risk_score")]
    public double AverageRiskScore { get; set; }

    [JsonPropertyName("average_verification_score")]
    public double AverageVerificationScore { get; set; }

    [JsonPropertyName("task_traces")]
    public List<VacpTaskTrace> TaskTraces { get; set; } = new();
}