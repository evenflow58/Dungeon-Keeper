using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A goblin minion's body and needs clock: tile movement (same API and semantics as Imp's) plus
/// Hunger and Energy that decay in real time. Hunger at 0 weakens it (half move speed); a further
/// starvationSecondsToDie at 0 kills it. Decisions (eat / sleep / wander) live in a separate driver.
/// </summary>
[DisallowMultipleComponent]
public class Goblin : MonoBehaviour
{
    public const float MaxNeed = 100f;

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Health health; // Optional: hit-point death alongside starvation; null = starvation only

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // Tiles per second (provisional: goblins amble slower than the imp)
    [SerializeField] private Vector2Int spawnTile;

    [Header("Needs")]
    [SerializeField] private float hungerSecondsToEmpty = 240f;  // ~4 min, DESIGN §A.5
    [SerializeField] private float energySecondsToEmpty = 300f;  // ~5 min, DESIGN §A.5
    [SerializeField] private float starvationSecondsToDie = 60f; // Time at Hunger 0 before death
    [SerializeField] private float weakenedMoveSpeedMultiplier = 0.5f; // DESIGN §A.5: half speed at Hunger 0
    [SerializeField] private float startingHunger = 100f;        // Needs on spawn (0–100)
    [SerializeField] private float startingEnergy = 100f;

    [Header("Model (code-built, ticket #76)")]
    [SerializeField] private float modelHeight = 0.8f;                              // Tiles: the stockiest creature
    [SerializeField] private Color skinColor = new Color(0.45f, 0.80f, 0.35f, 1f);  // Goblin green (its sprite-era color)
    [SerializeField] private Color bellyColor = new Color(0.33f, 0.62f, 0.25f, 1f); // Darker green belly
    [SerializeField] private float modelSmoothness = 0.35f;                         // Toy (vinyl) sheen

    [Header("Debug")]
    [SerializeField] private bool logStats = true;            // Console stats while playing (Update only; Tick stays silent)
    [SerializeField] private float logIntervalSeconds = 5f;   // Game seconds between periodic stat lines

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Health Health { get => health; set => health = value; }
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
    public Vector2Int SpawnTile { get => spawnTile; set => spawnTile = value; }
    public float HungerSecondsToEmpty { get => hungerSecondsToEmpty; set => hungerSecondsToEmpty = value; }
    public float EnergySecondsToEmpty { get => energySecondsToEmpty; set => energySecondsToEmpty = value; }
    public float StarvationSecondsToDie { get => starvationSecondsToDie; set => starvationSecondsToDie = value; }
    public float WeakenedMoveSpeedMultiplier { get => weakenedMoveSpeedMultiplier; set => weakenedMoveSpeedMultiplier = value; }
    public float StartingHunger { get => startingHunger; set => startingHunger = value; }
    public float StartingEnergy { get => startingEnergy; set => startingEnergy = value; }
    public bool LogStats { get => logStats; set => logStats = value; }
    public float LogIntervalSeconds { get => logIntervalSeconds; set => logIntervalSeconds = value; }

    /// <summary>0–100; 100 is full. Clamped on every set.</summary>
    public float Hunger { get => hunger; set => hunger = Mathf.Clamp(value, 0f, MaxNeed); }

    /// <summary>0–100; 100 is fully rested. Clamped on every set.</summary>
    public float Energy { get => energy; set => energy = Mathf.Clamp(value, 0f, MaxNeed); }

    /// <summary>Starving: Hunger at 0. Halves move speed here; the combat story halves fight speed.</summary>
    public bool IsWeakened => Hunger <= 0f;

    public float EffectiveMoveSpeed => IsWeakened ? moveSpeed * weakenedMoveSpeedMultiplier : moveSpeed;

    /// <summary>Seconds spent continuously at Hunger 0; resets to 0 whenever Hunger is above 0.</summary>
    public float StarvationElapsed { get; private set; }

    /// <summary>Final for the slice: a dead goblin's GameObject is deactivated and it never acts again.</summary>
    public bool IsDead { get; private set; }

    /// <summary>The board tile whose cell contains the goblin (its ground point).</summary>
    public Vector2Int CurrentTile { get; private set; }

    /// <summary>The waypoint the goblin is currently walking toward; equals CurrentTile when idle.</summary>
    public Vector2Int NextTile { get; private set; }

    public bool IsMoving => path.Count > 0;

    private float hunger = MaxNeed;
    private float energy = MaxNeed;

    // Waypoints still to visit; path[0] is the tile being walked toward (NextTile).
    private readonly List<Vector2Int> path = new List<Vector2Int>();
    private Vector2Int segmentStart;
    private GameObject model;
    private float logTimer;
    private bool wasWeakened;

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        health ??= GetComponent<Health>();
        Hunger = startingHunger; // Clamped by the setters
        Energy = startingEnergy;

        CreateModel();
        Spawn();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
        if (logStats) LogStatsStep(Time.deltaTime);
    }

    /// <summary>One-line snapshot of the goblin's state, used by the debug log.</summary>
    public string StatsLine() =>
        $"[Goblin] {name} tile={CurrentTile} hunger={Hunger:F1} energy={Energy:F1}" +
        $"{(health != null ? $" hp={health.CurrentHealth}/{health.MaxHealth}" : "")}" +
        $" speed={EffectiveMoveSpeed:F1}{(IsMoving ? " moving->" + NextTile : "")}" +
        $"{(IsWeakened ? $" WEAKENED starving={StarvationElapsed:F1}/{starvationSecondsToDie:F0}s" : "")}" +
        $"{(IsDead ? " DEAD" : "")}";

    // Logs state transitions immediately (weakened, recovered, died) and a full stat line every
    // logIntervalSeconds. Runs after Tick in Update, so a death is logged in the frame it happens.
    private void LogStatsStep(float deltaTime)
    {
        if (IsDead)
        {
            Debug.Log(StatsLine() + (KilledByDamage
                ? " — killed: 0 HP"
                : $" — starved after {StarvationElapsed:F1}s at Hunger 0"), this);
            return;
        }

        if (IsWeakened != wasWeakened)
        {
            wasWeakened = IsWeakened;
            Debug.Log(StatsLine() + (IsWeakened ? " — hunger hit 0: weakened" : " — fed: no longer weakened"), this);
        }

        logTimer += deltaTime;
        if (logTimer < logIntervalSeconds) return;
        logTimer = 0f;
        Debug.Log(StatsLine(), this);
    }

    /// <summary>Places the goblin on its spawn tile and clears any orders.</summary>
    public void Spawn()
    {
        PlaceOnTile(spawnTile);
    }

    /// <summary>
    /// Advances movement and the needs clock by deltaTime. Called from Update; public so tests can
    /// step it deterministically. No-op once dead.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (IsDead) return;
        if (KilledByDamage)
        {
            Die(); // Hit points ran out (since the last Tick)
            return;
        }

        Advance(deltaTime);

        Hunger -= MaxNeed / hungerSecondsToEmpty * deltaTime;
        Energy -= MaxNeed / energySecondsToEmpty * deltaTime;

        if (Hunger > 0f)
        {
            StarvationElapsed = 0f;
            return;
        }

        StarvationElapsed += deltaTime;
        if (StarvationElapsed >= starvationSecondsToDie) Die();
    }

    /// <summary>Snaps the goblin to the given tile's center and clears any orders.</summary>
    public void PlaceOnTile(Vector2Int tile)
    {
        path.Clear();
        CurrentTile = tile;
        NextTile = tile;
        segmentStart = tile;
        transform.position = TileCenter(tile);
    }

    /// <summary>
    /// Paths to the given tile and starts walking it, with Imp.SetDestination's semantics: mid-move the
    /// new path starts from the waypoint ahead; with no path the request is ignored (current orders kept).
    /// Returns whether a path was found. Always false once dead.
    /// </summary>
    public bool SetDestination(Vector2Int tile)
    {
        if (dungeonBoard == null || IsDead) return false;

        // Between tile centers, the goblin is committed to the waypoint ahead; otherwise it
        // stands on segmentStart's center (which is CurrentTile).
        bool betweenTiles = IsMoving && transform.position != TileCenter(segmentStart);
        Vector2Int origin = betweenTiles ? NextTile : segmentStart;

        List<Vector2Int> newPath = Pathfinder.FindPath(dungeonBoard, origin, tile);
        if (newPath.Count == 0) return false;

        path.Clear();
        if (betweenTiles)
        {
            // Finish the current segment first: newPath[0] is the waypoint ahead.
            path.AddRange(newPath);
        }
        else
        {
            // newPath[0] is the tile the goblin is already standing on.
            path.AddRange(newPath.GetRange(1, newPath.Count - 1));
            CurrentTile = origin;
        }

        NextTile = path.Count > 0 ? path[0] : CurrentTile;
        return true;
    }

    /// <summary>
    /// Moves the goblin along its path by deltaTime seconds' worth of travel at EffectiveMoveSpeed.
    /// Tick calls it; public so tests can step movement alone. No-op once dead.
    /// </summary>
    public void Advance(float deltaTime)
    {
        if (IsDead) return;

        float remaining = EffectiveMoveSpeed * deltaTime; // In tiles (= world units, 1 unit per tile)

        while (path.Count > 0 && remaining > 0f)
        {
            Vector3 target = TileCenter(path[0]);
            float distance = Vector3.Distance(transform.position, target);

            if (distance <= remaining)
            {
                // Reach this waypoint exactly, then carry leftover travel into the next one.
                transform.position = target;
                remaining -= distance;
                segmentStart = path[0];
                CurrentTile = path[0];
                path.RemoveAt(0);
                NextTile = path.Count > 0 ? path[0] : CurrentTile;
            }
            else
            {
                transform.position = Vector3.MoveTowards(transform.position, target, remaining);
                // Mid-segment, its cell is whichever segment end it's closer to.
                CurrentTile = distance - remaining < 0.5f * SegmentLength() ? path[0] : segmentStart;
                remaining = 0f;
            }
        }
    }

    private bool KilledByDamage => health != null && health.IsDead;

    /// <summary>The one death path, for starvation and for 0 HP alike.</summary>
    private void Die()
    {
        IsDead = true;
        path.Clear();
        NextTile = CurrentTile;
        gameObject.SetActive(false); // Slice placeholder for death: no corpse system
    }

    private float SegmentLength() =>
        Vector3.Distance(TileCenter(segmentStart), TileCenter(NextTile));

    // The tile's ground point; the root (and its model's base) stands there.
    private Vector3 TileCenter(Vector2Int tile) =>
        boardRenderer != null
            ? boardRenderer.GetTileCenterWorldPosition(tile.x, tile.y)
            : BoardRenderer.UnanchoredTileCenter(tile);

    /// <summary>The code-built model (null until CreateModel, which Start calls).</summary>
    public GameObject Model => model;

    /// <summary>Builds the 3D model once, standing on the ground point (its base at the root's y = 0).</summary>
    public void CreateModel()
    {
        if (model != null) return;
        model = CreatureModel.Build(transform, "GoblinModel", CreatureModel.GoblinRecipe(modelHeight, skinColor, bellyColor), modelSmoothness);
    }
}
