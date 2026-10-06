using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public float walkSpeed = 2.2f; public float runMultiplier = 2.6f;
    public float mouseSensitivity = 0.16f;
    public float gravity = -9.81f;
    public float jumpHeight = 1.2f;

    public static ScaffoldLadder ladder, ladderNear; public float climbSpeed = 2.4f;
    private CharacterController controller;
    private Camera playerCamera;
    private float verticalVelocity;
    private float cameraPitch = 0f; public static float recoilP, recoilY; // look offsets added by the gun this frame
    private float yaw = 0f;
    public Vector3 camLocalPos = new Vector3(0.45f, 0.75f, -2.2f);
    // crouch: hold Ctrl. Lower capsule, slower, no sprint; stands back up only when there is headroom
    public float crouchHeight = 1.0f, crouchSpeedMultiplier = 0.45f;
    public static bool Crouching, ForceCrouch; float standHeight = -1f, standCenterY, crouchBlend;

    // frame on which a click re-captured the mouse; guns ignore that click so getting back in doesn't fire a shot
    public static int RelockFrame = -1;
    void OnApplicationFocus(bool focused) { if (!focused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();
        yaw = transform.eulerAngles.y;
        if (playerCamera != null) { playerCamera.transform.localPosition = camLocalPos; playerCamera.transform.localRotation = Quaternion.identity; }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null) return;

        // Re-grab the mouse after alt-tab / Esc / leaving fullscreen: the browser drops pointer lock, so any click back in the game locks it again
        if (mouse != null && Cursor.lockState != CursorLockMode.Locked && CharacterSelect.Chosen != null && !keyboard.escapeKey.isPressed
            && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
        { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; RelockFrame = Time.frameCount; }

        // Mouse look (only while the mouse is captured, so moving over the page doesn't spin the camera)
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = mouse.delta.ReadValue();
            float mouseX = delta.x * mouseSensitivity;
            float mouseY = delta.y * mouseSensitivity;

            yaw += mouseX;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            cameraPitch -= mouseY; cameraPitch -= recoilP; yaw += recoilY; recoilP = 0f; recoilY = 0f;
            cameraPitch = Mathf.Clamp(cameraPitch, -45f, 85f);
            if (playerCamera != null)
                playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }

        // WASD movement
        float moveX = 0f, moveZ = 0f;
        if (keyboard.aKey.isPressed) moveX -= 1f;
        if (keyboard.dKey.isPressed) moveX += 1f;
        if (keyboard.sKey.isPressed) moveZ -= 1f;
        if (keyboard.wKey.isPressed) moveZ += 1f;

        // crouch (hold Ctrl)
        if (standHeight < 0f) { standHeight = controller.height; standCenterY = controller.center.y; }
        bool wantCrouch = (ForceCrouch || keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed || keyboard.cKey.isPressed) && ladder == null;
        if (!wantCrouch && Crouching)
        {
            // only stand if nothing is above the head
            float bottom = transform.position.y + controller.center.y - controller.height * 0.5f;
            var from = new Vector3(transform.position.x, bottom + controller.radius, transform.position.z);
            if (Physics.SphereCast(from, controller.radius * 0.9f, Vector3.up, out var hh, standHeight - controller.radius * 2f + 0.05f, ~0, QueryTriggerInteraction.Ignore)
                && !hh.collider.transform.IsChildOf(transform)) wantCrouch = true;
        }
        Crouching = wantCrouch;
        crouchBlend = Mathf.MoveTowards(crouchBlend, Crouching ? 1f : 0f, Time.deltaTime * 6f);
        float h = Mathf.Lerp(standHeight, crouchHeight, crouchBlend);
        controller.height = h;
        controller.center = new Vector3(controller.center.x, standCenterY - (standHeight - h) * 0.5f, controller.center.z);   // feet stay on the ground

        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        move = Vector3.ClampMagnitude(move, 1f) * walkSpeed * (Crouching ? crouchSpeedMultiplier : (keyboard.leftShiftKey.isPressed ? runMultiplier : 1f));

        // Gravity + jump
        if (controller.isGrounded && verticalVelocity < 0)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        if (controller.isGrounded && !Crouching && keyboard.spaceKey.wasPressedThisFrame)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        if (ladder == null && ladderNear != null && keyboard.eKey.wasPressedThisFrame) ladder = ladderNear;
        if (ladder != null && keyboard.spaceKey.wasPressedThisFrame) { ladder = null; verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity); }
        if (ladder != null)
        {
            // climbing: held on the ladder's centre line; W/S up/down; at the top, W steps off onto the deck
            verticalVelocity = 0f;
            var lt = ladder.transform; var local = lt.InverseTransformPoint(transform.position);
            float feet = transform.position.y + controller.center.y - controller.height * 0.5f;
            bool atTop = feet >= ladder.topFeet - 0.02f;
            Vector3 pull = lt.TransformVector(new Vector3(-local.x, 0f, -local.z)) * 6f; pull.y = 0f;
            float up = moveZ * climbSpeed;
            if (atTop && up > 0f) { up = 0f; pull = transform.forward * walkSpeed; }        // step off at the top
            if (feet + up * Time.deltaTime > ladder.topFeet + 0.05f) up = Mathf.Max(0f, (ladder.topFeet + 0.05f - feet) / Time.deltaTime);
            if (moveX != 0f) pull += transform.right * moveX * walkSpeed;                   // side-step off
            move = pull; move.y = up;
        }
        Vector3 velocity = move;
        if (ladder == null) velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        // Escape unlocks the cursor so you can get back to the Editor
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    float camDist = -1f; float ladderBlend; bool wasRemapped;
    void LateUpdate()
    {
        if (playerCamera != null)
        {
            Quaternion rot = transform.rotation * Quaternion.Euler(cameraPitch, 0f, 0f);
            Vector3 origin = transform.position + controller.center;
            // on a ladder: face the rungs, camera close at head height so the ladder fills the view
            ladderBlend = Mathf.MoveTowards(ladderBlend, ladder != null ? 1f : 0f, Time.deltaTime * 4f);
            if (ladder != null)
            {
                float want = Quaternion.LookRotation(ladder.transform.forward).eulerAngles.y;
                yaw = Mathf.MoveTowardsAngle(yaw, want, Time.deltaTime * 900f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                cameraPitch = Mathf.Clamp(cameraPitch, -60f, 60f);
                rot = transform.rotation * Quaternion.Euler(cameraPitch, 0f, 0f);
            }
            Vector3 target = transform.TransformPoint(Vector3.Lerp(camLocalPos - new Vector3(0f, (standHeight > 0f ? standHeight - controller.height : 0f), 0f), new Vector3(0f, 0.55f, -0.15f), ladderBlend));
            bool remapped = SeamlessPortal.Remap(ref origin, ref target, ref rot);
            if (wasRemapped != remapped) { var acd = playerCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); if (acd) acd.resetHistory = true; }
            wasRemapped = remapped;
            Vector3 dir = target - origin; float dist = dir.magnitude;
            float allowed = dist; RaycastHit hit;
            // while remapped, the body is already past the door (inside the wall on this side), so skip camera collision
            if (!remapped && dist > 0.001f && Physics.SphereCast(origin, 0.22f, dir / dist, out hit, dist, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                allowed = Mathf.Max(0f, hit.distance - 0.02f);
            // inside a room whose walls face inward (scanned rooms): also test backfaces so the camera never leaves the room
            if (!remapped && dist > 0.001f)
            {
                bool bf = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
                foreach (var bh in Physics.RaycastAll(origin, dir / dist, dist + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    if (!bh.collider.transform.IsChildOf(transform)) allowed = Mathf.Min(allowed, Mathf.Max(0f, bh.distance - 0.3f));
                Physics.queriesHitBackfaces = bf;
            }
            // snap in instantly (never clip), ease back out smoothly (no pops)
            camDist = (camDist < 0f || allowed < camDist) ? allowed : Mathf.MoveTowards(camDist, allowed, Time.deltaTime * 2.5f);
            playerCamera.transform.SetPositionAndRotation(dist > 0.001f ? origin + dir / dist * camDist : target, rot);
            playerCamera.nearClipPlane = 0.01f;
        }
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // Moves the player instantly (used by the museum entrance). Safe with CharacterController.
    public void Teleport(Vector3 position, float yaw)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        controller.enabled = false;
        transform.position = position;
        this.yaw = yaw;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        controller.enabled = true;
        verticalVelocity = -2f;
        cameraPitch = 0f;
        if (playerCamera != null) playerCamera.transform.localEulerAngles = Vector3.zero;
    }
}
