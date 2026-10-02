using UnityEngine;

// Lives in the streamed scenes and registers their assets with Res on load (see Res.cs, ResRegistryBuilder).
// Own file, named after the class, so Unity can reference the script from Addressables scene bundles.
public class ResourceRegistry : MonoBehaviour
{
    public string[] keys; public Object[] objs;
    void Awake() { Debug.Log("ResourceRegistry: " + (keys == null ? 0 : keys.Length) + " entries in " + gameObject.scene.name); if (keys == null) return; for (int i = 0; i < keys.Length && i < objs.Length; i++) Res.Register(keys[i], objs[i]); }
}
