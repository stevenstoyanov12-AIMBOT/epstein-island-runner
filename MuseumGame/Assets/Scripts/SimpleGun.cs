using UnityEngine;
using UnityEngine.InputSystem;

// Temporary hitscan gun: left click fires; every surface gets a bullet mark + dust, and hit objects get OnBulletHit.
public class SimpleGun : MonoBehaviour
{
    public float range = 200f, fireDelay = 0.09f; public float kickUp = 2.4f, kickSide = 0.7f, recover = 0.65f; float pendUp, pendSide, owed, spread;
    float next; static Material holeMat, dustMat;
    void Update()
    {
        var m = Mouse.current; var cam = Camera.main;
        Recoil(); if (CrateSpawn.Hidden) return; if (m == null || cam == null || !m.leftButton.wasPressedThisFrame || Time.time < next) return; // semi-auto: one shot per click
        Kick();
        next = Time.time + fireDelay;
        var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        RaycastHit hit = default(RaycastHit); bool found = false;
        // third-person camera sits behind the player: ignore anything between the camera and the player's body,
        // then make sure nothing blocks the line from the gun (chest) to the aimed point (no shooting through walls)
        Vector3 chest = transform.root.position + Vector3.up * 1.2f;
        float skip = Mathf.Max(0f, Vector3.Dot(chest - cam.transform.position, cam.transform.forward) - 0.1f);
        foreach (var hh in hits) { if (hh.collider.transform.IsChildOf(transform.root) || hh.distance < skip) continue; hit = hh; found = true; break; }
        Vector3 aimPoint = found ? hit.point : cam.transform.position + cam.transform.forward * range;
        Vector3 toAim = aimPoint - chest; float aimLen = toAim.magnitude;
        if (aimLen > 0.05f)
        {
            var block = Physics.RaycastAll(chest, toAim / aimLen, aimLen - 0.02f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(block, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var bh in block) { if (bh.collider.transform.IsChildOf(transform.root)) continue; hit = bh; found = true; break; }
        }
        if (found)
        {
            var mr = hit.collider.GetComponent<Renderer>();
            if (mr && mr.sharedMaterial && mr.sharedMaterial.name.StartsWith("Hall_Stone")) { StoneImpactFX.Play(hit, mr.sharedMaterial); }
            else if (!hit.collider.GetComponentInParent<GlassCase>()) Impact(hit);
            hit.collider.SendMessageUpwards("OnBulletHit", hit, SendMessageOptions.DontRequireReceiver);
            FlyBullet(cam, hit.point, hit.normal);
        }
    }
    public static void Impact(RaycastHit hit)
    {
        Dust(hit.point, hit.normal, 25, 0.6f, new Color(0.75f,0.73f,0.7f,0.8f));
    }
    public static void Hole(RaycastHit hit)
    {
        if (!holeMat) holeMat = Res.Load<Material>("FX_Hole");
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
        q.transform.SetPositionAndRotation(hit.point + hit.normal * 0.01f, Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0,0,Random.Range(0,360)));
        q.transform.localScale = Vector3.one * Random.Range(0.06f, 0.1f); q.GetComponent<Renderer>().sharedMaterial = holeMat;
        q.transform.SetParent(hit.collider.transform, true); Destroy(q, 60f);
    }
    public static ParticleSystem Dust(Vector3 p, Vector3 n, int count, float speed, Color c)
    {
        if (!dustMat) dustMat = Res.Load<Material>("FX_Dust");
        var go = new GameObject("Dust"); go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(n));
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMat;
        var mn = ps.main; mn.loop = false; mn.duration = 0.2f; mn.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.2f); mn.startColor = c;
        mn.startSpeed = new ParticleSystem.MinMaxCurve(speed*0.3f, speed*2f); mn.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.12f); mn.gravityModifier = 0.6f;
        var e = ps.emission; e.rateOverTime = 0; e.SetBursts(new[]{ new ParticleSystem.Burst(0, count) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35; sh.radius = 0.02f;
        ps.Play(); Destroy(go, 0.5f); return ps;
    }
    static Mesh casingMesh; static Material casingMat;
    // the bullet flies from the gun to where you shot, drops there and lies around for a few seconds
    void FlyBullet(Camera cam, Vector3 target, Vector3 normal)
    {
        if (casingMesh == null) casingMesh = Res.Load<Mesh>("BulletCasing");
        if (casingMesh == null) return;
        if (casingMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit"); casingMat = new Material(sh) { name = "BrassCasing" };
            casingMat.SetColor("_BaseColor", new Color(0.86f, 0.62f, 0.22f)); casingMat.SetFloat("_Metallic", 0.9f); casingMat.SetFloat("_Smoothness", 0.7f);
        }
        var t = cam.transform; Vector3 from = t.position + t.right * 0.15f - t.up * 0.12f + t.forward * 0.4f;
        var g = new GameObject("BulletCasing"); g.transform.position = from; g.transform.localScale = Vector3.one * 0.0095f * 1.6f;
        g.transform.rotation = Quaternion.LookRotation(target - from) * Quaternion.Euler(90f, 0, 0);
        g.AddComponent<MeshFilter>().sharedMesh = casingMesh; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = casingMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var f = g.AddComponent<BulletFlight>(); f.from = from; f.to = target + normal * 0.03f; f.normal = normal; f.mesh = casingMesh; f.ignoreRoot = transform.root;
    }
    void Kick()
    {
        float up = kickUp * Random.Range(0.85f, 1.15f); pendUp += up; owed += up * recover;
        pendSide += Random.Range(-kickSide, kickSide); spread = Mathf.Min(spread + 9f, 26f);
    }
    // snappy upward kick, then slow settle back down (like a real pistol)
    void Recoil()
    {
        float dt = Time.deltaTime;
        float a = pendUp * Mathf.Min(1f, dt * 30f), s = pendSide * Mathf.Min(1f, dt * 30f);
        pendUp -= a; pendSide -= s;
        float back = 0f; if (pendUp < 0.05f && owed > 0f) { back = Mathf.Min(owed, owed * dt * 7f + dt * 0.5f); owed -= back; }
        FirstPersonController.recoilP += a - back; FirstPersonController.recoilY += s;
        spread = Mathf.MoveTowards(spread, 0f, dt * 45f);
    }
    static Texture2D ring, dot;
    static Texture2D MakeRing(int size, float r, float w, bool fill)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false); t.filterMode = FilterMode.Bilinear; float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float core = fill ? Mathf.Clamp01(r - d + 0.5f) : Mathf.Clamp01(w * 0.5f - Mathf.Abs(d - r) + 0.5f);
            float edge = fill ? Mathf.Clamp01(r + 1.5f - d + 0.5f) : Mathf.Clamp01(w * 0.5f + 1.5f - Mathf.Abs(d - r) + 0.5f);
            // white core with dark outline for visibility on bright walls
            var col = Color.Lerp(new Color(0, 0, 0, edge * 0.55f), new Color(1, 1, 1, 0.92f), core);
            t.SetPixel(x, y, col);
        }
        t.Apply(); return t;
    }
    void OnGUI()
    {
        if (!ring) ring = MakeRing(128, 58f, 3.2f, false);
        if (!dot) dot = MakeRing(16, 2.2f, 0, true);
        float cx = Screen.width / 2f, cy = Screen.height / 2f, R = 11f + spread * 0.9f, s = R * 128f / 58f;
        GUI.DrawTexture(new Rect(cx - s / 2f, cy - s / 2f, s, s), ring);
        GUI.DrawTexture(new Rect(cx - 8, cy - 8, 16, 16), dot);
    }
}

public class BulletFlight : MonoBehaviour
{
    public Vector3 from, to, normal; public Mesh mesh; public Transform ignoreRoot; float t, dur; bool landed;
    void Start() { dur = Mathf.Clamp(Vector3.Distance(from, to) / 45f, 0.08f, 0.5f); }
    void Update()
    {
        if (landed) return;
        t += Time.deltaTime / dur; transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t)); transform.Rotate(Vector3.up, 900f * Time.deltaTime, Space.Self);
        if (t < 1f) return;
        landed = true;
        var cc = gameObject.AddComponent<CapsuleCollider>(); cc.direction = 1; cc.center = mesh.bounds.center; cc.height = mesh.bounds.size.y; cc.radius = mesh.bounds.extents.x;
        if (ignoreRoot) foreach (var pc in ignoreRoot.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(cc, pc, true);
        var rb = gameObject.AddComponent<Rigidbody>(); rb.mass = 0.01f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = normal * 0.8f + Random.insideUnitSphere * 0.3f; rb.angularVelocity = Random.insideUnitSphere * 12f;
        Destroy(gameObject, 3.5f);
    }
}
