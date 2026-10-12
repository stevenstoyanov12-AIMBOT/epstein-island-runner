using System.Collections.Generic;
using UnityEngine;

// Stone hit effect: flying chunks, grit, a dust puff and a crack decal.
// Put one in the scene, assign the assets, then call StoneImpactFX.Spawn(point, normal) from the hit code.
public class StoneImpactFX : MonoBehaviour
{
    public static StoneImpactFX Instance;

    [Header("Assets (Assets/Effects/StoneImpact)")]
    public Mesh[] chunkMeshes;          // StoneChunk_0..7 from StoneChunks.fbx
    public Texture2D dustSheet;         // DustPuff_4x4
    public Texture2D gritAtlas;         // Grit_2x2
    public Texture2D decalTexture;      // ImpactDecal

    [Header("Look")]
    public Color stoneColor = new Color(0.78f, 0.76f, 0.72f);
    public float scale = 1f;
    public int poolSize = 10;
    public float decalLife = 12f;
    [Tooltip("Real physics stones (old museum effect) instead of particle chunks")]
    public bool physicsStones = true;

    Material chunkMat, dustMat, gritMat, decalMat;
    readonly List<Fx> pool = new List<Fx>();
    MaterialPropertyBlock mpb;
    int next;

    class Fx { public Transform root; public ParticleSystem chunks, grit, dust; public Transform decal; public MeshRenderer decalRend; public float born; }

    void Awake()
    {
        Instance = this;
        Shader unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader lit = Shader.Find("Universal Render Pipeline/Particles/Lit");
        if (lit == null) lit = unlit;  // Unity null: ?? doesn't work on Shader

        chunkMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = stoneColor };
        chunkMat.enableInstancing = true;
        dustMat = Transparent(unlit, dustSheet);
        gritMat = Transparent(lit, gritAtlas);
        decalMat = Transparent(unlit, decalTexture);
        decalMat.SetColor("_BaseColor", Color.white);

        for (int i = 0; i < poolSize; i++) pool.Add(Build(i));
    }

    static Material Transparent(Shader s, Texture tex)
    {
        var m = new Material(s);
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Surface", 1);  // transparent
        m.SetFloat("_Blend", 0);    // alpha
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    public static void Spawn(Vector3 point, Vector3 normal)
    {
        if (Instance != null) Instance.Play(point, normal);
    }

    void Play(Vector3 point, Vector3 normal)
    {
        var fx = pool[next]; next = (next + 1) % pool.Count;
        fx.root.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
        fx.root.gameObject.SetActive(true);
        fx.chunks.Clear(); fx.grit.Clear(); fx.dust.Clear();
        fx.grit.Play(); fx.dust.Play();
        if (physicsStones) OldStoneImpactFX.Fragments(point, normal, chunkMat, scale);
        else fx.chunks.Play();

        // decal sits on the surface, random spin, lifted a hair to avoid z-fighting
        fx.decal.SetPositionAndRotation(point + normal * 0.004f,
            Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
        fx.decal.localScale = Vector3.one * Random.Range(0.3f, 0.45f) * scale;
        fx.decal.gameObject.SetActive(true);
        fx.born = Time.time;
    }

    void Update()
    {
        foreach (var fx in pool)
        {
            if (!fx.decal.gameObject.activeSelf) continue;
            float age = Time.time - fx.born;
            float a = Mathf.Clamp01((decalLife - age) / 2f);  // fade out over the last 2s
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(1, 1, 1, a));
            fx.decalRend.SetPropertyBlock(mpb);
            if (age > decalLife) fx.decal.gameObject.SetActive(false);
        }
    }

    Fx Build(int i)
    {
        var fx = new Fx();
        fx.root = new GameObject("StoneImpact_" + i).transform;
        fx.root.SetParent(transform, false);

        // chunks: real meshes, gravity, bounce off the floor
        fx.chunks = NewSystem(fx.root, "Chunks", chunkMat);
        {
            var m = fx.chunks.main;
            m.duration = 0.1f; m.loop = false; m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * scale, 6f * scale);
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f * scale, 0.09f * scale);
            m.startRotation3D = true;
            m.startRotationX = m.startRotationY = m.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.gravityModifier = 1.6f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = stoneColor;
            Burst(fx.chunks, 10, 16);
            Cone(fx.chunks, 40f);
            var rot = fx.chunks.rotationOverLifetime;
            rot.enabled = true; rot.separateAxes = true;
            rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var col = fx.chunks.collision;
            col.enabled = true; col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium;
            col.bounce = 0.35f; col.dampen = 0.4f; col.lifetimeLoss = 0f; col.radiusScale = 0.5f;
            FadeOutSize(fx.chunks, 0.85f);
            var r = fx.chunks.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            if (chunkMeshes != null && chunkMeshes.Length > 0) r.SetMeshes(chunkMeshes);
            r.alignment = ParticleSystemRenderSpace.Local;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // grit: lots of small spinning flakes, fast and wide
        fx.grit = NewSystem(fx.root, "Grit", gritMat);
        {
            var m = fx.grit.main;
            m.duration = 0.1f; m.loop = false; m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f * scale, 9f * scale);
            m.startSize = new ParticleSystem.MinMaxCurve(0.012f * scale, 0.035f * scale);
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.gravityModifier = 1.2f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = stoneColor;
            Burst(fx.grit, 30, 45);
            Cone(fx.grit, 60f);
            Sheet(fx.grit, 2, true);
            var rot = fx.grit.rotationOverLifetime;
            rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            FadeOutSize(fx.grit, 0.7f);
        }

        // dust: slow puff that drifts off the wall, grows and fades
        fx.dust = NewSystem(fx.root, "Dust", dustMat);
        {
            var m = fx.dust.main;
            m.duration = 0.15f; m.loop = false; m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * scale, 1.4f * scale);
            m.startSize = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.7f * scale);
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.gravityModifier = -0.02f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = new Color(stoneColor.r, stoneColor.g, stoneColor.b, 0.85f);
            Burst(fx.dust, 5, 8);
            Cone(fx.dust, 55f);
            Sheet(fx.dust, 4, false);
            var size = fx.dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 2f)));
            var vel = fx.dust.limitVelocityOverLifetime;
            vel.enabled = true; vel.limit = 0.3f * scale; vel.dampen = 0.12f;
            var rot = fx.dust.rotationOverLifetime;
            rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            fx.dust.GetComponent<ParticleSystemRenderer>().sortingFudge = -10;
        }

        // crack decal: a quad on the hit surface
        var dq = GameObject.CreatePrimitive(PrimitiveType.Quad);
        dq.name = "Decal"; Destroy(dq.GetComponent<Collider>());
        dq.transform.SetParent(transform, false);
        fx.decal = dq.transform;
        fx.decalRend = dq.GetComponent<MeshRenderer>();
        fx.decalRend.sharedMaterial = decalMat;
        fx.decalRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dq.SetActive(false);

        fx.root.gameObject.SetActive(false);
        return fx;
    }

    static ParticleSystem NewSystem(Transform parent, string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        var e = ps.emission; e.rateOverTime = 0;
        return ps;
    }

    static void Burst(ParticleSystem ps, short min, short max)
    {
        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, min, max) });
    }

    // emits outward from the surface (root's forward = hit normal)
    static void Cone(ParticleSystem ps, float angle)
    {
        var s = ps.shape;
        s.enabled = true; s.shapeType = ParticleSystemShapeType.Cone;
        s.angle = angle; s.radius = 0.02f;
    }

    static void Sheet(ParticleSystem ps, int tiles, bool randomFrame)
    {
        var t = ps.textureSheetAnimation;
        t.enabled = true; t.numTilesX = tiles; t.numTilesY = tiles;
        if (randomFrame)
        {
            t.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            t.startFrame = new ParticleSystem.MinMaxCurve(0f, tiles * tiles);
        }
    }

    static void FadeOutSize(ParticleSystem ps, float holdUntil)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(holdUntil, 1), new Keyframe(1, 0)));
    }
}
