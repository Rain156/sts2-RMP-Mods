using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.SettingsUI;

/// <summary>
/// Monitors for the settings screen and injects RMP controls
/// (difficulty scaling toggle) below the Modding section in the General tab.
///
/// v0.1.7: the player-limit paginator was removed — every lobby is always 16.
///
/// Approach:
///   Polls for NSettingsScreen in the SceneTree. When found (and not
///   yet injected), inserts the mod controls. Watches for settings
///   close to persist config.
/// </summary>
public partial class SettingsModule : IRMPModule
{
    public string Name => "SettingsUI";

    private static readonly Color DividerColor = new(0.91f, 0.86f, 0.75f, 0.25f);
    private const string DividerName = "RmpDivider";
    private const string DifficultyScalingRowName = "RmpDifficultyScaling";

    private ReflectionCache _cache = null!;
    private ConfigManager _config = null!;

    // Reflection targets for NPaginator internals
    private FieldInfo? _paginatorOptionsField;
    private FieldInfo? _paginatorCurrentIndexField;
    private FieldInfo? _paginatorLabelField;
    private MethodInfo? _getSettingsOptionsMethod;
    private FieldInfo? _panelFirstControlField;

    // Track injected paginators
    private readonly HashSet<NPaginator> _difficultyPaginators = new();

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        _cache = cache;
        _config = config;

        _paginatorOptionsField = cache.GetField(typeof(NPaginator), "_options");
        _paginatorCurrentIndexField = cache.GetField(typeof(NPaginator), "_currentIndex");
        _paginatorLabelField = cache.GetField(typeof(NPaginator), "_label");
        _getSettingsOptionsMethod = cache.GetMethod(typeof(NSettingsPanel), "GetSettingsOptionsRecursive");
        _panelFirstControlField = cache.GetField(typeof(NSettingsPanel), "_firstControl");
    }

    public Node? CreateNode() => new SettingsNode(this);

    public void Cleanup()
    {
        _difficultyPaginators.Clear();
    }

    // ── Settings Node ─────────────────────────────────────────────────

    private partial class SettingsNode : Node
    {
        private readonly SettingsModule _mod;
        private int _frameCounter;
        private bool _injected;
        private NSettingsScreen? _lastScreen;

        public SettingsNode(SettingsModule mod)
        {
            _mod = mod;
            Name = "SettingsNode";
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 30 != 0) return;

            var screen = SceneMonitor.FindSettingsScreen();

            // Detect settings screen closed → save config
            if (screen == null && _lastScreen != null)
            {
                _mod._config.Save();
                _injected = false;
                _lastScreen = null;
                return;
            }

            if (screen != _lastScreen)
            {
                _lastScreen = screen;
                _injected = false;
            }

            if (screen == null || _injected) return;

            try
            {
                InjectSettings(screen);
                _injected = true;
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP:Settings] Injection failed: {ex}");
                _injected = true; // Don't retry
            }
        }

        private void InjectSettings(NSettingsScreen screen)
        {
            NSettingsPanel generalPanel = screen.GetNode<NSettingsPanel>("%GeneralSettings");
            VBoxContainer vbox = generalPanel.Content;
            RemoveExistingInjectedControls(vbox);

            Control? anchor = screen.GetNodeOrNull<Control>("%Modding")
                ?? screen.GetNodeOrNull<Control>("%SendFeedback");
            if (anchor == null)
            {
                Log.Warn("[RMP:Settings] Anchor node not found.");
                return;
            }
            int insertIdx = anchor.GetIndex() + 1;

            // Template label for consistent styling
            RichTextLabel? templateLabel = vbox.GetNodeOrNull<RichTextLabel>("Screenshake/Label")
                ?? anchor.GetNodeOrNull<RichTextLabel>("Label");

            // 1. Divider
            ColorRect divider = new()
            {
                Name = DividerName,
                CustomMinimumSize = new Vector2(0, 2),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Color = DividerColor
            };
            vbox.AddChild(divider);
            vbox.MoveChild(divider, insertIdx);

            // 2. Difficulty scaling row
            MarginContainer scalingRow = CreateSettingsRow(DifficultyScalingRowName);
            if (templateLabel != null)
            {
                RichTextLabel scalingLabel = (RichTextLabel)templateLabel.Duplicate();
                scalingLabel.Text = Localization.Get("SETTINGS_DIFFICULTY_SCALING_LABEL", "Difficulty Scaling");
                scalingLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
                scalingRow.AddChild(scalingLabel);
            }
            NPaginator? scalingPaginator = CreateModPaginator("DifficultyScalingPaginator");
            if (scalingPaginator != null)
            {
                scalingRow.AddChild(scalingPaginator);
                vbox.AddChild(scalingRow);
                vbox.MoveChild(scalingRow, insertIdx + 1);
                SetupDifficultyPaginator(scalingPaginator);
            }

            // 3. Rebuild focus chain
            RebuildFocusChain(generalPanel);
        }

        private static void RemoveExistingInjectedControls(VBoxContainer vbox)
        {
            RemoveInjectedControl(vbox, DividerName);
            RemoveInjectedControl(vbox, DifficultyScalingRowName);
            // Also remove the v0.1.6 player-limit row if it was left behind from a previous install.
            RemoveInjectedControl(vbox, "RmpPlayerLimit");
        }

        private static void RemoveInjectedControl(VBoxContainer vbox, string controlName)
        {
            Control? existing = vbox.GetNodeOrNull<Control>(controlName);
            if (existing == null) return;

            vbox.RemoveChild(existing);
            existing.QueueFree();
        }

        private static MarginContainer CreateSettingsRow(string name)
        {
            MarginContainer row = new()
            {
                Name = name,
                CustomMinimumSize = new Vector2(0, 64)
            };
            row.AddThemeConstantOverride("margin_left", 12);
            row.AddThemeConstantOverride("margin_top", 0);
            row.AddThemeConstantOverride("margin_right", 12);
            row.AddThemeConstantOverride("margin_bottom", 0);
            return row;
        }

        private NPaginator? CreateModPaginator(string name)
        {
            string scenePath = SceneHelper.GetScenePath("screens/paginator");
            PackedScene? scene = ResourceLoader.Load<PackedScene>(scenePath, null, ResourceLoader.CacheMode.Reuse);
            if (scene == null) return null;

            Node template = scene.Instantiate();
            RmpPaginator paginator = new((p, idx) => OnPaginatorChanged(p, idx))
            {
                Name = name,
                CustomMinimumSize = new Vector2(324, 64),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
                FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };

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
            if (node.Owner == oldOwner) node.Owner = newOwner;
            foreach (Node child in node.GetChildren())
                AdoptOwnership(child, oldOwner, newOwner);
        }

        private void SetupDifficultyPaginator(NPaginator paginator)
        {
            if (_mod._paginatorOptionsField?.GetValue(paginator) is not List<string> options) return;
            options.Clear();
            options.Add("OFF");
            options.Add("ON");

            int idx = ProtocolConfig.DifficultyScalingEnabled ? 1 : 0;
            _mod._paginatorCurrentIndexField?.SetValue(paginator, idx);
            if (_mod._paginatorLabelField?.GetValue(paginator) is MegaLabel label)
                label.SetTextAutoSize(options[idx]);

            _mod._difficultyPaginators.Add(paginator);
            paginator.TreeExiting += () => _mod._difficultyPaginators.Remove(paginator);
        }

        private void OnPaginatorChanged(NPaginator paginator, int index)
        {
            if (!_mod._difficultyPaginators.Contains(paginator)) return;

            if (_mod._paginatorOptionsField?.GetValue(paginator) is not List<string> options) return;
            if (index < 0 || index >= options.Count) return;

            // Update label display
            if (_mod._paginatorLabelField?.GetValue(paginator) is MegaLabel label)
                label.SetTextAutoSize(options[index]);

            ProtocolConfig.SetDifficultyScalingEnabled(options[index] == "ON");
            _mod._config.Save();
        }

        private void RebuildFocusChain(NSettingsPanel panel)
        {
            if (_mod._getSettingsOptionsMethod == null || _mod._panelFirstControlField == null) return;

            List<Control> controls = new();
            ParameterInfo[] parameters = _mod._getSettingsOptionsMethod.GetParameters();
            object?[] arguments = parameters.Length switch
            {
                2 => new object?[] { panel.Content, controls },
                3 => new object?[] { panel.Content, controls, false },
                _ => throw new TargetParameterCountException(
                    $"Unsupported NSettingsPanel.GetSettingsOptionsRecursive signature with {parameters.Length} parameters.")
            };
            _mod._getSettingsOptionsMethod.Invoke(panel, arguments);

            for (int i = 0; i < controls.Count; i++)
            {
                controls[i].FocusNeighborLeft = controls[i].GetPath();
                controls[i].FocusNeighborRight = controls[i].GetPath();
                controls[i].FocusNeighborTop = i > 0 ? controls[i - 1].GetPath() : controls[i].GetPath();
                controls[i].FocusNeighborBottom = i < controls.Count - 1
                    ? controls[i + 1].GetPath() : controls[i].GetPath();
            }

            if (controls.Count > 0)
                _mod._panelFirstControlField.SetValue(panel, controls[0]);
        }
    }

    // ── RmpPaginator ──────────────────────────────────────────────────
    // NPaginator uses virtual OnIndexChanged, not a Godot signal.
    // Subclass to intercept index changes via override.

    private partial class RmpPaginator : NPaginator
    {
        private readonly Action<NPaginator, int> _callback;

        public RmpPaginator(Action<NPaginator, int> callback)
        {
            _callback = callback;
        }

        public override void _Ready()
        {
            // NPaginator._Ready() throws for subclasses; use ConnectSignals() directly
            ConnectSignals();
        }

        protected override void OnIndexChanged(int index)
        {
            _callback(this, index);
        }
    }
}
