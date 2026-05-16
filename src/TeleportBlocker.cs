using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using static FFXIVClientStructs.FFXIV.Client.Game.ActionManager;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Delegates;

namespace SayusGagExtender;

public unsafe sealed class TeleportBlocker : IDisposable
{
    private readonly Plugin plugin;
    private delegate bool UseActionDelegate(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted);

    private readonly Hook<UseActionDelegate> useActionHook;

    public bool Enabled => plugin.Configuration.TeleportBlockFeature;
    public bool IsActive => IsBlockMoodleActiveCached();
    private bool cachedMoodleActive = false;
    private long nextMoodleRefreshMs;


    public TeleportBlocker(Plugin plugin)
    {
        this.plugin = plugin;
        this.useActionHook = Plugin.GameInterop.HookFromAddress<UseActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction,
            this.UseActionDetour);
        this.useActionHook.Enable();
    }

    public void Enable()
    {
        plugin.Configuration.TeleportBlockFeature = true;
        //this.Enabled = true;

        //if (!this.useActionHook.IsEnabled)
        //    this.useActionHook.Enable();
    }

    public void Disable()
    {
        plugin.Configuration.TeleportBlockFeature = false;
        //this.Enabled = false;

        // You may leave the hook enabled and just let Enabled gate behavior.
        // Disabling it here is cleaner if this class only does teleport blocking.
        //if (this.useActionHook.IsEnabled)
        //    this.useActionHook.Disable();
    }

    private bool UseActionDetour(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted)
    {
        //Plugin.ChatGui.Print($"Use Action");
        if (this.Enabled && IsTeleportOrReturn(actionType, actionId))
        {
            //Plugin.ChatGui.Print($"!string.IsNullOrEmpty");
            if (IsBlockMoodleActiveCached(forceRefresh: true))
            {
                //Plugin.ChatGui.Print($"Blocked teleport / return action due to active moodle: Type ={ actionType}, Id ={ actionId}, Moodle = {plugin.Configuration.TeleportBlockMoodle}");
                Plugin.ChatGui.Print($"Blocked teleport / return action");
                return false;
            }
            //Plugin.ChatGui.Print($"Blocked teleport /return action: Type ={ actionType}, Id ={ actionId}");
            //return false;
        }

        return this.useActionHook.Original(
            actionManager,
            actionType,
            actionId,
            targetId,
            extraParam,
            mode,
            comboRouteId,
            outOptAreaTargeted);
    }
    public bool IsBlockMoodleActiveCached(bool forceRefresh = false)
    {
        var moodles = plugin.Configuration.TeleportBlockMoodles;
        if (moodles == null || moodles.Count == 0)
            return false;

        var now = Environment.TickCount64;
        if (!forceRefresh && now < this.nextMoodleRefreshMs)
            return this.cachedMoodleActive;

        foreach (var moodle in moodles)
        {
            var id = moodle.Key;
            if (id == null || id == Guid.Empty)
                continue;

            this.cachedMoodleActive = plugin.MoodlesApi.IsStatusActive(id);
        }

        this.nextMoodleRefreshMs = now + 5000;
        return this.cachedMoodleActive;
    }

    private static bool IsTeleportOrReturn(ActionType actionType, uint actionId)
    {
        //Plugin.ChatGui.Print($"Action Type ={actionType}, Id ={actionId}");
        // Hotbar/menu GeneralActions:
        // 7 = Return
        // 8 = Teleport
        if (actionType == ActionType.GeneralAction && actionId is 7 or 8)
            return true;

        // Casted actions:
        // 5 = Return
        // 6 = Teleport
        // 11408 = additional teleport-like action used by the client/plugin reference
        if (actionType == ActionType.Action && actionId is 5 or 6 or 11408)
            return true;

        return false;
    }

    public void Dispose()
    {
        this.useActionHook.Dispose();
    }
}
