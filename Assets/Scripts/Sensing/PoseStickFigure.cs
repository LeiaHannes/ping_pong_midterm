using UnityEngine;

public class PoseStickFigure : MonoBehaviour
{
    [SerializeField] private PoseReceiver receiver;
    [SerializeField] private float minVisibility = 0.5f;

    // Pairs of landmark indices to connect with lines
    static readonly int[,] Bones =
    {
        { 11, 12 }, { 23, 24 }, { 11, 23 }, { 12, 24 },   // torso
        { 11, 13 }, { 13, 15 },                            // left arm
        { 12, 14 }, { 14, 16 },                            // right arm
    };

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || receiver == null) return;
        if (!receiver.TryGetLatest(out PoseMessage p)) return;

        Gizmos.color = Color.cyan;
        for (int b = 0; b < Bones.GetLength(0); b++)
        {
            int i = Bones[b, 0];
            int j = Bones[b, 1];
            if (p.vis[i] < minVisibility || p.vis[j] < minVisibility) continue;
            Gizmos.DrawLine(ToWorld(p, i), ToWorld(p, j));
        }

        Gizmos.color = Color.yellow;      // wrists
        Gizmos.DrawSphere(ToWorld(p, 15), 0.04f);
        Gizmos.DrawSphere(ToWorld(p, 16), 0.04f);

        Gizmos.color = Color.white;       // nose, as a stand-in for the head
        Gizmos.DrawSphere(ToWorld(p, 0), 0.08f);
    }

    // Landmarks are relative to the hips; place them relative to this object
    Vector3 ToWorld(PoseMessage p, int i) => transform.TransformPoint(p.Landmark(i));
}