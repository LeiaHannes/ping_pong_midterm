using UnityEngine;

public class AvatarRig : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] private Transform torso;
    [SerializeField] private Transform head;
    [SerializeField] private Transform offHand;

    [Header("Smoothing (higher = snappier)")]
    [SerializeField] private float bodySharpness = 8f;
    [SerializeField] private float headSharpness = 10f;
    [SerializeField] private float handSharpness = 15f;

    private IAvatarSource source;

    void Awake()
    {
        source = GetComponent<IAvatarSource>();
        if (source == null)
            Debug.LogError("AvatarRig needs an avatar source component on the same object.");
    }

    void LateUpdate()
    {
        if (source == null) return;

        AvatarPose target = source.GetPose();
        float dt = Time.deltaTime;

        float body = Blend(bodySharpness, dt);
        torso.SetPositionAndRotation(
            Vector3.Lerp(torso.position, target.TorsoPosition, body),
            Quaternion.Slerp(torso.rotation, target.TorsoRotation, body));

        head.rotation = Quaternion.Slerp(head.rotation, target.HeadRotation, Blend(headSharpness, dt));
        offHand.position = Vector3.Lerp(offHand.position, target.OffHandPosition, Blend(handSharpness, dt));
    }

    // Fraction of the way to move toward a target this frame (frame-rate independent)
    static float Blend(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);
}