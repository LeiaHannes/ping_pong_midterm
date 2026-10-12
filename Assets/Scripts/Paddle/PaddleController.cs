using UnityEngine;

[DefaultExecutionOrder(-100)]

public class PaddleController : MonoBehaviour
{
    [Header("Live readout")]
    [SerializeField] private float debugSpeed;

    private IPaddleInput[] inputs;
    private PaddlePose previous;
    private PaddlePose current;
    private Vector3 velocity;

    public PaddlePose Previous => previous;
    public PaddlePose Current => current;
    public Vector3 Velocity => velocity;

    void Awake()
    {
        inputs = GetComponents<IPaddleInput>();
        if (inputs.Length == 0)
            Debug.LogError("PaddleController needs a paddle input component on the same object.");

        current = new PaddlePose(transform.position, transform.rotation);
        previous = current;
    }

    IPaddleInput ActiveInput()
    {
        foreach (IPaddleInput i in inputs)
            if (i is MonoBehaviour mb && mb.enabled)
                return i;
        return null;
    }

    void FixedUpdate()
    {
        IPaddleInput input = ActiveInput();
        if (input == null) return;

        previous = current;
        current = input.GetPose();
        velocity = (current.Position - previous.Position) / Time.fixedDeltaTime;
        debugSpeed = velocity.magnitude;
    }

    void Update()
    {
        float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        PaddlePose shown = PaddlePose.Lerp(previous, current, alpha);
        transform.SetPositionAndRotation(shown.Position, shown.Rotation);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.blue;       // face direction
        Gizmos.DrawRay(current.Position, current.Normal * 0.15f);

        Gizmos.color = Color.magenta;    // velocity (scaled down to fit)
        Gizmos.DrawRay(current.Position, velocity * 0.05f);
    }
}