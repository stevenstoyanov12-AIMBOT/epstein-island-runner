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
    public Material dustMaterial, gritMaterial;
    public Mesh bulletMesh;                  // the game's BulletCasing model, flown to the impact like SimpleGun does
    Material brass;

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
        FlyBullet(hit.point, hit.normal);
        if (statue != null) statue.Hit(hit.point, ray.direction, hit.normal);
        else if (hit.rigidbody != null) hit.rigidbody.AddForceAtPosition(ray.direction * 3f, hit.point, ForceMode.Impulse);
    }

    // same as SimpleGun.FlyBullet in the game: a spinning brass bullet flies from the gun to the hit and drops off it
    void FlyBullet(Vector3 target, Vector3 normal)
    {
        if (bulletMesh == null) return;
        if (brass == null)
        {
            brass = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BrassCasing" };
            brass.SetColor("_BaseColor", new Color(0.86f, 0.62f, 0.22f));
            brass.SetFloat("_Metallic", 0.9f);
            brass.SetFloat("_Smoothness", 0.7f);
        }
        var t = transform;
        Vector3 from = t.position + t.right * 0.15f - t.up * 0.12f + t.forward * 0.4f;
        var g = new GameObject("Bullet");
        g.layer = 2;   // Ignore Raycast: a landed bullet must not block the next shot
        g.transform.position = from;
        g.transform.localScale = Vector3.one * 0.0095f * 1.6f;
        g.transform.rotation = Quaternion.LookRotation(target - from) * Quaternion.Euler(90f, 0, 0);
        g.AddComponent<MeshFilter>().sharedMesh = bulletMesh;
        var mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = brass;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var f = g.AddComponent<StatueBullet>();
        f.from = from; f.to = target + normal * 0.03f; f.normal = normal; f.mesh = bulletMesh;
    }

    void Respawn()
    {
        if (statuePrefab == null) return;
        foreach (var s in FindObjectsByType<DestructibleStatue>(FindObjectsSortMode.None)) Destroy(s.gameObject);
        var go = Instantiate(statuePrefab, statuePosition, statueRotation);
        go.name = statuePrefab.name;
        var ds = go.AddComponent<DestructibleStatue>();
        ds.dustMaterial = dustMaterial;
        ds.gritMaterial = gritMaterial;
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

// Port of the game's BulletFlight: lerps to the target spinning, then becomes a small physics object and falls.
public class StatueBullet : MonoBehaviour
{
    public Vector3 from, to, normal; public Mesh mesh; float t, dur; bool landed;
    void Start() { dur = Mathf.Clamp(Vector3.Distance(from, to) / 45f, 0.08f, 0.5f); }
    void Update()
    {
        if (landed) return;
        t += Time.deltaTime / dur; transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t)); transform.Rotate(Vector3.up, 900f * Time.deltaTime, Space.Self);
        if (t < 1f) return;
        landed = true;
        var cc = gameObject.AddComponent<CapsuleCollider>(); cc.direction = 1; cc.center = mesh.bounds.center; cc.height = mesh.bounds.size.y; cc.radius = mesh.bounds.extents.x;
        var rb = gameObject.AddComponent<Rigidbody>(); rb.mass = 0.01f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = normal * 0.8f + Random.insideUnitSphere * 0.3f; rb.angularVelocity = Random.insideUnitSphere * 12f;
        Destroy(gameObject, 3.5f);
    }
}
