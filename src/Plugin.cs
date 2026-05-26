using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using SayusGagExtender.API;
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
    public API.PenumbraApi PenumbraApi { get; private set; }
    public API.Chat2Api Chat2Api { get; private set; }
    public API.EmoteApi EmoteApi { get; private set; }
    public API.CustomizePlusApi CustomizePlusApi { get; private set; }
    public API.GagSpeak.GagSpeakReflectionContext GagSpeakContext { get; private set; }
    public API.GagSpeak.GagSpeakRestraintSetApi GagSpeakRestraintSetApi { get; private set; }
    public API.GagSpeak.GagSpeakRestrictionsApi GagSpeakRestrictionsApi { get; private set; }
    public API.GagSpeak.GagSpeakChatMonitorApi GagSpeakChatMonitorApi { get; private set; }
    public API.GagSpeak.GagSpeakGagsApi GagSpeakGagsApi { get; private set; }
    public API.HonorificApi HonorificApi { get; private set; }
    public API.CammyApi CammyApi { get; private set; }
    public XivMessengerApi XivMessengerApi { get; private set; }
    public CharacterHelper CharacterHelper { get; set; }
    public EmoteGuard EmoteGuard { get; set; }
    public AutoAttackKiller AutoAttackKiller { get; set; }
    public WeaponSheather WeaponSheather { get; set; }
    public RandomZapSender RandomZapSender { get; set; }
    public RandomVibeSender RandomVibeSender { get; set; }
    public FriendListHelper FriendListHelper { get; set; }
    public MoodleEnforcer MoodleEnforcer { get; set; }
    public TeleportBlocker TeleportBlocker { get; set; }
    public MountBlocker MountBlocker { get; set; }
    //public JobSwitchBlocker JobSwitchBlocker { get; set; }
    public ChatMonitor ChatMonitor { get; set; }
    public BlindfoldMonitor BlindfoldMonitor { get; set; }
    public MirrorGagSpeak MirrorGagSpeak { get; set; }
    public PenumbraEnforcer PenumbraEnforcer { get; set; }
    public EmoteEnforcer EmoteEnforcer { get; set; }
    public CustomizePlusEnforcer CustomizePlusEnforcer { get; set; }
    public MovementBlocker MovementBlocker { get; set; }
    public ActionBlocker ActionBlocker { get; set; }
    public RemoteChatCommandMonitor RemoteChatCommandMonitor { get; set; }
    public Pedometer Pedometer { get; set; }
    public FatigueTracker FatigueTracker { get; set; }
    public FatigueHandler FatigueHandler { get; set; }
    public HonorificManager HonorificManager { get; private set; }
    public HonorificEnforcer HonorificEnforcer { get; set; }
    public CammyEnforcer CammyEnforcer { get; set; }
    public XIVMessengerManager XIVMessengerManager { get; set; }
    public JobManager JobManager { get; set; }



    private const string CommandName = "/sge";
    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Sayus Gag Extender");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private MiniWindow MiniWindow { get; init; }
    private ControllerWindow ControllerWindow { get; init; }

    public Plugin()
    {
        Instance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        MiniWindow = new MiniWindow(this);
        ControllerWindow = new ControllerWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(MiniWindow);
        WindowSystem.AddWindow(ControllerWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/sge help"
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
        this.PenumbraApi = new API.PenumbraApi(this);
        this.EmoteApi = new API.EmoteApi(this);
        this.CustomizePlusApi = new API.CustomizePlusApi(this);
        this.GagSpeakContext = new GagSpeakReflectionContext(this);
        this.GagSpeakRestraintSetApi = new GagSpeakRestraintSetApi(this, GagSpeakContext);
        this.GagSpeakRestrictionsApi = new GagSpeakRestrictionsApi(this, GagSpeakContext);
        this.GagSpeakChatMonitorApi = new GagSpeakChatMonitorApi(this, GagSpeakContext);
        this.GagSpeakGagsApi = new GagSpeakGagsApi(this, GagSpeakContext);
        this.Chat2Api = new API.Chat2Api(this);
        this.HonorificApi = new API.HonorificApi(this);
        this.CammyApi = new API.CammyApi(this);
        this.XivMessengerApi = new API.XivMessengerApi(this);

        Utils = new Utils(Instance);
        FriendListHelper = new FriendListHelper(Instance);
        CharacterHelper = new CharacterHelper(Instance);
        MoodleEnforcer = new MoodleEnforcer(Instance);
        TeleportBlocker = new TeleportBlocker(Instance);
        MountBlocker = new MountBlocker(Instance);
        //JobSwitchBlocker = new JobSwitchBlocker(Instance);
        EmoteGuard = new EmoteGuard(Instance);
        AutoAttackKiller = new AutoAttackKiller(Instance);
        WeaponSheather = new WeaponSheather(Instance);
        RandomZapSender = new RandomZapSender(Instance);
        RandomVibeSender = new RandomVibeSender(Instance);
        MirrorGagSpeak = new MirrorGagSpeak(Instance);
        BlindfoldMonitor = new BlindfoldMonitor(Instance);
        ChatMonitor = new ChatMonitor(Instance);
        PenumbraEnforcer = new PenumbraEnforcer(Instance);
        EmoteEnforcer = new EmoteEnforcer(Instance);
        CustomizePlusEnforcer = new CustomizePlusEnforcer(Instance);
        MovementBlocker = new MovementBlocker(Instance);
        ActionBlocker = new ActionBlocker(Instance);
        RemoteChatCommandMonitor = new RemoteChatCommandMonitor(Instance);
        Pedometer = new Pedometer(Instance);
        FatigueTracker = new FatigueTracker(Instance);
        FatigueHandler = new FatigueHandler(Instance);
        HonorificManager = new HonorificManager(Instance);
        HonorificEnforcer = new HonorificEnforcer(Instance);
        CammyEnforcer = new CammyEnforcer(Instance);
        XIVMessengerManager = new XIVMessengerManager(Instance);
        JobManager = new JobManager(Instance);

        if (Configuration.OpenMainWindowOnStartup)
        {
            //MainWindow.Toggle();
            //MainWindow.BringToFront();
            if (Configuration.ControllerWindowPreferred) ControllerWindow.IsOpen = true;
            else MainWindow.IsOpen = true;
        }
        if (Configuration.OpenConfigWindowOnStartup)
        {
            ConfigWindow.IsOpen = true;
        }
        if (Configuration.OpenMiniWindowOnStartup)
        {
            MiniWindow.IsOpen = true;
        }
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
        MiniWindow.Dispose();
        ControllerWindow.Dispose();

        EmoteGuard?.Dispose();
        AutoAttackKiller?.Dispose();
        WeaponSheather?.Dispose();
        RandomZapSender?.Dispose();
        RandomVibeSender?.Dispose();
        TeleportBlocker?.Dispose();
        MountBlocker?.Dispose();
        //JobSwitchBlocker?.Dispose();
        ChatMonitor?.Dispose();
        BlindfoldMonitor?.Dispose();
        MirrorGagSpeak?.Dispose();
        MoodleEnforcer?.Dispose();
        PenumbraEnforcer?.Dispose();
        EmoteEnforcer?.Dispose();
        CustomizePlusEnforcer?.Dispose();
        MovementBlocker?.Dispose();
        CharacterHelper?.Dispose();
        RemoteChatCommandMonitor?.Dispose();
        Pedometer?.Dispose();
        FatigueTracker?.Dispose();
        FatigueHandler?.Dispose(); 
        HonorificManager?.Dispose();
        HonorificEnforcer?.Dispose();
        CammyEnforcer?.Dispose();
        XIVMessengerManager?.Dispose();
        JobManager?.Dispose();


        GagSpeakRestraintSetApi?.Dispose();
        GagSpeakRestrictionsApi?.Dispose();
        GagSpeakChatMonitorApi?.Dispose();
        GagSpeakGagsApi?.Dispose();
        MoodlesApi?.Dispose();
        PenumbraApi?.Dispose();
        CustomizePlusApi.Dispose();
        HonorificApi?.Dispose();
        CammyApi?.Dispose();
        XivMessengerApi?.Dispose();


        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {




        if (args.StartsWith("help"))
        {
            //ChatGui.Print($"/sge autozap on : Enables auto zap feature");
            //ChatGui.Print($"/sge autozap off : Disables auto zap feature");
            //ChatGui.Print($"/sge handguard : Enables handguard feature");
            ChatGui.Print($"/sge apply restriction [name]: applies restriction in next available layer");
            ChatGui.Print($"/sge remove restriction [name]: removes restriction from any layer");
            ChatGui.Print($"/sge apply gag [name]: applies gag in next available layer");
            ChatGui.Print($"/sge remove gag [name]: removes gag from any layer");
            ChatGui.Print($"/sge apply restraintset [name]: applies restraintset");
            ChatGui.Print($"/sge remove restraintset [name]: removes restraintset");
            return;
        }
        //if (args.StartsWith("autozap on"))
        //{
        //    RandomEmoteSender.Enable();
        //    return;
        //}
        //if (args.StartsWith("autozap off"))
        //{
        //    RandomEmoteSender.Disable();
        //    return;
        //}
        //if (args.StartsWith("handguard"))
        //{
        //    WeaponSheather.Enable();
        //    AutoAttackKiller.Enable();
        //    ChatGui.Print($"Hand guard Enabled, can only be deactivated form settings");
        //    return;
        //}
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
        if (args.StartsWith("apply gag ", StringComparison.OrdinalIgnoreCase))
        {
            string restrictionName = args.Substring("apply gag ".Length).Trim();

            GagSpeakGagsApi.ApplyGag(restrictionName);
            return;
        }
        if (args.StartsWith("remove gag ", StringComparison.OrdinalIgnoreCase))
        {
            string restrictionName = args.Substring("remove gag ".Length).Trim();

            GagSpeakGagsApi.RemoveGag(restrictionName);
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


        if (args.Equals("pedometer speed start", StringComparison.OrdinalIgnoreCase))
        {
            Pedometer.StartSpeedRecord();
            return;
        }

        if (args.Equals("pedometer speed stop", StringComparison.OrdinalIgnoreCase))
        {
            Pedometer.StopSpeedRecordAndPrint();
            return;
        }

        if (args.Equals("pedometer speed", StringComparison.OrdinalIgnoreCase))
        {
            Pedometer.PrintCurrentSpeed();
            return;
        }

        if (args.Equals("pedometer totals", StringComparison.OrdinalIgnoreCase))
        {
            Pedometer.PrintTotals();
            return;
        }

        if (args.Equals("pedometer reset", StringComparison.OrdinalIgnoreCase))
        {
            Pedometer.ResetTotals();
            return;
        }

        if (args.Equals("fatigue status", StringComparison.OrdinalIgnoreCase))
        {
            FatigueTracker.PrintStatus();
            return;
        }

        if (args.Equals("fatigue reset", StringComparison.OrdinalIgnoreCase))
        {
            FatigueTracker.ResetFatigue();
            return;
        }

        if (args.StartsWith("fatigue set ", StringComparison.OrdinalIgnoreCase))
        {
            var valueText = args.Substring("fatigue set ".Length).Trim();

            if (float.TryParse(valueText, out var percent))
                FatigueTracker.SetFatiguePercent(percent);
            else
                ChatGui.PrintError("Usage: /sge fatigue set [percent]");

            return;
        }

        if (args.Equals("fatigue speed start", StringComparison.OrdinalIgnoreCase))
        {
            FatigueTracker.StartSpeedRecord();
            return;
        }

        if (args.Equals("fatigue speed stop", StringComparison.OrdinalIgnoreCase))
        {
            FatigueTracker.StopSpeedRecordAndPrint();
            return;
        }




        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi()
    {
        if (Configuration.ControllerWindowPreferred) ControllerWindow.Toggle();
        else MainWindow.Toggle();
    }

    public void ToggleControllerUi()
    {
        ControllerWindow.Toggle();
    }
    public void ToggleMiniUi() => MiniWindow.Toggle();
    public void RefreshControllerUserInputState(string name, string world)
    {
        ControllerWindow.RefreshUserInputState(name, world);
    }
    public void SetControllerTempHonorificInputState(string name, string world, string json)
    {
        ControllerWindow.SetTempHonorificInputState(name, world, json);
    }
}
