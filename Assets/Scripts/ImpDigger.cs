using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The imp's dig work loop: picks the nearest reachable Designated tile, walks to a Floor
/// tile next to it, digs for digSecondsPerTile, converts it to Floor, and repeats.
/// Designated tiles with no reachable stand-point stay Designated and are skipped.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Imp))]
public class ImpDigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private Imp imp;

    [Header("Digging")]
    [SerializeField] private float digSecondsPerTile = 2.5f;
    [SerializeField] private float idleRecheckSeconds = 0.25f; // How often an idle imp rescans for work

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public Imp Imp { get => imp; set => imp = value; }
    public float DigSecondsPerTile { get => digSecondsPerTile; set => digSecondsPerTile = value; }
    public float IdleRecheckSeconds { get => idleRecheckSeconds; set => idleRecheckSeconds = value; }

    /// <summary>The Designated tile being worked, or null when idle.</summary>
    public Vector2Int? CurrentTarget { get; private set; }

    /// <summary>The Floor tile the imp stands on to dig CurrentTarget (meaningful only while it has a target).</summary>
    public Vector2Int StandPoint { get; private set; }

    public bool IsDigging { get; private set; }
    public float DigElapsed { get; private set; }
    public float DigProgress => digSecondsPerTile > 0f ? Mathf.Clamp01(DigElapsed / digSecondsPerTile) : 1f;

    // True after a state transition (start, completed dig, dropped target): select on the next Tick
    // instead of waiting for the idle recheck interval.
    private bool selectPending = true;
    private float idleTimer;

    private void Start()
    {
        imp ??= GetComponent<Imp>();
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances the work loop by deltaTime. Called from Update; public so tests can step
    /// it deterministically alongside Imp.Advance.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (dungeonBoard == null || imp == null) return;

        if (CurrentTarget == null)
            TickIdle(deltaTime);
        else if (IsDigging)
            TickDigging(deltaTime);
        else
            TickTraveling();
    }

    private void TickIdle(float deltaTime)
    {
        if (!selectPending)
        {
            idleTimer += deltaTime;
            if (idleTimer < idleRecheckSeconds) return;
        }
        idleTimer = 0f;
        selectPending = false;

        if (!TrySelectTarget(dungeonBoard, imp.CurrentTile, out Vector2Int target, out Vector2Int standPoint))
            return;

        // Commit to this target; selection doesn't re-run while traveling.
        if (!imp.SetDestination(standPoint))
        {
            selectPending = true; // Board changed between selection and dispatch
            return;
        }
        CurrentTarget = target;
        StandPoint = standPoint;
    }

    private void TickTraveling()
    {
        if (imp.IsMoving) return;

        if (imp.CurrentTile != StandPoint || !IsStillDesignated())
        {
            DropTarget(); // Designation cleared en route, or the imp ended up elsewhere
            return;
        }
        IsDigging = true;
        DigElapsed = 0f;
    }

    private void TickDigging(float deltaTime)
    {
        if (!IsStillDesignated() || !IsAdjacent(imp.CurrentTile, CurrentTarget.Value))
        {
            DropTarget(); // Abandon: no conversion
            return;
        }

        DigElapsed += deltaTime;
        if (DigElapsed < digSecondsPerTile) return;

        Vector2Int dug = CurrentTarget.Value;
        DropTarget();
        dungeonBoard.SetTile(dug.x, dug.y, TileState.Floor);
    }

    private void DropTarget()
    {
        CurrentTarget = null;
        IsDigging = false;
        DigElapsed = 0f;
        selectPending = true;
    }

    private bool IsStillDesignated()
    {
        Vector2Int t = CurrentTarget.Value;
        return dungeonBoard.GetTile(t.x, t.y) == TileState.Designated;
    }

    private static bool IsAdjacent(Vector2Int a, Vector2Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

    /// <summary>
    /// Picks the Designated tile with the shortest path to a stand-point (a Floor tile next to it),
    /// scanning x-ascending then y-ascending; ties keep the earlier tile in that scan order.
    /// Tiles with no reachable stand-point are skipped and left as they are.
    /// standPoint is the last tile of Pathfinder.FindPathToAdjacent's path.
    /// </summary>
    public static bool TrySelectTarget(DungeonBoard board, Vector2Int fromTile, out Vector2Int target, out Vector2Int standPoint)
    {
        target = default;
        standPoint = default;
        int bestCost = int.MaxValue;

        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height; y++)
            {
                if (board.GetTile(x, y) != TileState.Designated) continue;

                List<Vector2Int> path = Pathfinder.FindPathToAdjacent(board, fromTile, new Vector2Int(x, y));
                if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable, or not strictly nearer

                bestCost = path.Count;
                target = new Vector2Int(x, y);
                standPoint = path[path.Count - 1];
            }
        }

        return bestCost != int.MaxValue;
    }
}
