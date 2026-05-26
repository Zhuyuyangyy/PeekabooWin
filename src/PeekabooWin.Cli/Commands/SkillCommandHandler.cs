using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Cli.Commands;

public class SkillCommandHandler : ICommandHandler
{
    private readonly VisualSkillStore _skillStore;
    private readonly VacpSkillIntegration _skillIntegration;
    private readonly SkillReplayEngine _replayEngine;
    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;
    private readonly InputService _inputService;
    private readonly UIAutomationService _uiaService;
    private readonly OcrService _ocrService;

    public string CommandName => "skill";

    public SkillCommandHandler(
        VisualSkillStore skillStore,
        VacpSkillIntegration skillIntegration,
        SkillReplayEngine replayEngine,
        WindowService windowService,
        CaptureService captureService,
        InputService inputService,
        UIAutomationService uiaService,
        OcrService ocrService)
    {
        _skillStore = skillStore;
        _skillIntegration = skillIntegration;
        _replayEngine = replayEngine;
        _windowService = windowService;
        _captureService = captureService;
        _inputService = inputService;
        _uiaService = uiaService;
        _ocrService = ocrService;
    }

    public async Task<int> ExecuteAsync(string[] args)
    {
        var command = args[0].ToLower();
        return command switch
        {
            "skill-list" => HandleSkillList(args),
            "skill-replay" => await HandleSkillReplay(args),
            "skill-seed" => HandleSkillSeed(args),
            "skill-search" => HandleSkillSearch(args),
            "skill-search-context" => HandleSkillSearchContext(args),
            "skill-use-preview" => HandleSkillUsePreview(args),
            "skill-execute-guided" => HandleSkillExecuteGuided(args),
            _ => 1
        };
    }

    private int HandleSkillList(string[] args)
    {
        var skills = _skillIntegration.GetAllSkills();

        var result = CommandResult.Ok("skill-list", new
        {
            count = skills.Count,
            skills = skills.Select(s => new
            {
                s.SkillId,
                s.Name,
                s.AppPattern,
                s.ScreenType,
                s.RiskLevel,
                s.SuccessRate,
                s.UsageCount,
                s.CreatedAt
            })
        });
        CliHelpers.PrintJson(result);
        return 0;
    }

    private async Task<int> HandleSkillReplay(string[] args)
    {
        var skillId = CliHelpers.GetFlag(args, "--id", "-i");
        string? window = CliHelpers.GetFlag(args, "--window", "-w");
        bool dryRun = CliHelpers.HasFlag(args, "--dry-run", "-d");
        bool execute = CliHelpers.HasFlag(args, "--execute", "-e");

        if (string.IsNullOrEmpty(skillId))
        {
            CliHelpers.PrintError("skill-replay", "Missing --id flag", "MISSING_ARGUMENT", "Usage: skill-replay --id <skill_id> [--dry-run | --execute] [--window W]");
            return 1;
        }

        var skill = _skillStore.Get(skillId);

        if (skill == null)
        {
            CliHelpers.PrintError("skill-replay", $"Skill not found: {skillId}", "SKILL_NOT_FOUND");
            return 1;
        }

        if (!dryRun && !execute)
        {
            dryRun = true;
            PekaLogger.Info("SkillReplay", "No --dry-run or --execute flag specified, defaulting to dry-run");
        }

        var report = await _replayEngine.ReplayAsync(skill, window, dryRun);
        _skillStore.Add(skill);

        var cmdResult = CommandResult.Ok("skill-replay", new
        {
            report.SkillId,
            report.SkillName,
            report.DryRun,
            report.StepsTotal,
            report.StepsExecuted,
            report.StepsBlocked,
            report.VerificationPassed,
            report.TracePath,
            steps = report.StepRecords.Select(r => new
            {
                r.StepIndex,
                r.StepDescription,
                r.ParsedAction,
                r.Target,
                r.DryRunSkipped,
                r.RiskBlocked,
                r.RiskScore,
                r.Executed,
                r.Success,
                r.Error
            })
        });
        CliHelpers.PrintJson(cmdResult);
        return report.VerificationPassed ? 0 : 1;
    }

    private int HandleSkillSeed(string[] args)
    {
        _skillStore.SeedDemo();
        var result = CommandResult.Ok("skill-seed", new
        {
            message = "Demo skills seeded (Notepad Text Entry + Dialog Confirm)",
            count = _skillStore.GetAll().Count
        });
        CliHelpers.PrintJson(result);
        return 0;
    }

    private int HandleSkillSearch(string[] args)
    {
        var task = CliHelpers.GetFlag(args, "--task", "-t") ?? CliHelpers.GetFlag(args, "--text", "-x");
        var app = CliHelpers.GetFlag(args, "--app", "-a");
        var text = CliHelpers.GetFlag(args, "--visible-text", "-v");
        var title = CliHelpers.GetFlag(args, "--window", "-w");

        if (string.IsNullOrEmpty(task))
        {
            CliHelpers.PrintError("skill-search", "Missing --task flag");
            return 1;
        }

        var searchResults = _skillIntegration.Search(task, app, text, title);

        var output = new
        {
            query = task,
            app_pattern = app,
            results = searchResults.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Skill.AppPattern,
                r.Skill.ScreenType,
                r.Skill.RiskLevel,
                r.Skill.UsageCount,
                scope = r.Skill.Scope == null ? null : new
                {
                    r.Skill.Scope.SupportedApps,
                    r.Skill.Scope.RequiredAnchors,
                    r.Skill.Scope.ForbiddenDomains,
                    r.Skill.Scope.MinRiskLevel
                },
                score = new
                {
                    r.Score.AppMatch,
                    r.Score.TextMatch,
                    r.Score.ActionSequenceMatch,
                    r.Score.RiskMatch,
                    r.Score.RecencyFactor,
                    r.Score.Total,
                    r.Score.IsUsable
                },
                r.Reason
            }).ToList()
        };

        var cmdResult = CommandResult.Ok("skill-search", output);
        CliHelpers.PrintJson(cmdResult);
        return 0;
    }

    private int HandleSkillSearchContext(string[] args)
    {
        var task = CliHelpers.GetFlag(args, "--task", "-t") ?? CliHelpers.GetFlag(args, "--text", "-x");
        var windowTitle = CliHelpers.GetFlag(args, "--window", "-w");

        if (string.IsNullOrEmpty(task))
        {
            CliHelpers.PrintError("skill-search-context", "Missing --task flag");
            return 1;
        }

        var sig = _skillIntegration.BuildWindowSignature(windowTitle);
        var searchResults = _skillIntegration.SearchWithContext(task, windowTitle);
        var visibleHints = sig.VisibleTexts;
        var anchors = sig.AnchorCandidates;
        var profile = sig.Profile;

        var output = new
        {
            query = task,
            window_title = windowTitle ?? "(foreground window)",
            app_profile = profile == null ? null : new
            {
                profile.AppName,
                profile.ProcessName,
                profile.AppId,
                profile.WindowType,
                profile.InputMode,
                profile.RiskDomain,
                visibleTextHints = visibleHints
            },
            anchor_candidates = anchors,
            window_signature = new
            {
                sig.WindowTitle,
                sig.ProcessName,
                sig.WindowType,
                sig.InputMode,
                sig.RiskDomain,
                sig.CapturedAt
            },
            results = searchResults.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Skill.AppPattern,
                r.Skill.ScreenType,
                r.Skill.RiskLevel,
                scope = r.Skill.Scope == null ? null : new
                {
                    r.Skill.Scope.SupportedApps,
                    r.Skill.Scope.RequiredAnchors,
                    r.Skill.Scope.ForbiddenDomains,
                    r.Skill.Scope.MinRiskLevel
                },
                score = new
                {
                    r.Score.Total,
                    r.Score.IsUsable
                },
                r.Reason
            }).ToList()
        };

        var cmdResult = CommandResult.Ok("skill-search-context", output);
        CliHelpers.PrintJson(cmdResult);
        return 0;
    }

    private int HandleSkillUsePreview(string[] args)
    {
        var task = CliHelpers.GetFlag(args, "--task", "-t");
        var app = CliHelpers.GetFlag(args, "--app", "-a");

        if (string.IsNullOrEmpty(task))
        {
            CliHelpers.PrintError("skill-use-preview", "Missing --task flag");
            return 1;
        }

        var searchResults = _skillIntegration.Search(task, app, null, null);
        var usable = searchResults.Where(r => _skillIntegration.Policy.CanUseSkill(r, task)).ToList();
        var best = usable.FirstOrDefault();

        var output = new
        {
            query = task,
            app_pattern = app,
            all_results_count = searchResults.Count,
            usable_count = usable.Count,
            top_candidate = best != null ? new
            {
                best.Skill.SkillId,
                best.Skill.Name,
                best.Skill.RiskLevel,
                best.Score.Total,
                best.Score.IsUsable,
                would_use_skill_hint = best.Score.Total >= 0.7
            } : null,
            usable_skills = usable.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Score.Total,
                r.Score.IsUsable
            }).ToList()
        };

        var cmdResult = CommandResult.Ok("skill-use-preview", output);
        CliHelpers.PrintJson(cmdResult);
        return 0;
    }

    private int HandleSkillExecuteGuided(string[] args)
    {
        var task = CliHelpers.GetFlag(args, "--task", "-t");
        var app = CliHelpers.GetFlag(args, "--app", "-a");

        if (string.IsNullOrEmpty(task))
        {
            CliHelpers.PrintError("skill-execute-guided", "Missing --task flag");
            return 1;
        }

        var searchResults = _skillIntegration.Search(task, app, null, null);
        var usable = searchResults.Where(r => _skillIntegration.Policy.CanUseSkill(r, task)).ToList();
        var best = usable.FirstOrDefault();

        var preview = new
        {
            query = task,
            app_pattern = app,
            search_count = searchResults.Count,
            usable_count = usable.Count,
            top_skill = best?.Skill.Name,
            top_score = best?.Score.Total,
            skill_hint_injected = best != null && best.Score.Total >= 0.7
        };

        var cmdResult = CommandResult.Ok("skill-execute-guided", new
        {
            preview,
            search_results = searchResults.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Score.Total,
                r.Score.IsUsable
            }),
            note = "V0.8: skill-execute-guided shows search preview. Use 'agent --task ...' for full guided execution."
        });
        CliHelpers.PrintJson(cmdResult);
        return 0;
    }
}
