using System;
using System.Runtime.InteropServices;

namespace VisibilityPlus;

/// <summary>
/// Spawns actor-attached VFX via the game's ActorVfx path, matching VFXEditor's
/// Plugin.ResourceLoader.ActorVfxCreate(path, caster, target, -1, 0, 0, 0) and
/// ActorVfxRemove(vfx, 1). Falls back gracefully if signatures break.
/// </summary>
internal static class VfxHelper
{
    // From VFXEditor Interop/Constants.cs + FFXIVClientStructs VfxObject.Create.
    private const string ActorVfxCreateSig = "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 AC 24 ?? ?? ?? ?? 0F 28 F3 49 8B F8";
    private const string ActorVfxRemoveSig = "0F 11 48 10 48 8D 05"; // +7 deref as in VFXEditor/ResourceLoader.cs:10

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ActorVfxCreateDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string path,
        nint casterPtr,
        nint targetPtr,
        float a4,
        byte a5,
        ushort a6,
        byte a7);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ActorVfxRemoveDelegate(nint vfxPtr, byte flag);

    private static ActorVfxCreateDelegate? createActorVfx;
    private static ActorVfxRemoveDelegate? removeActorVfx;
    private static bool triedInit;
    private static bool initFailedLogged;

    private static void EnsureInit()
    {
        if (triedInit)
            return;
        triedInit = true;
        try
        {
            if (Service.SigScanner.TryScanText(ActorVfxCreateSig, out var createPtr))
                createActorVfx = Marshal.GetDelegateForFunctionPointer<ActorVfxCreateDelegate>(createPtr);
            else if (!initFailedLogged)
            {
                initFailedLogged = true;
                Service.PluginLog.Warning("[Visibility Plus] ActorVfxCreate signature not found — VFX will be skipped.");
            }

            // VFXEditor: Scan 0F 11 48 10 48 8D 05 -> +7 -> Rip -> real fn.
            if (Service.SigScanner.TryScanText(ActorVfxRemoveSig, out var tmpPtr))
            {
                try
                {
                    nint temp = tmpPtr + 7;
                    nint rip = temp + Marshal.ReadInt32(temp) + 4;
                    nint fn = Marshal.ReadIntPtr(rip);
                    removeActorVfx = Marshal.GetDelegateForFunctionPointer<ActorVfxRemoveDelegate>(fn);
                }
                catch (Exception ex)
                {
                    Service.PluginLog.Warning($"[Visibility Plus] ActorVfxRemove deref failed: {ex.Message}");
                }
            }
            if (removeActorVfx == null)
                Service.PluginLog.Warning("[Visibility Plus] ActorVfxRemove signature not found — chain stop may not work.");
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[Visibility Plus] VfxHelper init failed: {ex.Message}");
        }
    }

    /// <summary>Spawn actor VFX on target (caster == target for self-attached). Returns VFX ptr or 0.</summary>
    public static bool TrySpawnOnActor(string path, nint casterAddress, nint targetAddress, out nint vfxPtr)
    {
        vfxPtr = nint.Zero;
        EnsureInit();
        if (createActorVfx == null)
            return false;
        if (casterAddress == nint.Zero || targetAddress == nint.Zero)
            return false;
        try
        {
            // VFXEditor uses -1, 0, 0, 0 — 0 made the effect invisible/audible-only.
            vfxPtr = createActorVfx(path, casterAddress, targetAddress, -1f, 0, 0, 0);
            return vfxPtr != nint.Zero;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[Visibility Plus] Spawn VFX '{path}' failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Attempt to destroy a previously spawned actor VFX.</summary>
    public static bool TryRemove(nint vfxPtr)
    {
        EnsureInit();
        if (vfxPtr == nint.Zero || removeActorVfx == null)
            return false;
        try
        {
            removeActorVfx(vfxPtr, 1);
            return true;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[Visibility Plus] Remove VFX 0x{vfxPtr:X} failed: {ex.Message}");
            return false;
        }
    }
}
