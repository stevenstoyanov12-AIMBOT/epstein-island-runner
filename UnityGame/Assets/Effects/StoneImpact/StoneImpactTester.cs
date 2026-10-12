using UnityEngine;
using UnityEngine.InputSystem;

// Test bench: drop on any object in an empty scene and press Play.
// Builds a stone wall + floor, sets up StoneImpactFX, left click shoots.
public class StoneImpactTester : MonoBehaviour
{
    void Start()
    {
        var fx = FindFirstObjectByType<StoneImpactFX>();
        if (fx == null)
        {
            var go = new GameObject("StoneImpactFX");
            go.SetActive(false);  // fill assets before Awake runs
            fx = go.AddComponent<StoneImpactFX>();
#if UNITY_EDITOR
            const string dir = "Assets/Effects/StoneImpact/";
            var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(dir + "StoneChunks.fbx");
            var meshes = new System.Collections.Generic.List<Mesh>();
            foreach (var a in all) if (a is Mesh m) meshes.Add(m);
            fx.chunkMeshes = meshes.ToArray();
            fx.dustSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "DustPuff_4x4.png");
            fx.gritAtlas = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "Grit_2x2.png");
            fx.decalTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "ImpactDecal.png");
#endif
            go.SetActive(true);
        }

        var stone = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.8f, 0.79f, 0.76f) };
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "TestWall"; wall.transform.position = new Vector3(0, 1.5f, 4); wall.transform.localScale = new Vector3(6, 3, 0.5f);
        wall.GetComponent<Renderer>().sharedMaterial = stone;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "TestFloor"; floor.transform.position = new Vector3(0, -0.05f, 2); floor.transform.localScale = new Vector3(10, 0.1f, 10);
        floor.GetComponent<Renderer>().sharedMaterial = stone;

        var cam = Camera.main;
        if (cam != null) { cam.transform.position = new Vector3(0, 1.6f, -1); cam.transform.rotation = Quaternion.identity; }
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Camera.main == null) return;
        var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, 100f)) StoneImpactFX.Spawn(hit.point, hit.normal);
    }
}
