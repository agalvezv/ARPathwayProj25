using System.Collections;
using UnityEngine;

public class WorldManager : MonoBehaviour
{
    [SerializeField] private float retryInterval = 1f;
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;
    [SerializeField] private MonoBehaviour[] menuScripts;
    [SerializeField] private float switchDebounce = 0.15f;

    private enum InputMode { Unknown, Controllers, Hands }
    private InputMode currentMode = InputMode.Unknown;
    private InputMode requestedMode = InputMode.Unknown;
    private float lastRequestTime;

    void Awake()
    {
        HideGuardianBoundary();
        StartCoroutine(EnsureBoundaryHidden());
    }

    void Start()
    {
        EvaluateAndApplyMode(immediate: true);
    }

    void Update()
    {
        EvaluateAndApplyMode();
    }

    private void EvaluateAndApplyMode(bool immediate = false)
    {
        bool handTracked = IsHandTracked();
        bool controllerUsed = IsControllerBeingUsed();

        InputMode candidate;
        if (controllerUsed && !handTracked)
            candidate = InputMode.Controllers;
        else if (handTracked && !controllerUsed)
            candidate = InputMode.Hands;
        else if (controllerUsed && handTracked)
            candidate = InputMode.Controllers;
        else
            candidate = currentMode;

        if (candidate != currentMode)
        {
            if (candidate != requestedMode)
            {
                requestedMode = candidate;
                lastRequestTime = Time.time;
            }
            else
            {
                if (immediate || Time.time - lastRequestTime >= switchDebounce)
                {
                    currentMode = candidate;
                    ApplyMode();
                }
            }
        }
        else
        {
            requestedMode = currentMode;
        }
    }

    private bool IsHandTracked()
    {
        bool left = leftHand != null && leftHand.IsTracked;
        bool right = rightHand != null && rightHand.IsTracked;
        return left || right;
    }

    private bool IsControllerBeingUsed()
    {
        if (OVRInput.GetDown(OVRInput.Button.One) ||
            OVRInput.GetDown(OVRInput.Button.Two) ||
            OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
            OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
            return true;

        Vector2 thumbL = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        Vector2 thumbR = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        if (thumbL.magnitude > 0.1f || thumbR.magnitude > 0.1f)
            return true;

        float triggerL = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
        float triggerR = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);
        if (triggerL > 0.1f || triggerR > 0.1f)
            return true;

        return false;
    }

    private void ApplyMode()
    {
        bool enableMenus = currentMode == InputMode.Controllers;
        foreach (var script in menuScripts)
        {
            if (script == null) continue;
            script.enabled = enableMenus;
        }
    }

    private void HideGuardianBoundary()
    {
        if (OVRManager.boundary != null)
        {
            OVRManager.boundary.SetVisible(false);
        }
    }

    private IEnumerator EnsureBoundaryHidden()
    {
        while (true)
        {
            yield return new WaitForSeconds(retryInterval);
            HideGuardianBoundary();
        }
    }
}
