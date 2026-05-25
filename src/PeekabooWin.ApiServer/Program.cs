using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Agent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SkillTransferController>();

var app = builder.Build();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// Skills list
app.MapGet("/api/v1/skills/list", () => {
    var store = new VisualSkillStore();
    var skills = store.GetAll();
    return Results.Ok(new { count = skills.Count, skills });
});

// Transfer decide
app.MapPost("/api/v1/transfer/decide", (TransferRequest req) => {
    var ctrl = app.Services.GetRequiredService<SkillTransferController>();
    var skill = new VisualSkill { SkillId = req.SkillId, RiskLevel = req.SkillRiskLevel, RiskDomain = req.SkillRiskDomain, ContextAnchors = req.ContextAnchors ?? new() };
    var targetApp = new AppProfile { AppId = req.AppId, RiskDomain = req.AppRiskDomain };
    var ctx = new TransferContext { Skill = skill, CurrentApp = targetApp, TaskText = req.TaskText, SkillMatchScore = req.Score, VisibleTexts = req.VisibleTexts ?? new() };
    var decision = ctrl.Decide(ctx);
    return Results.Ok(new { action = decision.Action.ToString(), reason = decision.Reason, score = decision.SkillMatchScore, blockReason = decision.BlockReason });
});

// App profile
app.MapGet("/api/v1/app/profile", (string? processName, string? windowTitle) => {
    if (string.IsNullOrEmpty(processName)) return Results.BadRequest("processName required");
    var sig = WindowSignature.FromProcessAndTitle(processName, windowTitle ?? "");
    var profile = AppProfile.FromWindowSignature(sig);
    return Results.Ok(new { appId = profile.AppId, windowType = profile.WindowType, inputMode = profile.InputMode, riskDomain = profile.RiskDomain, anchors = profile.KnownAnchors });
});

// Window capture (placeholder)
app.MapPost("/api/v1/window/capture", () => {
    return Results.Ok(new { message = "V0.10: window capture + OCR not yet implemented", status = "planned" });
});

app.Run("http://0.0.0.0:8025");

public class TransferRequest {
    public string SkillId { get; set; } = "";
    public string SkillRiskLevel { get; set; } = "L0";
    public string SkillRiskDomain { get; set; } = "neutral";
    public string AppId { get; set; } = "";
    public string AppRiskDomain { get; set; } = "neutral";
    public string? TaskText { get; set; }
    public double Score { get; set; } = 0.75;
    public List<string>? ContextAnchors { get; set; }
    public List<string>? VisibleTexts { get; set; }
}