namespace PeekabooWin.Cli.Commands;

public class ServerCommandHandler : ICommandHandler
{
    public string CommandName => "server";

    public async Task<int> ExecuteAsync(string[] args)
    {
        string? portStr = CliHelpers.GetFlag(args, "--port", "-p");
        int port = int.TryParse(portStr, out var p) ? p : 8080;

        Console.WriteLine($"[PeekabooWin] Starting HTTP API server on port {port}...");
        var server = new ApiServer(port);
        server.Start();

        var tcs = new TaskCompletionSource<int>();

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            server.Stop();
            tcs.SetResult(0);
        };

        Console.WriteLine("[PeekabooWin] API server running. Press Ctrl+C to stop.");
        return await tcs.Task;
    }
}
