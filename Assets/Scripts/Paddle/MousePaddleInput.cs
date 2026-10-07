using UnityEngine;
using UnityEngine.InputSystem;

public class MousePaddleInput : MonoBehaviour, IPaddleInput
{
    [Header("Movement plane")]
    [SerializeField] private float restZ = -1.55f;
    [SerializeField] private Vector2 xRange = new Vector2(-1.0f, 1.0f);
    [SerializeField] private Vector2 yRange = new Vector2(0.75f, 1.5f);

    [Header("Face angles (scroll = tilt, Q/E = aim, middle click = reset)")]
    [SerializeField] private float tiltStep = 5f;     // degrees per scroll notch
    [SerializeField] private float aimSpeed = 90f;    // degrees per second
    [SerializeField] private float tilt;              // + closed (face down), - open (face up)
    [SerializeField] private float aim;               // + aims toward the right

    [Header("Swing (left click)")]
    [SerializeField] private float swingDistance = 0.35f;
    [SerializeField] private float swingTime = 0.12f;
    [SerializeField] private float returnTime = 0.25f;

    private Vector3 planePoint;
    private float swingStart = -100f;

    void Awake()
    {
        planePoint = new Vector3(0.3f, 0.95f, restZ);
    }

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null) return;

        // Mouse position -> point on a vertical plane at restZ
        Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, restZ));
        if (plane.Raycast(ray, out float distance))
        {
            Vector3 p = ray.GetPoint(distance);
            planePoint = new Vector3(
                Mathf.Clamp(p.x, xRange.x, xRange.y),
                Mathf.Clamp(p.y, yRange.x, yRange.y),
                restZ);
        }

        // Tilt with the scroll wheel (one step per notch)
        float scroll = mouse.scroll.ReadValue().y;
        if (scroll > 0f) tilt -= tiltStep;
        if (scroll < 0f) tilt += tiltStep;
        tilt = Mathf.Clamp(tilt, -60f, 60f);

        // Aim with Q/E
        if (kb != null)
        {
            if (kb.qKey.isPressed) aim -= aimSpeed * Time.deltaTime;
            if (kb.eKey.isPressed) aim += aimSpeed * Time.deltaTime;
        }
        aim = Mathf.Clamp(aim, -45f, 45f);

        if (mouse.middleButton.wasPressedThisFrame)
        {
            tilt = 0f;
            aim = 0f;
        }

        // Start a swing (only if the previous one has finished)
        if (mouse.leftButton.wasPressedThisFrame && Time.time - swingStart > swingTime + returnTime)
            swingStart = Time.time;
    }

    public PaddlePose GetPose()
    {
        Vector3 position = planePoint;
        position.z += swingDistance * SwingCurve(Time.time - swingStart);
        Quaternion rotation = Quaternion.Euler(tilt, aim, 0f);
        return new PaddlePose(position, rotation);
    }

    // 0 -> 1 smoothly over swingTime, then 1 -> 0 smoothly over returnTime
    float SwingCurve(float t)
    {
        if (t < 0f) return 0f;
        if (t < swingTime)
            return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t / swingTime);
        t -= swingTime;
        if (t < returnTime)
            return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * t / returnTime);
        return 0f;
    }
}