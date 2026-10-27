using System;
using UnityEngine;

public enum SfxCue
{
    None,
    UiClick,
    GateChoose,
    MagicCast,
    MagicHit,
    ProjectileHitWall,
    BurstCharge,
    BurstRelease,
    ChargerWindupBeat,
    ChargerLunge,
    ChargerLungeHit,
    GuardianSwing,
    GuardianHit,
    GuardianEnrage,
    ShooterShot,
    ShooterHit,
    EnemyDeath
}

// Semua efek suara game di satu tempat. Script gameplay cukup memanggil SfxManager.Play(SfxCue.X).
// Slot yang kosong tidak bersuara, jadi suara bisa diisi bertahap.
// Tiap slot boleh berisi beberapa clip, satu dipilih acak (tidak sama dengan sebelumnya) supaya tidak monoton.
// Suara gameplay ikut berhenti saat game dijeda, suara klik UI tetap terdengar di menu jeda.
[DisallowMultipleComponent]
public class SfxManager : MonoBehaviour
{
    [Serializable]
    public class SfxEntry
    {
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        // Pitch acak di rentang ini. Variasi kecil membuat suara berulang tidak terdengar seperti rekaman yang sama.
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        // Jeda minimal antar pemutaran. Mencegah suara menumpuk, mis. banyak proyektil kena di frame yang sama.
        public float minInterval = 0.05f;

        [NonSerialized] public float lastPlayTime = float.NegativeInfinity;
        [NonSerialized] public int lastIndex = -1;
    }

    [Header("Mixing")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.8f;
    // Jumlah suara gameplay yang bisa terdengar bersamaan.
    [SerializeField] private int voices = 12;

    [Header("UI dan Gate")]
    [SerializeField] private SfxEntry uiClick = new SfxEntry();
    [SerializeField] private SfxEntry gateChoose = new SfxEntry();

    [Header("MC")]
    // Bola Aura dilepas.
    [SerializeField] private SfxEntry magicCast = new SfxEntry();
    // Bola Aura mengenai musuh.
    [SerializeField] private SfxEntry magicHit = new SfxEntry();
    // Proyektil siapa pun menabrak dinding atau obstacle.
    [SerializeField] private SfxEntry projectileHitWall = new SfxEntry();
    // Tombol Aura Burst ditekan (awal animasi).
    [SerializeField] private SfxEntry burstCharge = new SfxEntry();
    // Ledakan Aura Burst (saat damage mendarat).
    [SerializeField] private SfxEntry burstRelease = new SfxEntry();

    [Header("Charger")]
    // Tiap ketukan "tung" selama windup (3 kali).
    [SerializeField] private SfxEntry chargerWindupBeat = new SfxEntry();
    [SerializeField] private SfxEntry chargerLunge = new SfxEntry();
    [SerializeField] private SfxEntry chargerLungeHit = new SfxEntry();

    [Header("Guardian")]
    // Awal ayunan, mis. suara whoosh.
    [SerializeField] private SfxEntry guardianSwing = new SfxEntry();
    [SerializeField] private SfxEntry guardianHit = new SfxEntry();
    [SerializeField] private SfxEntry guardianEnrage = new SfxEntry();

    [Header("Shooter")]
    [SerializeField] private SfxEntry shooterShot = new SfxEntry();
    // Proyektil Shooter mengenai MC.
    [SerializeField] private SfxEntry shooterHit = new SfxEntry();

    [Header("Umum")]
    [SerializeField] private SfxEntry enemyDeath = new SfxEntry();

    private AudioSource[] pool;
    private AudioSource uiSource;
    private int nextVoice;

    public static SfxManager Current { get; private set; }

    public static void Play(SfxCue cue)
    {
        if (Current != null)
            Current.PlayCue(cue);
    }

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            Debug.LogWarning("SfxManager: ada lebih dari satu di scene, yang ini dimatikan.", this);
            enabled = false;
            return;
        }

        Current = this;

        pool = new AudioSource[Mathf.Max(1, voices)];

        for (int i = 0; i < pool.Length; i++)
            pool[i] = CreateSource("Voice " + i, false);

        uiSource = CreateSource("UI", true);
    }

    private void OnEnable()
    {
        EnemyHealth.AnyDied += HandleEnemyDied;
    }

    private void OnDisable()
    {
        EnemyHealth.AnyDied -= HandleEnemyDied;
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }

    public void PlayCue(SfxCue cue)
    {
        if (!enabled)
            return;

        SfxEntry entry = Entry(cue);

        if (entry == null || entry.clips == null || entry.clips.Length == 0)
            return;

        float now = Time.unscaledTime;

        if (now - entry.lastPlayTime < entry.minInterval)
            return;

        AudioClip clip = PickClip(entry);

        if (clip == null)
            return;

        entry.lastPlayTime = now;

        AudioSource source = cue == SfxCue.UiClick ? uiSource : NextVoice();
        source.Stop();
        source.clip = clip;
        source.volume = sfxVolume * entry.volume;
        source.pitch = UnityEngine.Random.Range(entry.pitchRange.x, entry.pitchRange.y);
        source.Play();
    }

    private void HandleEnemyDied(EnemyHealth enemy)
    {
        PlayCue(SfxCue.EnemyDeath);
    }

    private SfxEntry Entry(SfxCue cue)
    {
        switch (cue)
        {
            case SfxCue.UiClick: return uiClick;
            case SfxCue.GateChoose: return gateChoose;
            case SfxCue.MagicCast: return magicCast;
            case SfxCue.MagicHit: return magicHit;
            case SfxCue.ProjectileHitWall: return projectileHitWall;
            case SfxCue.BurstCharge: return burstCharge;
            case SfxCue.BurstRelease: return burstRelease;
            case SfxCue.ChargerWindupBeat: return chargerWindupBeat;
            case SfxCue.ChargerLunge: return chargerLunge;
            case SfxCue.ChargerLungeHit: return chargerLungeHit;
            case SfxCue.GuardianSwing: return guardianSwing;
            case SfxCue.GuardianHit: return guardianHit;
            case SfxCue.GuardianEnrage: return guardianEnrage;
            case SfxCue.ShooterShot: return shooterShot;
            case SfxCue.ShooterHit: return shooterHit;
            case SfxCue.EnemyDeath: return enemyDeath;
            default: return null;
        }
    }

    private static AudioClip PickClip(SfxEntry entry)
    {
        int count = entry.clips.Length;
        int index = UnityEngine.Random.Range(0, count);

        if (count > 1 && index == entry.lastIndex)
            index = (index + 1) % count;

        entry.lastIndex = index;
        return entry.clips[index];
    }

    // Pakai sumber yang sedang diam. Jika semua sibuk, suara paling lama ditimpa (urutan bergilir).
    private AudioSource NextVoice()
    {
        for (int i = 0; i < pool.Length; i++)
        {
            int index = (nextVoice + i) % pool.Length;

            if (!pool[index].isPlaying)
            {
                nextVoice = (index + 1) % pool.Length;
                return pool[index];
            }
        }

        AudioSource stolen = pool[nextVoice];
        nextVoice = (nextVoice + 1) % pool.Length;
        return stolen;
    }

    private AudioSource CreateSource(string sourceName, bool ignorePause)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = ignorePause;

        return source;
    }
}
