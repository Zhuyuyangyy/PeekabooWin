using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Verification;
using PeekabooWin.Core.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace PeekabooWin.Cli.Bootstrap;

public static class ServiceRegistration
{
    public static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<WindowService>();
        services.AddSingleton<CaptureService>();
        services.AddSingleton<InputService>();
        services.AddSingleton<UIAutomationService>();
        services.AddSingleton<OcrService>();
        services.AddSingleton<VisualSkillStore>();
        services.AddSingleton<VacpSkillIntegration>();
        services.AddSingleton<ActionRiskGate>();
        services.AddSingleton<RecoveryPlanner>();
        services.AddSingleton<ActionVerifier>();
        services.AddSingleton<ElementCandidateRanker>();
        services.AddSingleton<SkillReplayEngine>();
        services.AddSingleton<SkillTransferController>();
        services.AddSingleton<TempFileManager>();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<TaskParser>();
        services.AddSingleton<ActionExecutor>();
        services.AddSingleton<VacpTraceLogger>();
        services.AddSingleton<AgentOrchestrator>();
        services.AddSingleton<AgentService>(sp =>
        {
            var orchestrator = sp.GetRequiredService<AgentOrchestrator>();
            return new AgentService(orchestrator);
        });

        services.AddSingleton<CommandRouter>();

        services.AddTransient<Commands.WindowCommandHandler>();
        services.AddTransient<Commands.UiaCommandHandler>();
        services.AddTransient<Commands.OcrCommandHandler>();
        services.AddTransient<Commands.AgentCommandHandler>();
        services.AddTransient<Commands.SkillCommandHandler>();
        services.AddTransient<Commands.ServerCommandHandler>();

        return services.BuildServiceProvider();
    }
}
