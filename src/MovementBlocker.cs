using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SayusGagExtender;

public sealed unsafe class MovementBlocker : IDisposable
{
    private readonly Plugin plugin;
    private readonly HashSet<string> blockSources = [];
    public bool Enabled => blockSources.Count > 0;
    public IReadOnlyCollection<string> BlockSources => blockSources;


    private static readonly InputId[] BlockedInputs =
    [
        // Normal movement
        InputId.MOVE_AND_STEER,
        InputId.MOVE_ANGLE_DESCENT,
        InputId.MOVE_ANGLE_RISING,
        InputId.MOVE_LEFT,
        InputId.MOVE_RIGHT,
        InputId.MOVE_DESCENT,
        InputId.MOVE_RETENTION,
        InputId.MOVE_STRIFE_L,
        InputId.MOVE_STRIFE_R,
        InputId.MOVE_FORE,
        InputId.MOVE_BACK,
        
        // Jump
        InputId.JUMP,

        // Jump
        InputId.AUTORUN_KEY,
        InputId.AUTORUN_PAD,

        // Camera / turn inputs
        InputId.CAMERA_LEFT,
        InputId.CAMERA_RIGHT,
        InputId.CAMERA_UP,
        InputId.CAMERA_DOWN,

        // Mouse-camera related inputs.
        // If your FFXIVClientStructs version does not have these names,
        // comment them out first and build again.
        InputId.CAMERA_MOUSE_OK,
        InputId.CAMERA_MOUSE_CANCEL,
    ];

    public MovementBlocker(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.GameInterop.InitializeFromAttributes(this);
        
        IsInputIdPressedHook.Enable();
        IsInputIdDownHook.Enable();
        IsInputIdHeldHook.Enable();
        IsInputIdUnknownHook.Enable();

        // This one handles mouse-driven movement/turning better than InputId alone.
        MouseMoveBlockHook.Enable();
        RMIWalkHook.Enable();
        //log.Information("EmoteMovementBlocker initialized.");
    }

    public void Dispose()
    {
        ClearAllBlocks();

        IsInputIdPressedHook.Dispose();
        IsInputIdDownHook.Dispose();
        IsInputIdHeldHook.Dispose();
        IsInputIdUnknownHook.Dispose();
        MouseMoveBlockHook.Dispose();
        RMIWalkHook.Dispose();

    }
    /// <summary>
    /// Request movement blocking from a named owner/source.
    /// Movement will remain blocked until every source has called ClearBlock.
    /// </summary>
    public void RequestBlock(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            source = "unknown";

        var wasEnabled = Enabled;

        blockSources.Add(source);

        if (!wasEnabled && Enabled)
        {
            //Plugin.ChatGui.Print("Movement Block Enabled");
            plugin.Utils.ExecuteNativeCommand("/automove off");
            EnableFullMovementLock();
        }
    }

    /// <summary>
    /// Clear movement blocking for a named owner/source.
    /// Movement only unlocks once all sources have cleared.
    /// </summary>
    public void ClearBlock(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            source = "unknown";

        var wasEnabled = Enabled;

        blockSources.Remove(source);

        if (wasEnabled && !Enabled)
        {
            //Plugin.ChatGui.Print("Movement Block Disabled");
            DisableFullMovementLock();
        }
    }

    /// <summary>
    /// Emergency clear, useful on plugin shutdown.
    /// </summary>
    public void ClearAllBlocks()
    {
        var wasEnabled = Enabled;

        blockSources.Clear();

        if (wasEnabled)
            DisableFullMovementLock();
    }

    private bool ShouldBlock(InputId inputId)
    {
        return Enabled && BlockedInputs.Contains(inputId);
    }

    private delegate byte IsInputIdPressedDelegate(void* inputData, InputId inputId);

    [Signature(Signatures.IsInputIdPressed, DetourName = nameof(IsInputIdPressedDetour), Fallibility = Fallibility.Auto)]
    private Hook<IsInputIdPressedDelegate> IsInputIdPressedHook = null!;

    private byte IsInputIdPressedDetour(void* inputData, InputId inputId)
    {
        if (ShouldBlock(inputId))
            return 0x00;

        return IsInputIdPressedHook.Original(inputData, inputId);
    }

    private delegate byte IsInputIdDownDelegate(void* inputData, InputId inputId);

    [Signature(Signatures.IsInputIdDown, DetourName = nameof(IsInputIdDownDetour), Fallibility = Fallibility.Auto)]
    private Hook<IsInputIdDownDelegate> IsInputIdDownHook = null!;

    private byte IsInputIdDownDetour(void* inputData, InputId inputId)
    {
        if (ShouldBlock(inputId))
            return 0x00;

        return IsInputIdDownHook.Original(inputData, inputId);
    }

    private delegate byte IsInputIdHeldDelegate(void* inputData, InputId inputId);

    [Signature(Signatures.IsInputIdHeld, DetourName = nameof(IsInputIdHeldDetour), Fallibility = Fallibility.Auto)]
    private Hook<IsInputIdHeldDelegate> IsInputIdHeldHook = null!;

    private byte IsInputIdHeldDetour(void* inputData, InputId inputId)
    {
        if (ShouldBlock(inputId))
            return 0x00;

        return IsInputIdHeldHook.Original(inputData, inputId);
    }

    private delegate byte IsInputIdUnknownDelegate(void* inputData, InputId inputId);

    [Signature(Signatures.IsInputIdUnknown, DetourName = nameof(IsInputIdUnknownDetour), Fallibility = Fallibility.Auto)]
    private Hook<IsInputIdUnknownDelegate> IsInputIdUnknownHook = null!;

    private byte IsInputIdUnknownDetour(void* inputData, InputId inputId)
    {
        if (ShouldBlock(inputId))
            return 0x00;

        return IsInputIdUnknownHook.Original(inputData, inputId);
    }

    private delegate void MouseMoveBlockDelegate(
        void* thisPtr,
        float* wishDirH,
        float* wishDirV,
        float* rotateDir,
        byte* alignWithCamera,
        byte* autoRun,
        byte dontRotateWithCamera);

    [Signature(Signatures.MouseMoveBlock, DetourName = nameof(MouseMoveBlockDetour), Fallibility = Fallibility.Auto)]
    private Hook<MouseMoveBlockDelegate> MouseMoveBlockHook = null!;
    
    private void MouseMoveBlockDetour(
        void* thisPtr,
        float* wishDirH,
        float* wishDirV,
        float* rotateDir,
        byte* alignWithCamera,
        byte* autoRun,
        byte dontRotateWithCamera)
    {
        MouseMoveBlockHook.Original(
            thisPtr,
            wishDirH,
            wishDirV,
            rotateDir,
            alignWithCamera,
            autoRun,
            dontRotateWithCamera);

        if (!Enabled)
            return;


        // Block mouse-driven movement and right-click turning.
        if (wishDirH != null)
            *wishDirH = 0f;

        if (wishDirV != null)
            *wishDirV = 0f;

        if (rotateDir != null)
            *rotateDir = 0f;

        if (alignWithCamera != null)
            *alignWithCamera = 0;

        if (autoRun != null)
            *autoRun = 0;
    }

    private static class Signatures
    {
        // Same signature family used by GagSpeak.
        public const string IsInputIdPressed =
            "E8 ?? ?? ?? ?? 84 C0 74 ?? 8D 93";

        public const string IsInputIdDown =
            "E8 ?? ?? ?? ?? 48 8B 75 ?? BB";

        public const string IsInputIdHeld =
            "E8 ?? ?? ?? ?? 84 C0 74 ?? EB ?? BE";

        public const string IsInputIdUnknown =
            "E8 ?? ?? ?? ?? 84 C0 8B EF";

        // Same mouse movement block signature used by GagSpeak.
        public const string MouseMoveBlock =
            "48 8b c4 4c 89 48 ?? 53 55 57 41 54 48 81 ec ?? 00 00 00";
        
        public const string RMIWalk =
    "E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D";
        public const string ForceDisableMovement =
    "F3 0F 10 05 ?? ?? ?? ?? 0F 2E C7";

    }

    private delegate void RMIWalkDelegate(
        void* self,
        float* sumLeft,
        float* sumForward,
        float* sumTurnLeft,
        byte* haveBackwardOrStrafe,
        byte* a6,
        byte bAdditiveUnk);

    [Signature(Signatures.RMIWalk, DetourName = nameof(RMIWalkDetour), Fallibility = Fallibility.Auto)]
    private Hook<RMIWalkDelegate> RMIWalkHook = null!;


    private void RMIWalkDetour(
    void* self,
    float* sumLeft,
    float* sumForward,
    float* sumTurnLeft,
    byte* haveBackwardOrStrafe,
    byte* a6,
    byte bAdditiveUnk)
    {
        RMIWalkHook.Original(
            self,
            sumLeft,
            sumForward,
            sumTurnLeft,
            haveBackwardOrStrafe,
            a6,
            bAdditiveUnk);

        if (!Enabled)
            return;

        // Movement
        if (sumLeft != null)
            *sumLeft = 0f;

        if (sumForward != null)
            *sumForward = 0f;

        // This is the important one for right-click character turning.
        if (sumTurnLeft != null)
            *sumTurnLeft = 0f;

        if (haveBackwardOrStrafe != null)
            *haveBackwardOrStrafe = 0;

        if (a6 != null)
            *a6 = 0;
    }


    [Signature(Signatures.ForceDisableMovement, ScanType = ScanType.StaticAddress, Fallibility = Fallibility.Infallible)]
    private readonly nint forceDisableMovementPtr;

    private bool forceDisableMovementSetByUs;

    private unsafe ref int ForceDisableMovement => ref *(int*)(forceDisableMovementPtr + 4);

    private unsafe void EnableFullMovementLock()
    {
        if (ForceDisableMovement > 0)
            return;

        ForceDisableMovement = 1;
        forceDisableMovementSetByUs = true;
    }

    private unsafe void DisableFullMovementLock()
    {
        if (!forceDisableMovementSetByUs)
            return;

        // Only clear it if it still looks like our lock.
        // This avoids wiping another plugin's lock in most simple cases.
        if (ForceDisableMovement > 0)
            ForceDisableMovement = 0;

        forceDisableMovementSetByUs = false;
    }
}
