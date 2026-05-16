using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;

namespace SayusGagExtender;

/// <summary>
/// Blocks action execution at ActionManager.UseAction level.
/// This does not block keys/input; it blocks action requests after the game resolves them.
///
/// Important: Emote actions are intentionally never blocked here because Emote blocking
/// is handled separately by EmoteEnforcer / EmoteApi.
/// </summary>
public sealed unsafe class ActionBlocker : IDisposable
{
    private readonly Plugin plugin;
    private readonly HashSet<string> blockSources = [];

    public bool Enabled => blockSources.Count > 0;
    public IReadOnlyCollection<string> BlockSources => blockSources;

    private Hook<ActionManager.Delegates.UseAction> UseActionHook = null!;

    public ActionBlocker(Plugin plugin)
    {
        this.plugin = plugin;

        UseActionHook = Plugin.GameInterop.HookFromAddress<ActionManager.Delegates.UseAction>(
            ActionManager.MemberFunctionPointers.UseAction,
            UseActionDetour);

        UseActionHook.Enable();
    }

    public void Dispose()
    {
        ClearAllBlocks();
        UseActionHook.Dispose();
    }

    /// <summary>
    /// Request action blocking from a named owner/source.
    /// Actions remain blocked until every source has called ClearBlock.
    /// </summary>
    public void RequestBlock(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            source = "unknown";

        var wasEnabled = Enabled;

        blockSources.Add(source);

        if (!wasEnabled && Enabled)
            Plugin.ChatGui.Print("Action Block Enabled");
    }

    /// <summary>
    /// Clear action blocking for a named owner/source.
    /// Actions only unlock once all sources have cleared.
    /// </summary>
    public void ClearBlock(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            source = "unknown";

        var wasEnabled = Enabled;

        blockSources.Remove(source);

        if (wasEnabled && !Enabled)
            Plugin.ChatGui.Print("Action Block Disabled");
    }

    /// <summary>
    /// Emergency clear, useful on plugin shutdown.
    /// </summary>
    public void ClearAllBlocks()
    {
        var wasEnabled = Enabled;

        blockSources.Clear();

        if (wasEnabled)
            Plugin.ChatGui.Print("Action Block Disabled");
    }

    private bool ShouldBlock(ActionType actionType, uint actionId)
    {
        if (!Enabled)
            return false;

        // skips if any
        // if (actionType == ActionType.)
        //     return false;

        return true;
    }

    private bool UseActionDetour(
        ActionManager* thisPtr,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted)
    {
        if (ShouldBlock(actionType, actionId))
            return false;

        return UseActionHook.Original(
            thisPtr,
            actionType,
            actionId,
            targetId,
            extraParam,
            mode,
            comboRouteId,
            outOptAreaTargeted);
    }
}

/*
Plugin integration
==================

Add a property/field beside MovementBlocker:

    public ActionBlocker ActionBlocker { get; private set; } = null!;

Initialize after your services / HookProvider are ready:

    ActionBlocker = new ActionBlocker(this);

Dispose it beside MovementBlocker:

    ActionBlocker.Dispose();


EmoteEnforcer integration
=========================

In EmoteEnforcer.Enforce(), replace the movement-only block request with both blockers.
Also set RequestedBlockState; the current uploaded file checks it but never updates it.

    if (IsActive != RequestedBlockState)
    {
        if (IsActive)
        {
            plugin.MovementBlocker.RequestBlock(nameof(EmoteEnforcer));
            plugin.ActionBlocker.RequestBlock(nameof(EmoteEnforcer));
        }
        else
        {
            plugin.MovementBlocker.ClearBlock(nameof(EmoteEnforcer));
            plugin.ActionBlocker.ClearBlock(nameof(EmoteEnforcer));
        }

        RequestedBlockState = IsActive;
    }

In CancelCurrentEnforcedEmoteOnce(), clear both blockers and reset RequestedBlockState:

    plugin.MovementBlocker.ClearBlock(nameof(EmoteEnforcer));
    plugin.ActionBlocker.ClearBlock(nameof(EmoteEnforcer));
    RequestedBlockState = false;

*/
