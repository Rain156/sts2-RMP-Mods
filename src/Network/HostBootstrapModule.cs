using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// Pre-host bootstrapper for multiplayer menu flows.
/// Replaces the vanilla 4-player host setup for both new runs and loaded runs.
/// </summary>
public partial class HostBootstrapModule : IRMPModule
{
    private const ushort DefaultEnetPort = 33771;

    private static readonly Dictionary<INetGameService, int> TrackedHostCapacities = new();
    private static readonly PropertyInfo? SerializableRunGameModeProperty =
        typeof(SerializableRun).GetProperty("GameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? SerializableRunGameModeField =
        typeof(SerializableRun).GetField("GameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? typeof(SerializableRun).GetField("gameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private bool _deferToDcip;

    public string Name => "HostBootstrap";

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        if (IsDirectConnectIpLoaded())
        {
            _deferToDcip = true;
            Log.Warn(
                "[RMP:HostBootstrap] DirectConnectIP detected — RMP is yielding multiplayer host bootstrap " +
                "to DCIP to avoid transport protocol conflicts (DCIP replaces NetHostGameService with its " +
                "own DirectHost/DirectClient). While DCIP is loaded, RMP's 4-16 player slider will NOT " +
                "affect host capacity: Steam mode stays at vanilla 4, Direct-IP mode is capped at DCIP's " +
                "hardcoded 16. Remove one of the two mods to regain full control.");
        }
    }

    public Node? CreateNode() => _deferToDcip ? null : new HostBootstrapNode();

    private static bool IsDirectConnectIpLoaded()
    {
        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(asm.GetName().Name, "DirectConnectIP", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // ignored — fall through to false
        }
        return false;
    }

    public void Cleanup()
    {
        TrackedHostCapacities.Clear();
    }

    internal static bool TryGetTrackedHostCapacity(INetGameService? netService, out int capacity)
    {
        if (netService != null && TrackedHostCapacities.TryGetValue(netService, out capacity))
            return true;

        capacity = default;
        return false;
    }

    internal static string GetTransportName(INetGameService netService)
    {
        return netService.Platform == PlatformType.Steam ? "Steam" : "ENet";
    }

    private partial class HostBootstrapNode : Node
    {
        private readonly HashSet<ulong> _patchedMainMenuSubmenus = new();
        private readonly HashSet<ulong> _patchedHostSubmenus = new();
        private int _frameCounter;

        public HostBootstrapNode()
        {
            Name = "HostBootstrapNode";
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 2 != 0)
                return;

            PatchMainMenuSubmenu(SceneMonitor.FindMultiplayerSubmenu());
            PatchHostSubmenu(SceneMonitor.FindMultiplayerHostSubmenu());
        }

        private void PatchMainMenuSubmenu(NMultiplayerSubmenu? submenu)
        {
            if (submenu == null)
                return;

            ulong submenuId = submenu.GetInstanceId();
            if (_patchedMainMenuSubmenus.Contains(submenuId))
                return;

            NButton? hostButton = submenu.GetNodeOrNull<NButton>("ButtonContainer/HostButton");
            NButton? loadButton = submenu.GetNodeOrNull<NButton>("ButtonContainer/LoadButton");
            if (hostButton == null || loadButton == null)
                return;

            if (!ReplaceReleasedHandler(hostButton, submenu, "OnHostPressed", _ => OnMainMenuHostPressed(submenu)))
                return;

            if (!ReplaceReleasedHandler(loadButton, submenu, "StartLoad", _ => OnMainMenuLoadPressed(submenu)))
                return;

            _patchedMainMenuSubmenus.Add(submenuId);
            Log.Info("[RMP:HostBootstrap] Patched NMultiplayerSubmenu host/load handlers.");
        }

        private void PatchHostSubmenu(NMultiplayerHostSubmenu? submenu)
        {
            if (submenu == null)
                return;

            ulong submenuId = submenu.GetInstanceId();
            if (_patchedHostSubmenus.Contains(submenuId))
                return;

            NButton? standardButton = submenu.GetNodeOrNull<NButton>("StandardButton");
            NButton? dailyButton = submenu.GetNodeOrNull<NButton>("DailyButton");
            NButton? customButton = submenu.GetNodeOrNull<NButton>("CustomRunButton");
            if (standardButton == null || dailyButton == null || customButton == null)
                return;

            if (!ReplaceReleasedHandler(standardButton, submenu, "OnStandardPressed",
                    _ => OnHostSubmenuPressed(submenu, GameMode.Standard)))
            {
                return;
            }

            if (!ReplaceReleasedHandler(dailyButton, submenu, "OnDailyPressed",
                    _ => OnHostSubmenuPressed(submenu, GameMode.Daily)))
            {
                return;
            }

            if (!ReplaceReleasedHandler(customButton, submenu, "OnCustomPressed",
                    _ => OnHostSubmenuPressed(submenu, GameMode.Custom)))
            {
                return;
            }

            _patchedHostSubmenus.Add(submenuId);
            Log.Info("[RMP:HostBootstrap] Patched NMultiplayerHostSubmenu handlers.");
        }

        private void OnMainMenuHostPressed(NMultiplayerSubmenu submenu)
        {
            NSubmenuStack? stack = submenu.GetAncestorOfType<NSubmenuStack>();
            if (stack == null)
            {
                Log.Warn("[RMP:HostBootstrap] Failed to locate submenu stack for multiplayer submenu.");
                return;
            }

            if (SaveManager.Instance.Progress.NumberOfRuns > 0)
            {
                NMultiplayerHostSubmenu hostSubmenu = stack.GetSubmenuType<NMultiplayerHostSubmenu>();
                PatchHostSubmenu(hostSubmenu);
                stack.Push(hostSubmenu);
                return;
            }

            Control? loadingOverlay = submenu.GetNodeOrNull<Control>("%LoadingOverlay");
            if (loadingOverlay == null)
            {
                Log.Warn("[RMP:HostBootstrap] Multiplayer submenu loading overlay not found.");
                return;
            }

            TaskHelper.RunSafely(StartNewRunHostAsync(
                GameMode.Standard,
                loadingOverlay,
                stack,
                ProtocolConfig.TargetPlayerLimit));
        }

        private void OnMainMenuLoadPressed(NMultiplayerSubmenu submenu)
        {
            NSubmenuStack? stack = submenu.GetAncestorOfType<NSubmenuStack>();
            Control? loadingOverlay = submenu.GetNodeOrNull<Control>("%LoadingOverlay");
            NButton? loadButton = submenu.GetNodeOrNull<NButton>("ButtonContainer/LoadButton");
            if (stack == null || loadingOverlay == null)
            {
                Log.Warn("[RMP:HostBootstrap] Failed to resolve load-run host prerequisites.");
                return;
            }

            PlatformType platformType = GetPreferredHostPlatform();
            ReadSaveResult<SerializableRun> saveResult =
                SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(
                    PlatformUtil.GetLocalPlayerId(platformType));

            if (!saveResult.Success || saveResult.SaveData == null)
            {
                Log.Warn("[RMP:HostBootstrap] Invalid multiplayer run save detected.");
                loadButton?.Disable();
                NErrorPopup? modalToCreate = NErrorPopup.Create(
                    new LocString("main_menu_ui", "INVALID_SAVE_POPUP.title"),
                    new LocString("main_menu_ui", "INVALID_SAVE_POPUP.description_run"),
                    new LocString("main_menu_ui", "INVALID_SAVE_POPUP.dismiss"),
                    showReportBugButton: true);
                if (modalToCreate != null && NModalContainer.Instance != null)
                {
                    NModalContainer.Instance.Add(modalToCreate);
                    NModalContainer.Instance.ShowBackstop();
                }
                return;
            }

            // v0.1.7: always host at the fixed 16 cap; saved runs will simply fill a subset of the slots.
            TaskHelper.RunSafely(StartLoadedRunHostAsync(saveResult.SaveData, loadingOverlay, stack, ProtocolConfig.TargetPlayerLimit));
        }

        private void OnHostSubmenuPressed(NMultiplayerHostSubmenu submenu, GameMode gameMode)
        {
            NSubmenuStack? stack = submenu.GetAncestorOfType<NSubmenuStack>();
            Control? loadingOverlay = submenu.GetNodeOrNull<Control>("%LoadingOverlay");
            if (stack == null || loadingOverlay == null)
            {
                Log.Warn("[RMP:HostBootstrap] Failed to resolve multiplayer host submenu prerequisites.");
                return;
            }

            TaskHelper.RunSafely(StartNewRunHostAsync(
                gameMode,
                loadingOverlay,
                stack,
                ProtocolConfig.TargetPlayerLimit));
        }

        private static bool ReplaceReleasedHandler(
            NButton button,
            object signalTarget,
            string originalMethodName,
            Action<NButton> replacement)
        {
            try
            {
                Callable originalCallable = CreatePrivateReleasedCallable(signalTarget, originalMethodName);
                if (button.IsConnected(NClickableControl.SignalName.Released, originalCallable))
                    button.Disconnect(NClickableControl.SignalName.Released, originalCallable);

                button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(replacement));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP:HostBootstrap] Failed to rewire {signalTarget.GetType().Name}.{originalMethodName}: {ex.Message}");
                return false;
            }
        }

        private static Callable CreatePrivateReleasedCallable(object target, string methodName)
        {
            MethodInfo? method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, methodName);

            Action<NButton> action =
                (Action<NButton>)Delegate.CreateDelegate(typeof(Action<NButton>), target, method);
            return Callable.From<NButton>(action);
        }

        private static async Task StartNewRunHostAsync(
            GameMode gameMode,
            Control loadingOverlay,
            NSubmenuStack stack,
            int hostCapacity)
        {
            loadingOverlay.Visible = true;
            try
            {
                NetHostGameService netService = new NetHostGameService();
                NetErrorInfo? netError = await StartHostAsync(netService, hostCapacity);
                if (netError.HasValue)
                {
                    ShowNetError(netError.Value);
                    return;
                }

                TrackHostCapacity(netService, hostCapacity);

                switch (gameMode)
                {
                    case GameMode.Standard:
                    {
                        NCharacterSelectScreen screen = stack.GetSubmenuType<NCharacterSelectScreen>();
                        screen.InitializeMultiplayerAsHost(netService, hostCapacity);
                        stack.Push(screen);
                        break;
                    }
                    case GameMode.Daily:
                    {
                        NDailyRunScreen screen = stack.GetSubmenuType<NDailyRunScreen>();
                        screen.InitializeMultiplayerAsHost(netService);
                        stack.Push(screen);
                        break;
                    }
                    default:
                    {
                        NCustomRunScreen screen = stack.GetSubmenuType<NCustomRunScreen>();
                        screen.InitializeMultiplayerAsHost(netService, hostCapacity);
                        stack.Push(screen);
                        break;
                    }
                }

                Log.Info(
                    $"[RMP:HostBootstrap] Hosted {gameMode} lobby via {GetTransportName(netService)} with capacity {hostCapacity}.");
            }
            catch (Exception ex)
            {
                ShowNetError(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
                Log.Warn($"[RMP:HostBootstrap] New-run host startup failed: {ex}");
                throw;
            }
            finally
            {
                loadingOverlay.Visible = false;
            }
        }

        private static async Task StartLoadedRunHostAsync(
            SerializableRun run,
            Control loadingOverlay,
            NSubmenuStack stack,
            int hostCapacity)
        {
            loadingOverlay.Visible = true;
            try
            {
                NetHostGameService netService = new NetHostGameService();
                NetErrorInfo? netError = await StartHostAsync(netService, hostCapacity);
                if (netError.HasValue)
                {
                    ShowNetError(netError.Value);
                    return;
                }

                TrackHostCapacity(netService, hostCapacity);

                GameMode gameMode = ResolveGameMode(run);
                switch (gameMode)
                {
                    case GameMode.Daily:
                    {
                        NDailyRunLoadScreen screen = stack.GetSubmenuType<NDailyRunLoadScreen>();
                        screen.InitializeAsHost(netService, run);
                        stack.Push(screen);
                        break;
                    }
                    case GameMode.Custom:
                    {
                        NCustomRunLoadScreen screen = stack.GetSubmenuType<NCustomRunLoadScreen>();
                        screen.InitializeAsHost(netService, run);
                        stack.Push(screen);
                        break;
                    }
                    default:
                    {
                        NMultiplayerLoadGameScreen screen = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
                        screen.InitializeAsHost(netService, run);
                        stack.Push(screen);
                        break;
                    }
                }

                Log.Info(
                    $"[RMP:HostBootstrap] Hosted loaded {gameMode} lobby via {GetTransportName(netService)} " +
                    $"with capacity {hostCapacity}, savePlayers={run.Players.Count}, connectedPlayers=1.");
            }
            catch (Exception ex)
            {
                ShowNetError(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
                Log.Warn($"[RMP:HostBootstrap] Loaded-run host startup failed: {ex}");
                throw;
            }
            finally
            {
                loadingOverlay.Visible = false;
            }
        }

        private static async Task<NetErrorInfo?> StartHostAsync(NetHostGameService netService, int hostCapacity)
        {
            if (GetPreferredHostPlatform() == PlatformType.Steam)
                return await netService.StartSteamHost(hostCapacity);

            return netService.StartENetHost(DefaultEnetPort, hostCapacity);
        }

        private static PlatformType GetPreferredHostPlatform()
        {
            return SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp")
                ? PlatformType.Steam
                : PlatformType.None;
        }

        private static void TrackHostCapacity(INetGameService netService, int hostCapacity)
        {
            TrackedHostCapacities[netService] = hostCapacity;
        }

        private static void ShowNetError(NetErrorInfo error)
        {
            NErrorPopup? popup = NErrorPopup.Create(error);
            if (popup != null && NModalContainer.Instance != null)
                NModalContainer.Instance.Add(popup);
        }

        private static GameMode ResolveGameMode(SerializableRun run)
        {
            if (SerializableRunGameModeProperty?.GetValue(run) is GameMode propertyValue)
                return propertyValue;

            if (SerializableRunGameModeField?.GetValue(run) is GameMode fieldValue)
                return fieldValue;

            if (run.DailyTime.HasValue)
                return GameMode.Daily;

            return run.Modifiers.Count > 0 ? GameMode.Custom : GameMode.Standard;
        }
    }
}
