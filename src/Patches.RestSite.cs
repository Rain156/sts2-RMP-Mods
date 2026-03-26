using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.Network;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private static readonly Vector2 LeftExtraFrontOffset = new Vector2(-250f, 35f);

	private static readonly Vector2 LeftExtraBackOffset = new Vector2(-240f, -20f);

	private static readonly Vector2 RightExtraFrontOffset = new Vector2(250f, 35f);

	private static readonly Vector2 RightExtraBackOffset = new Vector2(240f, -20f);

	private static readonly Vector2 LogXOffsetLeft = new Vector2(-250f, 0f);

	private static readonly Vector2 LogXOffsetRight = new Vector2(250f, 0f);

	private static readonly Vector2 ExtraSeatStep = new Vector2(70f, -45f);

	[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
	private static class NRestSiteRoomReadyPatch
	{
		private static readonly MethodInfo? CharacterContainerGetter = AccessTools.PropertyGetter(typeof(List<Control>), "Item");

		private static readonly MethodInfo? SafeContainerGetter = AccessTools.Method(typeof(NRestSiteRoomReadyPatch), nameof(GetContainerSafe));

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (CodeInstruction instruction in instructions)
			{
				if (CharacterContainerGetter != null && SafeContainerGetter != null && instruction.Calls(CharacterContainerGetter))
				{
					yield return new CodeInstruction(OpCodes.Call, SafeContainerGetter);
					continue;
				}
				yield return instruction;
			}
		}

		private static Control GetContainerSafe(List<Control> containers, int index)
		{
			if (containers.Count == 0)
			{
				throw new InvalidOperationException("No character containers found in rest site room.");
			}
			EnsureRestSiteContainers(containers, index + 1);
			return containers[NormalizeWrappedIndex(index, containers.Count)];
		}

		private static int NormalizeWrappedIndex(int index, int count)
		{
			int num = index % count;
			return num >= 0 ? num : num + count;
		}

		private static void EnsureRestSiteContainers(List<Control> containers, int requiredCount)
		{
			if (requiredCount <= containers.Count)
			{
				return;
			}
			Control parent = containers[0].GetParent<Control>();
			if (parent == null)
			{
				return;
			}
			EnsureExtraLogs(parent);
			int templateCount = containers.Count;
			while (containers.Count < requiredCount)
			{
				int count = containers.Count;
				Control source = containers[count % templateCount];
				Control control = source.Duplicate() as Control ?? new Control();
				RemoveAllChildren(control);
				control.Name = $"Character_Auto_{count + 1}";
				control.Position = GetExtraContainerPosition(containers, count);
				parent.AddChild(control);
				containers.Add(control);
			}
		}

		private static void RemoveAllChildren(Node node)
		{
			for (int i = node.GetChildCount() - 1; i >= 0; i--)
			{
				Node child = node.GetChild(i);
				node.RemoveChild(child);
				child.QueueFree();
			}
		}

		private static Vector2 GetExtraContainerPosition(List<Control> containers, int index)
		{
			if (containers.Count < 4)
			{
				return containers[containers.Count - 1].Position;
			}
			if (index >= ProtocolConfig.TargetPlayerLimit)
			{
				Log.Warn($"Rest site character index {index} exceeds configured target limit {ProtocolConfig.TargetPlayerLimit}.");
			}
			if (index < 4)
			{
				return containers[index].Position;
			}
			int extraSeatIndex = index - 4;
			bool isLeftSide = extraSeatIndex % 2 == 0;
			int depthLevel = extraSeatIndex / 2;
			Vector2 frontSeatPosition = isLeftSide ? containers[0].Position + LeftExtraFrontOffset : containers[1].Position + RightExtraFrontOffset;
			Vector2 backSeatPosition = isLeftSide ? containers[2].Position + LeftExtraBackOffset : containers[3].Position + RightExtraBackOffset;
			if (depthLevel == 0)
			{
				return frontSeatPosition;
			}
			if (depthLevel == 1)
			{
				return backSeatPosition;
			}
			int extraDepth = depthLevel - 1;
			Vector2 extraOffset = new Vector2((isLeftSide ? -1f : 1f) * ExtraSeatStep.X * extraDepth, ExtraSeatStep.Y * extraDepth);
			return backSeatPosition + extraOffset;
		}

		private static void EnsureExtraLogs(Control parent)
		{
			Node? background = parent.GetChildCount() > 0 ? parent.GetChild(0) : null;
			if (background == null || background.GetNodeOrNull<Node>("AutoExtraLogsMarker") != null)
			{
				return;
			}
			Node marker = new Node();
			marker.Name = "AutoExtraLogsMarker";
			background.AddChild(marker);
			bool leftLogOk = DuplicateShiftedNode(background, "RestSiteLLog", LogXOffsetLeft, "AutoL");
			bool rightLogOk = DuplicateShiftedNode(background, "RestSiteRLog", LogXOffsetRight, "AutoR");
			bool leftLogLayer2Ok = DuplicateShiftedNode(background, "RestSiteLighting/RestSiteLLog2", LogXOffsetLeft, "AutoL");
			bool rightLogLayer2Ok = DuplicateShiftedNode(background, "RestSiteLighting/RestSiteRLog2", LogXOffsetRight, "AutoR");
			if (!leftLogOk && !rightLogOk && !leftLogLayer2Ok && !rightLogLayer2Ok)
			{
				Log.Warn("No rest site log nodes found for duplication. Scene tree may have changed.");
			}
		}

		private static bool DuplicateShiftedNode(Node root, string nodePath, Vector2 offset, string suffix)
		{
			Node node = root.GetNodeOrNull<Node>(nodePath);
			if (node == null)
			{
				Log.Warn($"Rest site node not found: {nodePath}");
				return false;
			}
			Node parent = node.GetParent();
			if (parent == null)
			{
				Log.Warn($"Rest site node has no parent: {nodePath}");
				return false;
			}
			Node node2 = node.Duplicate();
			node2.Name = $"{node.Name}_{suffix}";
			parent.AddChild(node2);
			if (node is Control control && node2 is Control control2)
			{
				control2.Position = control.Position + offset;
			}
			if (node is Node2D node3 && node2 is Node2D node4)
			{
				node4.Position = node3.Position + offset;
			}
			return true;
		}
	}

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
		int? hoveredOptionIndex = RunManager.Instance.RestSiteSynchronizer.GetHoveredOptionIndex(playerId);
		if (!hoveredOptionIndex.HasValue)
		{
			return null;
		}
		IReadOnlyList<RestSiteOption> optionsForPlayer = RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);
		int value = hoveredOptionIndex.Value;
		if ((uint)value >= (uint)optionsForPlayer.Count)
		{
			return null;
		}
		return optionsForPlayer[value];
	}

	private static bool IsRemote(NRestSiteCharacter character) => !LocalContext.IsMe(character.Player);

	[HarmonyPatch(typeof(NRestSiteRoom), "OnPlayerChangedHoveredRestSiteOption")]
	private static class NRestSiteRoomHoverPatch
	{
		private static bool Prefix(NRestSiteRoom __instance, ulong playerId)
		{
			if (!TryGetCharacter(__instance, playerId, out var nRestSiteCharacter))
			{
				return false;
			}
			nRestSiteCharacter.ShowHoveredRestSiteOption(TryGetHoveredOption(playerId));
			return false;
		}
	}

	[HarmonyPatch(typeof(NRestSiteRoom), "OnBeforePlayerSelectedRestSiteOption")]
	private static class NRestSiteRoomBeforeSelectPatch
	{
		private static bool Prefix(NRestSiteRoom __instance, RestSiteOption option, ulong playerId)
		{
			if (TryGetCharacter(__instance, playerId, out var nRestSiteCharacter))
			{
				nRestSiteCharacter.SetSelectingRestSiteOption(option);
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(NRestSiteRoom), "OnAfterPlayerSelectedRestSiteOption")]
	private static class NRestSiteRoomAfterSelectPatch
	{
		private static bool Prefix(NRestSiteRoom __instance, RestSiteOption option, bool success, ulong playerId)
		{
			if (!TryGetCharacter(__instance, playerId, out var nRestSiteCharacter))
			{
				return false;
			}
			nRestSiteCharacter.SetSelectingRestSiteOption(null);
			if (success)
			{
				nRestSiteCharacter.ShowSelectedRestSiteOption(option);
				if (IsRemote(nRestSiteCharacter))
				{
					MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(option.DoRemotePostSelectVfx());
				}
			}
			return false;
		}
	}
}
