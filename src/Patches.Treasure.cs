using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private const float FallbackRelicHolderXStep = 220f;

	private const float MinRelicHolderXStep = 190f;

	private const float MinRelicHolderYStep = 120f;

	private const int NetworkSkipVoteAsByte = 255;

	private static readonly Dictionary<NTreasureRoomRelicCollection, NChoiceSelectionSkipButton> TreasureSkipButtons = new Dictionary<NTreasureRoomRelicCollection, NChoiceSelectionSkipButton>();

	private static readonly Dictionary<string, Dictionary<string, string>> TreasureLocalizationCache = new Dictionary<string, Dictionary<string, string>>();

	private static readonly FieldInfo? HoldersInUseField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holdersInUse");

	private static readonly FieldInfo? MultiplayerHoldersField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_multiplayerHolders");

	private static readonly FieldInfo? RunStateField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_runState");

	private static readonly FieldInfo? SyncPlayerCollectionField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection");

	private static readonly FieldInfo? SyncLocalPlayerIdField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_localPlayerId");

	private static readonly FieldInfo? SyncActionQueueField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_actionQueueSynchronizer");

	private static readonly FieldInfo? SyncCurrentRelicsField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics");

	private static readonly FieldInfo? SyncRngField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng");

	private static readonly FieldInfo? SyncVotesField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes");

	private static readonly FieldInfo? SyncPredictedVoteField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_predictedVote");

	private static readonly FieldInfo? VotesChangedEventField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "VotesChanged");

	private static readonly FieldInfo? RelicsAwardedEventField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "RelicsAwarded");

	private static readonly MethodInfo? EndRelicVotingMethod = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "EndRelicVoting");

	private static readonly HashSet<TreasureRoomRelicSynchronizer> TreasureLocalVotePendingStates = new HashSet<TreasureRoomRelicSynchronizer>();

	private static readonly HashSet<TreasureRoomRelicSynchronizer> TreasureLocalSkipLockedStates = new HashSet<TreasureRoomRelicSynchronizer>();

	// ── Holder 扩展 & 布局 ─────────────────────────────────────────────────

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
			Node parent = template.GetParent();
			for (int i = multiplayerHolders.Count; i < currentRelics.Count; i++)
			{
				if (template.Duplicate() is not NTreasureRoomRelicHolder newHolder)
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

	// ── 跳过按钮 ──────────────────────────────────────────────────────────

	/// <summary>创建跳过按钮并挂载到遗物选择 UI。</summary>
	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), "_Ready")]
	private static class NTreasureRoomRelicCollectionReadyPatch
	{
		private static void Postfix(NTreasureRoomRelicCollection __instance)
		{
			if (TreasureSkipButtons.ContainsKey(__instance))
			{
				return;
			}
			string scenePath = SceneHelper.GetScenePath("ui/choice_selection_skip_button");
			PackedScene? scene = PreloadManager.Cache.GetScene(scenePath);
			if (scene == null)
			{
				Log.Warn($"Failed to load skip button scene: {scenePath}");
				return;
			}
			NChoiceSelectionSkipButton skipButton = scene.Instantiate<NChoiceSelectionSkipButton>(PackedScene.GenEditState.Disabled);
			skipButton.Name = "TreasureSkipButton";
			skipButton.Position = new Vector2(0f, 420f);
			MegaLabel? label = skipButton.GetNodeOrNull<MegaLabel>("Label");
			label?.SetTextAutoSize(GetTreasureLocalizedText("TREASURE_RELIC_SKIP_BUTTON", "Skip"));
			skipButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnTreasureSkipReleased));
			__instance.AddChild(skipButton);
			__instance.Connect(Control.SignalName.Resized, Callable.From(() => UpdateSkipButtonLayout(__instance)));
			TreasureSkipButtons[__instance] = skipButton;
			UpdateSkipButtonLayout(__instance);
		}
	}

	/// <summary>每次 InitializeRelics 后同步按钮可见性与启用状态。</summary>
	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
	private static class NTreasureRoomRelicCollectionInitializeSkipPatch
	{
		private static void Postfix(NTreasureRoomRelicCollection __instance)
		{
			if (!TreasureSkipButtons.TryGetValue(__instance, out NChoiceSelectionSkipButton? button))
			{
				return;
			}
			bool hasRelics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics?.Count > 0;
			button.Visible = hasRelics;
			if (hasRelics)
			{
				TreasureRoomRelicSynchronizer synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
				bool isPending = TreasureLocalVotePendingStates.Contains(synchronizer);
				bool isSkipLocked = TreasureLocalSkipLockedStates.Contains(synchronizer);
				SetSkipButtonState(button, isEnabled: !isPending && !isSkipLocked);
				UpdateSkipButtonLayout(__instance);
				button.AnimateIn();
			}
		}
	}

	/// <summary>跟随 SetSelectionEnabled 同步跳过按钮的启用状态。</summary>
	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.SetSelectionEnabled))]
	private static class NTreasureRoomRelicCollectionSetSelectionEnabledPatch
	{
		private static void Postfix(NTreasureRoomRelicCollection __instance, bool isEnabled)
		{
			if (!TreasureSkipButtons.TryGetValue(__instance, out NChoiceSelectionSkipButton? button))
			{
				return;
			}
			bool hasRelics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics?.Count > 0;
			TreasureRoomRelicSynchronizer synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
			bool isPending = TreasureLocalVotePendingStates.Contains(synchronizer);
			bool isSkipLocked = TreasureLocalSkipLockedStates.Contains(synchronizer);
			SetSkipButtonState(button, isEnabled && hasRelics && !isPending && !isSkipLocked);
		}
	}

	/// <summary>节点退出场景树时清理按钮字典，防止内存泄漏。</summary>
	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), "_ExitTree")]
	private static class NTreasureRoomRelicCollectionExitPatch
	{
		private static void Prefix(NTreasureRoomRelicCollection __instance)
		{
			TreasureSkipButtons.Remove(__instance);
		}
	}

	/// <summary>
	/// 拦截 PickRelicLocally 仅处理跳过（index == -1）。
	/// 正常遗物选择（index >= 0）放行给原版处理。
	/// </summary>
	[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), "PickRelicLocally")]
	private static class TreasureRoomRelicSynchronizerSkipPatch
	{
		private static bool Prefix(TreasureRoomRelicSynchronizer __instance, int index)
		{
			if (index != -1)
			{
				return !TreasureLocalSkipLockedStates.Contains(__instance);
			}
			if (TreasureLocalVotePendingStates.Contains(__instance))
			{
				return false;
			}
			IPlayerCollection? playerCollection = GetSyncPlayerCollection(__instance);
			ulong? localPlayerId = GetSyncLocalPlayerId(__instance);
			ActionQueueSynchronizer? actionQueue = GetSyncActionQueueSynchronizer(__instance);
			if (playerCollection == null || !localPlayerId.HasValue || actionQueue == null)
			{
				return false;
			}
			Player? player = playerCollection.GetPlayer(localPlayerId.Value) ?? LocalContext.GetMe(playerCollection.Players);
			if (player == null)
			{
				return false;
			}
			TreasureLocalVotePendingStates.Add(__instance);
			TreasureLocalSkipLockedStates.Add(__instance);
			SetSyncPredictedVote(__instance, -1);
			actionQueue.RequestEnqueue(new PickRelicAction(player, -1));
			InvokeVotesChanged(__instance);
			return false;
		}
	}

	[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
	private static class TreasureRoomRelicSynchronizerBeginSkipPatch
	{
		private static void Postfix(TreasureRoomRelicSynchronizer __instance)
		{
			TreasureLocalVotePendingStates.Remove(__instance);
			TreasureLocalSkipLockedStates.Remove(__instance);
			SetSyncPredictedVote(__instance, null);
		}
	}

	[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), "CompleteWithNoRelics")]
	private static class TreasureRoomRelicSynchronizerCompleteNoRelicsSkipPatch
	{
		private static void Prefix(TreasureRoomRelicSynchronizer __instance)
		{
			TreasureLocalVotePendingStates.Remove(__instance);
			TreasureLocalSkipLockedStates.Remove(__instance);
			SetSyncPredictedVote(__instance, null);
		}
	}

	[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
	private static class TreasureRoomRelicSynchronizerOnPickedSkipPatch
	{
		private static bool Prefix(TreasureRoomRelicSynchronizer __instance, Player player, int index)
		{
			if (index == NetworkSkipVoteAsByte)
			{
				index = -1;
			}
			List<RelicModel>? syncCurrentRelics = GetSyncCurrentRelics(__instance);
			IPlayerCollection? syncPlayerCollection = GetSyncPlayerCollection(__instance);
			List<int?>? syncVotes = GetSyncVotes(__instance);
			if (syncCurrentRelics == null || syncPlayerCollection == null || syncVotes == null)
			{
				return false;
			}
			if (index < -1 || index >= syncCurrentRelics.Count)
			{
				return false;
			}
			int playerSlotIndex = syncPlayerCollection.GetPlayerSlotIndex(player);
			if (playerSlotIndex < 0)
			{
				if (LocalContext.IsMe(player))
				{
					TreasureLocalVotePendingStates.Remove(__instance);
					SetSyncPredictedVote(__instance, null);
					InvokeVotesChanged(__instance);
				}
				return false;
			}
			while (syncVotes.Count <= playerSlotIndex)
			{
				syncVotes.Add(null);
			}
			syncVotes[playerSlotIndex] = index;
			if (LocalContext.IsMe(player))
			{
				TreasureLocalVotePendingStates.Remove(__instance);
				SetSyncPredictedVote(__instance, null);
			}
			InvokeVotesChanged(__instance);
			int expectedCount = syncPlayerCollection.Players.Count;
			bool allVoted = syncVotes.Count >= expectedCount && syncVotes.Take(expectedCount).All((int? vote) => vote.HasValue);
			if (allVoted)
			{
				if (syncVotes.Take(expectedCount).All((int? vote) => vote == -1))
				{
					__instance.CompleteWithNoRelics();
					return false;
				}
				Dictionary<int, List<Player>> playersByRelicIndex = new Dictionary<int, List<Player>>();
				for (int i = 0; i < syncCurrentRelics.Count; i++)
				{
					playersByRelicIndex[i] = new List<Player>();
				}
				for (int i = 0; i < expectedCount; i++)
				{
					if (!syncVotes[i].HasValue || syncVotes[i] == -1)
					{
						continue;
					}
					int value = syncVotes[i]!.Value;
					if (value < 0 || value >= syncCurrentRelics.Count)
					{
						Log.Warn($"Invalid vote index {value} from player slot {i}, ignoring.");
						continue;
					}
					playersByRelicIndex[value].Add(syncPlayerCollection.Players[i]);
				}
				List<RelicPickingResult> results = new List<RelicPickingResult>();
				List<RelicModel> unclaimedRelics = new List<RelicModel>();
				RelicPickingFightMove[] values = Enum.GetValues<RelicPickingFightMove>();
				Rng? syncRng = GetSyncRng(__instance);
				for (int i = 0; i < syncCurrentRelics.Count; i++)
				{
					List<Player> playersVotedForRelic = playersByRelicIndex[i];
					if (playersVotedForRelic.Count == 0)
					{
						unclaimedRelics.Add(syncCurrentRelics[i]);
					}
					else if (playersVotedForRelic.Count == 1)
					{
						results.Add(new RelicPickingResult
						{
							type = RelicPickingResultType.OnlyOnePlayerVoted,
							player = playersVotedForRelic[0],
							relic = syncCurrentRelics[i]
						});
					}
					else if (playersVotedForRelic.Count > 1)
					{
						results.Add(RelicPickingResult.GenerateRelicFight(playersVotedForRelic, syncCurrentRelics[i], () => syncRng != null ? syncRng.NextItem(values) : values[0]));
					}
				}
				HashSet<int> skipVoterSlots = new HashSet<int>();
				for (int i = 0; i < expectedCount; i++)
				{
					if (syncVotes[i] == -1)
					{
						skipVoterSlots.Add(i);
					}
				}
				List<Player> playersWithoutRelic = syncPlayerCollection.Players.Where((Player p, int slotIndex) => !skipVoterSlots.Contains(slotIndex) && results.All((RelicPickingResult r) => r.player != p)).ToList();
				if (syncRng != null)
				{
					unclaimedRelics.StableShuffle(syncRng);
				}
				for (int i = 0; i < Math.Min(unclaimedRelics.Count, playersWithoutRelic.Count); i++)
				{
					results.Add(new RelicPickingResult
					{
						type = RelicPickingResultType.ConsolationPrize,
						player = playersWithoutRelic[i],
						relic = unclaimedRelics[i]
					});
				}
				if (results.Count > 0)
				{
					InvokeRelicsAwarded(__instance, results);
				}
				TreasureLocalVotePendingStates.Remove(__instance);
				TreasureLocalSkipLockedStates.Remove(__instance);
				SetSyncPredictedVote(__instance, null);
				InvokeEndRelicVoting(__instance);
			}
			return false;
		}
	}

	// ── 事件处理 ──────────────────────────────────────────────────────────

	private static void OnTreasureSkipReleased(NButton button)
	{
		TreasureRoomRelicSynchronizer synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
		if (synchronizer.CurrentRelics == null)
		{
			return;
		}
		if (button.GetParent() is not NTreasureRoomRelicCollection collection)
		{
			return;
		}
		collection.SetSelectionEnabled(isEnabled: false);
		synchronizer.PickRelicLocally(-1);
	}

	// ── 辅助方法 ──────────────────────────────────────────────────────────

	private static List<NTreasureRoomRelicHolder>? GetHoldersInUse(NTreasureRoomRelicCollection collection)
		=> HoldersInUseField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;

	private static List<NTreasureRoomRelicHolder>? GetMultiplayerHolders(NTreasureRoomRelicCollection collection)
		=> MultiplayerHoldersField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;

	private static IRunState? GetRunState(NTreasureRoomRelicCollection collection)
		=> RunStateField?.GetValue(collection) as IRunState;

	private static IPlayerCollection? GetSyncPlayerCollection(TreasureRoomRelicSynchronizer synchronizer)
		=> SyncPlayerCollectionField?.GetValue(synchronizer) as IPlayerCollection;

	private static ulong? GetSyncLocalPlayerId(TreasureRoomRelicSynchronizer synchronizer)
		=> SyncLocalPlayerIdField?.GetValue(synchronizer) is ulong id ? id : null;

	private static ActionQueueSynchronizer? GetSyncActionQueueSynchronizer(TreasureRoomRelicSynchronizer synchronizer)
		=> SyncActionQueueField?.GetValue(synchronizer) as ActionQueueSynchronizer;

	private static List<int?>? GetSyncVotes(TreasureRoomRelicSynchronizer synchronizer)
		=> SyncVotesField?.GetValue(synchronizer) as List<int?>;

	private static List<RelicModel>? GetSyncCurrentRelics(TreasureRoomRelicSynchronizer synchronizer)
		=> SyncCurrentRelicsField?.GetValue(synchronizer) as List<RelicModel>;

	private static Rng? GetSyncRng(TreasureRoomRelicSynchronizer synchronizer)
		=> SyncRngField?.GetValue(synchronizer) as Rng;

	private static void SetSyncPredictedVote(TreasureRoomRelicSynchronizer synchronizer, int? vote)
	{
		if (SyncPredictedVoteField == null)
		{
			return;
		}
		Type fieldType = SyncPredictedVoteField.FieldType;
		if (fieldType == typeof(int?))
		{
			SyncPredictedVoteField.SetValue(synchronizer, vote);
			return;
		}
		if (fieldType == typeof(int))
		{
			SyncPredictedVoteField.SetValue(synchronizer, vote ?? -1);
		}
	}

	private static void InvokeVotesChanged(TreasureRoomRelicSynchronizer synchronizer)
	{
		if (VotesChangedEventField?.GetValue(synchronizer) is Action action)
		{
			action();
		}
	}

	private static void InvokeRelicsAwarded(TreasureRoomRelicSynchronizer synchronizer, List<RelicPickingResult> results)
	{
		if (RelicsAwardedEventField?.GetValue(synchronizer) is Action<List<RelicPickingResult>> action)
		{
			action(results);
		}
	}

	private static void SetSkipButtonState(NButton button, bool isEnabled)
	{
		if (isEnabled)
		{
			button.Enable();
			button.Modulate = Colors.White;
		}
		else
		{
			button.Disable();
			button.Modulate = new Color(0.5f, 0.5f, 0.5f, 1f);
		}
	}

	private static void UpdateSkipButtonLayout(NTreasureRoomRelicCollection collection)
	{
		if (!TreasureSkipButtons.TryGetValue(collection, out NChoiceSelectionSkipButton? button))
		{
			return;
		}
		Vector2 viewportSize = collection.GetViewportRect().Size;
		Vector2 buttonSize = button.Size;
		if (buttonSize == Vector2.Zero)
		{
			buttonSize = button.GetCombinedMinimumSize();
		}
		float marginX = 36f;
		float marginY = 110f;
		button.GlobalPosition = new Vector2(viewportSize.X - buttonSize.X - marginX, viewportSize.Y - buttonSize.Y - marginY);
	}

	// ── 本地化 ────────────────────────────────────────────────────────────

	private static string GetTreasureLocalizedText(string key, string fallbackText)
	{
		string languageCode = GetTreasureLanguageCode();
		if (TryGetTreasureLocValue(languageCode, key, out string value))
		{
			return value;
		}
		if (languageCode != "en_us" && TryGetTreasureLocValue("en_us", key, out value))
		{
			return value;
		}
		return fallbackText;
	}

	private static string GetTreasureLanguageCode()
	{
		string language = LocManager.Instance?.Language ?? "eng";
		if (string.Equals(language, "zhs", StringComparison.OrdinalIgnoreCase))
		{
			return "zh_cn";
		}
		return "en_us";
	}

	private static bool TryGetTreasureLocValue(string languageCode, string key, out string value)
	{
		Dictionary<string, string> table = GetTreasureLocalizationTable(languageCode);
		if (table.TryGetValue(key, out string? result) && result != null)
		{
			value = result;
			return true;
		}
		value = string.Empty;
		return false;
	}

	private static Dictionary<string, string> GetTreasureLocalizationTable(string languageCode)
	{
		if (TreasureLocalizationCache.TryGetValue(languageCode, out Dictionary<string, string>? cached))
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
			Log.Warn($"Failed to load treasure localization file: {filePath}. {ex.Message}");
		}
		TreasureLocalizationCache[languageCode] = table;
		return table;
	}

	private static void InvokeEndRelicVoting(TreasureRoomRelicSynchronizer synchronizer)
	{
		if (EndRelicVotingMethod == null)
		{
			Log.Warn("EndRelicVoting method not found; relic voting state may not be cleared properly.");
			return;
		}
		EndRelicVotingMethod.Invoke(synchronizer, null);
	}
}
