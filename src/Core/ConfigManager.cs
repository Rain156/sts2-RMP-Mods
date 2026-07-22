using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace RemoveMultiplayerPlayerLimit.Core;

/// <summary>
/// Manages config.ini read/write for mod settings.
/// IMPORTANT: Uses .ini format — game scans .json as manifests.
///
/// v0.1.7: player limit is fixed at 16 and no longer configurable;
/// config.ini only stores the macOS TLS workaround and the difficulty
/// scaling toggle.
/// </summary>
public class ConfigManager
{
    private const string ModFolderName = "RemoveMultiplayerPlayerLimit";
    private const string ConfigFileName = "config.ini";
    private const string LegacyConfigFileName = "config.json";
    private const bool DefaultMacOsTlsWorkaround = true;

    public static ConfigManager? Instance { get; private set; }

    public bool DifficultyScaling => ProtocolConfig.DifficultyScalingEnabled;
    public bool MacOsTlsWorkaround { get; set; } = DefaultMacOsTlsWorkaround;

    private string? _configPath;

    public ConfigManager()
    {
        Instance = this;
        LoadOrCreateConfig();
    }

    // ── Public API ────────────────────────────────────────────────────

    public void SetDifficultyScaling(bool value)
    {
        ProtocolConfig.SetDifficultyScalingEnabled(value);
        Save();
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(_configPath)) return;
        try
        {
            using var writer = new StreamWriter(_configPath, false);
            writer.WriteLine("[macos]");
            writer.WriteLine($"tls_workaround={MacOsTlsWorkaround.ToString().ToLowerInvariant()}");
            writer.WriteLine();
            writer.WriteLine("[multiplayer]");
            writer.WriteLine($"difficulty_scaling={ProtocolConfig.DifficultyScalingEnabled.ToString().ToLowerInvariant()}");
        }
        catch (Exception ex)
        {
            Log.Warn($"[RMP] Failed to save config: {ex.Message}");
        }
    }

    // ── Private ───────────────────────────────────────────────────────

    private void LoadOrCreateConfig()
    {
        string modDir = ResolveModDirectory();
        Directory.CreateDirectory(modDir);
        _configPath = Path.Combine(modDir, ConfigFileName);

        string legacyPath = Path.Combine(modDir, LegacyConfigFileName);
        if (File.Exists(legacyPath) && !File.Exists(_configPath))
            MigrateLegacyJsonConfig(legacyPath);

        if (File.Exists(_configPath))
        {
            try
            {
                ParseIniConfig(_configPath);
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP] Failed to parse config: {ex.Message}");
                BackupCorruptedConfig(_configPath);
            }
        }
        Save();
    }

    private void ParseIniConfig(string path)
    {
        string currentSection = "";
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1].Trim();
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            switch (currentSection)
            {
                case "macos" when key == "tls_workaround":
                    MacOsTlsWorkaround = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "multiplayer" when key == "difficulty_scaling":
                    ProtocolConfig.SetDifficultyScalingEnabled(
                        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
                    break;
                // max_player_limit is intentionally ignored (v0.1.7: fixed at 16).
            }
        }
    }

    private void MigrateLegacyJsonConfig(string jsonPath)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("macos_tls_workaround", out JsonElement tlsEl))
                MacOsTlsWorkaround = tlsEl.ValueKind == JsonValueKind.True;
            Save();
            File.Delete(jsonPath);
            Log.Info("[RMP] Migrated config.json to config.ini");
        }
        catch (Exception ex)
        {
            Log.Warn($"[RMP] Failed to migrate legacy config: {ex.Message}");
        }
    }

    private static string ResolveModDirectory()
    {
        string? asmLocation = Assembly.GetExecutingAssembly().Location;
        string? asmDir = string.IsNullOrWhiteSpace(asmLocation)
            ? null : Path.GetDirectoryName(asmLocation);
        if (!string.IsNullOrWhiteSpace(asmDir) && Directory.Exists(asmDir))
            return asmDir;

        string fallback = Path.Combine(AppContext.BaseDirectory, "mods", ModFolderName);
        if (Directory.Exists(fallback)) return fallback;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StS2Mods", ModFolderName);
    }

    private static void BackupCorruptedConfig(string configPath)
    {
        if (!File.Exists(configPath)) return;
        string backup = $"{configPath}.bak";
        if (File.Exists(backup))
            backup = $"{configPath}.{DateTime.Now:yyyyMMddHHmmss}.bak";
        File.Move(configPath, backup);
    }
}
