using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Cli.Commands;

public class AgentCommandHandler : ICommandHandler
{
    private readonly AgentService _agentService;

    public string CommandName => "agent";

    public AgentCommandHandler(AgentService agentService)
    {
        _agentService = agentService;
    }

    public async Task<int> ExecuteAsync(string[] args)
    {
        var command = args[0].ToLower();
        return command switch
        {
            "agent" => await HandleAgent(args),
            _ => 1
        };
    }

    private async Task<int> HandleAgent(string[] args)
    {
        string? task = CliHelpers.GetFlag(args, "--task", "-t");
        int maxSteps = int.TryParse(CliHelpers.GetFlag(args, "--max-steps", "-m") ?? "5", out var ms) ? ms : 5;
        bool dryRun = CliHelpers.HasFlag(args, "--dry-run", "-d");
        string? context = CliHelpers.GetFlag(args, "--context", "-c");

        if (string.IsNullOrEmpty(task))
        {
            CliHelpers.PrintError("agent", "Missing --task flag");
            return 1;
        }

        var request = new AgentTaskRequest
        {
            Task = task,
            Context = context,
            MaxSteps = maxSteps,
            DryRun = dryRun
        };

        var result = await _agentService.ExecuteTaskAsync(request);
        var cmdResult = CommandResult.Ok("agent", result);
        CliHelpers.PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }
}
