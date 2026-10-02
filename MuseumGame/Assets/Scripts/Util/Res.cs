using System.Collections.Generic;
using UnityEngine;

// Replacement for Resources.Load: assets live in Assets/ResData and are shipped inside the streamed (Addressables) scene
// through a ResourceRegistry component, so they stay out of the tiny base download.
public static class Res
{
    static readonly Dictionary<string, List<Object>> map = new Dictionary<string, List<Object>>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { map.Clear(); }
    public static void Register(string key, Object o) { if (o == null) return; List<Object> l; if (!map.TryGetValue(key, out l)) map[key] = l = new List<Object>(); l.Add(o); }

    public static T Load<T>(string path) where T : Object
    {
        List<Object> l;
        if (map.TryGetValue(path, out l)) foreach (var o in l) if (o != null && o is T) return (T)o;   // skip assets that were unloaded with their scene (the select screen)
        var all = LoadAll<T>(path); return all.Length > 0 ? all[0] : null;
    }
    public static T[] LoadAll<T>(string path) where T : Object
    {
        var r = new List<T>(); List<Object> l;
        if (map.TryGetValue(path, out l)) foreach (var o in l) if (o != null && o is T) r.Add((T)o);
#if UNITY_EDITOR
        if (r.Count == 0)   // editor / unregistered: read straight from the asset folder
        {
            string dir = System.IO.Path.GetDirectoryName("Assets/ResData/" + path).Replace('\\', '/'), name = System.IO.Path.GetFileName(path);
            if (System.IO.Directory.Exists(dir))
                foreach (var f in System.IO.Directory.GetFiles(dir))
                {
                    if (f.EndsWith(".meta")) continue; string fp = f.Replace('\\', '/');
                    if (System.IO.Path.GetFileNameWithoutExtension(fp) != name) continue;
                    var main = UnityEditor.AssetDatabase.LoadMainAssetAtPath(fp); if (main is T) r.Add((T)(Object)main);
                    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fp)) if (o is T && !(o is GameObject) && !r.Contains((T)o)) r.Add((T)o);
                }
        }
#endif
        if (r.Count == 0) { var rl = Resources.LoadAll<T>(path); r.AddRange(rl); }
        return r.ToArray();
    }
}
