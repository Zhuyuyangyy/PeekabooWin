namespace PeekabooWin.Cli.Commands;

public interface ICommandHandler
{
    string CommandName { get; }
    Task<int> ExecuteAsync(string[] args);
}
