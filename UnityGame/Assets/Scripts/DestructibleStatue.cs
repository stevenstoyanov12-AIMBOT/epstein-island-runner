using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Uncharted-style marble statue that comes apart under fire. Put it on the root of VenusStatue.fbx.
// Children: "Intact" (seamless shell shown until the first real break), Fig_## (figure pieces), Base_## (plinth pieces).
// Every hit gets the game's stone impact (fragments, lingering dust, grit). The first shots only chip the surface;
// after that each hit knocks off the figure pieces near the impact, the killing shot drops whatever is left, and
// pieces that lose their support fall with them. The plinth never breaks.
public class DestructibleStatue : MonoBehaviour
{
    public int chipOnlyHits = 2;            // these first hits only chip the surface
    public float hitRadius = 0.18f;         // figure pieces whose surface is this close to the impact break off
    public int hitsToCollapse = 6;          // this hit on the figure brings the rest of it down (plinth hits don't count)
    public Vector2 pushSpeed = new Vector2(0.2f, 0.6f);   // m/s along the bullet for a piece that is hit directly
    public float debrisLifetime = 8f;       // then the debris sinks into the ground and is removed
    public Material dustMaterial, gritMaterial;

    // Fired for every hit, so multiplayer can replay remote shots: point and direction in world space.
    public event Action<Vector3, Vector3> OnHit;

    class Piece
    {
        public Transform t;
        public Collider col;
        public Bounds bounds;
        public bool figure, loose;
        public List<Piece> touching = new List<Piece>();
        public List<Contact> contacts = new List<Contact>();
    }

    // How two pieces actually meet: how many surface points lie on the other piece, and where.
    class Contact { public Piece other; public int points; public Vector3 centre; }

    readonly List<Piece> pieces = new List<Piece>();
    readonly List<Collider> statueColliders = new List<Collider>();
    GameObject intact;
    Material marble;
    int hits;

    void Awake()
    {
        Debug.Log("DestructibleStatue v4: real-contact support, no floating pieces");
        var tr = transform.Find("Intact");
        if (tr != null)
        {
            intact = tr.gameObject;
            marble = intact.GetComponent<Renderer>().sharedMaterial;
            // the shell gets its own collider so the chip-only shots land on the real surface
            var mc = intact.AddComponent<MeshCollider>();
            mc.sharedMesh = intact.GetComponent<MeshFilter>().sharedMesh;
            statueColliders.Add(mc);
        }
        foreach (Transform c in transform)
        {
            bool fig = c.name.StartsWith("Fig");
            if (!fig && !c.name.StartsWith("Base")) continue;
            var mf = c.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var mc = c.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = fig;                                   // only figure pieces ever get a Rigidbody
            mc.enabled = intact == null;
            var r = c.GetComponent<Renderer>();
            r.enabled = intact == null;
            if (marble == null) marble = r.sharedMaterial;
            pieces.Add(new Piece { t = c, col = mc, figure = fig, bounds = r.bounds });
            statueColliders.Add(mc);
        }
        // which pieces really touch: sample each piece's surface and count points lying on its neighbour
        foreach (var p in pieces) p.col.enabled = true;
        Physics.SyncTransforms();
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var a = pieces[i].bounds; a.Expand(0.02f);
                if (!a.Intersects(pieces[j].bounds)) continue;
                Measure(pieces[i], pieces[j]);
            }
        if (intact != null) foreach (var p in pieces) p.col.enabled = false;
    }

    const float ContactGap = 0.006f;     // surface points this close to the neighbour count as touching
    const int MinContact = 6;            // fewer touching points than this is a crumb-thin link: it holds nothing

    void Measure(Piece a, Piece b)
    {
        int n = 0; var sum = Vector3.zero;
        foreach (var (src, dst) in new[] { (a, b), (b, a) })
        {
            var verts = src.t.GetComponent<MeshFilter>().sharedMesh.vertices;
            int step = Mathf.Max(1, verts.Length / 400);
            for (int k = 0; k < verts.Length; k += step)
            {
                var w = src.t.TransformPoint(verts[k]);
                if (!dst.bounds.Contains(w) && dst.bounds.SqrDistance(w) > ContactGap * ContactGap) continue;
                var c = dst.col.ClosestPoint(w);
                if ((c - w).sqrMagnitude < ContactGap * ContactGap) { n++; sum += w; }
            }
        }
        if (n < MinContact) return;
        var centre = sum / n;
        a.touching.Add(b); b.touching.Add(a);
        a.contacts.Add(new Contact { other = b, points = n, centre = centre });
        b.contacts.Add(new Contact { other = a, points = n, centre = centre });
    }

    public bool Collapsed => hits >= hitsToCollapse;

    public void Hit(Vector3 point, Vector3 direction) => Hit(point, direction, -direction);

    public void Hit(Vector3 point, Vector3 direction, Vector3 normal)
    {
        OnHit?.Invoke(point, direction);
        direction = direction.normalized;
        normal = normal.sqrMagnitude > 0 ? normal.normalized : -direction;

        StatueImpactFX.Play(point, normal, marble, dustMaterial, gritMaterial, 1f, true, statueColliders);
        var nearest = Nearest(point);
        if (nearest == null || !nearest.figure) return;       // the plinth only chips, and doesn't count as a hit
        hits++;
        if (hits <= chipOnlyHits) return;                     // still just chipping the surface
        ShowPieces();
        if (hits >= hitsToCollapse)
        {
            foreach (var p in pieces) if (p.figure) Break(p, point, direction, 0.15f);
        }
        else
        {
            foreach (var p in pieces)
                if (p.figure && !p.loose && Vector3.Distance(p.col.ClosestPoint(point), point) < hitRadius)
                    Break(p, point, direction, p == nearest ? 1f : 0.4f);
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

    Piece Nearest(Vector3 point)
    {
        Piece best = null;
        float bestD = float.MaxValue;
        foreach (var p in pieces)
        {
            if (p.loose) continue;
            float d = Vector3.Distance(p.bounds.ClosestPoint(point), point);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    // A piece comes loose and falls at full weight: it never touches the other figure pieces, standing or loose
    // (like the game's FragmentFade), so nothing holds it up or pops it out; it lands on the plinth or the ground.
    void Break(Piece p, Vector3 point, Vector3 direction, float strength)
    {
        if (p.loose || !p.figure) return;
        p.loose = true;
        foreach (var o in pieces)
            if (o != p && o.figure) Physics.IgnoreCollision(p.col, o.col, true);   // still lands on the plinth and the ground

        var b = p.bounds;
        var rb = p.t.gameObject.AddComponent<Rigidbody>();
        rb.mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * 1000f, 2f, 300f);
        rb.linearDamping = 0f;
        rb.angularDamping = 0.3f;
        rb.maxDepenetrationVelocity = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        rb.AddForce(flat * UnityEngine.Random.Range(pushSpeed.x, pushSpeed.y) * strength, ForceMode.VelocityChange);
        rb.AddTorque(UnityEngine.Random.insideUnitSphere * 1.5f * strength, ForceMode.VelocityChange);

        StatueImpactFX.Play(b.center, -direction, marble, dustMaterial, gritMaterial, 1.5f, false);
        p.t.gameObject.AddComponent<StatueDebris>();
        StartCoroutine(Sink(p.t, rb));
    }

    // Figure pieces that are no longer carried up from the plinth fall. A piece is carried by a standing neighbour
    // it really touches, where the touch is below or beside its centre (a piece can't hang from one above it).
    void DropUnsupported()
    {
        var held = new HashSet<Piece>();
        var queue = new Queue<Piece>();
        foreach (var p in pieces)
            if (!p.figure) { held.Add(p); queue.Enqueue(p); }
        while (queue.Count > 0)
        {
            var below = queue.Dequeue();
            foreach (var c in below.contacts)
            {
                var up = c.other;
                if (up.loose || held.Contains(up)) continue;
                if (c.centre.y > up.bounds.center.y + 0.03f) continue;     // touching only from above: no support
                held.Add(up); queue.Enqueue(up);
            }
        }
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
}

// Keeps a falling statue piece honest: it may slide and tumble, but it never gets thrown upward or flung far
// (whatever it bumps into), so it always drops like a heavy stone.
public class StatueDebris : MonoBehaviour
{
    Rigidbody rb;
    void Awake() { rb = GetComponent<Rigidbody>(); }
    void FixedUpdate()
    {
        var v = rb.linearVelocity;
        if (v.y > 0.3f) v.y = 0.3f;                                    // a small bounce at most
        var flat = new Vector2(v.x, v.z);
        if (flat.magnitude > 2.5f) { flat = flat.normalized * 2.5f; v.x = flat.x; v.z = flat.y; }
        rb.linearVelocity = v;
        if (rb.angularVelocity.magnitude > 8f) rb.angularVelocity = rb.angularVelocity.normalized * 8f;
    }
}
