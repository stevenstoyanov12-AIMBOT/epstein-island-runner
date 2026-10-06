using UnityEngine;

// Pistol reload motion layered on top of whatever the body animation is doing (works while walking, crouched, etc.):
// the gun dips and tilts toward the player, the off hand drops to the belt for a fresh magazine, slaps it in, and the
// gun comes back up. Used for the local body and for other players (RemotePlayer), so everyone sees the reload.
// Replace with a real Mixamo "Pistol Reload" clip later if wanted; the timing comes from SimpleGun.ReloadTime.
public static class ReloadPose
{
    // t = 0..1 through the reload
    public static void Apply(Animator anim, Transform body, float t)
    {
        if (anim == null || !anim.isHuman || t <= 0f || t >= 1f) return;
        float inW = Smooth(t / 0.18f), outW = 1f - Smooth((t - 0.8f) / 0.2f), w = Mathf.Min(inW, outW);
        if (w <= 0f) return;
        // left hand: down to the belt (0.25-0.5), back up with the magazine (0.5-0.62), slap (0.62-0.68)
        float fetch = Bump(t, 0.22f, 0.36f, 0.52f), slap = Bump(t, 0.6f, 0.64f, 0.7f);

        Vector3 right = body.right, fwd = body.forward;
        Rot(anim, HumanBodyBones.RightUpperArm, right, 18f * w);                 // gun lowered
        Rot(anim, HumanBodyBones.RightLowerArm, right, 22f * w);
        Rot(anim, HumanBodyBones.RightHand, fwd, -38f * w);                      // tilted in to look at the grip
        Rot(anim, HumanBodyBones.RightHand, right, 15f * w + 6f * slap);         // kicks a touch on the slap
        Rot(anim, HumanBodyBones.LeftUpperArm, right, (10f + 35f * fetch) * w);  // reach down to the belt
        Rot(anim, HumanBodyBones.LeftLowerArm, right, (-15f + 40f * fetch - 25f * slap) * w);
        Rot(anim, HumanBodyBones.LeftHand, fwd, 20f * w);
        Rot(anim, HumanBodyBones.Head, right, 10f * w);                          // glance down at the gun
    }

    static void Rot(Animator a, HumanBodyBones b, Vector3 axis, float deg)
    {
        var t = a.GetBoneTransform(b);
        if (t != null && deg != 0f) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation;
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float Bump(float t, float a, float peak, float b) => t < a || t > b ? 0f : t < peak ? Smooth((t - a) / (peak - a)) : 1f - Smooth((t - peak) / (b - peak));
}

// Applies the reload motion to the local player's body after its own animation logic ran.
[DefaultExecutionOrder(2300)]
public class LocalReloadPose : MonoBehaviour
{
    Animator anim;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { var p = GameObject.Find("Player"); if (p && !p.GetComponent<LocalReloadPose>()) p.AddComponent<LocalReloadPose>(); }
    void LateUpdate()
    {
        if (!SimpleGun.Reloading) return;
        if (anim == null || !anim.isActiveAndEnabled) anim = GetComponentInChildren<Animator>(false);
        ReloadPose.Apply(anim, transform, SimpleGun.ReloadProgress);
    }
}
