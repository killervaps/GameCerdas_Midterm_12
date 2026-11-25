using UnityEngine;

public enum ZoneState
{
    // Tidak ada MC maupun musuh di zona.
    Empty,
    // MC di zona tanpa musuh, progres naik.
    Player,
    // Ada musuh di zona, progres berhenti.
    Contested
}

// Zona Hold Zone di tengah satu room. Dibuat HoldZoneObjective saat ronde mulai dan dihapus saat ronde selesai.
//   Progres  naik 1/Capture Seconds per detik saat MC di zona dan tidak ada musuh di zona.
//            Berhenti saat ada musuh di zona (contested), walau MC di dalam.
//            Turun Decay Per Second saat MC di luar zona dan zona tidak contested.
//   Visual   cincin di lantai dan lampu di tengah. Biru = dikuasai MC, kuning = contested, abu = kosong.
public class HoldZone : MonoBehaviour
{
    private const int RingSegments = 64;

    private static readonly Color PlayerColor = new Color(0.3f, 0.65f, 1f);
    private static readonly Color ContestedColor = new Color(1f, 0.8f, 0.2f);
    private static readonly Color EmptyColor = new Color(0.6f, 0.6f, 0.65f);

    private LineRenderer ring;
    private LineRenderer progressRing;
    private Light glow;
    private float captureSeconds = 40f;
    private float decayPerSecond = 0.02f;
    private float heightTolerance = 2f;
    private Color currentColor = EmptyColor;

    public float Radius { get; private set; }

    public float Progress01 { get; private set; }

    public ZoneState State { get; private set; } = ZoneState.Empty;

    public bool PlayerInside { get; private set; }

    public int EnemiesInside { get; private set; }

    public float CaptureSeconds => captureSeconds;

    public void Setup(float radius, float capture, float decay)
    {
        Radius = Mathf.Max(1f, radius);
        captureSeconds = Mathf.Max(1f, capture);
        decayPerSecond = Mathf.Max(0f, decay);
        Progress01 = 0f;

        ring = CreateRing("Ring", 0.12f, Radius);
        progressRing = CreateRing("Progress", 0.22f, Radius - 0.35f);

        GameObject lightObject = new GameObject("Glow");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = Vector3.up * 2.5f;
        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = Radius + 3f;
        glow.intensity = 2f;
        glow.shadows = LightShadows.None;

        ApplyColor(EmptyColor);
        UpdateProgressRing();
    }

    // Dipanggil HoldZoneObjective tiap frame dengan daftar musuh hidup.
    public void Tick(float deltaTime, PlayerHealth player, BattleManager battle)
    {
        PlayerInside = player != null && !player.IsDead && Contains(player.transform.position);
        EnemiesInside = 0;

        if (battle != null)
        {
            foreach (EnemyHealth enemy in battle.Alive)
            {
                if (enemy != null && !enemy.IsDead && Contains(enemy.transform.position))
                    EnemiesInside++;
            }
        }

        if (EnemiesInside > 0)
        {
            State = ZoneState.Contested;
        }
        else if (PlayerInside)
        {
            State = ZoneState.Player;
            Progress01 = Mathf.Clamp01(Progress01 + deltaTime / captureSeconds);
        }
        else
        {
            State = ZoneState.Empty;
            Progress01 = Mathf.Clamp01(Progress01 - decayPerSecond * deltaTime);
        }

        Color target = State == ZoneState.Player ? PlayerColor : State == ZoneState.Contested ? ContestedColor : EmptyColor;
        ApplyColor(Color.Lerp(currentColor, target, 1f - Mathf.Exp(-8f * deltaTime)));
        UpdateProgressRing();
    }

    public bool Contains(Vector3 position)
    {
        Vector3 offset = position - transform.position;

        if (Mathf.Abs(offset.y) > heightTolerance)
            return false;

        offset.y = 0f;
        return offset.sqrMagnitude <= Radius * Radius;
    }

    // ---------- Visual ----------

    private LineRenderer CreateRing(string ringName, float width, float radius)
    {
        GameObject ringObject = new GameObject(ringName);
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = Vector3.up * 0.06f;

        LineRenderer line = ringObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.alignment = LineAlignment.TransformZ;
        line.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
            line.material = new Material(shader);

        SetCircle(line, radius, 1f);
        return line;
    }

    // Lingkaran (sebagian, sesuai fraction) di bidang XY lokal, yang diputar menjadi bidang lantai.
    private static void SetCircle(LineRenderer line, float radius, float fraction)
    {
        int count = Mathf.Max(2, Mathf.CeilToInt(RingSegments * Mathf.Clamp01(fraction)) + 1);
        line.loop = fraction >= 0.999f;
        line.positionCount = line.loop ? RingSegments : count;

        for (int i = 0; i < line.positionCount; i++)
        {
            float t = line.loop ? (float)i / RingSegments : (float)i / (count - 1) * fraction;
            float angle = t * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, 0f));
        }
    }

    private void UpdateProgressRing()
    {
        if (progressRing == null)
            return;

        bool visible = Progress01 > 0.005f;
        progressRing.enabled = visible;

        if (visible)
            SetCircle(progressRing, Radius - 0.35f, Progress01);
    }

    private void ApplyColor(Color color)
    {
        currentColor = color;

        if (ring != null)
            ring.startColor = ring.endColor = color;

        if (progressRing != null)
            progressRing.startColor = progressRing.endColor = Color.Lerp(color, Color.white, 0.35f);

        if (glow != null)
            glow.color = color;
    }

    private void OnDestroy()
    {
        DestroyMaterial(ring);
        DestroyMaterial(progressRing);
    }

    private static void DestroyMaterial(LineRenderer line)
    {
        if (line != null && line.material != null)
            Destroy(line.material);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = State == ZoneState.Contested ? ContestedColor : State == ZoneState.Player ? PlayerColor : EmptyColor;

        const int segments = 48;
        Vector3 previous = transform.position + new Vector3(Radius, 0.1f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = transform.position + new Vector3(Mathf.Cos(angle) * Radius, 0.1f, Mathf.Sin(angle) * Radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
