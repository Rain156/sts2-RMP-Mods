using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace RemoveMultiplayerPlayerLimit;

[ModInitializer("Initialize")]
public static partial class ModEntry
{
	private const int DefaultPlayerLimit = 8;

	private const int MinSupportedPlayerLimit = 4;

	private const int MaxSupportedPlayerLimit = 16;

	private const int ProtocolSlotIdBits = 4;

	private const int ProtocolLobbyListLengthBits = 5;

	private const bool DefaultMacOsTlsWorkaroundEnabled = true;

	private const int VanillaMultiplayerHolderCount = 4;

	private const int VanillaSlotIdBits = 2;

	private const int VanillaLobbyListLengthBits = 3;

	private const string ModFolderName = "RemoveMultiplayerPlayerLimit";

	private const string ConfigFileName = "config.ini";

	private const string LegacyConfigFileName = "config.json";

	private static int TargetPlayerLimit { get; set; } = DefaultPlayerLimit;

	private static int SlotIdBits { get; set; } = ProtocolSlotIdBits;

	private static int LobbyListLengthBits { get; set; } = ProtocolLobbyListLengthBits;

	private static bool MacOsTlsWorkaroundEnabled { get; set; } = DefaultMacOsTlsWorkaroundEnabled;

	private static string? ConfigFilePath { get; set; }

	private static readonly FieldInfo? MaxPlayersField = AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby), "<MaxPlayers>k__BackingField");

	public static void Initialize()
	{
		LoadOrCreateConfig();
		EnsureLinuxHarmonyDependenciesLoaded();
		int slotIdCapacity = 1 << SlotIdBits;
		int lobbyListLengthCapacity = 1 << LobbyListLengthBits;
		new Harmony("cn.remove.multiplayer.playerlimit").PatchAll();
		Log.Info($"RemoveMultiplayerPlayerLimit loaded. Target limit: {TargetPlayerLimit}, protocol slot bits: {SlotIdBits}, slot capacity: {slotIdCapacity}, protocol lobby bits: {LobbyListLengthBits}, lobby list capacity: {lobbyListLengthCapacity}, macOS TLS workaround: {MacOsTlsWorkaroundEnabled}");
	}

	private static void LoadOrCreateConfig()
	{
		string modDirectory = ResolveModDirectory();
		Directory.CreateDirectory(modDirectory);
		ConfigFilePath = Path.Combine(modDirectory, ConfigFileName);
		string legacyPath = Path.Combine(modDirectory, LegacyConfigFileName);
		if (File.Exists(legacyPath) && !File.Exists(ConfigFilePath))
		{
			MigrateLegacyJsonConfig(legacyPath);
		}
		if (File.Exists(ConfigFilePath))
		{
			try
			{
				ParseIniConfig(ConfigFilePath);
				return;
			}
			catch (Exception ex)
			{
				Log.Warn($"Failed to parse config at {ConfigFilePath}: {ex.Message}");
				BackupCorruptedConfig(ConfigFilePath);
			}
		}
		SaveModConfig();
	}

	private static void ParseIniConfig(string path)
	{
		string currentSection = "";
		foreach (string rawLine in File.ReadAllLines(path))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line[0] == ';' || line[0] == '#')
			{
				continue;
			}
			if (line[0] == '[' && line[^1] == ']')
			{
				currentSection = line[1..^1].Trim();
				continue;
			}
			int eq = line.IndexOf('=');
			if (eq < 0)
			{
				continue;
			}
			string key = line[..eq].Trim();
			string value = line[(eq + 1)..].Trim();
			switch (currentSection)
			{
				case "macos" when key == "tls_workaround":
					MacOsTlsWorkaroundEnabled = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
					break;
				case "multiplayer" when key == "max_player_limit" && int.TryParse(value, out int rawLimit):
					TargetPlayerLimit = Math.Clamp(rawLimit, MinSupportedPlayerLimit, MaxSupportedPlayerLimit);
					break;
			}
		}
	}

	internal static void SaveModConfig()
	{
		if (string.IsNullOrEmpty(ConfigFilePath))
		{
			return;
		}
		try
		{
			using var writer = new StreamWriter(ConfigFilePath, false);
			writer.WriteLine("[macos]");
			writer.WriteLine($"tls_workaround={MacOsTlsWorkaroundEnabled.ToString().ToLowerInvariant()}");
			writer.WriteLine();
			writer.WriteLine("[multiplayer]");
			writer.WriteLine($"max_player_limit={TargetPlayerLimit}");
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to save config: {ex.Message}");
		}
	}

	private static void MigrateLegacyJsonConfig(string jsonPath)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
			if (doc.RootElement.TryGetProperty("max_player_limit", out JsonElement limitEl) && limitEl.TryGetInt32(out int raw))
			{
				TargetPlayerLimit = Math.Clamp(raw, MinSupportedPlayerLimit, MaxSupportedPlayerLimit);
			}
			if (doc.RootElement.TryGetProperty("macos_tls_workaround", out JsonElement tlsEl))
			{
				MacOsTlsWorkaroundEnabled = tlsEl.ValueKind == JsonValueKind.True;
			}
			SaveModConfig();
			File.Delete(jsonPath);
			Log.Info("Migrated config.json to config.ini");
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to migrate legacy config: {ex.Message}");
		}
	}

	private static string ResolveModDirectory()
	{
		string? assemblyLocation = Assembly.GetExecutingAssembly().Location;
		string? assemblyDirectory = string.IsNullOrWhiteSpace(assemblyLocation) ? null : Path.GetDirectoryName(assemblyLocation);
		if (!string.IsNullOrWhiteSpace(assemblyDirectory) && Directory.Exists(assemblyDirectory))
		{
			return assemblyDirectory;
		}
		string fallbackModDirectory = Path.Combine(AppContext.BaseDirectory, "mods", ModFolderName);
		if (Directory.Exists(fallbackModDirectory))
		{
			return fallbackModDirectory;
		}
		string appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appDataRoot, "StS2Mods", ModFolderName);
	}

	private static void BackupCorruptedConfig(string configPath)
	{
		if (!File.Exists(configPath))
		{
			return;
		}
		string backupPath = $"{configPath}.bak";
		if (File.Exists(backupPath))
		{
			backupPath = $"{configPath}.{DateTime.Now:yyyyMMddHHmmss}.bak";
		}
		File.Move(configPath, backupPath);
	}
}
