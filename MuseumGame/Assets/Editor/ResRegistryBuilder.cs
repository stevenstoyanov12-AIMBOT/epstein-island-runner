using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Fills the ResourceRegistry of the streamed scenes with the assets in Assets/ResData.
public static class ResRegistryBuilder
{
    [MenuItem("Tools/Update Resource Registry")]
    public static void Update()
    {
        EditorSceneManager.SaveOpenScenes();
        Fill("Assets/Scenes/Outside.unity", null);
        Fill("Assets/Scenes/Select.unity", "SelectModels/");
    }

    static void Fill(string scenePath, string prefix)
    {
        if (!System.IO.File.Exists(scenePath)) return;
        var sc = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var go = GameObject.Find("ResourceRegistry"); if (go == null) go = new GameObject("ResourceRegistry");
        // earlier builds piled up duplicate / broken registry components: keep exactly one, with a proper script reference
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        var regs = go.GetComponents<ResourceRegistry>();
        for (int r = 1; r < regs.Length; r++) Object.DestroyImmediate(regs[r]);
        var reg = regs.Length > 0 ? regs[0] : go.AddComponent<ResourceRegistry>();
        var keys = new List<string>(); var objs = new List<Object>();
        foreach (var f in System.IO.Directory.GetFiles("Assets/ResData", "*", System.IO.SearchOption.AllDirectories))
        {
            if (f.EndsWith(".meta")) continue; string p = f.Replace('\\', '/');
            string key = p.Substring("Assets/ResData/".Length); key = key.Substring(0, key.LastIndexOf('.'));
            if (prefix != null && !key.StartsWith(prefix)) continue;
            var main = AssetDatabase.LoadMainAssetAtPath(p); if (main == null) continue;
            keys.Add(key); objs.Add(main);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p)) if (o != null && o != main && !(o is GameObject) && !(o is Component)) { keys.Add(key); objs.Add(o); }
        }
        reg.keys = keys.ToArray(); reg.objs = objs.ToArray();
        EditorUtility.SetDirty(reg); EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
        Debug.Log("Resource registry " + scenePath + ": " + keys.Count + " entries");
    }
}
