using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Display-case glass: shots 1-3 grow a spiderweb crack at each impact; shot 4 shatters every pane into Blender-fractured shards.
public class GlassCase : MonoBehaviour
{
    public GameObject shardsPrefab; int hits; bool broken; Bounds glass; Transform webT;
    static Material crackMat, sparkMat, hiddenMat;
    readonly List<GameObject> webs = new List<GameObject>();

    static void Mats()
    {
        if (crackMat) return;
        crackMat = Res.Load<Material>("FX_GlassCrack"); sparkMat = Res.Load<Material>("FX_Grit"); hiddenMat = Res.Load<Material>("FX_Hidden");
    }

    int GlassSlot(Renderer r){ var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].name.Contains("Glass")) return i; return -1; }

    void OnBulletHit(RaycastHit hit)
    {
        if (broken) return; Mats();
        var r = GetComponent<Renderer>(); int gs = GlassSlot(r);
        // only glass counts: glass box is the upper part of the case
        var b = r.bounds; bool onGlass = hit.point.y > b.min.y + b.size.y * 0.52f;
        if (!onGlass) return;
        glass = new Bounds(new Vector3(b.center.x, b.min.y + b.size.y * 0.765f, b.center.z), new Vector3(b.size.x * 0.94f, b.size.y * 0.45f, b.size.z * 0.94f));
        // ignore hits on the brass frame (near pane edges)
        var q = hit.point; float m = 0.05f;
        int edges = 0; if (Mathf.Abs(q.x - glass.min.x) < m || Mathf.Abs(q.x - glass.max.x) < m) edges++; if (Mathf.Abs(q.z - glass.min.z) < m || Mathf.Abs(q.z - glass.max.z) < m) edges++; if (Mathf.Abs(q.y - glass.min.y) < m || Mathf.Abs(q.y - glass.max.y) < m) edges++;
        if (edges >= 2) return;
        hits++;
        Sparkle(hit.point, hit.normal, 20 + hits * 15);
        if (hits < 4) { webs.Add(Web(hit, 0.09f + 0.04f * hits, hits)); return; }
        StartCoroutine(Shatter(hit));
    }

    GameObject Web(RaycastHit hit, float radius, int level)
    {
        var go = new GameObject("GlassCrack"); go.transform.SetPositionAndRotation(hit.point + hit.normal * 0.004f, Quaternion.LookRotation(-hit.normal));
        go.transform.SetParent(transform, true); webT = go.transform;
        var vs = new List<Vector3>(); var tr = new List<int>();
        int spokes = Random.Range(9, 14); var ends = new Vector2[spokes]; var angs = new float[spokes];
        for (int s = 0; s < spokes; s++)
        {
            float a = (s + Random.Range(-0.35f, 0.35f)) / spokes * Mathf.PI * 2; angs[s] = a; float len = radius * Random.Range(0.6f, 1.15f);
            Vector2 p = Vector2.zero; int segs = Random.Range(4, 7);
            for (int k = 0; k < segs; k++)
            {
                float aa = a + Random.Range(-0.18f, 0.18f); var q = p + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * (len / segs);
                Line(vs, tr, p, q, 0.0011f * (1 - k / (float)segs) + 0.0003f); p = q;
            }
            ends[s] = p;
        }
        // concentric rings between spokes
        for (int ring = 1; ring <= 1 + level; ring++)
        {
            float rr = radius * ring / (2.5f + level);
            for (int s = 0; s < spokes; s++)
            {
                if (Random.value < 0.2f) continue;
                int t = (s + 1) % spokes; float a0 = angs[s], a1 = angs[t]; if (a1 < a0) a1 += Mathf.PI * 2;
                var p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rr * Random.Range(0.9f, 1.1f); var p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rr * Random.Range(0.9f, 1.1f);
                var mid = (p0 + p1) * 0.5f * Random.Range(0.85f, 0.97f);
                Line(vs, tr, p0, mid, 0.0004f); Line(vs, tr, mid, p1, 0.0004f);
            }
        }
        // crushed centre
        for (int k = 0; k < 10; k++) { float a = Random.value * 6.283f; Line(vs, tr, Vector2.zero, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * 0.06f, 0.0008f); }
        var m = new Mesh(); m.SetVertices(vs); m.SetTriangles(tr, 0); m.RecalculateNormals(); m.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = crackMat;
        return go;
    }
    void Line(List<Vector3> vs, List<int> tr, Vector2 a, Vector2 b, float w)
    {
        // clip segment to the glass pane
        Vector3 wa = webT.TransformPoint(a), wb = webT.TransformPoint(b); var gb = glass; gb.Expand(new Vector3(0.02f,0.02f,0.02f));
        bool ia = gb.Contains(wa), ib = gb.Contains(wb); if (!ia && !ib) return;
        if (!ia || !ib) { Vector2 inP = ia ? a : b, outP = ia ? b : a, lo = inP, hi = outP; for (int k = 0; k < 12; k++) { var mid = (lo + hi) * 0.5f; if (gb.Contains(webT.TransformPoint(mid))) lo = mid; else hi = mid; } a = inP; b = lo; if ((b - a).sqrMagnitude < 1e-8f) return; }
        var d = (b - a).normalized; var n = new Vector2(-d.y, d.x) * w; int i = vs.Count;
        vs.Add(a + n); vs.Add(b + n); vs.Add(b - n); vs.Add(a - n);
        tr.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
    }

    void Sparkle(Vector3 p, Vector3 n, int count)
    {
        var go = new GameObject("GlassSpark"); go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(n));
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMat;
        var m = ps.main; m.loop = false; m.duration = 0.05f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f); m.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.025f); m.gravityModifier = 1.5f;
        m.startColor = new Color(0.9f, 0.97f, 1f, 1f);
        var e = ps.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 45; sh.radius = 0.01f;
        var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.bounce = 0.4f;
        ps.Play(); Destroy(go, 1.5f);
    }

    IEnumerator Shatter(RaycastHit hit)
    {
        broken = true; var r = GetComponent<Renderer>(); int gs = GlassSlot(r);
        foreach (var w in webs) if (w) Destroy(w);
        if (gs >= 0) { var ms = r.sharedMaterials; ms[gs] = hiddenMat; r.sharedMaterials = ms; }
        Sparkle(hit.point, hit.normal, 160);
        if (!shardsPrefab) yield break;
        var f = Instantiate(shardsPrefab, transform.position, transform.rotation); f.transform.localScale = transform.lossyScale;
        var glassMat = Res.Load<Material>("FX_GlassShard");
        var list = new List<Transform>();
        foreach (var mr in f.GetComponentsInChildren<MeshRenderer>())
        {
            mr.transform.localRotation = Quaternion.identity; mr.transform.localPosition = Vector3.zero; mr.transform.localScale = Vector3.one;
            mr.sharedMaterial = glassMat; list.Add(mr.transform);
        }
        // shards closest to impact go first, rest follow over ~0.15s (ripple)
        list.Sort((a, b) => (a.GetComponent<Renderer>().bounds.center - hit.point).sqrMagnitude.CompareTo((b.GetComponent<Renderer>().bounds.center - hit.point).sqrMagnitude));
        var center = r.bounds.center; int i = 0;
        foreach (var t in list)
        {
            var c = t.GetComponent<Renderer>().bounds.center;
            var mc = t.gameObject.AddComponent<MeshCollider>(); mc.convex = true;
            var rb = t.gameObject.AddComponent<Rigidbody>(); rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.mass = 0.08f; rb.linearDamping = 0.2f;
            float near = Mathf.Clamp01(1f - Vector3.Distance(c, hit.point) / 1.2f);
            rb.linearVelocity = (-hit.normal * (1.5f + 4f * near)) + (c - center).normalized * Random.Range(0.3f, 1.5f) + Random.insideUnitSphere * 0.5f;
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            t.gameObject.AddComponent<FragmentFade>().life = Random.Range(6f, 10f);
            if (++i % 25 == 0) yield return null;
        }
        Destroy(f, 12f);
    }
}
