using UnityEngine;
using System.Collections;

// Uncharted-style stone hit: chunky marble fragments with physics, a billowing dust cloud that lingers, and fine grit.
public static class StoneImpactFX
{
    static Mesh[] rocks; static Material dustMat, gritMat; static Texture2D soft;

    static void Init()
    {
        if (rocks != null) return;
        rocks = new Mesh[6];
        for (int k = 0; k < rocks.Length; k++)
        {
            var src = GameObject.CreatePrimitive(PrimitiveType.Sphere); var m = Object.Instantiate(src.GetComponent<MeshFilter>().sharedMesh); Object.Destroy(src);
            var v = m.vertices; var rnd = new System.Random(k * 7919);
            var squash = new Vector3(0.6f + (float)rnd.NextDouble() * 0.8f, 0.4f + (float)rnd.NextDouble() * 0.6f, 0.6f + (float)rnd.NextDouble() * 0.8f);
            // angular chunk: snap sphere verts onto a few random planes
            var planes = new Vector3[7]; for (int i = 0; i < planes.Length; i++) planes[i] = new Vector3((float)rnd.NextDouble()-0.5f,(float)rnd.NextDouble()-0.5f,(float)rnd.NextDouble()-0.5f).normalized;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                foreach (var n in planes) { float d = Vector3.Dot(p, n); float lim = 0.28f + 0.1f * (float)rnd.NextDouble(); if (d > lim) p -= n * (d - lim); }
                v[i] = Vector3.Scale(p, squash);
            }
            m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds(); rocks[k] = m;
        }
        dustMat = Res.Load<Material>("FX_Dust");
        gritMat = Res.Load<Material>("FX_Grit");
    }

    public static void Play(RaycastHit hit, Material stone, float scale = 1f)
    {
        Init();
        Vector3 p = hit.point, n = hit.normal;
        // 1. fragments
        int count = Random.Range(9, 16);
        for (int i = 0; i < count; i++)
        {
            var g = new GameObject("StoneFrag");
            float size = (i < 2 ? Random.Range(0.12f, 0.22f) : Random.Range(0.03f, 0.1f)) * scale;
            g.transform.SetPositionAndRotation(p + n * 0.05f + Random.insideUnitSphere * 0.08f, Random.rotation);
            g.transform.localScale = Vector3.one * size;
            g.AddComponent<MeshFilter>().sharedMesh = rocks[Random.Range(0, rocks.Length)];
            g.AddComponent<MeshRenderer>().sharedMaterial = stone;
            var c = g.AddComponent<BoxCollider>(); c.size = Vector3.one * 0.6f;
            var rb = g.AddComponent<Rigidbody>(); rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.mass = size * 8f; rb.linearDamping = 0.1f; rb.angularDamping = 0.2f;
            var dir = (n + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized;
            rb.linearVelocity = dir * Random.Range(1.5f, 5f); rb.angularVelocity = Random.insideUnitSphere * 15f;
            g.AddComponent<FragmentFade>().life = Random.Range(6f, 10f);
        }
        // 2. billowing dust cloud (lingers)
        var cloud = Make("StoneDust", p + n * 0.1f, n, dustMat);
        var m = cloud.main; m.duration = 0.05f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f); m.startSize = new ParticleSystem.MinMaxCurve(0.08f * scale, 0.18f * scale);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.78f, 0.8f, 0.55f), new Color(0.62f, 0.64f, 0.68f, 0.4f));
        m.gravityModifier = -0.02f; m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
        var e = cloud.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0, (short)Random.Range(8, 12)) });
        var sh = cloud.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55; sh.radius = 0.1f;
        var sz = cloud.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 2f));
        var lv = cloud.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 3f; lv.multiplyDragByParticleSize = false;
        var col = cloud.colorOverLifetime; col.enabled = true; var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0, 1) });
        col.color = gr;
        var rot = cloud.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        cloud.Play(); Object.Destroy(cloud.gameObject, 0.5f);
        // 3. fast grit spray
        var grit = Make("StoneGrit", p + n * 0.02f, n, gritMat);
        var gm = grit.main; gm.duration = 0.05f; gm.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
        gm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f); gm.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f); gm.gravityModifier = 1.2f;
        gm.startColor = new Color(0.85f, 0.85f, 0.87f, 1f);
        var ge = grit.emission; ge.SetBursts(new[] { new ParticleSystem.Burst(0, (short)Random.Range(40, 70)) });
        var gs = grit.shape; gs.shapeType = ParticleSystemShapeType.Cone; gs.angle = 40; gs.radius = 0.02f;
        var gcol = grit.collision; gcol.enabled = true; gcol.type = ParticleSystemCollisionType.World; gcol.bounce = 0.3f; gcol.dampen = 0.5f;
        grit.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Stretch;
        grit.GetComponent<ParticleSystemRenderer>().velocityScale = 0.03f;
        grit.Play(); Object.Destroy(grit.gameObject, 0.5f);
    }

    static ParticleSystem Make(string name, Vector3 p, Vector3 n, Material mat)
    {
        var go = new GameObject(name); go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(n));
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
        var e = ps.emission; e.rateOverTime = 0;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat; return ps;
    }
}

public class FragmentFade : MonoBehaviour
{
    // Every broken piece (glass shards, stone chunks, planks...) must visibly fall and land:
    //  - pieces never collide with each other or with the object they broke off (no depenetration "pop" into the void)
    //  - fast continuous collision so small pieces can't tunnel through the floor
    //  - if a piece still ends up below the floor or flies off, it is put back on the floor under where it spawned
    public float life = 8f;
    float floorY = float.NegativeInfinity; Vector3 spawn; Rigidbody rb;
    static readonly System.Collections.Generic.List<Collider> recent = new System.Collections.Generic.List<Collider>();
    static float recentTime = -10f;
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        var col = GetComponent<Collider>();
        var mc = col as MeshCollider;
        if (mc != null && (mc.sharedMesh == null || mc.sharedMesh.bounds.size.sqrMagnitude < 1e-6f)) { Destroy(mc); col = null; }
        if (col == null) { var bc = gameObject.AddComponent<BoxCollider>(); col = bc; }
        // paper-thin pieces (glass shards) tunnel into the floor and then get shot back out: give them a box at least 3 cm thick
        if (col is MeshCollider)
        {
            var mb = ((MeshCollider)col).sharedMesh.bounds;
            if (Mathf.Min(mb.size.x * transform.lossyScale.x, Mathf.Min(mb.size.y * transform.lossyScale.y, mb.size.z * transform.lossyScale.z)) < 0.03f)
            {
                DestroyImmediate(col);
                var bc = gameObject.AddComponent<BoxCollider>(); bc.center = mb.center;
                var ls = transform.lossyScale; float m = 0.03f;
                bc.size = new Vector3(Mathf.Max(mb.size.x, m / Mathf.Abs(ls.x)), Mathf.Max(mb.size.y, m / Mathf.Abs(ls.y)), Mathf.Max(mb.size.z, m / Mathf.Abs(ls.z)));
                col = bc;
            }
        }   // fallback when a convex hull can't be built
        if (rb) { rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.maxDepenetrationVelocity = 0.5f; rb.useGravity = true; rb.isKinematic = false; }
        // no collisions between pieces of the same break, or with whatever they overlap at spawn (the thing that broke)
        // pieces from one break spawn over several frames: group everything spawned within 0.5 s
        if (Time.time - recentTime > 0.5f) recent.Clear();
        recentTime = Time.time;
        foreach (var o in recent) if (o) Physics.IgnoreCollision(col, o);
        recent.Add(col);
        spawn = col.bounds.center;   // shard meshes can be offset from their pivot: track the actual piece
        var b = col.bounds; var ignored = new System.Collections.Generic.HashSet<Collider>();
        foreach (var o in Physics.OverlapBox(b.center, b.extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            if (o == col || o.transform.IsChildOf(transform) || o.GetComponentInParent<CharacterController>() != null) continue;
            if (o.bounds.size.x > 4f && o.bounds.size.z > 4f) continue;   // never ignore floors (wide both ways); ignore the rest of the broken object (e.g. column core)
            Physics.IgnoreCollision(col, o); ignored.Add(o);
        }
        // the lowest real floor under the spawn point (a piece may bounce off a balcony and keep falling) (skip the broken object itself and other pieces)
        foreach (var h in Physics.RaycastAll(spawn + Vector3.up * 0.2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider != col && !ignored.Contains(h.collider) && !h.collider.GetComponent<FragmentFade>() && !h.collider.isTrigger
                && (h.collider.bounds.size.x > 4f || h.collider.bounds.size.z > 4f)          // a real floor, not a chandelier ring / ledge / prop in between
                && Vector3.Dot(h.normal, Vector3.up) > 0.7f && (float.IsNegativeInfinity(floorY) || h.point.y < floorY) && h.point.y < spawn.y - 0.3f && !h.collider.transform.IsChildOf(transform.parent ? transform.parent : transform)) floorY = h.point.y;
        if (float.IsNegativeInfinity(floorY)) floorY = spawn.y - 40f;   // unknown: effectively only catches pieces falling out of the world
    }
    void FixedUpdate()
    {
        if (!rb) return;
        var c = rb.worldCenterOfMass;
        var flat = new Vector2(c.x - spawn.x, c.z - spawn.z);
        bool lost = c.y < floorY - 0.05f || flat.sqrMagnitude > 20f * 20f;   // falling any height is fine; only below the floor or flung far sideways
        if (lost)
        {
            var want = new Vector3(c.x, floorY + 0.03f, c.z); if (flat.sqrMagnitude > 20f * 20f) want = new Vector3(spawn.x, floorY + 0.03f, spawn.z);
            var d = want - c; rb.position += d; transform.position += d;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        if (rb.linearVelocity.sqrMagnitude > 8f * 8f) rb.linearVelocity = rb.linearVelocity.normalized * 8f;
        if (rb.angularVelocity.sqrMagnitude > 20f * 20f) rb.angularVelocity = rb.angularVelocity.normalized * 20f;
    }
    IEnumerator Start()
    {
        yield return new WaitForSeconds(life);
        var s0 = transform.localScale; float t = 0;
        while (t < 1f) { t += Time.deltaTime / 0.8f; transform.localScale = s0 * Mathf.Max(0.001f, 1 - t); yield return null; }
        Destroy(gameObject);
    }
}
