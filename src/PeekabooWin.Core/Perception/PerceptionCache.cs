using System.Collections.Concurrent;
using System.Security.Cryptography;
using PeekabooWin.Core.Infrastructure;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// 感知结果内存缓存 — 缓存 LLM 视觉定位结果，避免对相同截图重复调用 LLM
/// 
/// 缓存键 = SHA256(图片字节) + ":" + 任务描述
/// 线程安全，支持自动过期和容量限制
/// </summary>
public class PerceptionCache
{
    private const string LogTag = "PerceptionCache";

    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 创建感知缓存实例
    /// </summary>
    /// <param name="ttl">缓存条目生存时间（默认 5 秒）</param>
    /// <param name="maxEntries">最大缓存条目数（默认 20）</param>
    public PerceptionCache(TimeSpan? ttl = null, int maxEntries = 20)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(5);
        _maxEntries = maxEntries > 0 ? maxEntries : 20;
    }

    /// <summary>
    /// 获取缓存的定位结果
    /// </summary>
    /// <param name="imageHash">图片 SHA256 哈希</param>
    /// <param name="taskDescription">任务描述</param>
    /// <returns>缓存的结果，未命中或已过期返回 null</returns>
    public LlmGroundingResult? Get(string imageHash, string taskDescription)
    {
        if (string.IsNullOrEmpty(imageHash)) return null;

        var key = BuildKey(imageHash, taskDescription);

        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired(_ttl))
            {
                _cache.TryRemove(key, out _);
                PekaLogger.Debug(LogTag, $"Cache expired: {key[..16]}...");
                return null;
            }

            PekaLogger.Debug(LogTag, $"Cache hit: {key[..16]}...");
            return entry.Result;
        }

        return null;
    }

    /// <summary>
    /// 写入定位结果到缓存
    /// </summary>
    /// <param name="imageHash">图片 SHA256 哈希</param>
    /// <param name="taskDescription">任务描述</param>
    /// <param name="result">要缓存的定位结果</param>
    public void Set(string imageHash, string taskDescription, LlmGroundingResult result)
    {
        if (string.IsNullOrEmpty(imageHash) || result is null) return;

        var key = BuildKey(imageHash, taskDescription);

        // 容量控制：超出上限时淘汰最旧条目
        if (_cache.Count >= _maxEntries)
        {
            EvictOldest();
        }

        var entry = new CacheEntry(result, DateTime.UtcNow);
        _cache[key] = entry;

        PekaLogger.Debug(LogTag, $"Cache set: {key[..Math.Min(16, key.Length)]}... (total: {_cache.Count})");
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public void Invalidate()
    {
        var count = _cache.Count;
        _cache.Clear();
        PekaLogger.Debug(LogTag, $"Cache invalidated: {count} entries removed");
    }

    /// <summary>
    /// 计算图片字节的 SHA256 哈希
    /// </summary>
    /// <param name="imageBytes">图片字节数组</param>
    /// <returns>十六进制哈希字符串</returns>
    public static string ComputeHash(byte[] imageBytes)
    {
        if (imageBytes is not { Length: > 0 })
            return string.Empty;

        var hashBytes = SHA256.HashData(imageBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 当前缓存条目数
    /// </summary>
    public int Count => _cache.Count;

    #region Internal

    private static string BuildKey(string imageHash, string taskDescription)
    {
        // 格式: hash:task（task 为空时用空串）
        return $"{imageHash}:{taskDescription ?? ""}";
    }

    /// <summary>
    /// 淘汰最旧的缓存条目
    /// </summary>
    private void EvictOldest()
    {
        string? oldestKey = null;
        var oldestTime = DateTime.MaxValue;

        foreach (var kvp in _cache)
        {
            if (kvp.Value.CreatedAt < oldestTime)
            {
                oldestTime = kvp.Value.CreatedAt;
                oldestKey = kvp.Key;
            }
        }

        if (oldestKey is not null)
        {
            _cache.TryRemove(oldestKey, out _);
            PekaLogger.Debug(LogTag, $"Evicted oldest entry: {oldestKey[..Math.Min(16, oldestKey.Length)]}...");
        }
    }

    private sealed record CacheEntry(LlmGroundingResult Result, DateTime CreatedAt)
    {
        public bool IsExpired(TimeSpan ttl)
        {
            return DateTime.UtcNow - CreatedAt > ttl;
        }
    }

    #endregion
}
