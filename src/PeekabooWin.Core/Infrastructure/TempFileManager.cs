namespace PeekabooWin.Core.Infrastructure;

public class TempFileManager : IDisposable
{
    private readonly string _baseDir;
    private readonly List<string> _trackedFiles = [];
    private readonly List<string> _trackedDirs = [];
    private bool _disposed;

    public TempFileManager(string? baseDir = null)
    {
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PeekabooWin", "temp");
        EnsureDir(_baseDir);
    }

    public string BaseDir => _baseDir;

    public string CreateTempPath(string prefix, string extension = ".png")
    {
        var path = Path.Combine(_baseDir, $"{prefix}_{Guid.NewGuid():N}{extension}");
        _trackedFiles.Add(path);
        return path;
    }

    public string CreateSubDir(string name)
    {
        var dir = Path.Combine(_baseDir, name);
        EnsureDir(dir);
        _trackedDirs.Add(dir);
        return dir;
    }

    public void TrackFile(string path)
    {
        if (!_trackedFiles.Contains(path))
            _trackedFiles.Add(path);
    }

    public void CleanupFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { PekaLogger.Warn("TempFileManager", "Cleanup failed", ex); }
        _trackedFiles.Remove(path);
    }

    public void CleanupAll()
    {
        foreach (var f in _trackedFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch (Exception ex) { PekaLogger.Warn("TempFileManager", "Cleanup failed", ex); }
        }
        _trackedFiles.Clear();

        foreach (var d in _trackedDirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }
            catch (Exception ex) { PekaLogger.Warn("TempFileManager", "Cleanup failed", ex); }
        }
        _trackedDirs.Clear();
    }

    private static void EnsureDir(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        CleanupAll();
        _disposed = true;
    }
}
