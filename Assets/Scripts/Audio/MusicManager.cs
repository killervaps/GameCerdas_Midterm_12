using UnityEngine;

// Musik latar mengikuti state RunManager, dengan crossfade antar lagu.
//   Idle (menu utama)       Menu Music
//   Preparing (Safezone)    Safezone Music
//   Battle / RoundCleared   Battle Music
//   Victory / Defeat        Victory Music / Defeat Music
// Lagu yang sama tidak diputar ulang dari awal. Slot kosong berarti hening untuk state itu.
// Volume akhir = Music Volume (master) × volume lagu. Slider bisa digeser saat Play dan langsung terdengar.
// Saat game dijeda musik tetap jalan dengan volume lebih kecil (GameUI menjeda AudioListener, sumber ini mengabaikannya).
// Efek suara diurus SfxManager.
[DisallowMultipleComponent]
public class MusicManager : MonoBehaviour
{
    private enum Track
    {
        None,
        Menu,
        Safezone,
        Battle,
        Victory,
        Defeat
    }

    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;

    [Header("Tracks")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField, Range(0f, 1f)] private float menuVolume = 1f;
    [SerializeField] private AudioClip safezoneMusic;
    [SerializeField, Range(0f, 1f)] private float safezoneVolume = 1f;
    [SerializeField] private AudioClip battleMusic;
    [SerializeField, Range(0f, 1f)] private float battleVolume = 1f;
    [SerializeField] private AudioClip victoryMusic;
    [SerializeField, Range(0f, 1f)] private float victoryVolume = 1f;
    [SerializeField] private AudioClip defeatMusic;
    [SerializeField, Range(0f, 1f)] private float defeatVolume = 1f;

    [Header("Result")]
    // Matikan jika musik menang/kalah berupa jingle pendek yang cukup diputar sekali.
    [SerializeField] private bool loopVictory = true;
    [SerializeField] private bool loopDefeat = true;

    [Header("Mixing")]
    // Volume master untuk semua lagu.
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.6f;
    [SerializeField] private float crossfadeDuration = 1.2f;
    // Pengali volume musik saat game dijeda.
    [SerializeField, Range(0f, 1f)] private float pausedVolume = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool logChanges = true;

    private readonly Track[] sourceTrack = new Track[2];

    private AudioSource[] sources;
    private int current;
    private float fade = 1f;
    private float outgoingStartLevel;
    private float duck = 1f;

    public static MusicManager Current { get; private set; }

    public AudioClip CurrentClip => sources != null ? sources[current].clip : null;

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            Debug.LogWarning("MusicManager: ada lebih dari satu di scene, yang ini dimatikan.", this);
            enabled = false;
            return;
        }

        Current = this;

        sources = new[] { CreateSource("Music A"), CreateSource("Music B") };

        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();
    }

    private void OnEnable()
    {
        if (runManager != null)
            runManager.StateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (runManager != null)
            runManager.StateChanged -= HandleStateChanged;
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }

    private void Start()
    {
        HandleStateChanged(runManager != null ? runManager.State : RunState.Idle);
    }

    private void HandleStateChanged(RunState state)
    {
        switch (state)
        {
            case RunState.Idle:
                Play(Track.Menu);
                break;
            case RunState.Preparing:
                Play(Track.Safezone);
                break;
            case RunState.Battle:
            case RunState.RoundCleared:
                Play(Track.Battle);
                break;
            case RunState.Victory:
                Play(Track.Victory);
                break;
            case RunState.Defeat:
                Play(Track.Defeat);
                break;
        }
    }

    // Crossfade ke lagu milik track ini. Track tanpa clip memudarkan musik sampai hening.
    private void Play(Track track)
    {
        AudioClip clip = ClipOf(track);
        bool loop = LoopOf(track);
        AudioSource active = sources[current];

        // Lagu yang sama tidak diulang, tapi volume dan loop mengikuti track baru (mis. clip yang sama untuk menu dan Safezone).
        if (clip != null && active.clip == clip && active.isPlaying)
        {
            sourceTrack[current] = track;
            active.loop = loop;
            return;
        }

        if (clip == null && !active.isPlaying)
            return;

        // Sumber lama menjadi sumber yang memudar keluar, mulai dari volume yang sedang terdengar.
        outgoingStartLevel = fade;
        current = 1 - current;
        fade = 0f;

        AudioSource next = sources[current];
        next.Stop();
        next.clip = clip;
        next.loop = loop;
        next.volume = 0f;
        sourceTrack[current] = track;

        if (clip != null)
            next.Play();

        if (logChanges)
            Debug.Log("MusicManager: " + (clip != null ? clip.name : "hening"), this);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        bool paused = GameUI.Current != null && GameUI.Current.IsPaused;
        duck = Mathf.MoveTowards(duck, paused ? pausedVolume : 1f, dt * 2f);

        if (fade < 1f)
            fade = crossfadeDuration > 0f ? Mathf.Min(1f, fade + dt / crossfadeDuration) : 1f;

        int outgoingIndex = 1 - current;
        AudioSource incoming = sources[current];
        AudioSource outgoing = sources[outgoingIndex];

        incoming.volume = musicVolume * VolumeOf(sourceTrack[current]) * duck * fade;

        if (outgoing.isPlaying)
        {
            outgoing.volume = musicVolume * VolumeOf(sourceTrack[outgoingIndex]) * duck * outgoingStartLevel * (1f - fade);

            if (fade >= 1f)
                outgoing.Stop();
        }
    }

    private AudioClip ClipOf(Track track)
    {
        switch (track)
        {
            case Track.Menu: return menuMusic;
            case Track.Safezone: return safezoneMusic;
            case Track.Battle: return battleMusic;
            case Track.Victory: return victoryMusic;
            case Track.Defeat: return defeatMusic;
            default: return null;
        }
    }

    // Dibaca setiap frame supaya slider di Inspector langsung terdengar saat Play.
    private float VolumeOf(Track track)
    {
        switch (track)
        {
            case Track.Menu: return menuVolume;
            case Track.Safezone: return safezoneVolume;
            case Track.Battle: return battleVolume;
            case Track.Victory: return victoryVolume;
            case Track.Defeat: return defeatVolume;
            default: return 1f;
        }
    }

    private bool LoopOf(Track track)
    {
        switch (track)
        {
            case Track.Victory: return loopVictory;
            case Track.Defeat: return loopDefeat;
            default: return true;
        }
    }

    private AudioSource CreateSource(string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.priority = 0;
        source.ignoreListenerPause = true;

        return source;
    }
}
