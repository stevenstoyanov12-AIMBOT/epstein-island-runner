using UnityEngine;
using UnityEngine.InputSystem;

// Shows 'Press E to enter balcony' while looking at this door; E teleports the player.
public class DoorTeleport : MonoBehaviour
{
    public Vector3 destination = new Vector3(-26f, 10.8f, 17.8f);
    public float destinationYaw = 90f;
    public float range = 6f;
    public float maxAngle = 30f;
    public string prompt = "Press E to enter balcony";
    bool looking;
    GUIStyle style;

    bool Looking(Camera cam)
    {
        var r = GetComponent<Renderer>(); if (r == null) return false;
        Bounds b = r.bounds; Vector3 cp = cam.transform.position;
        if (Vector3.Distance(b.ClosestPoint(cp), cp) > range) return false;
        return Vector3.Angle(cam.transform.forward, b.center - cp) <= maxAngle;
    }

    void Update()
    {
        var cam = Camera.main; looking = false; if (cam == null) return;
        var player = cam.GetComponentInParent<FirstPersonController>(); if (player == null) return;
        looking = Looking(cam);
        var kb = Keyboard.current;
        if (looking && kb != null && kb.eKey.wasPressedThisFrame) { player.Teleport(destination, destinationYaw); looking = false; }
    }

    void OnGUI()
    {
        if (!looking) return;
        if (style == null) { style = new GUIStyle(GUI.skin.label); style.fontSize = 26; style.alignment = TextAnchor.MiddleCenter; style.normal.textColor = Color.white; }
        var rect = new Rect(0, Screen.height * 0.6f, Screen.width, 40);
        var sh = new GUIStyle(style); sh.normal.textColor = new Color(0, 0, 0, 0.8f);
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), prompt, sh);
        GUI.Label(rect, prompt, style);
    }
}
