using UnityEngine;

public struct PaddlePose
{
    public Vector3 Position;      // center of the blade
    public Quaternion Rotation;

    public PaddlePose(Vector3 position, Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    // Direction the forehand face points
    public Vector3 Normal => Rotation * Vector3.forward;

    // Blend between two poses (t = 0 gives a, t = 1 gives b)
    public static PaddlePose Lerp(PaddlePose a, PaddlePose b, float t) =>
        new PaddlePose(Vector3.Lerp(a.Position, b.Position, t),
                       Quaternion.Slerp(a.Rotation, b.Rotation, t));
}