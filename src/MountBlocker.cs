using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using static FFXIVClientStructs.FFXIV.Client.Game.ActionManager;

namespace SayusGagExtender;

public unsafe sealed class MountBlocker : IDisposable
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

    private long nextMountedCheckMs;
    private bool cachedMoodleActive;
    private long nextMoodleRefreshMs;

    public bool Enabled => plugin.Configuration.MountBlockFeature;

    public MountBlocker(Plugin plugin)
    {
        this.plugin = plugin;

        this.useActionHook = Plugin.GameInterop.HookFromAddress<UseActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction,
            this.UseActionDetour);

        this.useActionHook.Enable();

        Plugin.Framework.Update += this.OnFrameworkUpdate;
    }

    public void Enable()
    {
        plugin.Configuration.MountBlockFeature = true;
    }

    public void Disable()
    {
        plugin.Configuration.MountBlockFeature = false;
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
        if (this.Enabled && this.ShouldBlockMountAction(actionType, actionId))
        {
            //Plugin.ChatGui.Print($"Blocked mount action due to active moodle: Type={actionType}, Id={actionId}, Moodle={plugin.Configuration.MountBlockMoodle}");
            Plugin.ChatGui.Print($"Blocked mount action");

            return false;
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

    private bool ShouldBlockMountAction(ActionType actionType, uint actionId)
    {
        // Individual mounts are ActionType.Mount with the mount row/id.
        // Do not check specific ids; this should catch all normal mount actions.
        if (actionType != ActionType.Mount)
            return false;

        // Important: if already mounted, don't block mount-related actions.
        // This helps avoid accidentally blocking dismount behavior.
        if (Plugin.Condition[ConditionFlag.Mounted])
            return false;

        return this.IsBlockMoodleActiveCached();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.Enabled)
            return;

        if (!Plugin.Condition[ConditionFlag.Mounted])
            return;

        var now = Environment.TickCount64;

        // Only check while mounted every 5 seconds.
        if (now < this.nextMountedCheckMs)
            return;

        this.nextMountedCheckMs = now + 5000;

        if (!this.IsBlockMoodleActiveCached(forceRefresh: true))
            return;

        //Plugin.ChatGui.Print($"Dismounting due to active moodle: Moodle={plugin.Configuration.MountBlockMoodle}");
        Plugin.ChatGui.Print($"Dismounting due to restriction");

        plugin.Utils.ExecuteNativeCommand("/mount");
    }

    public bool IsBlockMoodleActiveCached(bool forceRefresh = false)
    {
        var moodles = plugin.Configuration.MountBlockMoodles;
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

    public void Dispose()
    {
        Plugin.Framework.Update -= this.OnFrameworkUpdate;
        this.useActionHook.Dispose();
    }
}
