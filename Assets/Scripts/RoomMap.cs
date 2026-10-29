using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A room's type (#165), from its contents. Identity only for now: rooms have no mechanical effect yet.</summary>
public enum RoomType
{
    Room,       // Generic: none of the typed contents
    Farm,
    CommonRoom,
    Bedroom,
    DiningHall,
    Infirmary
}

/// <summary>One typing rule (#165): a room holding any of these built placeables is this type.</summary>
public sealed class RoomRule
{
    public readonly RoomType type;
    public readonly PlaceableType[] contents;

    public RoomRule(RoomType type, params PlaceableType[] contents)
    {
        this.type = type;
        this.contents = contents ?? new PlaceableType[0];
    }
}

/// <summary>
/// How a room is typed (#165): the first rule in Priority whose contents the room holds wins; a room matching none is a
/// generic Room. The priority is decided (Infirmary > Dining hall > Bedroom > Common room > Farm). Healing pallets, dining
/// tables and recreation props come in later stories: their rules are already in place, empty, so each new type joins its
/// rule's contents and the order never changes.
/// </summary>
public static class RoomTyping
{
    public static readonly RoomRule[] Priority =
    {
        new RoomRule(RoomType.Infirmary),                          // Healing pallets: a later story
        new RoomRule(RoomType.DiningHall),                         // Dining tables: a later story
        new RoomRule(RoomType.Bedroom, PlaceableType.LairCot),
        new RoomRule(RoomType.CommonRoom),                         // Recreation props: a later story
        new RoomRule(RoomType.Farm, PlaceableType.MushroomPlot),
    };

    /// <summary>The type of a room holding these built placeables: the first matching rule's, else Room.</summary>
    public static RoomType TypeOf(ICollection<PlaceableType> contents)
    {
        if (contents == null || contents.Count == 0) return RoomType.Room;
        foreach (RoomRule rule in Priority)
            foreach (PlaceableType type in rule.contents)
                if (contents.Contains(type)) return rule.type;
        return RoomType.Room;
    }

    /// <summary>How a room type reads in the tooltip: "Bedroom", "Dining hall", "Room".</summary>
    public static string DisplayName(RoomType type)
    {
        switch (type)
        {
            case RoomType.Farm: return "Farm";
            case RoomType.CommonRoom: return "Common room";
            case RoomType.Bedroom: return "Bedroom";
            case RoomType.DiningHall: return "Dining hall";
            case RoomType.Infirmary: return "Infirmary";
            default: return "Room";
        }
    }
}

/// <summary>A detected room (#165): its tiles in board scan order and its type. No persistent identity in v1.</summary>
public sealed class Room
{
    public readonly int Index;               // Its place in this detection's list (not stable across changes)
    public readonly List<Vector2Int> Tiles;
    public readonly RoomType Type;

    public Room(int index, List<Vector2Int> tiles, RoomType type)
    {
        Index = index;
        Tiles = tiles;
        Type = type;
    }

    public string DisplayName => RoomTyping.DisplayName(Type);
}

/// <summary>
/// Room detection over plain board data (#165), apart from any view so tests drive it. A room is a maximal 4-connected
/// region of bare Floor: connectivity runs through Floor tiles that hold no wall and no door, never through Rock,
/// Designated, a wall, a door or the Entrance. So doors delimit rooms (two spaces joined only by a door are two rooms), and a
/// door tile, a wall tile and the Entrance belong to no room. A region 4-adjacent to the Entrance is open to the outside:
/// unenclosed, so it is no room of any type. No minimum size: a one-tile closet is a room.
/// </summary>
public static class RoomDetector
{
    private static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

    /// <summary>Whether a tile can be inside a room: in-bounds Floor with no wall or door on it.</summary>
    public static bool IsRoomFloor(DungeonBoard board, Vector2Int t) =>
        board.IsInBounds(t.x, t.y) && board.GetTile(t.x, t.y) == TileState.Floor && board.GetFeature(t.x, t.y) == TileFeature.None;

    /// <summary>
    /// Every room on the board, in scan order of each room's first tile. contentsAt names the built placeable on a tile
    /// (null for none); a null function types every room as a generic Room.
    /// </summary>
    public static List<Room> Detect(DungeonBoard board, Func<Vector2Int, PlaceableType?> contentsAt)
    {
        var rooms = new List<Room>();
        if (board == null) return rooms;
        var seen = new bool[board.Width, board.Height];
        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height; y++)
            {
                var start = new Vector2Int(x, y);
                if (seen[x, y] || !IsRoomFloor(board, start)) continue;

                var region = new List<Vector2Int>();
                bool open = false; // Touches the Entrance: the outside leaks in
                seen[x, y] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Vector2Int t = queue.Dequeue();
                    region.Add(t);
                    foreach (Vector2Int d in Dirs)
                    {
                        Vector2Int n = t + d;
                        if (!board.IsInBounds(n.x, n.y)) continue;
                        if (board.GetTile(n.x, n.y) == TileState.Entrance) open = true;
                        if (seen[n.x, n.y] || !IsRoomFloor(board, n)) continue;
                        seen[n.x, n.y] = true;
                        queue.Enqueue(n);
                    }
                }
                if (open) continue;

                region.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                var contents = new HashSet<PlaceableType>();
                if (contentsAt != null)
                    foreach (Vector2Int t in region)
                        if (contentsAt(t) is PlaceableType type) contents.Add(type);
                rooms.Add(new Room(rooms.Count, region, RoomTyping.TypeOf(contents)));
            }
        }
        return rooms;
    }
}

/// <summary>
/// The live room map (#165), a service on the DungeonManager: RoomDetector over the board and the built placeables. It is
/// invalidated by every tile change, partition change and placement change (dig, wall, door, removal, a cot finished) and
/// recomputed on the next query, so it is always current. No mechanical effect: the tooltip and the Room view read it.
/// </summary>
[DisallowMultipleComponent]
public class RoomMap : MonoBehaviour
{
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private PlacementManager placementManager; // Optional: unwired, every room is a generic Room

    public DungeonBoard Board
    {
        get => dungeonBoard;
        set
        {
            dungeonBoard = value;
            Subscribe();
            Invalidate();
        }
    }

    public PlacementManager PlacementManager
    {
        get => placementManager;
        set
        {
            placementManager = value;
            Subscribe();
            Invalidate();
        }
    }

    /// <summary>Raised when the rooms may have changed (before the recompute, which happens on the next query).</summary>
    public event Action Invalidated;

    /// <summary>Bumped on every recompute.</summary>
    public int Version { get; private set; }

    private List<Room> rooms;
    private int[,] roomIndex; // -1 = no room
    // #137: what our handlers are attached to in this domain (a reload drops C# event subscriptions).
    [NonSerialized] private DungeonBoard subscribedBoard;
    [NonSerialized] private PlacementManager subscribedManager;

    private void OnEnable() => Subscribe();

    private void OnDisable() => Unsubscribe();

    private void Start()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>() ?? FindAnyObjectByType<DungeonBoard>();
        placementManager ??= GetComponent<PlacementManager>() ?? FindAnyObjectByType<PlacementManager>();
        Subscribe();
        Invalidate();
    }

    private void Subscribe()
    {
        if (subscribedBoard != dungeonBoard)
        {
            if (subscribedBoard != null)
            {
                subscribedBoard.OnTileChanged -= HandleTileChanged;
                subscribedBoard.OnFeatureChanged -= HandleFeatureChanged;
            }
            subscribedBoard = dungeonBoard;
            if (dungeonBoard != null)
            {
                dungeonBoard.OnTileChanged += HandleTileChanged;
                dungeonBoard.OnFeatureChanged += HandleFeatureChanged;
            }
        }
        if (subscribedManager != placementManager)
        {
            if (subscribedManager != null) subscribedManager.PlaceablesChanged -= Invalidate;
            subscribedManager = placementManager;
            if (placementManager != null) placementManager.PlaceablesChanged += Invalidate;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedBoard != null)
        {
            subscribedBoard.OnTileChanged -= HandleTileChanged;
            subscribedBoard.OnFeatureChanged -= HandleFeatureChanged;
        }
        if (subscribedManager != null) subscribedManager.PlaceablesChanged -= Invalidate;
        subscribedBoard = null;
        subscribedManager = null;
    }

    private void HandleTileChanged(int x, int y, TileState state) => Invalidate();

    private void HandleFeatureChanged(int x, int y) => Invalidate();

    /// <summary>Marks the rooms stale; the next query recomputes them.</summary>
    public void Invalidate()
    {
        rooms = null;
        Invalidated?.Invoke();
    }

    /// <summary>Every room now.</summary>
    public IReadOnlyList<Room> Rooms
    {
        get
        {
            EnsureFresh();
            return rooms;
        }
    }

    /// <summary>The room a tile belongs to, or null (not bare Floor, unenclosed, or out of bounds).</summary>
    public Room RoomAt(Vector2Int tile)
    {
        EnsureFresh();
        if (roomIndex == null || dungeonBoard == null || !dungeonBoard.IsInBounds(tile.x, tile.y)) return null;
        int i = roomIndex[tile.x, tile.y];
        return i >= 0 ? rooms[i] : null;
    }

    private void EnsureFresh()
    {
        if (rooms != null) return;
        Subscribe(); // Tests wire and query without OnEnable having seen the references
        rooms = RoomDetector.Detect(dungeonBoard, ContentsAt);
        if (dungeonBoard != null)
        {
            roomIndex = new int[dungeonBoard.Width, dungeonBoard.Height];
            for (int x = 0; x < dungeonBoard.Width; x++)
                for (int y = 0; y < dungeonBoard.Height; y++)
                    roomIndex[x, y] = -1;
            foreach (Room room in rooms)
                foreach (Vector2Int t in room.Tiles)
                    roomIndex[t.x, t.y] = room.Index;
        }
        else roomIndex = null;
        Version++;
    }

    // Built placeables only: a site or an order isn't furniture yet.
    private PlaceableType? ContentsAt(Vector2Int tile)
    {
        if (placementManager == null) return null;
        Placeable p = placementManager.GetAt(tile);
        return p != null && p.IsBuilt ? p.Type : (PlaceableType?)null;
    }
}
