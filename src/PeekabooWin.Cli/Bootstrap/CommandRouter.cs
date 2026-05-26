using Microsoft.Extensions.DependencyInjection;
using PeekabooWin.Cli.Commands;

namespace PeekabooWin.Cli.Bootstrap;

public class CommandRouter
{
    private readonly IServiceProvider _provider;
    private readonly Dictionary<string, Type> _handlerMap;

    public CommandRouter(IServiceProvider provider)
    {
        _provider = provider;
        _handlerMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["list-windows"] = typeof(WindowCommandHandler),
            ["focus-window"] = typeof(WindowCommandHandler),
            ["screenshot"] = typeof(WindowCommandHandler),
            ["click"] = typeof(WindowCommandHandler),
            ["type"] = typeof(WindowCommandHandler),
            ["press"] = typeof(WindowCommandHandler),
            ["hotkey"] = typeof(WindowCommandHandler),
            ["window-info"] = typeof(WindowCommandHandler),
            ["click-rel"] = typeof(WindowCommandHandler),
            ["is-focused"] = typeof(WindowCommandHandler),

            ["inspect"] = typeof(UiaCommandHandler),
            ["find"] = typeof(UiaCommandHandler),
            ["click-element"] = typeof(UiaCommandHandler),
            ["find-by-control-type"] = typeof(UiaCommandHandler),

            ["ocr"] = typeof(OcrCommandHandler),
            ["find-on-screen"] = typeof(OcrCommandHandler),
            ["ocr-click"] = typeof(OcrCommandHandler),

            ["agent"] = typeof(AgentCommandHandler),

            ["skill-list"] = typeof(SkillCommandHandler),
            ["skill-replay"] = typeof(SkillCommandHandler),
            ["skill-seed"] = typeof(SkillCommandHandler),
            ["skill-search"] = typeof(SkillCommandHandler),
            ["skill-search-context"] = typeof(SkillCommandHandler),
            ["skill-use-preview"] = typeof(SkillCommandHandler),
            ["skill-execute-guided"] = typeof(SkillCommandHandler),

            ["server"] = typeof(ServerCommandHandler),
        };
    }

    public ICommandHandler? Resolve(string command)
    {
        if (!_handlerMap.TryGetValue(command, out var handlerType))
            return null;

        return (ICommandHandler)_provider.GetRequiredService(handlerType);
    }

    public bool HasCommand(string command) => _handlerMap.ContainsKey(command);
}
