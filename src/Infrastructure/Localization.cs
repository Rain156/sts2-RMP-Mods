using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

/// <summary>
/// Localization helper — loads translations from .json files in the mod's PCK.
/// Falls back to English, then to the provided fallback text.
/// </summary>
public static class Localization
{
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new();

    public static string Get(string key, string fallback)
    {
        string lang = GetLanguageCode();
        if (TryGet(lang, key, out string value)) return value;
        if (lang != "en_us" && TryGet("en_us", key, out value)) return value;
        return fallback;
    }

    private static string GetLanguageCode()
    {
        string language = LocManager.Instance?.Language ?? "eng";
        return string.Equals(language, "zhs", StringComparison.OrdinalIgnoreCase)
            ? "zh_cn" : "en_us";
    }

    private static bool TryGet(string langCode, string key, out string value)
    {
        var table = GetTable(langCode);
        if (table.TryGetValue(key, out string? result) && result != null)
        {
            value = result;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static Dictionary<string, string> GetTable(string langCode)
    {
        if (Cache.TryGetValue(langCode, out var cached)) return cached;

        string path = $"res://RemoveMultiplayerPlayerLimit/localization/{langCode}.json";
        Dictionary<string, string> table = new();
        try
        {
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText());
                if (parsed != null) table = parsed;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[RMP] Failed to load localization: {path}. {ex.Message}");
        }
        Cache[langCode] = table;
        return table;
    }
}
