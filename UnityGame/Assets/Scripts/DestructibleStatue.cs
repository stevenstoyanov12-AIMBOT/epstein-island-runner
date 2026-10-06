using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Uncharted-style marble statue that comes apart under fire. Put it on the root of VenusStatue.fbx.
// Children: "Intact" (seamless shell shown until the first real break), Fig_## (figure pieces), Base_## (plinth pieces).
// The first shots only chip it: a puff of marble dust and a few small fragments trickling down. After that each hit
// knocks off the pieces near the impact, every few hits a plinth chunk goes, the killing shot drops whatever is left,
// and pieces that lose their support fall with them. Pieces drop and tumble, they are not launched.
public class DestructibleStatue : MonoBehaviour
{
    public int chipOnlyHits = 2;            // these first hits only chip the surface (dust + fragments)
    public float hitRadius = 0.18f;         // pieces whose surface is this close to the impact break off
    public int hitsToCollapse = 7;          // this shot brings the rest of the figure down
    public int plinthEvery = 3;             // every Nth breaking hit also takes a chunk out of the plinth
    public Vector2 pushSpeed = new Vector2(0.3f, 1.1f);   // m/s along the bullet for a piece that is hit directly
    public float debrisLifetime = 8f;       // then the debris sinks into the ground and is removed
    public Material dustMaterial;

    // Fired for every hit, so multiplayer can replay remote shots: point and direction in world space.
    public event Action<Vector3, Vector3> OnHit;

    class Piece
    {
        public Transform t;
        public Collider col;
        public Bounds bounds;
        public bool figure, loose;
        public List<Piece> touching = new List<Piece>();
    }

    readonly List<Piece> pieces = new List<Piece>();
    GameObject intact;
    ParticleSystem puff, powder;
    Material marble;
    int hits, breaks;

    static Mesh chipMesh;
    static readonly Queue<GameObject> chips = new Queue<GameObject>();
    const int MaxChips = 80;

    void Awake()
    {
        var tr = transform.Find("Intact");
        if (tr != null)
        {
            intact = tr.gameObject;
            marble = intact.GetComponent<Renderer>().sharedMaterial;
            // the shell gets its own collider so the chip-only shots land on the real surface
            var mc = intact.AddComponent<MeshCollider>();
            mc.sharedMesh = intact.GetComponent<MeshFilter>().sharedMesh;
        }
        foreach (Transform c in transform)
        {
            bool fig = c.name.StartsWith("Fig");
            if (!fig && !c.name.StartsWith("Base")) continue;
            var mf = c.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var mc = c.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;                                  // needed once the piece gets a Rigidbody
            mc.enabled = intact == null;
            var r = c.GetComponent<Renderer>();
            r.enabled = intact == null;
            if (marble == null) marble = r.sharedMaterial;
            pieces.Add(new Piece { t = c, col = mc, figure = fig, bounds = r.bounds });
        }
        // which pieces rest on which: overlapping bounds, slightly grown
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var a = pieces[i].bounds; a.Expand(0.01f);
                if (!a.Intersects(pieces[j].bounds)) continue;
                pieces[i].touching.Add(pieces[j]);
                pieces[j].touching.Add(pieces[i]);
            }
        puff = MakePuff();
        powder = MakePowder();
    }

    // dustMaterial is usually set right after AddComponent, i.e. after Awake
    void Start()
    {
        if (dustMaterial == null) return;
        foreach (var ps in new[] { puff, powder })
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial;
    }

    public bool Collapsed => hits >= hitsToCollapse;

    public void Hit(Vector3 point, Vector3 direction) => Hit(point, direction, -direction);

    public void Hit(Vector3 point, Vector3 direction, Vector3 normal)
    {
        OnHit?.Invoke(point, direction);
        hits++;
        direction = direction.normalized;
        normal = normal.sqrMagnitude > 0 ? normal.normalized : -direction;

        Puff(point, normal, 30);
        Powder(point, 25);
        Chips(point, normal, UnityEngine.Random.Range(4, 8));
        if (hits <= chipOnlyHits) return;                     // still just chipping the surface

        ShowPieces();
        breaks++;
        var nearest = Nearest(point, null);
        if (nearest == null) return;
        if (hits >= hitsToCollapse)
        {
            foreach (var p in pieces) if (p.figure) Break(p, point, direction, 0.15f);
        }
        else if (nearest.figure)
        {
            foreach (var p in pieces)
                if (p.figure && !p.loose && Vector3.Distance(p.col.ClosestPoint(point), point) < hitRadius)
                    Break(p, point, direction, p == nearest ? 1f : 0.4f);
        }
        if (!nearest.figure || breaks % plinthEvery == 0)
        {
            var chunk = nearest.figure ? Nearest(point, false) : nearest;
            if (chunk != null) Break(chunk, point, direction, 0.5f);
        }
        DropUnsupported();
    }

    void ShowPieces()
    {
        if (intact == null || !intact.activeSelf) return;
        intact.SetActive(false);
        foreach (var p in pieces)
        {
            p.t.GetComponent<Renderer>().enabled = true;
            p.col.enabled = true;
        }
    }

    Piece Nearest(Vector3 point, bool? figure)
    {
        Piece best = null;
        float bestD = float.MaxValue;
        foreach (var p in pieces)
        {
            if (p.loose || (figure.HasValue && p.figure != figure.Value)) continue;
            float d = Vector3.Distance(p.bounds.ClosestPoint(point), point);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    // A piece comes loose: mostly it drops and tips over, a direct hit gives it a small shove along the bullet.
    void Break(Piece p, Vector3 point, Vector3 direction, float strength)
    {
        if (p.loose) return;
        p.loose = true;
        var b = p.bounds;
        var rb = p.t.gameObject.AddComponent<Rigidbody>();
        rb.mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * 1000f, 2f, 300f);   // only the ratio between pieces matters
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.6f;
        rb.maxDepenetrationVelocity = 0.4f;   // pieces start inside their neighbours' hulls: ease out, don't explode out
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // ignore the pieces it was sitting against for a moment, so it slides off instead of being shot out
        var ignored = new List<Collider>();
        foreach (var n in p.touching)
            if (!n.loose) { Physics.IgnoreCollision(p.col, n.col, true); ignored.Add(n.col); }

        var flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        var v = flat * UnityEngine.Random.Range(pushSpeed.x, pushSpeed.y) * strength;
        rb.AddForce(v, ForceMode.VelocityChange);
        rb.AddTorque(UnityEngine.Random.insideUnitSphere * 1.5f * strength, ForceMode.VelocityChange);

        Puff(b.center, -direction, 15);
        Powder(b.center, 40);
        StartCoroutine(Restore(p.col, ignored));
        StartCoroutine(Sink(p.t, rb));
    }

    IEnumerator Restore(Collider c, List<Collider> others)
    {
        yield return new WaitForSeconds(0.35f);
        foreach (var o in others)
            if (c != null && o != null) Physics.IgnoreCollision(c, o, false);
    }

    // Anything that no longer connects to a plinth piece standing on the ground falls.
    void DropUnsupported()
    {
        float floor = float.MaxValue;
        foreach (var p in pieces) floor = Mathf.Min(floor, p.bounds.min.y);
        var held = new HashSet<Piece>();
        var queue = new Queue<Piece>();
        foreach (var p in pieces)
            if (!p.loose && p.bounds.min.y < floor + 0.05f) { held.Add(p); queue.Enqueue(p); }
        while (queue.Count > 0)
            foreach (var n in queue.Dequeue().touching)
                if (!n.loose && held.Add(n)) queue.Enqueue(n);
        foreach (var p in pieces)
            if (!p.loose && !held.Contains(p)) Break(p, p.bounds.center, UnityEngine.Random.onUnitSphere, 0.1f);
    }

    IEnumerator Sink(Transform t, Rigidbody rb)
    {
        yield return new WaitForSeconds(debrisLifetime);
        rb.isKinematic = true;
        foreach (var c in t.GetComponents<Collider>()) c.enabled = false;
        for (float s = 0; s < 2f; s += Time.deltaTime)
        {
            t.position += Vector3.down * 0.25f * Time.deltaTime;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    // --- chips: small marble fragments that spray off the impact and patter down ----------------------

    void Chips(Vector3 at, Vector3 normal, int count)
    {
        if (marble == null) return;
        if (chipMesh == null) chipMesh = MakeChipMesh();
        for (int i = 0; i < count; i++)
        {
            while (chips.Count >= MaxChips)
            {
                var old = chips.Dequeue();
                if (old != null) Destroy(old);
            }
            var go = new GameObject("Chip");
            float s = UnityEngine.Random.Range(0.008f, 0.03f);
            go.transform.SetPositionAndRotation(at + normal * 0.02f, UnityEngine.Random.rotation);
            go.transform.localScale = new Vector3(s, s * UnityEngine.Random.Range(0.5f, 1f), s * UnityEngine.Random.Range(0.6f, 1.2f));
            go.AddComponent<MeshFilter>().sharedMesh = chipMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = marble;
            go.AddComponent<SphereCollider>().radius = 0.4f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.02f;
            rb.angularDamping = 0.2f;
            // out of the hole, a little sideways, then gravity takes over
            var spray = (normal + UnityEngine.Random.insideUnitSphere * 0.7f).normalized;
            rb.linearVelocity = spray * UnityEngine.Random.Range(0.6f, 2.2f);
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 20f;
            chips.Enqueue(go);
            Destroy(go, UnityEngine.Random.Range(4f, 6f));
        }
    }

    static Mesh MakeChipMesh()
    {
        // jagged little rock: an octahedron with jittered corners and flat shading
        var rnd = new System.Random(7);
        Vector3[] c =
        {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
        };
        for (int i = 0; i < c.Length; i++) c[i] *= 0.35f + 0.3f * (float)rnd.NextDouble();
        int[] f = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5 };
        var v = new Vector3[f.Length];
        var t = new int[f.Length];
        for (int i = 0; i < f.Length; i++) { v[i] = c[f[i]]; t[i] = i; }
        var m = new Mesh { name = "MarbleChip", vertices = v, triangles = t };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // --- dust: a quick puff out of the impact, and fine powder that drifts down after it --------------

    void Puff(Vector3 at, Vector3 normal, int count)
    {
        if (puff == null) return;
        puff.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal));
        puff.Emit(count);
    }

    void Powder(Vector3 at, int count)
    {
        if (powder == null) return;
        powder.transform.position = at;
        powder.Emit(count);
    }

    ParticleSystem MakePuff()
    {
        var ps = NewSystem("MarbleDustPuff", out var main);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
        main.startColor = new Color(0.93f, 0.91f, 0.87f, 0.6f);
        main.gravityModifier = 0.02f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.02f;
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 6f;                                     // bursts out, then hangs in the air
        Grow(ps, 0.5f, 3.5f);
        Fade(ps, 0.65f);
        return ps;
    }

    ParticleSystem MakePowder()
    {
        var ps = NewSystem("MarbleDustPowder", out var main);
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
        main.startColor = new Color(0.96f, 0.95f, 0.92f, 0.8f);
        main.gravityModifier = 0.25f;                       // fine grit trickling down the statue
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.04f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 0.8f;
        Grow(ps, 1f, 1.6f);
        Fade(ps, 0.9f);
        return ps;
    }

    ParticleSystem NewSystem(string name, out ParticleSystem.MainModule main)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 3000;
        var em = ps.emission;
        em.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (dustMaterial != null) r.sharedMaterial = dustMaterial;
        return ps;
    }

    static void Grow(ParticleSystem ps, float from, float to)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, from, 1, to));
    }

    static void Fade(ParticleSystem ps, float alpha)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(alpha, 0), new GradientAlphaKey(alpha * 0.6f, 0.4f), new GradientAlphaKey(0f, 1) });
        col.color = g;
    }
}
