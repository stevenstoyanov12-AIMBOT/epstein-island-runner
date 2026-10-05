using UnityEngine;
using UnityEngine.InputSystem;

// Shows a prompt when the player stands in front of the church door and
// teleports them into the museum when E is pressed.
public class MuseumEntrance : MonoBehaviour
{
    public FirstPersonController player;
    public Transform interiorSpawn;
    public Vector3 triggerCenter = new Vector3(15.0f, 1.0f, 24.0f);
    public Vector3 triggerHalfExtents = new Vector3(4.5f, 2.5f, 3.0f);
    public string prompt = "Press E to enter Pump Museum";

    private bool playerInside;
    private GUIStyle style;
    private GUIStyle boxStyle;

    void Update()
    {
        if (player == null) { var pg = GameObject.Find("Player"); if (pg) player = pg.GetComponent<FirstPersonController>(); }
        if (player == null) return;
        Vector3 d = player.transform.position - triggerCenter;
        playerInside = Mathf.Abs(d.x) <= triggerHalfExtents.x &&
                       Mathf.Abs(d.y) <= triggerHalfExtents.y &&
                       Mathf.Abs(d.z) <= triggerHalfExtents.z;

        var kb = Keyboard.current;
        if (playerInside && kb != null && kb.eKey.wasPressedThisFrame && interiorSpawn != null)
        {
            player.Teleport(interiorSpawn.position, interiorSpawn.eulerAngles.y);
            playerInside = false;
        }
    }

    void OnGUI()
    {
        if (!playerInside) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            boxStyle = new GUIStyle(GUI.skin.box);
        }
        float w = 560f, h = 70f;
        Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.72f, w, h);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.Label(r, prompt, style);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
        Gizmos.DrawCube(triggerCenter, triggerHalfExtents * 2f);
    }
}
