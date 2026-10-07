using UnityEngine;
using System.Collections.Generic;

// Uncharted-style marble statue: every bullet knocks off the pieces around where it hit, so the figure is chipped away
// shot by shot; the 6th shot brings down whatever is left. The plinth only loses a chunk every few hits.
// Works from the pre-cut model (children named Fig_## and Base_##). Other players' shots are replayed through
// SimpleGun.RemoteShot -> OnBulletHit, so everyone sees the same statue break.
public class ProgressiveStatue : MonoBehaviour, IWorldState
{
    public int shotsToDestroy = 6;
    public float firstRadius = 0.22f, radiusGrowth = 0.05f, debrisLifetime = 7f;
    class Piece { public Transform t; public MeshCollider col; public bool fig, gone; }
    readonly List<Piece> pieces = new List<Piece>();
    int shots, baseHits;
    string key;
    void Awake() { key = Net.PathOf(transform); }

    // late join: "shots:plinth hits:gone piece indices"
    public string Save()
    {
        var g = new List<int>(); for (int i = 0; i < pieces.Count; i++) if (pieces[i].gone) g.Add(i);
        return shots + ":" + baseHits + ":" + WorldState.Ints(g);
    }
    public void Load(string s)
    {
        var p = s.Split(':'); if (p.Length < 3) return;
        shots = Mathf.Max(shots, WorldState.PI(p[0])); baseHits = Mathf.Max(baseHits, WorldState.PI(p[1]));
        foreach (var i in WorldState.PInts(p[2]))
            if (i >= 0 && i < pieces.Count && !pieces[i].gone) { pieces[i].gone = true; if (pieces[i].t) Destroy(pieces[i].t.gameObject); }
    }
    static Material marble, inner; static ParticleSystem dustPrefab;

    void Start()
    {
        if (marble == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            marble = new Material(sh) { name = "StatueMarble" }; marble.SetColor("_BaseColor", new Color(0.9f, 0.89f, 0.86f)); marble.SetFloat("_Smoothness", 0.55f);
            inner = new Material(sh) { name = "StatueMarbleInner" }; inner.SetColor("_BaseColor", new Color(0.74f, 0.73f, 0.7f)); inner.SetFloat("_Smoothness", 0.12f);
        }
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            var r = mf.GetComponent<MeshRenderer>(); var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = (mats[i] != null && mats[i].name.Contains("Inner")) ? inner : marble;
            r.sharedMaterials = mats;
            var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; c.convex = true;
            pieces.Add(new Piece { t = mf.transform, col = c, fig = mf.name.StartsWith("Fig") });
        }
        WorldState.Register(key, this);
    }

    void OnBulletHit(RaycastHit hit)
    {
        int s0 = shots, b0 = baseHits;
        Shot(hit);
        if (shots != s0 || baseHits != b0) WorldState.Changed(key, this);
    }

    void Shot(RaycastHit hit)
    {
        if (shots >= shotsToDestroy) return;
        var hp = pieces.Find(p => p.col == hit.collider); if (hp == null || hp.gone) return;
        Dust(hit.point, hit.normal);
        Vector3 push = -hit.normal;
        if (!hp.fig)   // the plinth: a chunk every third hit
        {
            if (++baseHits % 3 == 0) Break(hp, hit.point, push);
            return;
        }
        shots++;
        if (shots >= shotsToDestroy) { foreach (var p in pieces) if (p.fig && !p.gone) Break(p, hit.point, push); return; }
        float r = firstRadius + radiusGrowth * shots; int broken = 0;
        foreach (var p in pieces)
            if (p.fig && !p.gone && Vector3.Distance(p.col.bounds.ClosestPoint(hit.point), hit.point) < r) { Break(p, hit.point, push); broken++; }
        if (broken == 0) Break(hp, hit.point, push);
        DropUnsupported(hit.point, push);
    }

    // a piece whose whole underside is gone falls too (no floating chunks)
    void DropUnsupported(Vector3 at, Vector3 push)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in pieces)
            {
                if (!p.fig || p.gone) continue;
                var b = p.col.bounds; bool held = false;
                foreach (var q in pieces)
                {
                    if (q == p || q.gone) continue;
                    var qb = q.col.bounds;
                    if (qb.max.y > b.min.y - 0.03f && qb.min.y < b.min.y + 0.05f && qb.max.x > b.min.x && qb.min.x < b.max.x && qb.max.z > b.min.z && qb.min.z < b.max.z) { held = true; break; }
                }
                if (!held) { Break(p, at, push * 0.3f); changed = true; }
            }
        }
    }

    void Break(Piece p, Vector3 from, Vector3 push)
    {
        p.gone = true;
        p.t.SetParent(null, true);
        var rb = p.t.gameObject.AddComponent<Rigidbody>();
        var size = p.col.bounds.size; rb.mass = Mathf.Max(2f, size.x * size.y * size.z * 2600f * 0.5f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var away = (p.col.bounds.center - from); away.y = 0f;
        rb.AddForce((push * 2.2f + away.normalized * 1.2f + Vector3.up * 1.0f + Random.insideUnitSphere * 0.6f) * rb.mass, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * rb.mass * 0.8f, ForceMode.Impulse);
        StartCoroutine(Sink(p.t.gameObject, rb));
    }

    System.Collections.IEnumerator Sink(GameObject g, Rigidbody rb)
    {
        yield return new WaitForSeconds(debrisLifetime);
        if (g == null) yield break;
        rb.isKinematic = true; foreach (var c in g.GetComponents<Collider>()) c.enabled = false;
        float t = 0f; var p0 = g.transform.position;
        while (t < 1.5f && g != null) { t += Time.deltaTime; g.transform.position = p0 + Vector3.down * (0.5f * t / 1.5f); yield return null; }
        if (g != null) Destroy(g);
    }

    static void Dust(Vector3 at, Vector3 n)
    {
        if (dustPrefab == null)
        {
            var go = new GameObject("StatueDust"); go.SetActive(false); var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.duration = 1f; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f); m.startColor = new Color(0.86f, 0.85f, 0.82f, 0.55f); m.gravityModifier = 0.05f; m.playOnAwake = false;
            var e = ps.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.05f;
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) }); col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var r = go.GetComponent<ParticleSystemRenderer>(); var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f); mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); r.sharedMaterial = mat;
            dustPrefab = ps;
        }
        var d = Instantiate(dustPrefab, at + n * 0.03f, Quaternion.LookRotation(n)); d.gameObject.SetActive(true); d.Play(); Destroy(d.gameObject, 3f);
    }
}
