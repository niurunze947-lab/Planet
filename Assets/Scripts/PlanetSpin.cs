using UnityEngine;

/// <summary>Rotates a body about its own axis. Negative speed spins retrograde (Venus-like).</summary>
public class PlanetSpin : MonoBehaviour
{
    [Tooltip("Degrees per second about the local Y axis.")]
    public float degreesPerSecond = 6f;

    [Tooltip("Tilt of the rotation axis in degrees.")]
    public float axialTilt = 23.4f;

    void Start()
    {
        transform.localRotation = Quaternion.Euler(0f, 0f, -axialTilt);
    }

    void Update()
    {
        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}
