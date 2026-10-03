using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal orbit/zoom camera for the planet demo. Drag with the left mouse button to orbit,
/// scroll to zoom. Also supports a slow idle drift so the scene is never static.
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    public Transform target;

    [Header("Orbit")]
    public float distance = 6f;
    public float minDistance = 1.2f;
    public float maxDistance = 40f;
    public float yaw = 20f;
    public float pitch = 15f;
    public float dragSensitivity = 0.25f;
    public float scrollSensitivity = 0.15f;

    [Header("Idle drift")]
    public bool autoOrbit = true;
    public float autoOrbitSpeed = 1.5f;

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (mouse != null)
        {
            if (mouse.leftButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue() * dragSensitivity;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance * (1f - scroll * scrollSensitivity * 0.01f),
                                       minDistance, maxDistance);
        }

        // Fall back to the legacy axis so the demo still works if the new Input System
        // package stops resolving devices.
        if (autoOrbit && (mouse == null || !mouse.leftButton.isPressed))
            yaw += autoOrbitSpeed * Time.deltaTime;

        if (kb != null && kb.escapeKey.wasPressedThisFrame) autoOrbit = false;

        Apply();
    }

    void Apply()
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 dir = rot * Vector3.forward;
        Vector3 focus = target != null ? target.position : Vector3.zero;
        transform.position = focus - dir * distance;
        transform.rotation = rot;
    }
}
