using UnityEngine;
using UnityEngine.InputSystem;

public class DebugControls : MonoBehaviour
{
    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) SetTimeScale(1f);
        if (kb.digit2Key.wasPressedThisFrame) SetTimeScale(0.25f);
        if (kb.digit3Key.wasPressedThisFrame) SetTimeScale(0.05f);
        if (kb.pKey.wasPressedThisFrame) SetTimeScale(Time.timeScale == 0f ? 1f : 0f);

        if (kb.iKey.wasPressedThisFrame)
        {
            BallSimulator.Interpolate = !BallSimulator.Interpolate;
            Debug.Log($"Interpolation: {(BallSimulator.Interpolate ? "ON" : "OFF")}");
        }
    }

    void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Debug.Log($"Time scale: {scale}");
    }
}