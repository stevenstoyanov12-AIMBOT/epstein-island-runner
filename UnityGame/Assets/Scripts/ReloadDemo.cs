using UnityEngine;
using UnityEngine.InputSystem;

// Reload preview in the Statue Garden: T restarts the clip, 1/2/3 = speed 1x / 0.5x / 0.25x.
public class ReloadDemo : MonoBehaviour
{
    Animator anim;
    void Start() { anim = GetComponent<Animator>(); }
    void Update()
    {
        var kb = Keyboard.current;
        if (anim == null || kb == null) return;
        if (kb.tKey.wasPressedThisFrame) anim.Play(0, 0, 0f);
        if (kb.digit1Key.wasPressedThisFrame) anim.speed = 1f;
        if (kb.digit2Key.wasPressedThisFrame) anim.speed = 0.5f;
        if (kb.digit3Key.wasPressedThisFrame) anim.speed = 0.25f;
    }
}
