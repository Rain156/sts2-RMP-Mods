using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private const float MerchantForwardShiftX = 160f;

	private const float MerchantForwardShiftY = 35f;

	private const float MerchantRowStartOffsetX = -110f;

	private const float MerchantRowStepY = -40f;

	private const float MerchantColumnStepX = -230f;

	[HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
	private static class NMerchantRoomLayoutPatch
	{
		private static void Postfix(NMerchantRoom __instance)
		{
			RepositionMerchantVisuals(__instance.PlayerVisuals);
		}
	}

	private static void RepositionMerchantVisuals(IReadOnlyList<NMerchantCharacter> visuals)
	{
		if (visuals.Count <= VanillaMultiplayerHolderCount)
		{
			return;
		}
		int rowCount = visuals.Count <= VanillaMultiplayerHolderCount * 2 ? 2 : Mathf.CeilToInt((float)visuals.Count / VanillaMultiplayerHolderCount);
		int columnCount = Mathf.CeilToInt((float)visuals.Count / rowCount);
		int visualIndex = 0;
		for (int row = 0; row < rowCount; row++)
		{
			float x = MerchantForwardShiftX + MerchantRowStartOffsetX * row;
			float y = MerchantForwardShiftY + MerchantRowStepY * row;
			for (int column = 0; column < columnCount && visualIndex < visuals.Count; column++)
			{
				NMerchantCharacter nMerchantCharacter = visuals[visualIndex];
				nMerchantCharacter.Position = new Vector2(x, y);
				x += MerchantColumnStepX;
				visualIndex++;
			}
		}
	}
}
