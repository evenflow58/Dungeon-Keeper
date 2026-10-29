using System;
using UnityEngine;

/// <summary>
/// #165: what stands on a Floor tile as a partition. Walls and doors are placeables (PlacementManager owns them); the board
/// holds only the built ones' footprint, so pathing, mana and room detection read one place. Sites and orders don't count.
/// </summary>
public enum TileFeature
{
    None,
    Wall, // Blocks everyone, and mana
    Door  // Minions pass, heroes never do; blocks mana while shut
}

/// <summary>#165: who is walking, for the pathing rules: walls block everyone, doors block heroes only.</summary>
public enum Mover
{
    Minion, // Imps and goblins (and the default for every caller that isn't a hero)
    Hero
}

public class DungeonBoard : MonoBehaviour
{
    [SerializeField] private int width = 48;
    [SerializeField] private int height = 32;
    [SerializeField] private int cavernSize = 6;
    [SerializeField] private Vector2Int entranceTile = new Vector2Int(24, 31); // The door, on the top edge above the cavern
    [SerializeField] private bool doorStartsUndug = true; // #163: the door is Rock until dug out; digging it makes the Entrance

    private TileState[,] tiles;
    private TileFeature[,] features; // #165: built walls and doors; all None after InitializeBoard
    private bool[,] innerDoorOpen;   // #165: a built door's open state (its Partition drives it while a minion passes)

    public int Width => width;
    public int Height => height;
    public int CavernSize => cavernSize;

    /// <summary>
    /// Where the dungeon's door is. Must agree with HeroSpawner's entranceTile. Takes effect on the next
    /// InitializeBoard (the board is generated at runtime).
    /// </summary>
    public Vector2Int EntranceTile { get => entranceTile; set => entranceTile = value; }

    /// <summary>
    /// #163: whether InitializeBoard leaves the door as undug Rock (the game) or opens it as the Entrance at once (staging
    /// and tests that need an open door). Takes effect on the next InitializeBoard.
    /// </summary>
    public bool DoorStartsUndug { get => doorStartsUndug; set => doorStartsUndug = value; }

    /// <summary>True for the door tile (TileState.Entrance).</summary>
    public bool IsEntrance(Vector2Int tile) => GetTile(tile.x, tile.y) == TileState.Entrance;

    /// <summary>
    /// #163: true once the door has been dug out, i.e. the door tile is the Entrance. Until then there is no way in or out:
    /// no hero arrives and no imp leaves on an expedition.
    /// </summary>
    public bool IsDoorOpen => IsEntrance(entranceTile);

    /// <summary>
    /// #163: what a dug-out tile becomes: the door tile becomes the Entrance, every other tile Floor. The dig pipeline
    /// asks this at conversion.
    /// </summary>
    public TileState DugStateOf(Vector2Int tile) => tile == entranceTile ? TileState.Entrance : TileState.Floor;

    /// <summary>
    /// #163: opens the door at once (the door tile becomes the Entrance, raising OnTileChanged as a dig would). Staging and
    /// tests; the game opens it by digging.
    /// </summary>
    public void OpenDoor() => SetTile(entranceTile.x, entranceTile.y, TileState.Entrance);

    public event Action<int, int, TileState> OnTileChanged;

    /// <summary>#165: raised when a tile's partition changes (a wall or door is built or removed), with its coordinates.</summary>
    public event Action<int, int> OnFeatureChanged;

    private void Awake()
    {
        tiles ??= InitializeBoard();
    }

    public TileState[,] InitializeBoard()
    {
        tiles = new TileState[width, height];
        features = new TileFeature[width, height];
        innerDoorOpen = new bool[width, height];

        // 1. Initialize all tiles to Rock
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                tiles[x, y] = TileState.Rock;
            }
        }

        // 2. Carve a 6x6 Floor cavern centered on the board
        int startX = (width - cavernSize) / 2;   // (48 - 6) / 2 = 21
        int startY = (height - cavernSize) / 2; // (32 - 6) / 2 = 13

        for (int x = startX; x < startX + cavernSize; x++)
        {
            for (int y = startY; y < startY + cavernSize; y++)
            {
                if (IsInBounds(x, y))
                {
                    tiles[x, y] = TileState.Floor;
                }
            }
        }

        // 3. The door. It sits on the board edge, outside the cavern, so the carve order doesn't interact. #163: it starts
        //    undug (Rock, like its neighbours) and becomes the Entrance when an imp digs it out (DugStateOf).
        if (IsInBounds(entranceTile.x, entranceTile.y))
        {
            tiles[entranceTile.x, entranceTile.y] = doorStartsUndug ? TileState.Rock : TileState.Entrance;
        }

        return tiles;
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    /// <summary>
    /// Terrain a minion can stand on and path through: in-bounds Floor without a wall (#165; a door is passable). The
    /// Entrance isn't walkable: only a hero stands on it (spawned there; Pathfinder's start-tile exception lets him step
    /// off it). Every caller that isn't a hero uses this rule.
    /// </summary>
    public bool IsWalkable(int x, int y) => IsWalkable(x, y, Mover.Minion);

    /// <summary>#165: walkability by mover: in-bounds Floor, never a wall; a door only for minions (heroes never pass one).</summary>
    public bool IsWalkable(int x, int y, Mover mover)
    {
        if (!IsInBounds(x, y) || GetTile(x, y) != TileState.Floor) return false;
        TileFeature feature = GetFeature(x, y);
        return feature == TileFeature.None || (feature == TileFeature.Door && mover == Mover.Minion);
    }

    // ---- partitions (#165) ----

    /// <summary>The built wall or door on a tile (None out of bounds, or with none).</summary>
    public TileFeature GetFeature(int x, int y)
    {
        tiles ??= InitializeBoard();
        return IsInBounds(x, y) ? features[x, y] : TileFeature.None;
    }

    /// <summary>
    /// Sets a tile's partition (a wall or door calls this when it's built and when it's removed). A new door starts shut.
    /// Raises OnFeatureChanged when it changes.
    /// </summary>
    public void SetFeature(int x, int y, TileFeature feature)
    {
        tiles ??= InitializeBoard();
        if (!IsInBounds(x, y) || features[x, y] == feature) return;
        features[x, y] = feature;
        innerDoorOpen[x, y] = false;
        OnFeatureChanged?.Invoke(x, y);
    }

    /// <summary>Whether a built door on this tile stands open (a minion passing through). False for any other tile.</summary>
    public bool IsInnerDoorOpen(int x, int y) => GetFeature(x, y) == TileFeature.Door && innerDoorOpen[x, y];

    /// <summary>Opens or shuts the door on this tile (the door's own driver, and a seam for tests). No-op without a door.</summary>
    public void SetInnerDoorOpen(int x, int y, bool open)
    {
        if (GetFeature(x, y) != TileFeature.Door) return;
        innerDoorOpen[x, y] = open;
    }

    /// <summary>#165: whether the tile's partition closes it to mana: a wall always, a door while shut.</summary>
    public bool BlocksMana(int x, int y)
    {
        TileFeature feature = GetFeature(x, y);
        return feature == TileFeature.Wall || (feature == TileFeature.Door && !innerDoorOpen[x, y]);
    }

    /// <summary>
    /// Undesignated diggable terrain: in-bounds Rock. (Dig work items are Designated tiles; the Entrance isn't diggable,
    /// but the undug door is Rock like any other, #163.)
    /// </summary>
    public bool IsDiggable(int x, int y) => IsInBounds(x, y) && GetTile(x, y) == TileState.Rock;

    public TileState GetTile(int x, int y)
    {
        tiles ??= InitializeBoard();

        if (!IsInBounds(x, y))
        {
            return TileState.Rock;
        }

        return tiles[x, y];
    }

    public void SetTile(int x, int y, TileState state)
    {
        tiles ??= InitializeBoard();

        if (!IsInBounds(x, y))
        {
            return;
        }

        if (tiles[x, y] != state)
        {
            tiles[x, y] = state;
            OnTileChanged?.Invoke(x, y, state);
        }
    }
}
