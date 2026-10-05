using UnityEngine;

// Another player as seen locally: smoothed pose, avatar animation, and a hit collider that forwards pistol damage to the network.
public class RemotePlayer : MonoBehaviour
{
    public string id; Animator anim; Vector3 target; float targetYaw, speed; bool crouch, shoot, alive = true; bool first = true;
    float headY; Transform model; Vector3 vel; GazeTarget gaze; bool wasShoot;

    public static RemotePlayer Create(string id, string character, Transform localRoot)
    {
        var prefab = Res.Load<GameObject>("SelectModels/" + character); if (prefab == null || localRoot == null) { Debug.LogWarning("RemotePlayer: cannot create '" + character + "' (prefab " + (prefab != null) + ", local player " + (localRoot != null) + ")"); return null; }
        var go = new GameObject("Remote_" + id);
        var rp = go.AddComponent<RemotePlayer>(); rp.id = id;
        var cc = localRoot.GetComponent<CharacterController>();
        var col = go.AddComponent<CapsuleCollider>();
        if (cc != null) { col.center = cc.center; col.height = cc.height; col.radius = cc.radius; } else { col.center = new Vector3(0f, 0.9f, 0f); col.height = 1.8f; col.radius = 0.3f; }
        rp.headY = col.center.y + col.height * 0.5f - 0.32f;
        var inst = Instantiate(prefab, go.transform); inst.name = "Model_" + character;
        CharacterSelect.FixSleeves(inst, character);
        var av = Object.FindFirstObjectByType<PlayerAvatarAnim>();
        Animator localAnim = av != null ? av.GetComponentInChildren<Animator>(false) : null;
        if (localAnim != null)
        {
            inst.transform.localPosition = localRoot.InverseTransformPoint(localAnim.transform.position);
            inst.transform.localRotation = Quaternion.Inverse(localRoot.rotation) * localAnim.transform.rotation;
        }
        inst.transform.localScale = Vector3.one;
        var a = inst.GetComponent<Animator>(); if (a == null) a = inst.AddComponent<Animator>();
        a.enabled = true; a.applyRootMotion = false; a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (localAnim != null) a.runtimeAnimatorController = localAnim.runtimeAnimatorController;
        rp.anim = a; rp.model = inst.transform;
        CharacterSelect.AttachGunTo(inst, a);
        var lg = localRoot.GetComponent<GazeTarget>();
        var gt = go.AddComponent<GazeTarget>(); gt.remoteProxy = true; gt.testShooting = false; if (lg != null) gt.aimOffset = lg.aimOffset; rp.gaze = gt;
        return rp;
    }

    public void Apply(Net.Msg m)
    {
        target = new Vector3(m.x, m.y, m.z); targetYaw = m.r; speed = m.sp; crouch = m.cr == 1; shoot = m.sh == 1; alive = m.al == 1;
        if (gaze != null) { gaze.proxyAlive = alive; if (shoot && !wasShoot) gaze.MakeNoise(); wasShoot = shoot; }
        if (first) { transform.position = target; transform.rotation = Quaternion.Euler(0f, targetYaw, 0f); first = false; }
    }

    void Update()
    {
        if (first) return;
        transform.position = Vector3.SmoothDamp(transform.position, target, ref vel, 0.1f);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), 1f - Mathf.Exp(-14f * Time.deltaTime));
        if (model != null && model.gameObject.activeSelf != alive) model.gameObject.SetActive(alive);
        GetComponent<Collider>().enabled = alive;
        if (anim != null && anim.runtimeAnimatorController != null && alive)
        {
            anim.SetBool("Crouch", crouch);
            anim.SetFloat("Speed", speed < 0.2f ? 0f : (crouch ? 1.5f : (speed > 4.5f ? 6f : 2f)), 0.15f, Time.deltaTime);
            anim.SetBool("Shooting", shoot);
            if (anim.layerCount > 2) anim.SetLayerWeight(2, Mathf.MoveTowards(anim.GetLayerWeight(2), shoot ? 1f : 0f, Time.deltaTime * 12f));
        }
    }

    // SimpleGun: hit.collider.SendMessageUpwards("OnBulletHit", hit)
    void OnBulletHit(RaycastHit hit)
    {
        if (!alive || Net.I == null) return;
        bool head = hit.point.y > transform.position.y + headY;
        var g = Object.FindFirstObjectByType<FirstPersonController>() != null ? Object.FindFirstObjectByType<FirstPersonController>().GetComponent<GazeTarget>() : null;
        float body = g != null ? Random.Range(g.bodyDamageMin, g.bodyDamageMax) : Random.Range(15f, 16.8f);
        float hd = g != null ? g.headDamage : 34f;
        Net.I.SendHit(id, head ? hd : body);
        StoneImpactFX_Safe(hit);
    }
    void StoneImpactFX_Safe(RaycastHit hit) { }
}
