using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

/// <summary>
/// Event-driven cache of high-value SceneTree nodes.
/// Avoids repeated full-tree DFS in idle states like the main menu.
/// </summary>
public partial class SceneRegistry : Node
{
    public static SceneRegistry? Instance { get; private set; }

    private NSettingsScreen? _settingsScreen;
    private NRestSiteRoom? _restSiteRoom;
    private NMerchantRoom? _merchantRoom;
    private NTreasureRoomRelicCollection? _treasureRoomRelicCollection;
    private NCharacterSelectScreen? _characterSelectScreen;
    private NCustomRunScreen? _customRunScreen;
    private NDailyRunScreen? _dailyRunScreen;
    private NMultiplayerLoadGameScreen? _multiplayerLoadGameScreen;
    private NCustomRunLoadScreen? _customRunLoadScreen;
    private NDailyRunLoadScreen? _dailyRunLoadScreen;
    private NMultiplayerSubmenu? _multiplayerSubmenu;
    private NMultiplayerHostSubmenu? _multiplayerHostSubmenu;

    internal NSettingsScreen? SettingsScreen => Validate(ref _settingsScreen);
    internal NRestSiteRoom? RestSiteRoom => Validate(ref _restSiteRoom);
    internal NMerchantRoom? MerchantRoom => Validate(ref _merchantRoom);
    internal NTreasureRoomRelicCollection? TreasureRoomRelicCollection => Validate(ref _treasureRoomRelicCollection);
    internal NCharacterSelectScreen? CharacterSelectScreen => Validate(ref _characterSelectScreen);
    internal NCustomRunScreen? CustomRunScreen => Validate(ref _customRunScreen);
    internal NDailyRunScreen? DailyRunScreen => Validate(ref _dailyRunScreen);
    internal NMultiplayerLoadGameScreen? MultiplayerLoadGameScreen => Validate(ref _multiplayerLoadGameScreen);
    internal NCustomRunLoadScreen? CustomRunLoadScreen => Validate(ref _customRunLoadScreen);
    internal NDailyRunLoadScreen? DailyRunLoadScreen => Validate(ref _dailyRunLoadScreen);
    internal NMultiplayerSubmenu? MultiplayerSubmenu => Validate(ref _multiplayerSubmenu);
    internal NMultiplayerHostSubmenu? MultiplayerHostSubmenu => Validate(ref _multiplayerHostSubmenu);

    public override void _EnterTree()
    {
        Name = "SceneRegistry";
        Instance = this;

        SceneTree tree = GetTree();
        tree.NodeAdded += OnNodeAdded;
        IndexExistingTree(tree.Root);
    }

    public override void _ExitTree()
    {
        SceneTree? tree = GetTree();
        if (tree != null)
            tree.NodeAdded -= OnNodeAdded;

        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private void OnNodeAdded(Node node)
    {
        Register(node);
    }

    private void IndexExistingTree(Node node)
    {
        Register(node);
        foreach (Node child in node.GetChildren())
            IndexExistingTree(child);
    }

    private void Register(Node node)
    {
        switch (node)
        {
            case NSettingsScreen settingsScreen:
                _settingsScreen = settingsScreen;
                break;
            case NRestSiteRoom restSiteRoom:
                _restSiteRoom = restSiteRoom;
                break;
            case NMerchantRoom merchantRoom:
                _merchantRoom = merchantRoom;
                break;
            case NTreasureRoomRelicCollection treasureRoomRelicCollection:
                _treasureRoomRelicCollection = treasureRoomRelicCollection;
                break;
            case NCharacterSelectScreen characterSelectScreen:
                _characterSelectScreen = characterSelectScreen;
                break;
            case NCustomRunScreen customRunScreen:
                _customRunScreen = customRunScreen;
                break;
            case NDailyRunScreen dailyRunScreen:
                _dailyRunScreen = dailyRunScreen;
                break;
            case NMultiplayerLoadGameScreen multiplayerLoadGameScreen:
                _multiplayerLoadGameScreen = multiplayerLoadGameScreen;
                break;
            case NCustomRunLoadScreen customRunLoadScreen:
                _customRunLoadScreen = customRunLoadScreen;
                break;
            case NDailyRunLoadScreen dailyRunLoadScreen:
                _dailyRunLoadScreen = dailyRunLoadScreen;
                break;
            case NMultiplayerSubmenu multiplayerSubmenu:
                _multiplayerSubmenu = multiplayerSubmenu;
                break;
            case NMultiplayerHostSubmenu multiplayerHostSubmenu:
                _multiplayerHostSubmenu = multiplayerHostSubmenu;
                break;
        }
    }

    private static T? Validate<T>(ref T? node) where T : Node
    {
        if (node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
            return node;

        node = null;
        return null;
    }
}
