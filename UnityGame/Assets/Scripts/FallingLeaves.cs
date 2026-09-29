using System.Collections.Generic;
using UnityEngine;

// Leaves that break off the crown and drift slowly to the ground, swaying and tumbling,
// then lie there for a while before shrinking away. Uses a small pool, no physics.
public class FallingLeaves : MonoBehaviour
{
    public Mesh crown;                 // leaf mesh of the tree; leaves detach from its vertices
    public Material leafMaterial;      // the same atlas material the tree leaves use
    public int maxLeaves = 60;
    public float leavesPerSecond = 1.5f;
    public float fallSpeed = 0.45f;    // metres per second, before sway
    public float groundHeight = 0.012f; // relative to this transform
    public float restTime = 8f;         // seconds a leaf lies on the ground

    class Leaf
    {
        public Transform t;
        public Vector3 spin;
        public float phase, swayAmp, swayFreq, landedAt, size;
        public bool active, landed;
    }

    readonly List<Leaf> pool = new List<Leaf>();
    Vector3[] spawnPoints;
    Mesh[] cellMeshes;
    float spawnTimer;

    void Start()
    {
        if (crown == null || leafMaterial == null)
        {
            enabled = false;
            return;
        }
        // every 9th vertex is enough spawn points and spreads them over the whole crown
        var verts = crown.vertices;
        spawnPoints = new Vector3[verts.Length / 9];
        for (int i = 0; i < spawnPoints.Length; i++)
            spawnPoints[i] = verts[i * 9];

        // one quad per leaf colour in the 2x2 atlas
        cellMeshes = new Mesh[4];
        for (int c = 0; c < 4; c++)
            cellMeshes[c] = LeafQuad(c);

        for (int i = 0; i < maxLeaves; i++)
        {
            var go = new GameObject("FallingLeaf");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = cellMeshes[i % 4];
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = leafMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            pool.Add(new Leaf { t = go.transform });
        }
    }

    static Mesh LeafQuad(int cell)
    {
        float u = (cell % 2) * 0.5f, v = 0.5f - (cell / 2) * 0.5f;
        var m = new Mesh { name = $"FallingLeaf_{cell}" };
        m.vertices = new[] { new Vector3(-0.5f, 0, -0.12f), new Vector3(0.5f, 0, -0.12f),
                             new Vector3(-0.5f, 0, 0.88f), new Vector3(0.5f, 0, 0.88f) };
        m.uv = new[] { new Vector2(u, v), new Vector2(u + 0.5f, v),
                       new Vector2(u, v + 0.5f), new Vector2(u + 0.5f, v + 0.5f) };
        m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void Update()
    {
        spawnTimer += Time.deltaTime * leavesPerSecond;
        while (spawnTimer >= 1f)
        {
            spawnTimer -= 1f;
            Spawn();
        }

        float time = Time.time;
        foreach (var leaf in pool)
        {
            if (!leaf.active) continue;
            var p = leaf.t.localPosition;
            if (!leaf.landed)
            {
                // flutter: side-to-side sway plus slower fall when the leaf swings flat
                float s = Mathf.Sin(time * leaf.swayFreq + leaf.phase);
                float drop = fallSpeed * (0.6f + 0.4f * Mathf.Abs(s));
                p += new Vector3(s * leaf.swayAmp, -drop, Mathf.Cos(time * leaf.swayFreq * 0.7f + leaf.phase) * leaf.swayAmp) * Time.deltaTime;
                leaf.t.localRotation *= Quaternion.Euler(leaf.spin * Time.deltaTime);
                if (p.y <= groundHeight)
                {
                    p.y = groundHeight;
                    leaf.landed = true;
                    leaf.landedAt = time;
                    // settle flat on the ground at a random heading
                    leaf.t.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
                }
                leaf.t.localPosition = p;
            }
            else
            {
                float age = time - leaf.landedAt;
                if (age > restTime)
                {
                    float k = 1f - (age - restTime) / 2f;  // shrink away over two seconds
                    if (k <= 0f)
                    {
                        leaf.active = false;
                        leaf.t.gameObject.SetActive(false);
                    }
                    else
                        leaf.t.localScale = Vector3.one * leaf.size * k;
                }
            }
        }
    }

    void Spawn()
    {
        var leaf = pool.Find(l => !l.active);
        if (leaf == null) return;
        leaf.active = true;
        leaf.landed = false;
        leaf.size = Random.Range(0.09f, 0.14f);
        leaf.phase = Random.Range(0f, Mathf.PI * 2f);
        leaf.swayAmp = Random.Range(0.3f, 0.7f);
        leaf.swayFreq = Random.Range(1.2f, 2.4f);
        leaf.spin = new Vector3(Random.Range(-120f, 120f), Random.Range(-90f, 90f), Random.Range(-160f, 160f));
        leaf.t.localPosition = spawnPoints[Random.Range(0, spawnPoints.Length)];
        leaf.t.localRotation = Random.rotation;
        leaf.t.localScale = Vector3.one * leaf.size;
        leaf.t.gameObject.SetActive(true);
    }
}
