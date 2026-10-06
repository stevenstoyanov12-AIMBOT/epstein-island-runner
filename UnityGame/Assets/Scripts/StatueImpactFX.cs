using UnityEngine;
using System.Collections;

// The stone hit from the game's shooting gallery (MuseumGame StoneImpactFX), for the statue sandbox:
// chunky angular marble fragments with physics, a billowing dust cloud that lingers, and a fast spray of fine grit.
public static class StatueImpactFX
{
    static Mesh[] rocks;

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
    }

    // fragments = false: dust and grit only (used for the puff when a whole piece comes loose)
    public static void Play(Vector3 p, Vector3 n, Material stone, Material dustMat, Material gritMat, float scale = 1f, bool fragments = true)
    {
        Init();
        // 1. fragments
        int count = fragments ? Random.Range(9, 16) : 0;
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
            g.AddComponent<StatueFragment>().life = Random.Range(6f, 10f);
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

// Small fragment from an impact: lives a few seconds, then shrinks away (same as the game's FragmentFade).
public class StatueFragment : MonoBehaviour
{
    public float life = 8f;
    IEnumerator Start()
    {
        yield return new WaitForSeconds(life);
        var s0 = transform.localScale; float t = 0;
        while (t < 1f) { t += Time.deltaTime / 0.8f; transform.localScale = s0 * Mathf.Max(0.001f, 1 - t); yield return null; }
        Destroy(gameObject);
    }
}
