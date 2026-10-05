using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Uncharted-style marble statue that comes apart under fire. Put it on the root of VenusStatue.fbx.
// Children: "Intact" (seamless shell shown until the first hit), Fig_## (figure pieces), Base_## (plinth pieces).
// Each hit knocks off the pieces near the impact with a puff of marble dust; every few hits a plinth chunk goes,
// the killing shot drops whatever is left, and pieces that lose their support fall with them.
public class DestructibleStatue : MonoBehaviour
{
    public float hitRadius = 0.2f;          // pieces whose surface is this close to the impact break off
    public int hitsToCollapse = 6;          // this shot brings the rest of the figure down
    public int plinthEvery = 3;             // every Nth hit also takes a chunk out of the plinth
    public float impulse = 2.5f;            // push along the bullet direction, in m/s
    public float density = 2700f;           // marble, kg/m3, for piece mass
    public float debrisLifetime = 7f;       // then the debris sinks into the ground and is removed
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
    ParticleSystem dust;
    int hits;

    void Awake()
    {
        var tr = transform.Find("Intact");
        if (tr != null) intact = tr.gameObject;
        foreach (Transform c in transform)
        {
            bool fig = c.name.StartsWith("Fig");
            if (!fig && !c.name.StartsWith("Base")) continue;
            var mf = c.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var mc = c.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;                                  // needed once the piece gets a Rigidbody
            c.GetComponent<Renderer>().enabled = intact == null;
            pieces.Add(new Piece { t = c, col = mc, figure = fig, bounds = c.GetComponent<Renderer>().bounds });
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
        dust = MakeDust();
    }

    public bool Collapsed => hits >= hitsToCollapse;

    public void Hit(Vector3 point, Vector3 direction)
    {
        OnHit?.Invoke(point, direction);
        hits++;
        if (intact != null && intact.activeSelf)
        {
            intact.SetActive(false);
            foreach (var p in pieces) p.t.GetComponent<Renderer>().enabled = true;
        }
        Dust(point, -direction, 40);

        var nearest = Nearest(point, null);
        if (nearest == null) return;
        if (hits >= hitsToCollapse)
        {
            foreach (var p in pieces) if (p.figure) Break(p, point, direction);
        }
        else if (nearest.figure)
        {
            foreach (var p in pieces)
                if (p.figure && !p.loose && Vector3.Distance(p.col.ClosestPoint(point), point) < hitRadius)
                    Break(p, point, direction);
        }
        if (!nearest.figure || hits % plinthEvery == 0)
        {
            var chunk = nearest.figure ? Nearest(point, false) : nearest;
            if (chunk != null) Break(chunk, point, direction);
        }
        DropUnsupported(direction);
    }

    Piece Nearest(Vector3 point, bool? figure)
    {
        Piece best = null;
        float bestD = float.MaxValue;
        foreach (var p in pieces)
        {
            if (p.loose || (figure.HasValue && p.figure != figure.Value)) continue;
            float d = Vector3.Distance(p.col.ClosestPoint(point), point);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    void Break(Piece p, Vector3 point, Vector3 direction)
    {
        if (p.loose) return;
        p.loose = true;
        var b = p.bounds;
        var rb = p.t.gameObject.AddComponent<Rigidbody>();
        rb.mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * density * 0.4f, 1f, 400f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var push = direction.normalized * impulse * UnityEngine.Random.Range(0.6f, 1.2f)
                 + Vector3.up * UnityEngine.Random.Range(0f, 0.8f);
        rb.AddForceAtPosition(push * rb.mass, p.col.ClosestPoint(point), ForceMode.Impulse);
        Dust(b.center, direction, 25);
        StartCoroutine(Sink(p.t, rb));
    }

    // Anything that no longer connects to a plinth piece standing on the ground falls.
    void DropUnsupported(Vector3 direction)
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
            if (!p.loose && !held.Contains(p)) Break(p, p.bounds.center, direction * 0.3f);
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

    void Dust(Vector3 at, Vector3 normal, int count)
    {
        if (dust == null) return;
        dust.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal.sqrMagnitude > 0 ? normal : Vector3.up));
        dust.Emit(count);
    }

    ParticleSystem MakeDust()
    {
        var go = new GameObject("MarbleDust");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
        main.startColor = new Color(0.95f, 0.93f, 0.9f, 0.55f);
        main.gravityModifier = 0.05f;
        main.maxParticles = 2000;
        var em = ps.emission;
        em.enabled = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.03f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.8f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0.7f, 0), new GradientAlphaKey(0f, 1) });
        col.color = g;
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 2.5f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (dustMaterial != null) r.sharedMaterial = dustMaterial;
        return ps;
    }
}
