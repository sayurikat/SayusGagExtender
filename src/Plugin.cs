using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using SayusGagExtender.API.GagSpeak;
using SayusGagExtender.Windows;
using System;
using System.IO;

namespace SayusGagExtender;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    public static Plugin? Instance { get; private set; }
    public Utils Utils { get; private set; }
    public API.MoodlesApi MoodlesApi { get; private set; }
    public API.Chat2Api Chat2Api { get; private set; }
    public API.GagSpeak.GagSpeakReflectionContext GagSpeakContext { get; private set; }
    public API.GagSpeak.GagSpeakRestraintSetApi GagSpeakRestraintSetApi { get; private set; }
    public API.GagSpeak.GagSpeakRestrictionsApi GagSpeakRestrictionsApi { get; private set; }
    public API.GagSpeak.GagSpeakChatMonitorApi GagSpeakChatMonitorApi { get; private set; }
    public API.GagSpeak.GagSpeakGagsApi GagSpeakGagsApi { get; private set; }
    public EmoteGuard EmoteGuard { get; set; }
    public AutoAttackKiller AutoAttackKiller { get; set; }
    public WeaponSheather WeaponSheather { get; set; }
    public RandomZapSender RandomEmoteSender { get; set; }
    public FriendListHelper FriendListHelper { get; set; }
    public TeleportBlocker TeleportBlocker { get; set; }
    public MountBlocker MountBlocker { get; set; }
    public JobSwitchBlocker JobSwitchBlocker { get; set; }
    public ChatMonitor ChatMonitor { get; set; }
    public BlindfoldMonitor BlindfoldMonitor { get; set; }
    public MirrorGagSpeak MirrorGagSpeak { get; set; }

    private const string CommandName = "/pmycommand";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Instance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "icon_512.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");





        DalamudReflector reflector = new DalamudReflector(PluginInterface, Framework, Log);

        ECommonsMain.Init(PluginInterface, this);

        this.MoodlesApi = new API.MoodlesApi(this);
        this.GagSpeakContext = new GagSpeakReflectionContext(this);
        this.GagSpeakRestraintSetApi = new GagSpeakRestraintSetApi(this, GagSpeakContext);
        this.GagSpeakRestrictionsApi = new GagSpeakRestrictionsApi(this, GagSpeakContext);
        this.GagSpeakChatMonitorApi = new GagSpeakChatMonitorApi(this, GagSpeakContext);
        this.GagSpeakGagsApi = new GagSpeakGagsApi(this, GagSpeakContext);
        this.Chat2Api = new API.Chat2Api(this);

        Utils = new Utils(Instance);
        FriendListHelper = new FriendListHelper(Instance);
        TeleportBlocker = new TeleportBlocker(Instance);
        MountBlocker = new MountBlocker(Instance);
        JobSwitchBlocker = new JobSwitchBlocker(Instance);
        EmoteGuard = new EmoteGuard(Instance);
        AutoAttackKiller = new AutoAttackKiller(Instance);
        WeaponSheather = new WeaponSheather(Instance);
        RandomEmoteSender = new RandomZapSender(Instance);
        MirrorGagSpeak = new MirrorGagSpeak(Instance);
        BlindfoldMonitor = new BlindfoldMonitor(Instance);
        ChatMonitor = new ChatMonitor(Instance);


    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        ECommonsMain.Dispose();

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        EmoteGuard?.Dispose();
        AutoAttackKiller?.Dispose();
        WeaponSheather?.Dispose();
        RandomEmoteSender?.Dispose();
        TeleportBlocker?.Dispose();
        MountBlocker?.Dispose();
        JobSwitchBlocker?.Dispose();
        GagSpeakRestraintSetApi?.Dispose();
        GagSpeakRestrictionsApi?.Dispose();
        GagSpeakChatMonitorApi?.Dispose();
        GagSpeakGagsApi?.Dispose();
        ChatMonitor?.Dispose();
        BlindfoldMonitor?.Dispose();
        MirrorGagSpeak?.Dispose();



        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        



        if (args.StartsWith("autozap on"))
        {
            RandomEmoteSender.Enable();
            return;
        }
        if (args.StartsWith("autozap off"))
        {
            RandomEmoteSender.Disable();
            return;
        }
        if (args.StartsWith("handguard"))
        {
            WeaponSheather.Enable();
            AutoAttackKiller.Enable();
            ChatGui.Print($"Hand guard Enabled, can only be deactivated form settings");
            return;
        }
        if (args.StartsWith("apply restriction ", StringComparison.OrdinalIgnoreCase))
        {
            string restrictionName = args.Substring("apply restriction ".Length).Trim();

            GagSpeakRestrictionsApi.ApplyRestriction(restrictionName);
            return;
        }
        if (args.StartsWith("remove restriction ", StringComparison.OrdinalIgnoreCase))
        {
            string restrictionName = args.Substring("remove restriction ".Length).Trim();

            GagSpeakRestrictionsApi.RemoveRestriction(restrictionName);
            return;
        }
        if (args.StartsWith("apply restraintset ", StringComparison.OrdinalIgnoreCase))
        {
            string restraintSetName = args.Substring("apply restraintset ".Length).Trim();

            GagSpeakRestraintSetApi.ApplyRestraintSet(restraintSetName);
            return;
        }
        if (args.StartsWith("remove restraintset ", StringComparison.OrdinalIgnoreCase))
        {
            string restraintSetName = args.Substring("remove restraintset ".Length).Trim();

            GagSpeakRestraintSetApi.RemoveRestraintSet(restraintSetName);
            return;
        }


        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
