using UnityEngine;
using UnityEngine.InputSystem;

// Test gun for the statue scene. Sits on the FlyCamera: the first click captures the mouse (FlyCamera),
// after that each left click fires a ray from the centre of the screen. R rebuilds the statue.
public class StatueShooter : MonoBehaviour
{
    public float range = 100f;
    public GameObject statuePrefab;          // set by the scene builder so R can respawn a fresh statue
    public Vector3 statuePosition;
    public Quaternion statueRotation = Quaternion.identity;
    public Material dustMaterial;

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame) Respawn();
        if (mouse == null || Cursor.lockState != CursorLockMode.Locked) return;
        if (!mouse.leftButton.wasPressedThisFrame) return;

        var ray = new Ray(transform.position, transform.forward);
        if (!Physics.Raycast(ray, out var hit, range)) return;
        var statue = hit.collider.GetComponentInParent<DestructibleStatue>();
        if (statue != null) statue.Hit(hit.point, ray.direction, hit.normal);
        else if (hit.rigidbody != null) hit.rigidbody.AddForceAtPosition(ray.direction * 3f, hit.point, ForceMode.Impulse);
    }

    void Respawn()
    {
        if (statuePrefab == null) return;
        foreach (var s in FindObjectsByType<DestructibleStatue>(FindObjectsSortMode.None)) Destroy(s.gameObject);
        var go = Instantiate(statuePrefab, statuePosition, statueRotation);
        go.name = statuePrefab.name;
        go.AddComponent<DestructibleStatue>().dustMaterial = dustMaterial;
    }

    void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        float x = Screen.width / 2f, y = Screen.height / 2f;
        GUI.color = new Color(1, 1, 1, 0.85f);
        GUI.DrawTexture(new Rect(x - 1, y - 8, 2, 16), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 8, y - 1, 16, 2), Texture2D.whiteTexture);
        GUI.Label(new Rect(10, 10, 400, 22), "Click: shoot   R: new statue   Esc: free mouse");
    }
}
