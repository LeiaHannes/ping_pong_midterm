using UnityEngine;

public struct AvatarPose
{
    public Vector3 TorsoPosition;      // waist pivot, world space
    public Quaternion TorsoRotation;
    public Quaternion HeadRotation;    // world space
    public Vector3 OffHandPosition;    // world space

    public AvatarPose(Vector3 torsoPosition, Quaternion torsoRotation,
                      Quaternion headRotation, Vector3 offHandPosition)
    {
        TorsoPosition = torsoPosition;
        TorsoRotation = torsoRotation;
        HeadRotation = headRotation;
        OffHandPosition = offHandPosition;
    }
}