using UnityEngine;
using System.Collections.Generic;

// Column base with 3 fixed breakable spots. Each spot is a slab of Voronoi-cracked stone shards over a rough crater.
// Shots near a spot crack the stone (visible fissures) and blast shards out as real physics pieces, growing the crater up to 6 shots.
public class DestructiblePlinth : MonoBehaviour, IWorldState
{
    public float spotSize = 0.9f, shardDepth = 0.14f, craterDepth = 0.32f; public int shardCount = 34, maxShotsPerSpot = 6;
    class Shard { public GameObject go; public Vector2 c; public bool gone; }
    class Spot { public Vector3 center, n, u, v; public List<Shard> shards = new List<Shard>(); public int shots; }
    readonly List<Spot> spots = new List<Spot>();
    Material stone, inner;
    string key;
    void Awake() { key = Net.PathOf(transform); }

    // late join: per spot "index:shots:gone shards:cracked shards"
    public string Save()
    {
        var a = new List<string>();
        for (int i = 0; i < spots.Count; i++)
        {
            var sp = spots[i]; if (sp.shots == 0) continue;
            var g = new List<int>(); var c = new List<int>();
            for (int k = 0; k < sp.shards.Count; k++)
            {
                var sh = sp.shards[k];
                if (sh.gone) g.Add(k); else if (sh.go && sh.go.transform.localScale.x < 0.97f) c.Add(k);
            }
            a.Add(i + ":" + sp.shots + ":" + WorldState.Ints(g) + ":" + WorldState.Ints(c));
        }
        return string.Join(";", a);
    }
    public void Load(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        foreach (var e in s.Split(';'))
        {
            var p = e.Split(':'); if (p.Length < 4) continue;
            int i = WorldState.PI(p[0]); if (i < 0 || i >= spots.Count) continue;
            var sp = spots[i]; sp.shots = Mathf.Max(sp.shots, WorldState.PI(p[1]));
            foreach (var k in WorldState.PInts(p[2]))
                if (k >= 0 && k < sp.shards.Count && !sp.shards[k].gone) { sp.shards[k].gone = true; if (sp.shards[k].go) Destroy(sp.shards[k].go); }
            foreach (var k in WorldState.PInts(p[3]))
            {
                if (k < 0 || k >= sp.shards.Count || sp.shards[k].gone || !sp.shards[k].go) continue;
                var t = sp.shards[k].go.transform; if (t.localScale.x < 0.97f) continue;
                t.localScale = new Vector3(0.95f, 0.95f, 1f); t.localPosition += new Vector3(0, 0, 0.01f);
            }
        }
    }

    void Start()
    {
        var r = GetComponent<Renderer>(); var b = r.bounds; stone = r.sharedMaterial;
        inner = new Material(stone) { name = "CraterInner" }; if (inner.HasProperty("_BaseColor")) { inner.SetColor("_BaseColor", new Color(0.33f, 0.33f, 0.35f)); inner.SetFloat("_Smoothness", 0.05f); inner.SetFloat("_Metallic", 0f); }
        float zf = 1.7f - b.center.z > 0 ? b.max.z : b.min.z; var nz = zf > b.center.z ? Vector3.forward : Vector3.back;
        AddSpot(new Vector3(b.center.x - 0.9f, b.min.y + 0.95f, zf), nz);
        AddSpot(new Vector3(b.center.x + 0.95f, b.min.y + 2.35f, zf), nz);
        AddSpot(new Vector3(b.min.x, b.min.y + 1.6f, b.center.z + 0.6f), Vector3.left);
        BuildBody(b);
        r.enabled = false;
        // keep original box collider for the solid body except spots: use shell mesh collider instead
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        WorldState.Register(key, this);
    }

    void AddSpot(Vector3 c, Vector3 n)
    {
        var s = new Spot { center = c, n = n, v = Vector3.up }; s.u = Vector3.Cross(Vector3.up, n).normalized; spots.Add(s);
        var rnd = new System.Random((int)(c.x * 131 + c.y * 71 + c.z * 37));
        float h = spotSize / 2f;
        var seeds = new List<Vector2>();
        for (int i = 0; i < shardCount; i++)
        {
            // denser in the middle
            float rr = Mathf.Pow((float)rnd.NextDouble(), 0.8f) * h * 1.35f, a = (float)rnd.NextDouble() * 6.283f;
            var p = new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr); p.x = Mathf.Clamp(p.x, -h * 0.97f, h * 0.97f); p.y = Mathf.Clamp(p.y, -h * 0.97f, h * 0.97f); seeds.Add(p);
        }
        var root = new GameObject(name + "_Spot").transform; root.SetParent(transform.parent, true); root.position = c; root.rotation = Quaternion.LookRotation(-n, Vector3.up);
        // local frame of root: +z points INTO the stone, x/y along the face
        for (int i = 0; i < seeds.Count; i++)
        {
            var poly = new List<Vector2> { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) };
            for (int j = 0; j < seeds.Count && poly.Count > 2; j++)
            {
                if (i == j) continue; var m = (seeds[i] + seeds[j]) * 0.5f; var nn = (seeds[j] - seeds[i]).normalized;
                poly = Clip(poly, m, nn);
            }
            if (poly.Count < 3) continue;
            var go = new GameObject("Shard"); go.transform.SetParent(root, false);
            var cen = Vector2.zero; foreach (var p in poly) cen += p; cen /= poly.Count;
            go.transform.localPosition = new Vector3(cen.x, cen.y, 0);
            float dBack = shardDepth * (0.7f + 0.6f * (float)rnd.NextDouble());
            go.AddComponent<MeshFilter>().sharedMesh = Extrude(poly, cen, dBack, rnd);
            go.AddComponent<MeshRenderer>().sharedMaterial = stone;
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<PlinthRelay>().owner = this;
            s.shards.Add(new Shard { go = go, c = cen });
        }
        // crater bowl behind the shards (rough, dark)
        var bowl = new GameObject("Crater"); bowl.transform.SetParent(root, false);
        bowl.AddComponent<MeshFilter>().sharedMesh = Bowl(h, rnd); bowl.AddComponent<MeshRenderer>().sharedMaterial = inner;
        bowl.AddComponent<MeshCollider>(); bowl.AddComponent<PlinthRelay>().owner = this;
    }

    static List<Vector2> Clip(List<Vector2> poly, Vector2 m, Vector2 n)
    {
        var o = new List<Vector2>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            float da = Vector2.Dot(a - m, n), db = Vector2.Dot(b - m, n);
            if (da <= 0) o.Add(a);
            if ((da < 0 && db > 0) || (da > 0 && db < 0)) o.Add(a + (b - a) * (da / (da - db)));
        }
        return o;
    }

    Mesh Extrude(List<Vector2> poly, Vector2 cen, float depth, System.Random rnd)
    {
        var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); int n = poly.Count;
        // front face (z=0, facing -z i.e. outward) and jagged back face
        for (int i = 0; i < n; i++) { var p = poly[i] - cen; vs.Add(new Vector3(p.x, p.y, 0)); uv.Add(poly[i]); }
        for (int i = 0; i < n; i++) { var p = (poly[i] - cen) * 0.85f; vs.Add(new Vector3(p.x, p.y, depth * (0.8f + 0.4f * (float)rnd.NextDouble()))); uv.Add(poly[i]); }
        for (int i = 1; i < n - 1; i++) { tr.Add(0); tr.Add(i + 1); tr.Add(i); tr.Add(n); tr.Add(n + i); tr.Add(n + i + 1); }
        for (int i = 0; i < n; i++) { int j = (i + 1) % n; tr.AddRange(new[] { i, j, n + j, i, n + j, n + i }); }
        var m = new Mesh(); m.SetVertices(vs); m.SetUVs(0, uv); m.SetTriangles(tr, 0);
        // ensure outward winding using centroid
        var cc = Vector3.zero; foreach (var v in vs) cc += v; cc /= vs.Count; var t = m.triangles;
        for (int k = 0; k < t.Length; k += 3) { var A = vs[t[k]]; var B = vs[t[k+1]]; var C = vs[t[k+2]]; if (Vector3.Dot(Vector3.Cross(B - A, C - A), (A + B + C) / 3f - cc) < 0) { var x = t[k+1]; t[k+1] = t[k+2]; t[k+2] = x; } }
        m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    Mesh Bowl(float h, System.Random rnd)
    {
        int N = 18; var vs = new List<Vector3>(); var tr = new List<int>(); var uv = new List<Vector2>();
        for (int y = 0; y <= N; y++) for (int x = 0; x <= N; x++)
        {
            float fx = -h + 2 * h * x / N, fy = -h + 2 * h * y / N;
            float e = Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fy)) / h; // 0 center .. 1 edge
            float d = craterDepth * (1 - e * e) * (0.75f + 0.5f * (float)rnd.NextDouble());
            if (x == 0 || y == 0 || x == N || y == N) d = 0;
            vs.Add(new Vector3(fx, fy, d + shardDepth * 0.5f)); uv.Add(new Vector2(fx, fy) * 2f);
        }
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { int i = y * (N + 1) + x; tr.AddRange(new[] { i, i + N + 1, i + 1, i + 1, i + N + 1, i + N + 2 }); }
        // side walls from face (z=0) to bowl rim
        int b0 = vs.Count; var rim = new[] { new Vector3(-h,-h,0), new Vector3(h,-h,0), new Vector3(h,h,0), new Vector3(-h,h,0) };
        foreach (var p in rim) { vs.Add(p); uv.Add(Vector2.zero); } foreach (var p in rim) { vs.Add(p + Vector3.forward * shardDepth * 0.5f); uv.Add(Vector2.zero); }
        for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; tr.AddRange(new[] { b0 + i, b0 + 4 + i, b0 + j, b0 + j, b0 + 4 + i, b0 + 4 + j }); }
        var m = new Mesh(); m.SetVertices(vs); m.SetUVs(0, uv); m.SetTriangles(tr, 0); m.RecalculateNormals();
        // normals should face -z (out of the hole towards viewer)
        var nr = m.normals; bool flip = false; foreach (var q in nr) { if (q.z > 0.5f) flip = true; break; }
        if (flip) { var t = m.triangles; for (int k = 0; k < t.Length; k += 3) { var x = t[k+1]; t[k+1] = t[k+2]; t[k+2] = x; } m.triangles = t; m.RecalculateNormals(); }
        m.RecalculateBounds(); return m;
    }

    // plinth body = box with the three spot squares left open
    void BuildBody(Bounds b)
    {
        var vs = new List<Vector3>(); var tr = new List<int>(); var uv = new List<Vector2>(); float cell = 0.05f;
        var faces = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down };
        foreach (var n in faces)
        {
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized, v = Vector3.Cross(n, u);
            float fu = Mathf.Abs(Vector3.Dot(b.size, new Vector3(Mathf.Abs(u.x), Mathf.Abs(u.y), Mathf.Abs(u.z)))), fv = Mathf.Abs(Vector3.Dot(b.size, new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z))));
            var fc = b.center + Vector3.Scale(n, b.extents);
            var holes = spots.FindAll(s => Vector3.Dot(s.n, n) > 0.9f);
            // grid of quads, skipping ones inside holes
            int nu = Mathf.RoundToInt(fu / cell), nv = Mathf.RoundToInt(fv / cell);
            if (holes.Count == 0) { nu = 1; nv = 1; }
            for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++)
            {
                float a0 = -fu / 2 + fu * i / nu, a1 = -fu / 2 + fu * (i + 1) / nu, c0 = -fv / 2 + fv * j / nv, c1 = -fv / 2 + fv * (j + 1) / nv;
                var mid = fc + u * (a0 + a1) / 2 + v * (c0 + c1) / 2; bool skip = false;
                foreach (var s in holes) { var d = mid - s.center; if (Mathf.Abs(Vector3.Dot(d, s.u)) < spotSize / 2 && Mathf.Abs(Vector3.Dot(d, s.v)) < spotSize / 2) skip = true; }
                if (skip) continue;
                int k = vs.Count;
                foreach (var p in new[] { fc + u * a0 + v * c0, fc + u * a1 + v * c0, fc + u * a1 + v * c1, fc + u * a0 + v * c1 }) { vs.Add(p - b.center); uv.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) * 0.5f); }
                tr.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
        }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(vs); m.SetUVs(0, uv); m.SetTriangles(tr, 0);
        var t = m.triangles; for (int k = 0; k < t.Length; k += 3) { var A = vs[t[k]]; var B = vs[t[k+1]]; var C = vs[t[k+2]]; if (Vector3.Dot(Vector3.Cross(B - A, C - A), (A + B + C) / 3f) < 0) { var x = t[k+1]; t[k+1] = t[k+2]; t[k+2] = x; } }
        m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
        var body = new GameObject(name + "_Body"); body.transform.position = b.center; body.transform.SetParent(transform.parent, true);
        body.AddComponent<MeshFilter>().sharedMesh = m; body.AddComponent<MeshRenderer>().sharedMaterial = stone; body.AddComponent<MeshCollider>().sharedMesh = m;
    }

    public void Hit(RaycastHit hit)
    {
        Spot s = null; foreach (var sp in spots) { var d = hit.point - sp.center; if (Mathf.Abs(Vector3.Dot(d, sp.u)) < spotSize * 0.6f && Mathf.Abs(Vector3.Dot(d, sp.v)) < spotSize * 0.6f && Mathf.Abs(Vector3.Dot(d, sp.n)) < 0.6f) s = sp; }
        if (s == null || s.shots >= maxShotsPerSpot) return; s.shots++;
        var root = s.shards.Count > 0 ? s.shards[0].go.transform.parent : null; if (!root) return;
        var lp = root.InverseTransformPoint(hit.point); var hp = new Vector2(lp.x, lp.y);
        float blast = 0.1f + 0.055f * s.shots, crack = blast + 0.18f;
        foreach (var sh in s.shards)
        {
            if (sh.gone) continue; float d = Vector2.Distance(sh.c, hp);
            if (d < blast || (d < blast * 1.3f && Random.value < 0.5f))
            {
                sh.gone = true; var g = sh.go; g.transform.SetParent(null, true);
                Destroy(g.GetComponent<PlinthRelay>()); var mc = g.GetComponent<MeshCollider>(); mc.convex = true;
                var rb = g.AddComponent<Rigidbody>(); rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.mass = 2f;
                rb.linearVelocity = (s.n * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 1.2f + Vector3.up * 0.5f); rb.angularVelocity = Random.insideUnitSphere * 10f;
                g.AddComponent<FragmentFade>().life = Random.Range(8f, 12f);
            }
            else if (d < crack)
            {
                // crack reveal: shrink toward its centre and sink a bit so fissures open up
                var t = sh.go.transform; if (t.localScale.x > 0.93f) { t.localScale = new Vector3(0.95f, 0.95f, 1f); t.localPosition += new Vector3(0, 0, Random.Range(0.003f, 0.02f)); t.localRotation = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(-3f, 3f), 0); }
            }
        }
        WorldState.Changed(key, this);
    }
}
public class PlinthRelay : MonoBehaviour { public DestructiblePlinth owner; void OnBulletHit(RaycastHit h){ if (owner) owner.Hit(h); } }
