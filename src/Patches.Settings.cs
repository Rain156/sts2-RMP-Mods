using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private static readonly Color SettingsDividerColor = new Color(0.91f, 0.86f, 0.75f, 0.25f);

	private static readonly FieldInfo? PaginatorOptionsField = AccessTools.Field(typeof(NPaginator), "_options");

	private static readonly FieldInfo? PaginatorCurrentIndexField = AccessTools.Field(typeof(NPaginator), "_currentIndex");

	private static readonly FieldInfo? PaginatorLabelField = AccessTools.Field(typeof(NPaginator), "_label");

	private static readonly MethodInfo? GetSettingsOptionsMethod = AccessTools.Method(typeof(NSettingsPanel), "GetSettingsOptionsRecursive");

	private static readonly FieldInfo? PanelFirstControlField = AccessTools.Field(typeof(NSettingsPanel), "_firstControl");

	private static readonly HashSet<NPaginator> PlayerLimitPaginators = new HashSet<NPaginator>();

	// ── 注入到 General 面板 Modding 行下方 ──────────────────────────────────

	[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
	private static class NSettingsScreenReadyPatch
	{
		private static void Postfix(NSettingsScreen __instance)
		{
			try
			{
				InjectRmpSettings(__instance);
			}
			catch (Exception ex)
			{
				Log.Warn($"Failed to inject RMP settings: {ex}");
			}
		}
	}

	[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuClosed))]
	private static class NSettingsScreenClosedPatch
	{
		private static void Postfix()
		{
			SaveModConfig();
		}
	}

	[HarmonyPatch(typeof(NPaginator), "OnIndexChanged")]
	private static class NPaginatorOnIndexChangedPatch
	{
		private static void Postfix(NPaginator __instance, int index)
		{
			if (!PlayerLimitPaginators.Contains(__instance))
			{
				return;
			}
			if (PaginatorOptionsField?.GetValue(__instance) is not List<string> options)
			{
				return;
			}
			if (index < 0 || index >= options.Count)
			{
				return;
			}
			// 更新标签显示（基类 OnIndexChanged 为空，必须手动更新）
			if (PaginatorLabelField?.GetValue(__instance) is MegaLabel label)
			{
				label.SetTextAutoSize(options[index]);
			}
			if (int.TryParse(options[index], out int newLimit))
			{
				TargetPlayerLimit = Math.Clamp(newLimit, MinSupportedPlayerLimit, MaxSupportedPlayerLimit);
				SaveModConfig();
			}
		}
	}

	private static void InjectRmpSettings(NSettingsScreen screen)
	{
		NSettingsPanel generalPanel = screen.GetNode<NSettingsPanel>("%GeneralSettings");
		VBoxContainer vbox = generalPanel.Content;

		Control? anchorNode = screen.GetNodeOrNull<Control>("%Modding")
			?? screen.GetNodeOrNull<Control>("%SendFeedback");
		if (anchorNode == null)
		{
			Log.Warn("Anchor node not found; RMP settings not injected.");
			return;
		}
		int insertIndex = anchorNode.GetIndex() + 1;

		// 1. 分隔线
		ColorRect divider = new ColorRect();
		divider.Name = "RmpDivider";
		divider.CustomMinimumSize = new Vector2(0, 2);
		divider.MouseFilter = Control.MouseFilterEnum.Ignore;
		divider.Color = SettingsDividerColor;

		// 2. 设置行
		MarginContainer row = new MarginContainer();
		row.Name = "RmpPlayerLimit";
		row.CustomMinimumSize = new Vector2(0, 64);
		row.AddThemeConstantOverride("margin_left", 12);
		row.AddThemeConstantOverride("margin_top", 0);
		row.AddThemeConstantOverride("margin_right", 12);
		row.AddThemeConstantOverride("margin_bottom", 0);

		// 3. 标签
		RichTextLabel? templateLabel = vbox.GetNodeOrNull<RichTextLabel>("Screenshake/Label")
			?? anchorNode.GetNodeOrNull<RichTextLabel>("Label");
		if (templateLabel != null)
		{
			RichTextLabel label = (RichTextLabel)templateLabel.Duplicate();
			label.Text = GetLocalizedText("SETTINGS_PLAYER_LIMIT_LABEL", "Max Players");
			label.MouseFilter = Control.MouseFilterEnum.Ignore;
			row.AddChild(label);
		}

		// 4. 翻页器 — paginator.tscn 根节点是 plain Control（无 NPaginator 脚本），
		//    因此需要创建真正的 NPaginator 并收养模板的可视化子节点
		NPaginator? paginator = CreatePlayerLimitPaginator();
		if (paginator == null)
		{
			Log.Warn("Failed to create player limit paginator.");
			return;
		}
		row.AddChild(paginator);

		// 5. 插入 VBox（此时子节点进入场景树，触发 _Ready）
		vbox.AddChild(divider);
		vbox.MoveChild(divider, insertIndex);
		vbox.AddChild(row);
		vbox.MoveChild(row, insertIndex + 1);

		// 6. 设置选项并重建焦点链
		SetupPlayerLimitPaginator(paginator);
		RebuildPanelFocusChain(generalPanel);
	}

	private static NPaginator? CreatePlayerLimitPaginator()
	{
		string scenePath = SceneHelper.GetScenePath("screens/paginator");
		PackedScene? scene = ResourceLoader.Load<PackedScene>(scenePath, null, ResourceLoader.CacheMode.Reuse);
		if (scene == null)
		{
			Log.Warn($"Failed to load: {scenePath}");
			return null;
		}

		// 实例化模板，然后将其子节点移植到真正的 NPaginator 上
		Node template = scene.Instantiate();

		NPaginator paginator = new NPaginator();
		paginator.Name = "PlayerLimitPaginator";
		paginator.CustomMinimumSize = new Vector2(324, 64);
		paginator.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
		paginator.FocusMode = Control.FocusModeEnum.All;
		paginator.MouseFilter = Control.MouseFilterEnum.Ignore;

		// 移植子节点并修正 Owner，使 %Label / %VfxLabel 唯一名称查找正确指向 paginator
		foreach (Node child in new List<Node>(template.GetChildren()))
		{
			template.RemoveChild(child);
			paginator.AddChild(child);
			AdoptOwnership(child, template, paginator);
		}

		template.Free();
		return paginator;
	}

	private static void AdoptOwnership(Node node, Node oldOwner, Node newOwner)
	{
		if (node.Owner == oldOwner)
		{
			node.Owner = newOwner;
		}
		foreach (Node child in node.GetChildren())
		{
			AdoptOwnership(child, oldOwner, newOwner);
		}
	}

	private static void SetupPlayerLimitPaginator(NPaginator paginator)
	{
		if (PaginatorOptionsField?.GetValue(paginator) is not List<string> options)
		{
			return;
		}
		options.Clear();
		for (int i = MinSupportedPlayerLimit; i <= MaxSupportedPlayerLimit; i++)
		{
			options.Add(i.ToString());
		}
		int currentIndex = Math.Max(0, options.IndexOf(TargetPlayerLimit.ToString()));
		PaginatorCurrentIndexField?.SetValue(paginator, currentIndex);
		if (PaginatorLabelField?.GetValue(paginator) is MegaLabel label)
		{
			label.SetTextAutoSize(options[currentIndex]);
		}
		PlayerLimitPaginators.Add(paginator);
		paginator.TreeExiting += () => PlayerLimitPaginators.Remove(paginator);
	}

	private static void RebuildPanelFocusChain(NSettingsPanel panel)
	{
		if (GetSettingsOptionsMethod == null || PanelFirstControlField == null)
		{
			return;
		}
		List<Control> controls = new List<Control>();
		GetSettingsOptionsMethod.Invoke(panel, new object[] { panel.Content, controls });
		for (int i = 0; i < controls.Count; i++)
		{
			controls[i].FocusNeighborLeft = controls[i].GetPath();
			controls[i].FocusNeighborRight = controls[i].GetPath();
			controls[i].FocusNeighborTop = (i > 0) ? controls[i - 1].GetPath() : controls[i].GetPath();
			controls[i].FocusNeighborBottom = (i < controls.Count - 1) ? controls[i + 1].GetPath() : controls[i].GetPath();
		}
		if (controls.Count > 0)
		{
			PanelFirstControlField.SetValue(panel, controls[0]);
		}
	}
}
