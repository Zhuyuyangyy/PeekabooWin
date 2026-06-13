using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PeekabooWin.Cli.Bootstrap;
using PeekabooWin.Cli.Commands;
using PeekabooWin.Core.Infrastructure;

namespace PeekabooWin.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        TraceIdProvider.BeginNew();

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLower();

        if (command is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        using var provider = ServiceRegistration.ConfigureServices();
        var router = provider.GetRequiredService<CommandRouter>();

        if (!router.HasCommand(command))
        {
            CliHelpers.PrintError(command, $"Unknown command: {command}", "UNKNOWN_COMMAND");
            return 1;
        }

        try
        {
            var handler = router.Resolve(command)!;
            return await handler.ExecuteAsync(args);
        }
        catch (PeekabooWin.Core.Exceptions.PeekabooException ex)
        {
            CliHelpers.PrintError(command, ex.Message, ex.ErrorCode, ex.Hint);
            return 1;
        }
        catch (Exception ex)
        {
            CliHelpers.PrintError(command, ex.Message);
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"
PeekabooWin - Windows Desktop Automation CLI (V0.10)

Usage: peekaboo-win <command> [options]

V0.6 - VACP Trusted Execution:
  click-rel --window K --x X --y Y  Click relative to window
  is-focused [--window K]          Check focus state
  find-on-screen TEXT              OCR find text on screen
  ocr-click --text TEXT            Find text and click

V0.7 - Visual Skill Memory:
  skill-list                       List extracted visual skills
  skill-replay --id ID [--window K]  Replay a saved skill
  skill-seed                       Seed demo skills (Notepad + Dialog)

V0.8 - Skill-Guided Execution:
  skill-search --task T            Search skills by task
  skill-use-preview --task T       Preview skill usage
  skill-execute-guided --task T    Guided skill execution

V0.9 - Multi-App Skill Generalization:
  skill-search-context --task T [--window W]  Context-aware skill search

V0.1 - Core:
  list-windows [--keyword K]    List all visible windows
  focus-window --window K       Focus window by title keyword
  screenshot --out PATH [--screen | --window K]
  click --x X --y Y            Click at coordinates
  type ""text""                 Type text
  press --key K                Press key (esc/enter/tab/backspace/delete)
  hotkey --keys CTRL+L         Execute hotkey
  window-info                  Show all windows detail

V0.2 - UIA Automation:
  inspect --window K [--max-depth N] [--json-out PATH]
  find --window K --name N
  find --window K --control-type TYPE
  find --window K --automation-id ID
  click-element --window K --name N [--dry-run]
  find-by-control-type --window K --control-type TYPE

V0.3 - OCR:
  ocr [--window K] [--text T] [--lang L] [--click]
  find-on-screen --text T [--window K]
  ocr-click --text T [--window K]
  ocr-scan [--window K]            Scan all visible text with screen coords (works on any app)

V0.4 - Agent:
  agent --task T [--max-steps N] [--dry-run] [--context C]

V0.5 - HTTP API Server:
  server [--port P]
");
    }
}
