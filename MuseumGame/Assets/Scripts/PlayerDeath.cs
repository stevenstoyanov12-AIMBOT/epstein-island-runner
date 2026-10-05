using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.InputSystem;

// Death for any player character: plays Resources/Death (humanoid clip, works on every character) and locks the controls.
// Call Die() from whatever kills the player (health, damage, other players in multiplayer). K kills yourself for testing.
[DefaultExecutionOrder(2100)]
public class PlayerDeath : MonoBehaviour
{
    public static event System.Action<GameObject> OnPlayerDied;
    public bool dead; System.Collections.Generic.List<Behaviour> locked = new System.Collections.Generic.List<Behaviour>(); PlayableGraph graph; Animator anim; float t0; Vector3 focus; Vector3 animLocal; Vector3 camDir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { var p = GameObject.Find("Player"); if (p && !p.GetComponent<PlayerDeath>()) p.AddComponent<PlayerDeath>(); }

    void Update() { if (!dead && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) Die(); }

    public void Die()
    {
        if (dead) return; dead = true; CrateSpawn.Hidden = false;
        var clips = Res.LoadAll<AnimationClip>("Death"); AnimationClip clip = null;
        foreach (var c in clips) if (c.name == "mixamo.com") clip = c; if (clip == null && clips.Length > 0) clip = clips[0];
        foreach (var m in GetComponents<MonoBehaviour>()) if (m != this && m != null && (m is FirstPersonController || m.GetType().Name.Contains("Gun") || m.GetType().Name == "CrateSpawn")) { if (m.enabled && !(m is CrateSpawn)) locked.Add(m); m.enabled = false; }
        foreach (var pa in GetComponentsInChildren<PlayerAvatarAnim>(true)) { if (pa.enabled) locked.Add(pa); pa.enabled = false; }
        anim = GetComponentInChildren<Animator>(false);   // the active model (the old HeistCharacter one is hidden)
        foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r is SkinnedMeshRenderer || r is MeshRenderer) r.enabled = true;
        if (anim) animLocal = anim.transform.localPosition;
        if (anim)
        {
            var head = anim.GetBoneTransform(HumanBodyBones.Head); if (head) head.localScale = Vector3.one;
            if (clip)
            {
                graph = PlayableGraph.Create("PlayerDeath"); graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var o = AnimationPlayableOutput.Create(graph, "death", anim); var cp = AnimationClipPlayable.Create(graph, clip);
                o.SetSourcePlayable(cp); graph.Play();
            }
        }
        t0 = Time.time; focus = transform.position + Vector3.up * 0.9f;
        var cam = Camera.main; if (cam) { var d = cam.transform.position - focus; d.y = 0; camDir = d.sqrMagnitude < 0.01f ? -transform.forward : d.normalized; }
        OnPlayerDied?.Invoke(gameObject);
    }

    public void Revive()
    {
        if (!dead) return; dead = false;
        if (graph.IsValid()) graph.Destroy();
        if (anim) anim.transform.localPosition = animLocal;
        foreach (var b in locked) if (b) b.enabled = true; locked.Clear();
        var cam = Camera.main; if (cam) cam.nearClipPlane = 0.05f;
    }

    void LateUpdate()
    {
        if (!dead) return;
        if (anim) { var hd = anim.GetBoneTransform(HumanBodyBones.Head); if (hd && hd.localScale.x < 0.5f) hd.localScale = Vector3.one; }   // head was hidden for first person
        if (anim)   // keep the collapsing body on the floor (the clip's hip height is for a different rig)
        {
            float lo = 999f; foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
            { var bt = anim.GetBoneTransform(hb); if (bt) lo = Mathf.Min(lo, bt.position.y); }
            float gy = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(transform.position + Vector3.up * 1f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && h.point.y > gy) gy = h.point.y;
            if (gy > -900f && lo < 900f) { float need = gy + 0.09f - lo; if (need > 0f) anim.transform.position += Vector3.up * need; }
        }
        var cam = Camera.main; if (!cam) return;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - t0) / 3f));
        Vector3 pos = focus + camDir * Mathf.Lerp(2.4f, 3.2f, k) + Vector3.up * Mathf.Lerp(0.7f, 1.6f, k);
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(focus - pos)); cam.nearClipPlane = 0.05f;
    }
    void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
}
