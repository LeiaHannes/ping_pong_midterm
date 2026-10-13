using UnityEngine;
using UnityEngine.InputSystem;

public class DisplayControls : MonoBehaviour
{
    [SerializeField] private Vector2Int windowedSize = new Vector2Int(1600, 900);

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f11Key.wasPressedThisFrame)
            ToggleFullscreen();
    }

    public void ToggleFullscreen()
    {
        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            // The desktop's resolution, i.e. the screen's native size
            Resolution native = Screen.currentResolution;
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            Screen.SetResolution(windowedSize.x, windowedSize.y, FullScreenMode.Windowed);
        }
    }
}