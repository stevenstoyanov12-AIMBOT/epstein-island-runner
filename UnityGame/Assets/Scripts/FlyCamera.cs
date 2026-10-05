using UnityEngine;
using UnityEngine.InputSystem;

// Free-fly inspection camera for Play mode.
// Click to capture the mouse, move the mouse to look around 360 degrees, Esc to release it.
// WASD move, E / Space up, Q / Ctrl down, Shift faster, mouse wheel changes speed.
public class FlyCamera : MonoBehaviour
{
    public float moveSpeed = 2f;
    public float fastMultiplier = 4f;
    public float lookSensitivity = 0.12f;

    float yaw;
    float pitch;

    void OnEnable()
    {
        var e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            var delta = mouse.delta.ReadValue() * lookSensitivity;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
            moveSpeed = Mathf.Clamp(moveSpeed * (scroll > 0f ? 1.15f : 1f / 1.15f), 0.1f, 50f);

        var move = Vector3.zero;
        if (kb.wKey.isPressed) move += Vector3.forward;
        if (kb.sKey.isPressed) move += Vector3.back;
        if (kb.aKey.isPressed) move += Vector3.left;
        if (kb.dKey.isPressed) move += Vector3.right;
        if (kb.eKey.isPressed || kb.spaceKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed || kb.leftCtrlKey.isPressed) move += Vector3.down;

        float speed = moveSpeed * (kb.leftShiftKey.isPressed ? fastMultiplier : 1f);
        // horizontal moves follow where you look; up/down stay vertical
        var world = transform.TransformDirection(new Vector3(move.x, 0f, move.z)) + Vector3.up * move.y;
        transform.position += world * speed * Time.deltaTime;
    }
}
