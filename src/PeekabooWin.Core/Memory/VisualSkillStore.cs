using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Persistent store for Visual Skills extracted from VACP traces.
/// V0.7 Visual Skill Memory.
/// </summary>
public class VisualSkillStore
{
    private readonly string _storePath;
    private List<VisualSkill> _skills = [];

    public VisualSkillStore(string? storePath = null)
    {
        _storePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PeekabooWin", "visual_skills.json");
        EnsureDir();
        Load();
    }

    private void EnsureDir()
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public IReadOnlyList<VisualSkill> GetAll() => _skills.AsReadOnly();

    public VisualSkill? Get(string skillId) => _skills.FirstOrDefault(s => s.SkillId == skillId);

    public void Add(VisualSkill skill)
    {
        _skills.RemoveAll(s => s.SkillId == skill.SkillId);
        _skills.Add(skill);
        Save();
    }

    public void Remove(string skillId)
    {
        _skills.RemoveAll(s => s.SkillId == skillId);
        Save();
    }

    public List<VisualSkill> Search(string appPattern, string screenType, int top = 5)
    {
        return _skills
            .Where(s =>
                (string.IsNullOrEmpty(appPattern) || WildcardMatch(s.AppPattern, appPattern)) &&
                (string.IsNullOrEmpty(screenType) || s.ScreenType.Equals(screenType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(s => s.SuccessRate)
            .ThenByDescending(s => s.UsageCount)
            .Take(top)
            .ToList();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_skills, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storePath, json);
    }

    private void Load()
    {
        if (!File.Exists(_storePath)) return;
        try
        {
            var json = File.ReadAllText(_storePath);
            _skills = JsonSerializer.Deserialize<List<VisualSkill>>(json) ?? [];
        }
        catch
        {
            _skills = [];
        }
    }

    private static bool WildcardMatch(string pattern, string value)
    {
        // Simple wildcard: * matches anything
        if (pattern == "*") return true;
        if (pattern.Contains('*'))
        {
            var parts = pattern.Split('*');
            var idx = 0;
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                var found = value.IndexOf(part, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return false;
                idx = found + part.Length;
            }
            return true;
        }
        return pattern.Equals(value, StringComparison.OrdinalIgnoreCase);
    }
}