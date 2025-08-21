using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Light))]
public class TorchSafeZone : MonoBehaviour
{
    [Header("Asignación desde el MapManager")]
    public int floorIndex;            // El mapa lo inyecta al instanciar
    public Vector2Int corridorCell;   // Celda de pasillo protegida

    [Header("Refs opcionales")]
    public ParticleSystem flameFX;    // Partículas (opcional)
    public AudioSource loopAudio;     // Zumbido/ambiente ON (opcional)

    [Header("Ciclo ON/OFF")]
    public float onSeconds = 10f;
    public float offSeconds = 10f;

    [Header("Activación")]
    public float activationRadius = 6f; // activa si el jugador está cerca
    public Transform player;            // si queda vacío lo buscamos por tag "Player"

    private Light torchLight;
    private MultiFloorDynamicMapManager map;
    private PlayerBatterySystem pbat;

    private Coroutine cycleRoutine;
    private bool currentlyOn = false;

    void Awake()
    {
        torchLight = GetComponent<Light>();
        map = MultiFloorDynamicMapManager.Instance;

        if (!player)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) player = go.transform;
        }
        if (player) pbat = player.GetComponent<PlayerBatterySystem>();

        // Arranca apagada y sin bloquear
        SetVisuals(false);
        SetSanctuary(false);
    }

    void OnEnable()
    {
        if (!map) map = MultiFloorDynamicMapManager.Instance;
        if (!player)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) player = go.transform;
        }
        if (player && !pbat) pbat = player.GetComponent<PlayerBatterySystem>();
    }

    void OnDisable()
    {
        StopCycle();
        SetVisuals(false);
        SetSanctuary(false);
    }

    void Update()
    {
        if (!map || !player) return;

        bool playerClose = IsPlayerNearProtectedCell();
        bool playerHasNoBattery = PlayerHasNoBatteryCharge();
        bool noBatteriesInMap = (map.CountBatteriesAllFloors() == 0);

        bool shouldCycle = playerClose && playerHasNoBattery && noBatteriesInMap;

        if (shouldCycle && cycleRoutine == null)
        {
            cycleRoutine = StartCoroutine(CycleLoop());
        }
        else if (!shouldCycle && cycleRoutine != null)
        {
            StopCycle();
            SetVisuals(false);
            SetSanctuary(false);
        }
    }

    private IEnumerator CycleLoop()
    {
        while (true)
        {
            // ON
            SetVisuals(true);
            SetSanctuary(true);
            yield return new WaitForSeconds(onSeconds);

            // OFF
            SetVisuals(false);
            SetSanctuary(false);
            yield return new WaitForSeconds(offSeconds);
        }
    }

    private void StopCycle()
    {
        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
            cycleRoutine = null;
        }
    }

    private void SetVisuals(bool on)
    {
        currentlyOn = on;
        if (torchLight) torchLight.enabled = on;

        if (flameFX)
        {
            var emission = flameFX.emission;
            emission.enabled = on;
            if (on && !flameFX.isPlaying) flameFX.Play();
            if (!on && flameFX.isPlaying) flameFX.Stop();
        }

        if (loopAudio)
        {
            if (on && !loopAudio.isPlaying) loopAudio.Play();
            if (!on && loopAudio.isPlaying) loopAudio.Stop();
        }
    }

    private void SetSanctuary(bool active)
    {
        if (!map) return;
        map.SetSanctuaryCell(floorIndex, corridorCell, active);
        // El MapManager ya emite OnMapUpdated en SetSanctuaryCell (si así lo implementaste),
        // haciendo que la IA recalcule rutas.
    }

    private bool IsPlayerNearProtectedCell()
    {
        if (!player || !map) return false;
        Vector3 cellWorld = map.CellCenterToWorld(corridorCell, floorIndex);
        cellWorld.y = player.position.y; // Comparación en plano XZ
        return Vector3.SqrMagnitude(player.position - cellWorld) <= (activationRadius * activationRadius);
    }

    private bool PlayerHasNoBatteryCharge()
    {
        if (pbat == null) return true; // Si no hay sistema, actúa como “sin batería” (modo ayuda)
        float total = pbat.curGreen + pbat.curRed + pbat.curBlue;
        return total <= 0.01f;
    }
}
