using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Destructible marble Venus (Models/Statues/VenusStatue.fbx). Children: "Intact" (seamless shell shown until the
// first real break), Fig_## (figure pieces), Base_## (plinth pieces, never break).
// Hits arrive through SimpleGun's OnBulletHit (local and replayed remote shots), so every client breaks it the same way.
// First shots only chip it (stone impact FX); then pieces near each hit fall; the 6th figure hit drops the rest;
// pieces no longer carried up from the plinth fall with them.
public class VenusStatueBreak : MonoBehaviour, IWorldState
{
    public int chipOnlyHits = 2;
    public float hitRadius = 0.18f;
    public int hitsToCollapse = 6;
    public Vector2 pushSpeed = new Vector2(0.2f, 0.6f);
    public float debrisLifetime = 10f;

    class Piece
    {
        public Transform t; public Collider col; public Bounds bounds; public bool figure, loose;
        public List<Contact> contacts = new List<Contact>();
    }
    class Contact { public Piece other; public Vector3 centre; }

    readonly List<Piece> pieces = new List<Piece>();
    GameObject intact;
    Material marble;
    int hits;
    string key;

    void Start() { WorldState.Register(key, this); }

    // late join: "figure hits:loose piece indices"; loose pieces are simply gone (they sink away after a few seconds anyway)
    public string Save()
    {
        var g = new List<int>(); for (int i = 0; i < pieces.Count; i++) if (pieces[i].loose) g.Add(i);
        return hits + ":" + WorldState.Ints(g);
    }
    public void Load(string s)
    {
        var p = s.Split(':'); if (p.Length < 2) return;
        hits = Mathf.Max(hits, WorldState.PI(p[0]));
        if (hits > chipOnlyHits) ShowPieces();
        foreach (var i in WorldState.PInts(p[1]))
        {
            if (i < 0 || i >= pieces.Count || pieces[i].loose) continue;
            var q = pieces[i]; q.loose = true;
            foreach (var o in pieces) if (o != q && o.figure) Physics.IgnoreCollision(q.col, o.col, true);
            Destroy(q.t.gameObject);
        }
    }

    void Awake()
    {
        key = Net.PathOf(transform);
        var tr = transform.Find("Intact");
        if (tr != null)
        {
            intact = tr.gameObject;
            marble = intact.GetComponent<Renderer>().sharedMaterial;
            if (!intact.GetComponent<Collider>()) intact.AddComponent<MeshCollider>().sharedMesh = intact.GetComponent<MeshFilter>().sharedMesh;
        }
        foreach (Transform c in transform)
        {
            bool fig = c.name.StartsWith("Fig");
            if (!fig && !c.name.StartsWith("Base")) continue;
            var mf = c.GetComponent<MeshFilter>(); if (mf == null) continue;
            var mc = c.GetComponent<MeshCollider>();
            if (mc == null) mc = c.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh; mc.convex = fig;
            var r = c.GetComponent<Renderer>();
            if (marble == null) marble = r.sharedMaterial;
            pieces.Add(new Piece { t = c, col = mc, figure = fig, bounds = r.bounds });
        }
        foreach (var p in pieces) p.col.enabled = true;
        Physics.SyncTransforms();
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var a = pieces[i].bounds; a.Expand(0.02f);
                if (a.Intersects(pieces[j].bounds)) Measure(pieces[i], pieces[j]);
            }
        bool showPieces = intact == null;
        foreach (var p in pieces) { p.col.enabled = showPieces; p.t.GetComponent<Renderer>().enabled = showPieces; }
    }

    const float ContactGap = 0.006f;
    const int MinContact = 6;

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
                if ((dst.col.ClosestPoint(w) - w).sqrMagnitude < ContactGap * ContactGap) { n++; sum += w; }
            }
        }
        if (n < MinContact) return;
        var centre = sum / n;
        a.contacts.Add(new Contact { other = b, centre = centre });
        b.contacts.Add(new Contact { other = a, centre = centre });
    }

    // SimpleGun (local shots and replayed remote shots) delivers hits here
    void OnBulletHit(RaycastHit hit)
    {
        StoneImpactFX.Play(hit, marble);
        var nearest = Nearest(hit.point);
        if (nearest == null || !nearest.figure) return;              // plinth: chips only, doesn't count
        hits++;
        if (hits <= chipOnlyHits) { WorldState.Changed(key, this); return; }
        ShowPieces();
        var dir = -hit.normal;
        if (hits >= hitsToCollapse) { foreach (var p in pieces) if (p.figure) Break(p, dir, 0.15f); }
        else
            foreach (var p in pieces)
                if (p.figure && !p.loose && Vector3.Distance(p.col.ClosestPoint(hit.point), hit.point) < hitRadius)
                    Break(p, dir, p == nearest ? 1f : 0.4f);
        DropUnsupported();
        WorldState.Changed(key, this);
    }

    void ShowPieces()
    {
        if (intact == null || !intact.activeSelf) return;
        intact.SetActive(false);
        foreach (var p in pieces) { p.t.GetComponent<Renderer>().enabled = true; p.col.enabled = true; }
    }

    Piece Nearest(Vector3 point)
    {
        Piece best = null; float bestD = float.MaxValue;
        foreach (var p in pieces)
        {
            if (p.loose) continue;
            float d = Vector3.Distance(p.bounds.ClosestPoint(point), point);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    void Break(Piece p, Vector3 direction, float strength)
    {
        if (p.loose || !p.figure) return;
        p.loose = true;
        foreach (var o in pieces) if (o != p && o.figure) Physics.IgnoreCollision(p.col, o.col, true);
        var b = p.bounds;
        var rb = p.t.gameObject.AddComponent<Rigidbody>();
        rb.mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * 1000f, 2f, 300f);
        rb.angularDamping = 0.3f; rb.maxDepenetrationVelocity = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        rb.AddForce(flat * Random.Range(pushSpeed.x, pushSpeed.y) * strength, ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 1.5f * strength, ForceMode.VelocityChange);
        p.t.gameObject.AddComponent<VenusDebris>();
        SimpleGun.Dust(b.center, Vector3.up, 30, 0.8f, new Color(0.92f, 0.91f, 0.9f, 0.7f));
        StartCoroutine(Sink(p.t, rb));
    }

    void DropUnsupported()
    {
        var held = new HashSet<Piece>(); var queue = new Queue<Piece>();
        foreach (var p in pieces) if (!p.figure) { held.Add(p); queue.Enqueue(p); }
        while (queue.Count > 0)
            foreach (var c in queue.Dequeue().contacts)
            {
                var up = c.other;
                if (up.loose || held.Contains(up) || c.centre.y > up.bounds.center.y + 0.03f) continue;
                held.Add(up); queue.Enqueue(up);
            }
        foreach (var p in pieces) if (!p.loose && !held.Contains(p)) Break(p, Random.onUnitSphere, 0.1f);
    }

    IEnumerator Sink(Transform t, Rigidbody rb)
    {
        yield return new WaitForSeconds(debrisLifetime);
        rb.isKinematic = true;
        foreach (var c in t.GetComponents<Collider>()) c.enabled = false;
        for (float s = 0; s < 2f; s += Time.deltaTime) { t.position += Vector3.down * 0.25f * Time.deltaTime; yield return null; }
        Destroy(t.gameObject);
    }
}

// A falling statue piece drops like heavy stone: never thrown upward or flung far.
public class VenusDebris : MonoBehaviour
{
    Rigidbody rb;
    void Awake() { rb = GetComponent<Rigidbody>(); }
    void FixedUpdate()
    {
        var v = rb.linearVelocity;
        if (v.y > 0.3f) v.y = 0.3f;
        var flat = new Vector2(v.x, v.z);
        if (flat.magnitude > 2.5f) { flat = flat.normalized * 2.5f; v.x = flat.x; v.z = flat.y; }
        rb.linearVelocity = v;
        if (rb.angularVelocity.magnitude > 8f) rb.angularVelocity = rb.angularVelocity.normalized * 8f;
    }
}
