using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.src;
using RemoveMultiplayerPlayetLimit.src;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RemoveMultiplayerPlayerLimit;

[ModInitializer("Initialize")]
public static partial class ModEntry
{
	public static Option Option { get; set; }

	public static Harmony Harmony { get; set; } = new("Rain156.RemoveMultiplayerPlayerLimit");

    internal const int DefaultPlayerLimit = 8;

    internal const int MinSupportedPlayerLimit = 4;

    internal const int MaxSupportedPlayerLimit = 16;

    internal const int VanillaSlotIdBits = 2;

    internal const int VanillaLobbyListLengthBits = 3;

    internal const string ModFolderName = "RemoveMultiplayerPlayerLimit";

    internal const string ConfigFileName = "config.json";

    private static int SlotIdBits { get; set; }

    private static int LobbyListLengthBits { get; set; }

	private static int SlotIdCapacity { get; set; }

	private static int LobbyListLengthCapacity { get; set; }

	public static void Initialize()
	{
		try
		{
            LoadOptions();

            SlotIdBits = RequiredBitsForExclusiveUpperBound(Option.PlayerLimit);

            LobbyListLengthBits = RequiredBitsForExclusiveUpperBound(Option.PlayerLimit + 1);

            SlotIdCapacity = 1 << SlotIdBits;

            LobbyListLengthCapacity = 1 << LobbyListLengthBits;

            Harmony.PatchAll();

            Log.Info($"RemoveMultiplayerPlayerLimit loaded. Target limit: {Option.PlayerLimit}, slot capacity: {SlotIdCapacity}, lobby list capacity: {LobbyListLengthCapacity}");
        }
		catch (Exception e)
		{
            File.AppendAllText(Path.Combine(Pathes.RootPath, "logs.txt"), e.Message + e.StackTrace);
        }
	}

	private static void LoadOptions()
	{
		string configPath = Pathes.ConfigPath;

		if (!File.Exists(configPath))
			WriteDefaultConfig(configPath, DefaultPlayerLimit);

		try
		{
			Option = JsonSerializer.Deserialize<Option>(File.ReadAllText(configPath));

            var clampedLimit = Math.Clamp(Option.PlayerLimit, MinSupportedPlayerLimit, MaxSupportedPlayerLimit);

            if (clampedLimit != Option.PlayerLimit)
			{
				Option.PlayerLimit = clampedLimit;

                WriteDefaultConfig(configPath, clampedLimit);
            }
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to parse config at {configPath}: {ex.Message}");

			BackupCorruptedConfig(configPath);
		}
	}

	private static JsonSerializerOptions _defaultOption = new() { WriteIndented = true };

	private static void WriteDefaultConfig(string configPath, int playerLimit)
	{
        // min_player / max_player are informational fields for users and are not parsed.
        string contents = JsonSerializer.Serialize(new Dictionary<string, int>
		{
			["player_limit"] = playerLimit,
            ["min_player"] = MinSupportedPlayerLimit,
            ["max_player"] = MaxSupportedPlayerLimit
        }, _defaultOption);

		File.WriteAllText(configPath, contents);
	}

	private static void BackupCorruptedConfig(string configPath)
	{
		if (!File.Exists(configPath))
			return;

		string backupPath = $"{configPath}.bak";

		if (File.Exists(backupPath))
			backupPath = $"{configPath}.{DateTime.Now:yyyyMMddHHmmss}.bak";

		File.Move(configPath, backupPath);
	}

	private static int RequiredBitsForExclusiveUpperBound(int upperBound)
	{
		upperBound = Math.Max(1, upperBound);

		return Math.Max(1, (int)Math.Ceiling(Math.Log2(upperBound)));
	}

	private static bool TryGetCharacter(NRestSiteRoom room, ulong playerId, out NRestSiteCharacter character)
	{
		character = room.Characters.FirstOrDefault(c => c.Player.NetId == playerId, null);

		return character != null;
	}

	private static RestSiteOption GetHoveredOption(ulong playerId)
	{
		var hoveredOptionIndex = RunManager.Instance.RestSiteSynchronizer.GetHoveredOptionIndex(playerId);

        if (!hoveredOptionIndex.HasValue)
			return null;

		var optionsForPlayer = RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);

		int value = hoveredOptionIndex.Value;

		if (value >= optionsForPlayer.Count)
			return null;

		return optionsForPlayer[value];
	}

	private static bool IsRemote(NRestSiteCharacter character) => !LocalContext.IsMe(character.Player);
}
