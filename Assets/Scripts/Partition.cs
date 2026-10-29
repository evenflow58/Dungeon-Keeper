using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A wall or door placeable's behavior (#165). Once built it puts its partition on the board (DungeonBoard.SetFeature), so
/// pathing, mana and room detection see it; removal takes it off again. A site or an unfunded order is nothing on the
/// board: it doesn't block until it stands.
/// A door is open while a minion is transiting it: an active imp or goblin whose tile, or the tile it is stepping onto, is
/// the door's, plus a short serialized linger after the last one leaves. Only minions open doors; a hero never does. While
/// open the board treats the tile as open to mana (the normal flow rules apply across it), so a unit or two sneaks through
/// before it shuts. The leaf swings on its hinge to match (visual only: the board's open flag is the state).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class Partition : MonoBehaviour
{
    private DungeonBoard board;
    private Placeable placeable;
    private float lingerSeconds;
    private float swingDegrees;
    private float swingDegreesPerSecond;
    private float lingerTimer;
    private Transform[] leafParts;
    private Vector3[] leafOffsets; // Each swinging part's offset from the leaf's centre, in the shut pose

    /// <summary>Wall or Door, from the placeable's type.</summary>
    public TileFeature Kind { get; private set; }

    public Vector2Int Tile => placeable != null ? placeable.Tile : default;

    /// <summary>True once built and on the board (until removed).</summary>
    public bool IsStanding { get; private set; }

    /// <summary>Door only: whether it stands open now (the board's flag). Always false for a wall or a site.</summary>
    public bool IsOpen => IsStanding && Kind == TileFeature.Door && board != null && board.IsInnerDoorOpen(Tile.x, Tile.y);

    /// <summary>The leaf's current opening angle in degrees (0 = shut). Door only.</summary>
    public float LeafAngle { get; private set; }

    /// <summary>True when the door frame is turned 90° (the passage runs along X).</summary>
    public bool TurnedAcross { get; private set; }

    /// <summary>
    /// Whether a minion is in the doorway (a seam for tests). Defaults to the live creatures: any active imp or goblin
    /// whose CurrentTile or NextTile is the tile.
    /// </summary>
    public Func<Vector2Int, bool> OccupiedProbe { get; set; } = MinionInDoorway;

    public float LingerSeconds { get => lingerSeconds; set => lingerSeconds = value; }

    /// <summary>PlacementManager calls this right after Initialize, with its serialized door tuning.</summary>
    public void Initialize(DungeonBoard board, float lingerSeconds, float swingDegrees, float swingDegreesPerSecond)
    {
        this.board = board;
        this.lingerSeconds = Mathf.Max(0f, lingerSeconds);
        this.swingDegrees = swingDegrees;
        this.swingDegreesPerSecond = Mathf.Max(0f, swingDegreesPerSecond);
        placeable = GetComponent<Placeable>();
        Kind = placeable.Type == PlaceableType.Door ? TileFeature.Door : TileFeature.Wall;
    }

    /// <summary>Built: the partition goes on the board (a door shut), and a door turns to face its passage.</summary>
    public void OnConstructed()
    {
        if (board == null || placeable == null) return;
        if (Kind == TileFeature.Door) OrientToPassage();
        board.SetFeature(Tile.x, Tile.y, Kind);
        IsStanding = true;
        lingerTimer = 0f;
    }

    /// <summary>Removal (PlacementManager.TryRemovePartition): off the board at once, before the object goes.</summary>
    public void OnRemoved()
    {
        if (IsStanding && board != null && board.GetFeature(Tile.x, Tile.y) == Kind) board.SetFeature(Tile.x, Tile.y, TileFeature.None);
        IsStanding = false;
    }

    private void OnDestroy() => OnRemoved(); // A destroyed partition never leaves a ghost blocker behind

    private void Update() => Tick(Time.deltaTime);

    /// <summary>
    /// Door only: opens while a minion is in the doorway, stays open lingerSeconds after the last leaves, then shuts; the
    /// leaf swings toward its pose. Game time (pause freezes it). Public so tests can step it.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsStanding || Kind != TileFeature.Door || board == null) return;

        if (OccupiedProbe != null && OccupiedProbe(Tile)) lingerTimer = lingerSeconds + Mathf.Epsilon;
        else lingerTimer = Mathf.Max(0f, lingerTimer - deltaTime);
        board.SetInnerDoorOpen(Tile.x, Tile.y, lingerTimer > 0f);

        float target = IsOpen ? swingDegrees : 0f;
        LeafAngle = swingDegreesPerSecond > 0f ? Mathf.MoveTowards(LeafAngle, target, swingDegreesPerSecond * deltaTime) : target;
        PoseLeaf();
    }

    // The frame spans X by default (a passage along Z). When the tiles north and south are both closed to minions (the
    // door sits in an east-west passage), it turns 90°.
    private void OrientToPassage()
    {
        Vector2Int t = Tile;
        bool northSouthClosed = !board.IsWalkable(t.x, t.y + 1) && !board.IsWalkable(t.x, t.y - 1);
        TurnedAcross = northSouthClosed;
        if (placeable.Model != null) placeable.Model.transform.localRotation = Quaternion.Euler(0f, TurnedAcross ? 90f : 0f, 0f);
    }

    private void PoseLeaf()
    {
        if (placeable == null || placeable.Model == null) return;
        if (leafParts == null)
        {
            Transform leaf = placeable.Model.transform.Find(PropModel.DoorLeafPart);
            if (leaf == null) return;
            var parts = new List<Transform> { leaf };
            foreach (Transform child in placeable.Model.transform)
                if (child.name.StartsWith("Band")) parts.Add(child); // The bands ride on the leaf
            leafParts = parts.ToArray();
            leafOffsets = new Vector3[leafParts.Length];
            for (int i = 0; i < leafParts.Length; i++) leafOffsets[i] = leafParts[i].localPosition - leaf.localPosition;
        }
        PropModel.DoorLeafPose(LeafAngle, out Vector3 position, out Quaternion rotation);
        for (int i = 0; i < leafParts.Length; i++)
        {
            leafParts[i].localPosition = position + rotation * leafOffsets[i];
            leafParts[i].localRotation = rotation;
        }
    }

    // ---- the live doorway probe ----

    private static int probeFrame = -1;
    private static readonly HashSet<Vector2Int> minionTiles = new HashSet<Vector2Int>();

    /// <summary>
    /// Whether an active imp or goblin stands in, or is stepping into, this tile. The tiles are gathered once per frame and
    /// shared by every door.
    /// </summary>
    public static bool MinionInDoorway(Vector2Int tile)
    {
        if (probeFrame != Time.frameCount || !Application.isPlaying)
        {
            probeFrame = Time.frameCount;
            minionTiles.Clear();
            foreach (Imp imp in FindObjectsByType<Imp>())
            {
                minionTiles.Add(imp.CurrentTile);
                minionTiles.Add(imp.NextTile);
            }
            foreach (Goblin goblin in FindObjectsByType<Goblin>())
            {
                if (goblin.IsDead) continue;
                minionTiles.Add(goblin.CurrentTile);
                minionTiles.Add(goblin.NextTile);
            }
        }
        return minionTiles.Contains(tile);
    }
}
