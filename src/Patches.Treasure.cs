using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private const float FallbackRelicHolderXStep = 220f;

	private const float MinRelicHolderXStep = 190f;

	private const float MinRelicHolderYStep = 120f;

	private static readonly Dictionary<string, Dictionary<string, string>> LocalizationCache = new Dictionary<string, Dictionary<string, string>>();

	private static readonly FieldInfo? HoldersInUseField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holdersInUse");

	private static readonly FieldInfo? MultiplayerHoldersField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_multiplayerHolders");

	private static readonly FieldInfo? RunStateField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_runState");

	private static readonly FieldInfo? SyncCurrentRelicsField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics");

	private static readonly FieldInfo? VotesChangedEventField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "VotesChanged");

	//  Holder 扩展 & 布局 

	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
	private static class NTreasureRoomRelicCollectionInitializePatch
	{
		private static void Prefix(NTreasureRoomRelicCollection __instance)
		{
			List<NTreasureRoomRelicHolder>? holdersInUse = GetHoldersInUse(__instance);
			holdersInUse?.Clear();
			List<NTreasureRoomRelicHolder>? multiplayerHolders = GetMultiplayerHolders(__instance);
			if (multiplayerHolders != null && multiplayerHolders.Count > VanillaMultiplayerHolderCount)
			{
				for (int i = multiplayerHolders.Count - 1; i >= VanillaMultiplayerHolderCount; i--)
				{
					NTreasureRoomRelicHolder holder = multiplayerHolders[i];
					multiplayerHolders.RemoveAt(i);
					holder.QueueFree();
				}
			}
			IReadOnlyList<RelicModel>? currentRelics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
			if (multiplayerHolders == null || currentRelics == null || currentRelics.Count <= multiplayerHolders.Count || multiplayerHolders.Count == 0)
			{
				return;
			}
			NTreasureRoomRelicHolder template = multiplayerHolders[multiplayerHolders.Count - 1];
			string scenePath = template.SceneFilePath;
			PackedScene? scene = null;
			if (!string.IsNullOrEmpty(scenePath))
			{
				scene = PreloadManager.Cache.GetScene(scenePath);
			}
			Node parent = template.GetParent();
			for (int i = multiplayerHolders.Count; i < currentRelics.Count; i++)
			{
				NTreasureRoomRelicHolder? newHolder = null;
				if (scene != null)
				{
					newHolder = scene.Instantiate<NTreasureRoomRelicHolder>();
				}
				else if (template.Duplicate() is NTreasureRoomRelicHolder duplicated)
				{
					newHolder = duplicated;
				}
				if (newHolder == null)
				{
					continue;
				}
				newHolder.Name = $"AutoHolder_{i + 1}";
				newHolder.Visible = false;
				parent.AddChild(newHolder);
				multiplayerHolders.Add(newHolder);
			}
		}

		private static void Postfix(NTreasureRoomRelicCollection __instance)
		{
			List<NTreasureRoomRelicHolder>? holdersInUse = GetHoldersInUse(__instance);
			if (holdersInUse == null || holdersInUse.Count <= VanillaMultiplayerHolderCount)
			{
				return;
			}
			float minX = float.MaxValue;
			float maxX = float.MinValue;
			float topY = float.MaxValue;
			float bottomY = float.MinValue;
			for (int i = 0; i < VanillaMultiplayerHolderCount; i++)
			{
				Vector2 position = holdersInUse[i].Position;
				minX = Math.Min(minX, position.X);
				maxX = Math.Max(maxX, position.X);
				topY = Math.Min(topY, position.Y);
				bottomY = Math.Max(bottomY, position.Y);
			}
			int holderCount = holdersInUse.Count;
			int maxColumns = holderCount >= 8 ? VanillaMultiplayerHolderCount : Math.Min(VanillaMultiplayerHolderCount, holderCount);
			maxColumns = Math.Max(2, maxColumns);
			int rowCount = (int)Math.Ceiling(holderCount / (float)maxColumns);
			float centerX = (minX + maxX) * 0.5f;
			float centerY = (topY + bottomY) * 0.5f;
			float xStep = (maxX - minX) / Math.Max(1, maxColumns - 1);
			xStep = xStep > 0f ? Math.Max(MinRelicHolderXStep, xStep) : FallbackRelicHolderXStep;
			float yStep = Math.Max(MinRelicHolderYStep, Math.Abs(bottomY - topY));
			int startIndex = 0;
			for (int i = 0; i < rowCount; i++)
			{
				int count = Math.Min(maxColumns, holderCount - startIndex);
				float y = centerY + (i - (rowCount - 1) * 0.5f) * yStep;
				LayoutRow(holdersInUse, startIndex, count, y, centerX, xStep);
				startIndex += count;
			}
		}

		private static void LayoutRow(List<NTreasureRoomRelicHolder> holders, int startIndex, int count, float y, float centerX, float xStep)
		{
			float startX = centerX - (count - 1) * xStep * 0.5f;
			for (int i = 0; i < count; i++)
			{
				holders[startIndex + i].Position = new Vector2(startX + i * xStep, y);
			}
		}
	}

	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), "get_DefaultFocusedControl")]
	private static class NTreasureRoomRelicCollectionDefaultFocusPatch
	{
		private static bool Prefix(NTreasureRoomRelicCollection __instance, ref Control __result)
		{
			List<NTreasureRoomRelicHolder>? holdersInUse = GetHoldersInUse(__instance);
			if (holdersInUse == null || holdersInUse.Count == 0)
			{
				return true;
			}
			IRunState? runState = GetRunState(__instance);
			int playerSlotIndex = 0;
			Player? me = runState != null ? LocalContext.GetMe(runState.Players) : null;
			if (me != null && runState != null)
			{
				playerSlotIndex = runState.GetPlayerSlotIndex(me);
			}
			playerSlotIndex = Math.Clamp(playerSlotIndex, 0, holdersInUse.Count - 1);
			__result = holdersInUse[playerSlotIndex];
			return false;
		}
	}

	//  遗物池空池兜底（草莓） 

	[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
	private static class TreasureRoomRelicSynchronizerBeginStrawberryPatch
	{
		private static void Postfix(TreasureRoomRelicSynchronizer __instance)
		{
			List<RelicModel>? currentRelics = GetSyncCurrentRelics(__instance);
			if (currentRelics != null)
			{
				bool hasChanges = false;
				for (int i = 0; i < currentRelics.Count; i++)
				{
					if (currentRelics[i] == null)
					{
						RelicModel? strawberry = ModelDb.Relic<Strawberry>();
						if (strawberry != null)
						{
							currentRelics[i] = strawberry;
							hasChanges = true;
						}
					}
				}
				if (hasChanges)
				{
					InvokeVotesChanged(__instance);
				}
			}
		}
	}

	//  辅助方法 

	private static List<NTreasureRoomRelicHolder>? GetHoldersInUse(NTreasureRoomRelicCollection collection)
	=> HoldersInUseField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;

	private static List<NTreasureRoomRelicHolder>? GetMultiplayerHolders(NTreasureRoomRelicCollection collection)
	=> MultiplayerHoldersField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;

	private static IRunState? GetRunState(NTreasureRoomRelicCollection collection)
	=> RunStateField?.GetValue(collection) as IRunState;

	private static List<RelicModel>? GetSyncCurrentRelics(TreasureRoomRelicSynchronizer synchronizer)
	=> SyncCurrentRelicsField?.GetValue(synchronizer) as List<RelicModel>;

	private static void InvokeVotesChanged(TreasureRoomRelicSynchronizer synchronizer)
	{
		if (VotesChangedEventField?.GetValue(synchronizer) is Action action)
		{
			action();
		}
	}

	//  本地化 

	private static string GetLocalizedText(string key, string fallbackText)
	{
		string languageCode = GetLanguageCode();
		if (TryGetLocValue(languageCode, key, out string value))
		{
			return value;
		}
		if (languageCode != "en_us" && TryGetLocValue("en_us", key, out value))
		{
			return value;
		}
		return fallbackText;
	}

	private static string GetLanguageCode()
	{
		string language = LocManager.Instance?.Language ?? "eng";
		if (string.Equals(language, "zhs", StringComparison.OrdinalIgnoreCase))
		{
			return "zh_cn";
		}
		return "en_us";
	}

	private static bool TryGetLocValue(string languageCode, string key, out string value)
	{
		Dictionary<string, string> table = GetLocalizationTable(languageCode);
		if (table.TryGetValue(key, out string? result) && result != null)
		{
			value = result;
			return true;
		}
		value = string.Empty;
		return false;
	}

	private static Dictionary<string, string> GetLocalizationTable(string languageCode)
	{
		if (LocalizationCache.TryGetValue(languageCode, out Dictionary<string, string>? cached))
		{
			return cached;
		}
		string filePath = $"res://RemoveMultiplayerPlayerLimit/localization/{languageCode}.json";
		Dictionary<string, string> table = new Dictionary<string, string>();
		try
		{
			using FileAccess file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
			if (file != null)
			{
				Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText());
				if (parsed != null)
				{
					table = parsed;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to load localization file: {filePath}. {ex.Message}");
		}
		LocalizationCache[languageCode] = table;
		return table;
	}
}