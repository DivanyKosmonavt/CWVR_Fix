using System.Collections;
using CWVR.Assets;
using CWVR.Input;
using CWVR.Player;
using Microsoft.MixedReality.Toolkit.Experimental.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CWVR.UI;

public class MainMenu : MonoBehaviour
{
    private Canvas[] canvases = [];
    private Transform xrOrigin;
    private TrackedPoseDriver cameraTracker;

    private void Start()
    {
        // Setup VR camera
        var camera = transform.Find("Main Camera").gameObject;

        xrOrigin = new GameObject("XR Origin").transform;
        xrOrigin.position = camera.transform.position;
        xrOrigin.eulerAngles = new Vector3(0, 180, 0);

        camera.transform.SetParent(xrOrigin, false);

        cameraTracker = camera.AddComponent<TrackedPoseDriver>();
        cameraTracker.positionAction = Actions.Instance.HeadPosition;
        cameraTracker.rotationAction = Actions.Instance.HeadRotation;
        cameraTracker.trackingStateInput = new InputActionProperty(Actions.Instance.HeadTrackingState);

        // Setup interactors
        Instantiate(AssetManager.MenuControllers, xrOrigin);

        // Disable default input module
        var input = FindFirstObjectByType<InputSystemUIInputModule>(FindObjectsInactive.Include);
        if (input)
            input.enabled = false;

        // Set up canvasses
        // Start runs after the scene singletons have finished Awake.
        var intro = FindFirstObjectByType<IntroScreenAnimator>(FindObjectsInactive.Include);
        var mainMenuCanvas = MainMenuHandler.Instance.UIHandler.GetComponentInParent<Canvas>(true).rootCanvas;
        var modalCanvas = Modal.Instance.GetComponentInParent<Canvas>(true).rootCanvas;
        canvases = [mainMenuCanvas, modalCanvas, intro ? intro.GetComponentInParent<Canvas>(true) : null];
        foreach (var canvas in canvases)
        {
            if (!canvas)
                continue;

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera.GetComponent<Camera>();
            canvas.transform.SetPositionAndRotation(xrOrigin.position + xrOrigin.forward * 3f + Vector3.up, xrOrigin.rotation);
            canvas.transform.localScale = Vector3.one * 0.0033f;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
        }

        var background = modalCanvas.transform.Find("Image");
        if (background)
            background.localScale = Vector3.one;
        // Required for VR UI interactions
        mainMenuCanvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        if (modalCanvas != mainMenuCanvas)
            modalCanvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        // Create keyboard
        var keyboard = Instantiate(AssetManager.Keyboard).GetComponent<NonNativeKeyboard>();
        keyboard.SubmitOnEnter = true;
        keyboard.transform.position = new Vector3(-20.0352f, 4.4452f, 34.5683f);
        keyboard.transform.eulerAngles = new Vector3(20, 180, 0);
        keyboard.transform.localScale = Vector3.one * 0.005f;

        var autoKeyboard = mainMenuCanvas.gameObject.AddComponent<AutoKeyboard>();
        autoKeyboard.keyboard = keyboard;

        if (Plugin.Config.FirstTimeLaunch.Value)
        {
            Modal.Show("Welcome to CW // VR",
                "Due to the nature of this game, the VR mod might cause more severe motion sickness than usual in some individuals.\nIf you suffer from motion sickness easily, it is recommended to enable \"Reduced Motion Sickness\".\nGoing for the full experience means that the camera can rotate on its own during ragdolls, death, sleeping and certain enemy attacks.\n(You can always change this setting in the VR Settings later).\n\nI wish you happy travels in trying to get SpöökFamous in VR!\n- DaXcess",
                [
                    new ModalOption("Full Experience", () => Plugin.Config.DisableRagdollCamera.Value = false),
                    new ModalOption("Reduced Motion Sickness", () => Plugin.Config.DisableRagdollCamera.Value = true)
                ], () => Plugin.Config.FirstTimeLaunch.Value = false);
        }

        StartCoroutine(ToggleDoF(false));
        StartCoroutine(AutoRotate());
    }

    private void OnDestroy()
    {
        if (VRSession.InVR || !cameraTracker) // If we're still in VR we just went into a game, destroying will be done for us by the engine
            return;
        
        // Revert camera
        var camera = cameraTracker.gameObject;
        Destroy(cameraTracker);

        camera.transform.SetParent(transform);
        camera.transform.localPosition = new Vector3(33.13f, 4.67f, 70.56f);
        camera.transform.localEulerAngles = new Vector3(0, 162.6179f, 0);
        
        // Destroy XR origin
        Destroy(xrOrigin.gameObject);
        
        // Re-enable default input system
        var input = FindFirstObjectByType<InputSystemUIInputModule>(FindObjectsInactive.Include);
        if (input)
            input.enabled = true;
        
        // Revert canvasses
        foreach (var canvas in canvases)
            if (canvas)
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Destroy keyboard
        Destroy(FindFirstObjectByType<AutoKeyboard>());
        Destroy(FindFirstObjectByType<NonNativeKeyboard>(FindObjectsInactive.Include).gameObject);
        
        // Re-enable Depth of Field
        StartCoroutine(ToggleDoF(true));
    }

    /// <summary>
    /// Disable Depth of Field since it incorrectly blurs text on the world canvas.
    /// </summary>
    private static IEnumerator ToggleDoF(bool active)
    {
        PostVolumeHandler pvh;
        while ((pvh = FindFirstObjectByType<PostVolumeHandler>()) is null)
            yield return null;

        yield return new WaitUntil(() => pvh.m_volume is not null);
        yield return new WaitUntil(() => pvh.m_volume.profile is not null);
        
        if (pvh.m_volume.profile.TryGet<DepthOfField>(out var dof))
            dof.active = active;
    }

    private IEnumerator AutoRotate()
    {
        var action = Actions.Instance.HeadRotation;
        
        while (action.ReadValue<Quaternion>().eulerAngles == Vector3.zero)
            yield return null;
        
        xrOrigin.eulerAngles = new Vector3(0, 180 - action.ReadValue<Quaternion>().eulerAngles.y, 0);
    }
}