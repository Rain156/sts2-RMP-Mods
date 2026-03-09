using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private const int VanillaMultiplayerHolderCount = 4;

	private const float FallbackRelicHolderXStep = 220f;

	private static readonly System.Reflection.FieldInfo HoldersInUseField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holdersInUse");

	private static readonly System.Reflection.FieldInfo MultiplayerHoldersField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_multiplayerHolders");

	private static readonly System.Reflection.FieldInfo RunStateField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_runState");

	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
	private static class NTreasureRoomRelicCollectionInitializePatch
	{
		private static void Prefix(NTreasureRoomRelicCollection __instance)
		{
			var holdersInUse = GetHoldersInUse(__instance);

			holdersInUse?.Clear();

			var multiplayerHolders = GetMultiplayerHolders(__instance);

			if (multiplayerHolders != null && multiplayerHolders.Count > VanillaMultiplayerHolderCount)
			{
				for (int i = multiplayerHolders.Count - 1; i >= VanillaMultiplayerHolderCount; i--)
				{
					NTreasureRoomRelicHolder nTreasureRoomRelicHolder = multiplayerHolders[i];

					multiplayerHolders.RemoveAt(i);

					nTreasureRoomRelicHolder.QueueFree();
				}
			}

			var currentRelics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;

			if (multiplayerHolders == null || currentRelics == null || currentRelics.Count <= multiplayerHolders.Count || multiplayerHolders.Count == 0)
				return;

			var template = multiplayerHolders[^1];

			Node parent = template.GetParent();

			for (int i = multiplayerHolders.Count; i < currentRelics.Count; i++)
			{
				if (template.Duplicate() is not NTreasureRoomRelicHolder holder)
					continue;

				holder.Name = $"AutoHolder_{i + 1}";

				holder.Visible = false;

				parent.AddChild(holder);

				multiplayerHolders.Add(holder);
			}
		}
		private static void Postfix(NTreasureRoomRelicCollection __instance)
		{
			var holdersInUse = GetHoldersInUse(__instance);

			if (holdersInUse == null || holdersInUse.Count <= VanillaMultiplayerHolderCount)
				return;

			var referenceHolderSampleCount = VanillaMultiplayerHolderCount;

			var minX = float.MaxValue;

			var maxX = float.MinValue;

			var topY = float.MaxValue;

			var bottomY = float.MinValue;

			for (var i = 0; i < referenceHolderSampleCount; i++)
			{
				var position = holdersInUse[i].Position;

				minX = Math.Min(minX, position.X);

				maxX = Math.Max(maxX, position.X);

				topY = Math.Min(topY, position.Y);

				bottomY = Math.Max(bottomY, position.Y);
			}

			var centerX = (minX + maxX) * 0.5f;

			// 基于原始4个holder，满8人使用3段间距，5-7人使用2段间距。
			var xStep = holdersInUse.Count >= Option.PlayerLimit ? (maxX - minX) / 3f : (maxX - minX) / 2f;

			var firstRowCount = Math.Min(VanillaMultiplayerHolderCount, (int)Math.Ceiling(holdersInUse.Count / 2f));

			xStep = xStep > 0f ? xStep : FallbackRelicHolderXStep;

			LayoutRow(holdersInUse, 0, firstRowCount, topY, centerX, xStep);

			var secondRowCount = holdersInUse.Count - firstRowCount;

			if (secondRowCount > 0)
				LayoutRow(holdersInUse, firstRowCount, secondRowCount, bottomY, centerX, xStep);
		}

		private static void LayoutRow(List<NTreasureRoomRelicHolder> holders, int startIndex, int count, float y, float centerX, float xStep)
		{
			var startX = centerX - (count - 1) * xStep * 0.5f;

			for (int i = 0; i < count; i++)
				holders[startIndex + i].Position = new Vector2(startX + i * xStep, y);
		}
	}

	[HarmonyPatch(typeof(NTreasureRoomRelicCollection), "get_DefaultFocusedControl")]
	private static class NTreasureRoomRelicCollectionDefaultFocusPatch
	{
		private static bool Prefix(NTreasureRoomRelicCollection __instance, ref Control __result)
		{
			var holdersInUse = GetHoldersInUse(__instance);

			if (holdersInUse == null || holdersInUse.Count == 0)
				return true;

			var runState = GetRunState(__instance);

			var playerSlotIndex = 0;

			var me = runState != null ? LocalContext.GetMe(runState.Players) : null;

			if (me != null && runState != null)
				playerSlotIndex = runState.GetPlayerSlotIndex(me);

			playerSlotIndex = Math.Clamp(playerSlotIndex, 0, holdersInUse.Count - 1);

			__result = holdersInUse[playerSlotIndex];

			return false;
		}
	}

	private static List<NTreasureRoomRelicHolder> GetHoldersInUse(NTreasureRoomRelicCollection collection)
	{
		return HoldersInUseField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;
	}

	private static List<NTreasureRoomRelicHolder> GetMultiplayerHolders(NTreasureRoomRelicCollection collection)
	{
		return MultiplayerHoldersField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;
	}

	private static IRunState GetRunState(NTreasureRoomRelicCollection collection)
	{
		return RunStateField?.GetValue(collection) as IRunState;
	}
}
