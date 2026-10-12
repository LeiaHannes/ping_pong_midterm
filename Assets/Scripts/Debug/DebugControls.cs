using UnityEngine;
using UnityEngine.InputSystem;


public class DebugControls : MonoBehaviour
{

    [SerializeField] private MousePaddleInput mouseInput;
    [SerializeField] private SensorPaddleInput sensorInput;

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

        if (kb.mKey.wasPressedThisFrame && mouseInput != null && sensorInput != null)
        {
            mouseInput.enabled = !mouseInput.enabled;
            sensorInput.enabled = !mouseInput.enabled;
            Debug.Log($"Paddle input: {(mouseInput.enabled ? "mouse" : "camera")}");
        }
    }

    void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Debug.Log($"Time scale: {scale}");
    }
}