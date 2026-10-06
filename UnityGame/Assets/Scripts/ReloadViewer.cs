using UnityEngine;
using UnityEngine.InputSystem;

// Reload test camera: drag to orbit, wheel to zoom, Space restarts the clip, 1/2/3 = 1x / 0.5x / 0.25x speed.
public class ReloadViewer : MonoBehaviour
{
    public Animator target;
    float yaw = 30f, pitch = 10f, dist = 2.6f;

    void Update()
    {
        var kb = Keyboard.current; var m = Mouse.current;
        if (target != null && kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) target.Play(0, 0, 0f);
            if (kb.digit1Key.wasPressedThisFrame) target.speed = 1f;
            if (kb.digit2Key.wasPressedThisFrame) target.speed = 0.5f;
            if (kb.digit3Key.wasPressedThisFrame) target.speed = 0.25f;
        }
        if (m != null)
        {
            if (m.leftButton.isPressed) { var d = m.delta.ReadValue(); yaw += d.x * 0.3f; pitch = Mathf.Clamp(pitch - d.y * 0.3f, -20f, 60f); }
            dist = Mathf.Clamp(dist - m.scroll.ReadValue().y * 0.002f, 0.8f, 6f);
        }
        var c = (target != null ? target.transform.position : Vector3.zero) + Vector3.up * 1.2f;
        transform.position = c + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -dist);
        transform.LookAt(c);
    }

    void OnGUI() { GUI.Label(new Rect(10, 10, 600, 22), "Drag: orbit   Wheel: zoom   Space: restart   1/2/3: speed 1x/0.5x/0.25x"); }
}
