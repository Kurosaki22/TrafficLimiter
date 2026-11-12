using System.Text.Json;

namespace TrafficLimiter.Core;

public enum Priority { Low, Normal, High, Critical }

/// <summary>Regel pro Anwendung (Pfad). Limits in Bytes/s, 0 = unbegrenzt.</summary>
public record AppRule(string AppPath, long DownLimit = 0, long UpLimit = 0, bool Block = false,
    Priority Priority = Priority.Normal, bool Enabled = true);

/// <summary>Benannte Sammlung von Regeln + Gesamtlimits (z. B. "Gaming").</summary>
public record Profile(string Name, List<AppRule> Rules, long GlobalDownLimit = 0, long GlobalUpLimit = 0);

/// <summary>Language: "auto" | "de" | "en". EngineEnabled = letzter Zustand der Traffic-Kontrolle (Standard: an).</summary>
public record Settings(List<AppRule> Rules, long GlobalDownLimit = 0, long GlobalUpLimit = 0,
    string Language = "auto", bool StartMinimized = false, bool Autostart = false, bool EngineEnabled = true, bool ShowChart = true,
    List<Profile>? Profiles = null, string? ActiveProfile = null);

public static class RuleStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrafficLimiter", "settings.json");

    public static Settings Load(string path)
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new([]); }
        catch (Exception e) when (e is IOException or JsonException) { return new([]); }
    }

    public static void Save(string path, Settings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}
