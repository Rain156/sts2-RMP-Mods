using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// Extends pre-run lobby flow beyond the vanilla 4-slot serializable limit
/// without Harmony by replacing the problematic host join response and the
/// ready/begin-run UI flow when extended mode is active.
/// </summary>
public partial class ExtendedLobbyModule : IRMPModule
{
    private static readonly MethodInfo? TryAddPlayerMethod = typeof(StartRunLobby).GetMethod(
        "TryAddPlayerInFirstAvailableSlot",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? UpdateMaxAscensionMethod = typeof(StartRunLobby).GetMethod(
        "UpdateMaxMultiplayerAscension",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? UpdatePreferredAscensionMethod = typeof(StartRunLobby).GetMethod(
        "UpdatePreferredAscension",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? RemoveConnectingPlayerMethod = typeof(StartRunLobby).GetMethod(
        "RemoveConnectingPlayer",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? StartRunHandleJoinMethod = typeof(StartRunLobby).GetMethod(
        "HandleClientLobbyJoinRequestMessage",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? GetRandomActListMethod = typeof(MegaCrit.Sts2.Core.Models.ActModel)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(method => method.Name == "GetRandomList" && method.GetParameters().Length == 3);
    private static readonly MethodInfo? GenericActMethod = typeof(ModelDb).GetMethod(
        "Act",
        BindingFlags.Public | BindingFlags.Static);

    private static readonly FieldInfo? DailyRunLobbyField = typeof(MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen)
        .GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? BeginningRunField = typeof(StartRunLobby)
        .GetField("_isBeginningRun", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(StartRunLobby).GetField("_beginningRun", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly HashSet<ulong> ExtendedRunStartingLobbyIds = new();
    private static readonly Dictionary<ulong, HostJoinPatchState> HostJoinPatchStates = new();

    public string Name => "ExtendedLobby";

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
    }

    public Node? CreateNode() => new ExtendedLobbyNode();

    public void Cleanup()
    {
        HostJoinPatchStates.Clear();
        ExtendedRunStartingLobbyIds.Clear();
    }

    internal static bool ShouldUseExtendedLobbyProtocol(StartRunLobby lobby)
    {
        return lobby.NetService.Type.IsMultiplayer()
            && ProtocolConfig.TargetPlayerLimit > ProtocolConfig.OfficialSerializableSlotLimit;
    }

    internal static bool TrySetPlayerReadyState(StartRunLobby lobby, ulong playerId, bool ready, out LobbyPlayer updatedPlayer)
    {
        int idx = lobby.Players.FindIndex(player => player.id == playerId);
        if (idx < 0)
        {
            updatedPlayer = default;
            return false;
        }

        updatedPlayer = lobby.Players[idx];
        updatedPlayer.isReady = ready;
        lobby.Players[idx] = updatedPlayer;
        return true;
    }

    internal static List<ActModel> BuildActsForBeginRun(
        string seed,
        string act1,
        StartRunLobby lobby,
        IReadOnlyList<LobbyPlayer> players)
    {
        UnlockState unlockState = new(players.Select(player => UnlockState.FromSerializable(player.unlockState)));
        Rng rng = new((uint)StringHelper.GetDeterministicHashCode(seed));
        List<ActModel> acts = InvokeGetRandomActList(seed, rng, unlockState, lobby.NetService.Type.IsMultiplayer());
        ActModel? chosenAct = GetAct(act1);
        if (chosenAct != null)
            acts[0] = chosenAct;
        return acts;
    }

    internal static void NotifyPlayerChanged(StartRunLobby lobby, LobbyPlayer player, bool isRandomCharacterResolution)
    {
        MethodInfo? method = lobby.LobbyListener.GetType().GetMethod("PlayerChanged");
        if (method == null)
            return;

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length >= 2)
            method.Invoke(lobby.LobbyListener, new object?[] { player, isRandomCharacterResolution });
        else
            method.Invoke(lobby.LobbyListener, new object?[] { player });
    }

    internal static bool TryBeginExtendedRun(StartRunLobby lobby)
    {
        if (!ShouldUseExtendedLobbyProtocol(lobby)
            || IsBeginningRun(lobby)
            || lobby.NetService.Type != NetGameType.Host)
        {
            return false;
        }

        if (lobby.Players.Count <= 1 || lobby.Players.Any(player => !player.isReady))
            return false;

        ulong lobbyId = lobby.GetHashCodeAsUlong();
        if (!ExtendedRunStartingLobbyIds.Add(lobbyId))
            return false;

        try
        {
            string seed = NGame.Instance?.DebugSeedOverride
                ?? (string.IsNullOrWhiteSpace(lobby.Seed)
                    ? SeedHelper.GetRandomSeed()
                    : SeedHelper.CanonicalizeSeed(lobby.Seed));

            UpdatePreferredAscensionMethod?.Invoke(lobby, null);
            NormalizeRandomCharacters(lobby, seed);
            List<ModifierModel> modifiers = lobby.Modifiers.ToList();
            List<ActModel> acts = BuildActsForBeginRun(seed, lobby.Act1, lobby, lobby.Players);

            RmpProtocol.BroadcastExtendedBeginRun(lobby.Players, seed, lobby.Act1, modifiers);
            BeginExtendedRunLocally(lobby, seed, acts, modifiers);

            if (lobby.NetService is NetHostGameService hostGameService)
                hostGameService.NetHost?.SetHostIsClosed(isClosed: true);

            Log.Info($"[RMP:ExtendedLobby] Started extended multiplayer run with {lobby.Players.Count} players.");
            return true;
        }
        finally
        {
            ExtendedRunStartingLobbyIds.Remove(lobbyId);
        }
    }

    internal static void BeginExtendedRunLocally(
        StartRunLobby lobby,
        string seed,
        List<ActModel> acts,
        IReadOnlyList<ModifierModel> modifiers)
    {
        lobby.NetService.SetBufferMessages(bufferMessages: true);
        BeginningRunField?.SetValue(lobby, true);

        try
        {
            lobby.LobbyListener.BeginRun(seed, acts, modifiers);
            Log.Info(
                $"[RMP:ExtendedLobby] Began local extended run transition with message buffering enabled " +
                $"({lobby.Players.Count} players, {lobby.NetService.Type}).");
        }
        catch
        {
            BeginningRunField?.SetValue(lobby, false);
            lobby.NetService.SetBufferMessages(bufferMessages: false);
            throw;
        }
    }

    internal static bool IsBeginningRun(StartRunLobby lobby)
        => BeginningRunField?.GetValue(lobby) is true;

    private static void NormalizeRandomCharacters(StartRunLobby lobby, string seed)
    {
        Rng rng = new((uint)StringHelper.GetDeterministicHashCode(seed));
        for (int i = 0; i < lobby.Players.Count; i++)
        {
            LobbyPlayer lobbyPlayer = lobby.Players[i];
            if (lobbyPlayer.character is not RandomCharacter)
                continue;

            lobbyPlayer.character = rng.NextItem(ModelDb.AllCharacters) ?? ModelDb.AllCharacters.First();
            lobby.Players[i] = lobbyPlayer;
            NotifyPlayerChanged(lobby, lobbyPlayer, true);
        }
    }

    private static ActModel? GetAct(string act1Key)
    {
        string? fullTypeName = act1Key switch
        {
            "overgrowth" => "MegaCrit.Sts2.Core.Models.Acts.Overgrowth",
            "underdocks" => "MegaCrit.Sts2.Core.Models.Acts.Underdocks",
            _ => null
        };

        if (fullTypeName == null || GenericActMethod == null)
            return null;

        Type? actType = typeof(ActModel).Assembly.GetType(fullTypeName);
        if (actType == null)
            return null;

        return GenericActMethod.MakeGenericMethod(actType).Invoke(null, null) as ActModel;
    }

    private static List<ActModel> InvokeGetRandomActList(string seed, Rng rng, UnlockState unlockState, bool isMultiplayer)
    {
        if (GetRandomActListMethod == null)
            throw new InvalidOperationException("ActModel.GetRandomList method was not found.");

        ParameterInfo firstParameter = GetRandomActListMethod.GetParameters()[0];
        object? result = firstParameter.ParameterType == typeof(string)
            ? GetRandomActListMethod.Invoke(null, new object?[] { seed, unlockState, isMultiplayer })
            : GetRandomActListMethod.Invoke(null, new object?[] { rng, unlockState, isMultiplayer });

        return ((IEnumerable<ActModel>)result!).ToList();
    }

    private sealed class HostJoinPatchState
    {
        public required StartRunLobby Lobby { get; init; }
        public required MessageHandlerDelegate<ClientLobbyJoinRequestMessage> OriginalJoinHandler { get; init; }
        public required MessageHandlerDelegate<ClientLobbyJoinRequestMessage> ReplacementJoinHandler { get; init; }
    }

    private sealed class ScreenPatchState
    {
        public required ulong ScreenId { get; init; }
        public required Node Screen { get; init; }
        public required Callable EmbarkReplacement { get; init; }
        public required Callable UnreadyReplacement { get; init; }
        public bool HasLoggedReady { get; set; }
    }

    private sealed partial class ExtendedLobbyNode : Node
    {
        private readonly Dictionary<string, string> _operationErrors = new();
        private ScreenPatchState? _standardScreenPatch;
        private ScreenPatchState? _customScreenPatch;
        private ScreenPatchState? _dailyScreenPatch;
        private int _frameCounter;

        public ExtendedLobbyNode()
        {
            Name = "ExtendedLobbyNode";
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 5 != 0)
                return;

            StartRunLobby? lobby = SceneMonitor.FindActiveStartRunLobby();
            if (lobby != null && lobby.NetService.Type == NetGameType.Host)
                RunGuarded("host join handler", () => EnsureHostJoinPatch(lobby));

            RunGuarded("standard lobby buttons", () => PatchStandardScreen(SceneRegistry.Instance?.CharacterSelectScreen));
            RunGuarded("custom lobby buttons", () => PatchCustomScreen(SceneRegistry.Instance?.CustomRunScreen));
            RunGuarded("daily lobby buttons", () => PatchDailyScreen(SceneRegistry.Instance?.DailyRunScreen));
        }

        private void PatchStandardScreen(NCharacterSelectScreen? screen)
        {
            if (screen == null)
            {
                ReleaseInactiveScreenPatch(ref _standardScreenPatch);
                return;
            }

            ulong id = screen.GetInstanceId();
            if (_standardScreenPatch?.ScreenId != id
                || !ReferenceEquals(_standardScreenPatch.Screen, screen))
            {
                _standardScreenPatch = new ScreenPatchState
                {
                    ScreenId = id,
                    Screen = screen,
                    EmbarkReplacement = Callable.From<NButton>(button => OnStandardEmbarkPressed(screen, button)),
                    UnreadyReplacement = Callable.From<NButton>(button => OnStandardUnreadyPressed(screen, button))
                };
            }

            EnsureScreenHandlers(
                screen,
                screen.GetNode<NButton>("ConfirmButton"),
                screen.GetNode<NButton>("UnreadyButton"),
                _standardScreenPatch,
                "standard");
        }

        private void PatchCustomScreen(NCustomRunScreen? screen)
        {
            if (screen == null)
            {
                ReleaseInactiveScreenPatch(ref _customScreenPatch);
                return;
            }

            ulong id = screen.GetInstanceId();
            if (_customScreenPatch?.ScreenId != id
                || !ReferenceEquals(_customScreenPatch.Screen, screen))
            {
                _customScreenPatch = new ScreenPatchState
                {
                    ScreenId = id,
                    Screen = screen,
                    EmbarkReplacement = Callable.From<NButton>(button => OnCustomEmbarkPressed(screen, button)),
                    UnreadyReplacement = Callable.From<NButton>(button => OnCustomUnreadyPressed(screen, button))
                };
            }

            EnsureScreenHandlers(
                screen,
                screen.GetNode<NButton>("ConfirmButton"),
                screen.GetNode<NButton>("UnreadyButton"),
                _customScreenPatch,
                "custom");
        }

        private void PatchDailyScreen(MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen? screen)
        {
            if (screen == null)
            {
                ReleaseInactiveScreenPatch(ref _dailyScreenPatch);
                return;
            }

            ulong id = screen.GetInstanceId();
            if (_dailyScreenPatch?.ScreenId != id
                || !ReferenceEquals(_dailyScreenPatch.Screen, screen))
            {
                _dailyScreenPatch = new ScreenPatchState
                {
                    ScreenId = id,
                    Screen = screen,
                    EmbarkReplacement = Callable.From<NButton>(button => OnDailyEmbarkPressed(screen, button)),
                    UnreadyReplacement = Callable.From<NButton>(button => OnDailyUnreadyPressed(screen, button))
                };
            }

            EnsureScreenHandlers(
                screen,
                screen.GetNode<NButton>("%ConfirmButton"),
                screen.GetNode<NButton>("%UnreadyButton"),
                _dailyScreenPatch,
                "daily");
        }

        private static void ReleaseInactiveScreenPatch(ref ScreenPatchState? state)
        {
            if (state != null
                && (!GodotObject.IsInstanceValid(state.Screen) || !state.Screen.IsInsideTree()))
            {
                state = null;
            }
        }

        private void RunGuarded(string operation, Action action)
        {
            try
            {
                action();
                if (_operationErrors.Remove(operation))
                    Log.Info($"[RMP:ExtendedLobby] Recovered {operation} maintenance.");
            }
            catch (Exception ex)
            {
                string error = $"{ex.GetType().FullName}: {ex.Message}";
                if (_operationErrors.TryGetValue(operation, out string? previousError)
                    && previousError == error)
                {
                    return;
                }

                _operationErrors[operation] = error;
                Log.Error($"[RMP:ExtendedLobby] Failed {operation} maintenance: {ex}");
            }
        }

        private void EnsureHostJoinPatch(StartRunLobby lobby)
        {
            ulong lobbyId = lobby.GetHashCodeAsUlong();
            if (HostJoinPatchStates.ContainsKey(lobbyId) || StartRunHandleJoinMethod == null)
                return;

            var originalHandler = (MessageHandlerDelegate<ClientLobbyJoinRequestMessage>)Delegate.CreateDelegate(
                typeof(MessageHandlerDelegate<ClientLobbyJoinRequestMessage>),
                lobby,
                StartRunHandleJoinMethod);

            MessageHandlerDelegate<ClientLobbyJoinRequestMessage> replacementHandler =
                (message, senderId) => HandleExtendedJoinRequest(lobby, originalHandler, message, senderId);

            lobby.NetService.UnregisterMessageHandler(originalHandler);
            lobby.NetService.RegisterMessageHandler(replacementHandler);

            HostJoinPatchStates[lobbyId] = new HostJoinPatchState
            {
                Lobby = lobby,
                OriginalJoinHandler = originalHandler,
                ReplacementJoinHandler = replacementHandler
            };

            Log.Info("[RMP:ExtendedLobby] Installed extended host join handler.");
        }

        private static void HandleExtendedJoinRequest(
            StartRunLobby lobby,
            MessageHandlerDelegate<ClientLobbyJoinRequestMessage> originalHandler,
            ClientLobbyJoinRequestMessage message,
            ulong senderId)
        {
            int prospectiveCount = lobby.Players.Count + 1;
            if (!ShouldUseExtendedLobbyProtocol(lobby)
                || prospectiveCount <= ProtocolConfig.OfficialSerializableSlotLimit)
            {
                originalHandler(message, senderId);
                return;
            }

            if (lobby.NetService.Type != NetGameType.Host || lobby.NetService is not NetHostGameService hostGameService)
                throw new InvalidOperationException("Extended join request received as non-host.");

            if (lobby.Players.Count >= lobby.MaxPlayers)
            {
                hostGameService.DisconnectClient(senderId, NetError.LobbyFull);
                return;
            }

            try
            {
                LobbyPlayer? joinedPlayer = (LobbyPlayer?)TryAddPlayerMethod?.Invoke(lobby, new object?[]
                {
                    message.unlockState,
                    message.maxAscensionUnlocked,
                    senderId
                });

                if (!joinedPlayer.HasValue)
                {
                    hostGameService.DisconnectClient(senderId, NetError.InternalError);
                    return;
                }

                UpdateMaxAscensionMethod?.Invoke(lobby, null);

                ClientLobbyJoinResponseMessage responseMessage = new()
                {
                    playersInLobby = BuildJoinResponsePlayers(lobby.Players, senderId),
                    ascension = lobby.Ascension,
                    dailyTime = lobby.DailyTime,
                    seed = lobby.Seed,
                    modifiers = lobby.Modifiers.Select(modifier => modifier.ToSerializable()).ToList()
                };

                hostGameService.SendMessage(responseMessage, senderId);
                hostGameService.SetPeerReadyForBroadcasting(senderId);

                if (joinedPlayer.Value.slotId < ProtocolConfig.OfficialSerializableSlotLimit)
                {
                    PlayerJoinedMessage joinedMessage = new()
                    {
                        lobbyPlayer = joinedPlayer.Value
                    };
                    foreach (LobbyPlayer player in lobby.Players)
                    {
                        if (player.id != lobby.NetService.NetId && player.id != senderId)
                            lobby.NetService.SendMessage(joinedMessage, player.id);
                    }
                }

                RemoveConnectingPlayerMethod?.Invoke(lobby, new object?[] { senderId });
                lobby.LobbyListener.PlayerConnected(joinedPlayer.Value);
                RmpProtocol.BroadcastLobbySnapshot(lobby.Players);
            }
            catch (Exception ex)
            {
                hostGameService.DisconnectClient(senderId, NetError.InternalError);
                Log.Error($"[RMP:ExtendedLobby] Failed to process extended join request: {ex}");
            }
        }

        private static List<LobbyPlayer> BuildJoinResponsePlayers(IReadOnlyList<LobbyPlayer> players, ulong joiningPlayerId)
        {
            List<LobbyPlayer> responsePlayers = players
                .Where(player => player.slotId < ProtocolConfig.OfficialSerializableSlotLimit)
                .Take(ProtocolConfig.OfficialSerializableSlotLimit)
                .ToList();

            if (responsePlayers.Any(player => player.id == joiningPlayerId))
                return responsePlayers;

            LobbyPlayer joiningPlayer = players.First(player => player.id == joiningPlayerId);
            if (joiningPlayer.slotId >= ProtocolConfig.OfficialSerializableSlotLimit)
                joiningPlayer.slotId = Math.Max(0, responsePlayers.Count - 1);

            if (responsePlayers.Count == ProtocolConfig.OfficialSerializableSlotLimit)
            {
                int replaceIndex = responsePlayers.FindLastIndex(player => player.id != players[0].id);
                if (replaceIndex < 0)
                    replaceIndex = responsePlayers.Count - 1;
                responsePlayers[replaceIndex] = joiningPlayer;
            }
            else
            {
                responsePlayers.Add(joiningPlayer);
            }

            return responsePlayers;
        }

        private static void EnsureScreenHandlers(
            object target,
            NButton embarkButton,
            NButton unreadyButton,
            ScreenPatchState state,
            string screenName)
        {
            EnsureReleasedHandler(
                embarkButton,
                target,
                "OnEmbarkPressed",
                state.EmbarkReplacement);
            EnsureReleasedHandler(
                unreadyButton,
                target,
                "OnUnreadyPressed",
                state.UnreadyReplacement);

            if (!state.HasLoggedReady)
            {
                state.HasLoggedReady = true;
                Log.Info($"[RMP:ExtendedLobby] Maintaining RMP ready handlers for the {screenName} lobby screen.");
            }
        }

        private static void EnsureReleasedHandler(
            NButton button,
            object target,
            string originalMethodName,
            Callable replacement)
        {
            Callable originalCallable = CreatePrivateReleasedCallable(target, originalMethodName);
            DisconnectReleasedHandler(button, target, originalMethodName, originalCallable);

            if (!button.IsConnected(NClickableControl.SignalName.Released, replacement))
                button.Connect(NClickableControl.SignalName.Released, replacement);
        }

        private static bool DisconnectReleasedHandler(
            NButton button,
            object target,
            string originalMethodName,
            Callable originalCallable)
        {
            bool disconnected = false;
            if (button.IsConnected(NClickableControl.SignalName.Released, originalCallable))
            {
                button.Disconnect(NClickableControl.SignalName.Released, originalCallable);
                disconnected = true;
            }

            if (target is not GodotObject targetObject)
                return disconnected;

            foreach (var connection in button.GetSignalConnectionList(NClickableControl.SignalName.Released))
            {
                if (!connection.TryGetValue("callable", out Variant callableVariant))
                    continue;

                Callable callable = callableVariant.AsCallable();
                if (!IsReleasedHandlerCallable(callable, targetObject, originalMethodName))
                    continue;

                if (button.IsConnected(NClickableControl.SignalName.Released, callable))
                    button.Disconnect(NClickableControl.SignalName.Released, callable);
                disconnected = true;
            }

            return disconnected;
        }

        private static bool IsReleasedHandlerCallable(Callable callable, GodotObject target, string methodName)
        {
            if (!ReferenceEquals(callable.Target, target))
                return false;

            if (callable.Method.ToString() == methodName)
                return true;

            return callable.Delegate?.Target == target
                && callable.Delegate.Method.Name == methodName;
        }

        private static Callable CreatePrivateReleasedCallable(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMethodException(target.GetType().FullName, methodName);

            Action<NButton> action = (Action<NButton>)Delegate.CreateDelegate(typeof(Action<NButton>), target, method);
            return Callable.From(action);
        }

        private static void OnStandardEmbarkPressed(NCharacterSelectScreen screen, NButton button)
        {
            StartRunLobby lobby = screen.Lobby;
            if (!ShouldUseExtendedLobbyProtocol(lobby))
            {
                InvokePrivateButtonMethod(screen, "OnEmbarkPressed", button);
                return;
            }

            if (!SaveManager.Instance.SeenFtue("accept_tutorials_ftue"))
            {
                if (NModalContainer.Instance != null)
                {
                    var ftue = NAcceptTutorialsFtue.Create(screen, delegate
                    {
                        OnStandardEmbarkPressed(screen, button);
                    });
                    if (ftue != null)
                        NModalContainer.Instance.Add(ftue);
                }
                return;
            }

            screen.GetNode<NButton>("ConfirmButton").Disable();
            screen.GetNode<NButton>("BackButton").Disable();
            lobby.Act1 = screen.GetNode<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NActDropdown>("%ActDropdown").CurrentOption;
            SetLocalReady(screen, lobby, ready: true);
            foreach (var charButton in screen.GetNode<Control>("CharSelectButtons/ButtonContainer").GetChildren().OfType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton>())
                charButton.Disable();

            if (!lobby.Players.All(player => player.isReady))
            {
                screen.GetNode<Control>("ReadyAndWaitingPanel").Visible = true;
                screen.GetNode<NButton>("UnreadyButton").Enable();
            }
        }

        private static void OnStandardUnreadyPressed(NCharacterSelectScreen screen, NButton button)
        {
            StartRunLobby lobby = screen.Lobby;
            if (!ShouldUseExtendedLobbyProtocol(lobby))
            {
                InvokePrivateButtonMethod(screen, "OnUnreadyPressed", button);
                return;
            }

            screen.GetNode<NButton>("ConfirmButton").Enable();
            screen.GetNode<NButton>("BackButton").Enable();
            screen.GetNode<NButton>("UnreadyButton").Disable();
            screen.GetNode<Control>("ReadyAndWaitingPanel").Visible = false;
            foreach (var charButton in screen.GetNode<Control>("CharSelectButtons/ButtonContainer").GetChildren().OfType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton>())
                charButton.Enable();
            SetLocalReady(screen, lobby, ready: false);
        }

        private static void OnCustomEmbarkPressed(NCustomRunScreen screen, NButton button)
        {
            StartRunLobby lobby = screen.Lobby;
            if (!ShouldUseExtendedLobbyProtocol(lobby))
            {
                InvokePrivateButtonMethod(screen, "OnEmbarkPressed", button);
                return;
            }

            screen.GetNode<NButton>("ConfirmButton").Disable();
            screen.GetNode<NButton>("BackButton").Disable();
            foreach (var charButton in screen.GetNode<Control>("LeftContainer/CharSelectButtons/ButtonContainer").GetChildren().OfType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton>())
                charButton.Disable();
            SetLocalReady(screen, lobby, ready: true);
            if (!lobby.Players.All(player => player.isReady))
            {
                screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = true;
                screen.GetNode<NButton>("UnreadyButton").Enable();
            }
        }

        private static void OnCustomUnreadyPressed(NCustomRunScreen screen, NButton button)
        {
            StartRunLobby lobby = screen.Lobby;
            if (!ShouldUseExtendedLobbyProtocol(lobby))
            {
                InvokePrivateButtonMethod(screen, "OnUnreadyPressed", button);
                return;
            }

            screen.GetNode<NButton>("ConfirmButton").Enable();
            screen.GetNode<NButton>("BackButton").Enable();
            screen.GetNode<NButton>("UnreadyButton").Disable();
            screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = false;
            foreach (var charButton in screen.GetNode<Control>("LeftContainer/CharSelectButtons/ButtonContainer").GetChildren().OfType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton>())
                charButton.Enable();
            SetLocalReady(screen, lobby, ready: false);
        }

        private static void OnDailyEmbarkPressed(MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen screen, NButton button)
        {
            StartRunLobby? lobby = DailyRunLobbyField?.GetValue(screen) as StartRunLobby;
            if (lobby == null)
                return;

            if (!ShouldUseExtendedLobbyProtocol(lobby))
            {
                InvokePrivateButtonMethod(screen, "OnEmbarkPressed", button);
                return;
            }

            screen.GetNode<NButton>("%ConfirmButton").Disable();
            screen.GetNode<NButton>("%BackButton").Disable();
            SetLocalReady(screen, lobby, ready: true);
            if (!lobby.Players.All(player => player.isReady))
            {
                screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = true;
                screen.GetNode<NButton>("%UnreadyButton").Enable();
            }
        }

        private static void OnDailyUnreadyPressed(MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen screen, NButton button)
        {
            StartRunLobby? lobby = DailyRunLobbyField?.GetValue(screen) as StartRunLobby;
            if (lobby == null)
                return;

            if (!ShouldUseExtendedLobbyProtocol(lobby))
            {
                InvokePrivateButtonMethod(screen, "OnUnreadyPressed", button);
                return;
            }

            screen.GetNode<NButton>("%ConfirmButton").Enable();
            screen.GetNode<NButton>("%BackButton").Enable();
            screen.GetNode<NButton>("%UnreadyButton").Disable();
            screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = false;
            SetLocalReady(screen, lobby, ready: false);
        }

        private static void SetLocalReady(Node screen, StartRunLobby lobby, bool ready)
        {
            if (!TrySetPlayerReadyState(lobby, lobby.NetService.NetId, ready, out LobbyPlayer updatedPlayer))
                return;

            NotifyPlayerChanged(lobby, updatedPlayer, false);

            if (lobby.NetService.Type == NetGameType.Host)
            {
                RmpProtocol.BroadcastExtendedReady(ready);
                TryBeginExtendedRun(lobby);
            }
            else
            {
                lobby.NetService.SendMessage(new RmpExtendedReadyStateMessage
                {
                    Ready = ready
                });
            }
        }

        private static void InvokePrivateButtonMethod(object target, string methodName, NButton button)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMethodException(target.GetType().FullName, methodName);
            method.Invoke(target, new object?[] { button });
        }
    }
}

internal static class ExtendedLobbyHashCodeExtensions
{
    public static ulong GetHashCodeAsUlong(this object value)
    {
        return unchecked((ulong)value.GetHashCode());
    }
}
