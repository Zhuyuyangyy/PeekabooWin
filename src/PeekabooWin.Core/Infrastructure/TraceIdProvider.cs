namespace PeekabooWin.Core.Infrastructure;

public static class TraceIdProvider
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string Current => _current.Value ??= Generate();

    public static string Generate()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    public static string BeginNew()
    {
        _current.Value = Generate();
        return _current.Value;
    }
}
