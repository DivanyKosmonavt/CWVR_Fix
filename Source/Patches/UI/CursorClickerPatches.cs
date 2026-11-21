using System.Collections.Generic;
using System.Reflection.Emit;
using CWVR.Player;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using static HarmonyLib.AccessTools;

namespace CWVR.Patches.UI;

[CWVRPatch]
internal static class CursorClickerPatches
{
    [HarmonyPatch(typeof(CursorClicker), nameof(CursorClicker.LateUpdate))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> CursorClickerTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        // Step 1: replace everything up to and including the first `stloc.1` (camera position + camera forward)
        var matcher = new CodeMatcher(instructions)
            .MatchForward(true, new CodeMatch(OpCodes.Stloc_1));

        var endOfRay = matcher.Pos;

        matcher
            .Start()
            .RemoveInstructions(endOfRay + 1)
            // Make the cursor follow the primary interactor instead of the main camera (head)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Call, PropertyGetter(typeof(VRSession), nameof(VRSession.Instance))),
                new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(VRSession), nameof(VRSession.LocalPlayer))),
                new CodeInstruction(OpCodes.Callvirt,
                    PropertyGetter(typeof(VRPlayer), nameof(VRPlayer.PrimaryInteractor))),
                new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(Component), nameof(Component.transform))),
                new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(Transform), nameof(Transform.position))),
                new CodeInstruction(OpCodes.Stloc_0),
            
                new CodeInstruction(OpCodes.Call, PropertyGetter(typeof(VRSession), nameof(VRSession.Instance))),
                new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(VRSession), nameof(VRSession.LocalPlayer))),
                new CodeInstruction(OpCodes.Callvirt,
                    PropertyGetter(typeof(VRPlayer), nameof(VRPlayer.PrimaryInteractor))),
                new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(Component), nameof(Component.transform))),
                new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(Transform), nameof(Transform.up))),
                new CodeInstruction(OpCodes.Stloc_1)
            )
            // Step 2: the click condition. Game versions differ here (keyboard key, mouse button, and since a later
            // update also a gamepad button), so replace the whole condition: from the mouse key check up to and
            // including the `brfalse` that follows the gamepad `wasPressedThisFrame` check.
            .MatchForward(false, new CodeMatch(OpCodes.Ldc_I4, 0x143));

        var conditionStart = matcher.Pos;

        matcher.MatchForward(false,
            new CodeMatch(OpCodes.Callvirt, PropertyGetter(typeof(UnityEngine.InputSystem.Controls.ButtonControl),
                nameof(UnityEngine.InputSystem.Controls.ButtonControl.wasPressedThisFrame))));

        // Pos is the wasPressedThisFrame call, the next instruction is the brfalse that jumps over the click logic
        var conditionBranch = matcher.Advance(1);
        var falseAddr = conditionBranch.Operand;
        var conditionEnd = conditionBranch.Pos;

        // Use PlayerInput inputs instead of hardcoded mouse/keyboard/gamepad inputs
        matcher
            .Advance(conditionStart - conditionEnd)
            .RemoveInstructions(conditionEnd - conditionStart + 1)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldsfld, Field(typeof(global::Player), nameof(global::Player.localPlayer))),
                new CodeInstruction(OpCodes.Ldfld, Field(typeof(global::Player), nameof(global::Player.input))),
                new CodeInstruction(OpCodes.Ldfld,
                    Field(typeof(global::Player.PlayerInput), nameof(global::Player.PlayerInput.interactWasPressed))),
                new CodeInstruction(OpCodes.Brfalse, falseAddr)
            )
            // Step 3: use cursorPos to detect clickable elements instead of the mouse location on screen. The game
            // converts cursorPos to a screen point itself for gamepads, so do the same for the mouse branch.
            // Modify the instruction in place so any branch label pointing at it stays valid.
            .MatchForward(false,
                new CodeMatch(OpCodes.Call,
                    PropertyGetter(typeof(UnityEngine.Input), nameof(UnityEngine.Input.mousePosition))));

        matcher.Opcode = OpCodes.Ldsfld;
        matcher.Operand = Field(typeof(MainCamera), nameof(MainCamera.instance));

        matcher.Advance(1).InsertAndAdvance(
            new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(MainCamera), "Cam")),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld, Field(typeof(CursorClicker), nameof(CursorClicker.cursorPos))),
            new CodeInstruction(OpCodes.Callvirt, Method(typeof(Camera), nameof(Camera.WorldToScreenPoint),
                [typeof(Vector3)]))
        );

        return matcher.InstructionEnumeration();
    }
}