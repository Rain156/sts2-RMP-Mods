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

	private const int VanillaMultiplayerHolderCount = 4;

	private const int VanillaSlotIdBits = 2;

	private const int VanillaLobbyListLengthBits = 3;

	private const string ModFolderName = "RemoveMultiplayerPlayerLimit";

	private const string ConfigFileName = "config.json";

	private static int TargetPlayerLimit { get; set; } = DefaultPlayerLimit;

	private static int SlotIdBits { get; set; } = RequiredBitsForExclusiveUpperBound(DefaultPlayerLimit);

	private static int LobbyListLengthBits { get; set; } = RequiredBitsForExclusiveUpperBound(DefaultPlayerLimit + 1);

	private static readonly FieldInfo? MaxPlayersField = AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby), "<MaxPlayers>k__BackingField");

	public static void Initialize()
	{
		TargetPlayerLimit = LoadOrCreatePlayerLimit();
		SlotIdBits = RequiredBitsForExclusiveUpperBound(TargetPlayerLimit);
		LobbyListLengthBits = RequiredBitsForExclusiveUpperBound(TargetPlayerLimit + 1);
		int slotIdCapacity = 1 << SlotIdBits;
		int lobbyListLengthCapacity = 1 << LobbyListLengthBits;
		new Harmony("cn.remove.multiplayer.playerlimit").PatchAll();
		Log.Info($"RemoveMultiplayerPlayerLimit loaded. Target limit: {TargetPlayerLimit}, slot bits: {SlotIdBits}, slot capacity: {slotIdCapacity}, lobby bits: {LobbyListLengthBits}, lobby list capacity: {lobbyListLengthCapacity}");
	}

	private static int LoadOrCreatePlayerLimit()
	{
		string modDirectory = ResolveModDirectory();
		Directory.CreateDirectory(modDirectory);
		string configPath = Path.Combine(modDirectory, ConfigFileName);
		if (!File.Exists(configPath))
		{
			WriteDefaultConfig(configPath, DefaultPlayerLimit);
			return DefaultPlayerLimit;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(configPath));
			if (jsonDocument.RootElement.TryGetProperty("max_player_limit", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int rawLimit))
			{
				int clampedLimit = Math.Clamp(rawLimit, MinSupportedPlayerLimit, MaxSupportedPlayerLimit);
				if (clampedLimit != rawLimit)
				{
					WriteDefaultConfig(configPath, clampedLimit);
				}
				return clampedLimit;
			}
			WriteDefaultConfig(configPath, DefaultPlayerLimit);
			return DefaultPlayerLimit;
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to parse config at {configPath}: {ex.Message}");
			BackupCorruptedConfig(configPath);
		}
		WriteDefaultConfig(configPath, DefaultPlayerLimit);
		return DefaultPlayerLimit;
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

	private static void WriteDefaultConfig(string configPath, int playerLimit)
	{
		// min_supported / max_supported are informational fields for users and are not parsed.
		string contents = JsonSerializer.Serialize(new Dictionary<string, int>
		{
			["max_player_limit"] = playerLimit,
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
}
