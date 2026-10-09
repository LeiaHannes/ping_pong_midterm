using UnityEngine;

public class ContactAudio : MonoBehaviour
{
    [Header("Clips (several per surface for variety)")]
    [SerializeField] private AudioClip[] tableClips;
    [SerializeField] private AudioClip[] paddleClips;
    [SerializeField] private AudioClip[] netClips;
    [SerializeField] private AudioClip[] floorClips;

    [Header("Loudness")]
    [SerializeField] private float fullVolumeSpeed = 8f;   // m/s that plays at full volume
    [SerializeField] private float minSpeed = 0.3f;        // quieter contacts are skipped
    [SerializeField] private float pitchJitter = 0.06f;

    [Header("Voices")]
    [SerializeField] private int voiceCount = 8;

    private AudioSource[] voices;
    private int nextVoice;

    void Awake()
    {
        voices = new AudioSource[voiceCount];
        for (int i = 0; i < voiceCount; i++)
        {
            var go = new GameObject($"Voice {i}");
            go.transform.SetParent(transform);

            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;                          // fully 3D
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 0.5f;
            src.maxDistance = 15f;
            voices[i] = src;
        }
    }

    void OnEnable() => BallSimulator.AnyContact += HandleContact;
    void OnDisable() => BallSimulator.AnyContact -= HandleContact;

    void HandleContact(BallSimulator ball, Contact c)
    {
        float speed = c.Velocity.magnitude;
        if (speed < minSpeed) return;

        AudioClip[] clips = c.Surface switch
        {
            ContactSurface.Table => tableClips,
            ContactSurface.Paddle => paddleClips,
            ContactSurface.Net => netClips,
            ContactSurface.Floor => floorClips,
            _ => null
        };
        if (clips == null || clips.Length == 0) return;

        // Take the next voice in rotation
        AudioSource src = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;

        src.transform.position = c.Position;
        src.clip = clips[Random.Range(0, clips.Length)];
        src.volume = Mathf.Clamp01(speed / fullVolumeSpeed);
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.Play();
    }
}