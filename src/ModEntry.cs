using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace RemoveMultiplayerPlayerLimit;

[ModInitializer("Initialize")]
public static partial class ModEntry
{
	private const int DefaultPlayerLimit = 8;

	private const int MinSupportedPlayerLimit = 4;

	private const int MaxSupportedPlayerLimit = 16;

	private const bool DefaultMacOsTlsWorkaroundEnabled = true;

	private const int VanillaMultiplayerHolderCount = 4;

	private const int VanillaSlotIdBits = 2;

	private const int VanillaLobbyListLengthBits = 3;

	private const string ModFolderName = "RemoveMultiplayerPlayerLimit";

	private const string ConfigFileName = "config.json";

	private static int TargetPlayerLimit { get; set; } = DefaultPlayerLimit;

	private static int SlotIdBits { get; set; } = RequiredBitsForExclusiveUpperBound(DefaultPlayerLimit);

	private static int LobbyListLengthBits { get; set; } = RequiredBitsForExclusiveUpperBound(DefaultPlayerLimit + 1);

	private static bool MacOsTlsWorkaroundEnabled { get; set; } = DefaultMacOsTlsWorkaroundEnabled;

	private static readonly FieldInfo? MaxPlayersField = AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby), "<MaxPlayers>k__BackingField");

	public static void Initialize()
	{
		ModConfig config = LoadOrCreateConfig();
		TargetPlayerLimit = config.MaxPlayerLimit;
		MacOsTlsWorkaroundEnabled = config.MacOsTlsWorkaroundEnabled;
		SlotIdBits = RequiredBitsForExclusiveUpperBound(TargetPlayerLimit);
		LobbyListLengthBits = RequiredBitsForExclusiveUpperBound(TargetPlayerLimit + 1);
		int slotIdCapacity = 1 << SlotIdBits;
		int lobbyListLengthCapacity = 1 << LobbyListLengthBits;
		new Harmony("cn.remove.multiplayer.playerlimit").PatchAll();
		Log.Info($"RemoveMultiplayerPlayerLimit loaded. Target limit: {TargetPlayerLimit}, slot bits: {SlotIdBits}, slot capacity: {slotIdCapacity}, lobby bits: {LobbyListLengthBits}, lobby list capacity: {lobbyListLengthCapacity}, macOS TLS workaround: {MacOsTlsWorkaroundEnabled}");
	}

	private static ModConfig LoadOrCreateConfig()
	{
		string modDirectory = ResolveModDirectory();
		Directory.CreateDirectory(modDirectory);
		string configPath = Path.Combine(modDirectory, ConfigFileName);
		if (!File.Exists(configPath))
		{
			ModConfig defaultConfig = new ModConfig(DefaultPlayerLimit, DefaultMacOsTlsWorkaroundEnabled);
			WriteDefaultConfig(configPath, defaultConfig);
			return defaultConfig;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(configPath));
			bool needsRewrite = false;
			int playerLimit = DefaultPlayerLimit;
			if (jsonDocument.RootElement.TryGetProperty("max_player_limit", out JsonElement limitElement) && limitElement.ValueKind == JsonValueKind.Number && limitElement.TryGetInt32(out int rawLimit))
			{
				playerLimit = Math.Clamp(rawLimit, MinSupportedPlayerLimit, MaxSupportedPlayerLimit);
				needsRewrite = playerLimit != rawLimit;
			}
			else
			{
				needsRewrite = true;
			}
			bool macOsTlsWorkaroundEnabled = DefaultMacOsTlsWorkaroundEnabled;
			if (jsonDocument.RootElement.TryGetProperty("macos_tls_workaround", out JsonElement tlsElement))
			{
				if (tlsElement.ValueKind == JsonValueKind.True)
				{
					macOsTlsWorkaroundEnabled = true;
				}
				else if (tlsElement.ValueKind == JsonValueKind.False)
				{
					macOsTlsWorkaroundEnabled = false;
				}
				else
				{
					needsRewrite = true;
				}
			}
			else
			{
				needsRewrite = true;
			}
			ModConfig config = new ModConfig(playerLimit, macOsTlsWorkaroundEnabled);
			if (needsRewrite)
			{
				WriteDefaultConfig(configPath, config);
			}
			return config;
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to parse config at {configPath}: {ex.Message}");
			BackupCorruptedConfig(configPath);
		}
		ModConfig fallbackConfig = new ModConfig(DefaultPlayerLimit, DefaultMacOsTlsWorkaroundEnabled);
		WriteDefaultConfig(configPath, fallbackConfig);
		return fallbackConfig;
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

	private static void WriteDefaultConfig(string configPath, ModConfig config)
	{
		// min_supported / max_supported are informational fields for users and are not parsed.
		string contents = JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["max_player_limit"] = config.MaxPlayerLimit,
			["macos_tls_workaround"] = config.MacOsTlsWorkaroundEnabled,
			["min_supported"] = MinSupportedPlayerLimit,
			["max_supported"] = MaxSupportedPlayerLimit
		}, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		File.WriteAllText(configPath, contents);
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

	private static int RequiredBitsForExclusiveUpperBound(int upperBound)
	{
		int normalizedBound = Math.Max(1, upperBound);
		int bitCount = 0;
		int capacity = 1;
		while (capacity < normalizedBound)
		{
			bitCount++;
			capacity <<= 1;
		}
		return Math.Max(1, bitCount);
	}

	private static int EnsureMin(int value, int min) => Math.Max(value, min);

	private static bool TryGetCharacter(NRestSiteRoom room, ulong playerId, out NRestSiteCharacter character)
	{
		NRestSiteCharacter? nRestSiteCharacter = room.Characters.FirstOrDefault((NRestSiteCharacter c) => c.Player.NetId == playerId);
		if (nRestSiteCharacter == null)
		{
			character = null!;
			return false;
		}
		character = nRestSiteCharacter;
		return true;
	}

	private static RestSiteOption? TryGetHoveredOption(ulong playerId)
	{
		int? hoveredOptionIndex = MegaCrit.Sts2.Core.Runs.RunManager.Instance.RestSiteSynchronizer.GetHoveredOptionIndex(playerId);
		if (!hoveredOptionIndex.HasValue)
		{
			return null;
		}
		IReadOnlyList<RestSiteOption> optionsForPlayer = MegaCrit.Sts2.Core.Runs.RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);
		int value = hoveredOptionIndex.Value;
		if ((uint)value >= (uint)optionsForPlayer.Count)
		{
			return null;
		}
		return optionsForPlayer[value];
	}

	private static bool IsRemote(NRestSiteCharacter character) => !LocalContext.IsMe(character.Player);

	private sealed record ModConfig(int MaxPlayerLimit, bool MacOsTlsWorkaroundEnabled);
}
