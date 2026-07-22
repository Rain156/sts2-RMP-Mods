using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

/// <summary>
/// SceneTree navigation and monitoring utilities.
/// Uses cached high-value nodes when available and only falls back to DFS when needed.
/// </summary>
public static class SceneMonitor
{
    private static readonly FieldInfo? DailyRunLobbyField =
        typeof(NDailyRunScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? MultiplayerLoadLobbyField =
        typeof(NMultiplayerLoadGameScreen).GetField("_runLobby", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? CustomRunLoadLobbyField =
        typeof(NCustomRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? DailyRunLoadLobbyField =
        typeof(NDailyRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

    public static Node CreateRegistryNode() => new SceneRegistry();

    /// <summary>Get the current active scene name.</summary>
    public static string GetCurrentSceneName()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        return tree.CurrentScene?.Name.ToString() ?? string.Empty;
    }

    /// <summary>Check if a scene whose name contains the given string is active.</summary>
    public static bool IsSceneActive(string nameContains)
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        return tree.CurrentScene?.Name.ToString().Contains(nameContains, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>Get the SceneTree root.</summary>
    public static Node GetRoot()
    {
        return ((SceneTree)Engine.GetMainLoop()).Root;
    }

    public static NSettingsScreen? FindSettingsScreen()
    {
        NSettingsScreen? screen = SceneRegistry.Instance?.SettingsScreen;
        return screen != null && screen.IsVisibleInTree() ? screen : null;
    }

    public static NRestSiteRoom? FindRestSiteRoom() => SceneRegistry.Instance?.RestSiteRoom;

    public static NMerchantRoom? FindMerchantRoom() => SceneRegistry.Instance?.MerchantRoom;

    public static NTreasureRoomRelicCollection? FindTreasureRoomRelicCollection() =>
        SceneRegistry.Instance?.TreasureRoomRelicCollection;

    public static NMultiplayerSubmenu? FindMultiplayerSubmenu() => SceneRegistry.Instance?.MultiplayerSubmenu;

    public static NMultiplayerHostSubmenu? FindMultiplayerHostSubmenu() => SceneRegistry.Instance?.MultiplayerHostSubmenu;

    public static NMultiplayerLoadGameScreen? FindMultiplayerLoadGameScreen() =>
        SceneRegistry.Instance?.MultiplayerLoadGameScreen;

    public static NCustomRunLoadScreen? FindCustomRunLoadScreen() => SceneRegistry.Instance?.CustomRunLoadScreen;

    public static NDailyRunLoadScreen? FindDailyRunLoadScreen() => SceneRegistry.Instance?.DailyRunLoadScreen;

    /// <summary>Find a node by name substring, searching recursively from the given root.</summary>
    public static Node? FindNodeByName(Node root, string nameContains)
    {
        if (root.Name.ToString().Contains(nameContains, StringComparison.Ordinal))
            return root;

        foreach (Node child in root.GetChildren())
        {
            Node? result = FindNodeByName(child, nameContains);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>Find the first node of a specific type in the tree.</summary>
    public static T? FindNodeOfType<T>(Node root) where T : Node
    {
        if (root == GetRoot() && TryGetCachedNode<T>(out T? cached))
            return cached;

        if (root is T target)
            return target;

        foreach (Node child in root.GetChildren())
        {
            T? result = FindNodeOfType<T>(child);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>Find a child node matching a predicate, recursively.</summary>
    public static Node? FindNode(Node root, Func<Node, bool> predicate)
    {
        if (predicate(root))
            return root;

        foreach (Node child in root.GetChildren())
        {
            Node? result = FindNode(child, predicate);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Finds the active StartRunLobby by checking known cached screen types first,
    /// then falling back to a one-off tree scan if needed.
    /// </summary>
    public static StartRunLobby? FindActiveStartRunLobby()
    {
        try
        {
            NCharacterSelectScreen? charSelect = SceneRegistry.Instance?.CharacterSelectScreen;
            if (charSelect?.Lobby != null)
                return charSelect.Lobby;

            NCustomRunScreen? customRun = SceneRegistry.Instance?.CustomRunScreen;
            if (customRun?.Lobby != null)
                return customRun.Lobby;

            if (DailyRunLobbyField?.GetValue(SceneRegistry.Instance?.DailyRunScreen) is StartRunLobby dailyLobby)
                return dailyLobby;

            Node root = GetRoot();

            charSelect = FindNodeOfType<NCharacterSelectScreen>(root);
            if (charSelect?.Lobby != null)
                return charSelect.Lobby;

            customRun = FindNodeOfType<NCustomRunScreen>(root);
            if (customRun?.Lobby != null)
                return customRun.Lobby;

            if (DailyRunLobbyField != null)
            {
                Node? dailyRun = FindNode(root, n =>
                    n.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen");
                if (dailyRun != null && DailyRunLobbyField.GetValue(dailyRun) is StartRunLobby fallbackDailyLobby)
                    return fallbackDailyLobby;
            }
        }
        catch
        {
            // Scene tree may be in transition.
        }

        return null;
    }

    public static LoadRunLobby? FindActiveLoadRunLobby()
    {
        try
        {
            if (MultiplayerLoadLobbyField?.GetValue(SceneRegistry.Instance?.MultiplayerLoadGameScreen) is LoadRunLobby standardLoadLobby)
                return standardLoadLobby;

            if (CustomRunLoadLobbyField?.GetValue(SceneRegistry.Instance?.CustomRunLoadScreen) is LoadRunLobby customLoadLobby)
                return customLoadLobby;

            if (DailyRunLoadLobbyField?.GetValue(SceneRegistry.Instance?.DailyRunLoadScreen) is LoadRunLobby dailyLoadLobby)
                return dailyLoadLobby;

            Node root = GetRoot();

            if (FindNodeOfType<NMultiplayerLoadGameScreen>(root) is { } standardLoadScreen
                && MultiplayerLoadLobbyField?.GetValue(standardLoadScreen) is LoadRunLobby fallbackStandardLoadLobby)
            {
                return fallbackStandardLoadLobby;
            }

            if (FindNodeOfType<NCustomRunLoadScreen>(root) is { } customLoadScreen
                && CustomRunLoadLobbyField?.GetValue(customLoadScreen) is LoadRunLobby fallbackCustomLoadLobby)
            {
                return fallbackCustomLoadLobby;
            }

            if (FindNodeOfType<NDailyRunLoadScreen>(root) is { } dailyLoadScreen
                && DailyRunLoadLobbyField?.GetValue(dailyLoadScreen) is LoadRunLobby fallbackDailyLoadLobby)
            {
                return fallbackDailyLoadLobby;
            }
        }
        catch
        {
            // Scene tree may be in transition.
        }

        return null;
    }

    public static string? GetActiveLoadLobbyScreenName()
    {
        if (MultiplayerLoadLobbyField?.GetValue(SceneRegistry.Instance?.MultiplayerLoadGameScreen) is LoadRunLobby)
            return nameof(NMultiplayerLoadGameScreen);

        if (CustomRunLoadLobbyField?.GetValue(SceneRegistry.Instance?.CustomRunLoadScreen) is LoadRunLobby)
            return nameof(NCustomRunLoadScreen);

        if (DailyRunLoadLobbyField?.GetValue(SceneRegistry.Instance?.DailyRunLoadScreen) is LoadRunLobby)
            return nameof(NDailyRunLoadScreen);

        return null;
    }

    private static bool TryGetCachedNode<T>(out T? node) where T : Node
    {
        SceneRegistry? registry = SceneRegistry.Instance;
        node = registry switch
        {
            null => null,
            _ when typeof(T) == typeof(NSettingsScreen) => registry.SettingsScreen as T,
            _ when typeof(T) == typeof(NRestSiteRoom) => registry.RestSiteRoom as T,
            _ when typeof(T) == typeof(NMerchantRoom) => registry.MerchantRoom as T,
            _ when typeof(T) == typeof(NTreasureRoomRelicCollection) => registry.TreasureRoomRelicCollection as T,
            _ when typeof(T) == typeof(NCharacterSelectScreen) => registry.CharacterSelectScreen as T,
            _ when typeof(T) == typeof(NCustomRunScreen) => registry.CustomRunScreen as T,
            _ when typeof(T) == typeof(NDailyRunScreen) => registry.DailyRunScreen as T,
            _ when typeof(T) == typeof(NMultiplayerLoadGameScreen) => registry.MultiplayerLoadGameScreen as T,
            _ when typeof(T) == typeof(NCustomRunLoadScreen) => registry.CustomRunLoadScreen as T,
            _ when typeof(T) == typeof(NDailyRunLoadScreen) => registry.DailyRunLoadScreen as T,
            _ when typeof(T) == typeof(NMultiplayerSubmenu) => registry.MultiplayerSubmenu as T,
            _ when typeof(T) == typeof(NMultiplayerHostSubmenu) => registry.MultiplayerHostSubmenu as T,
            _ => null
        };

        return node != null;
    }
}
