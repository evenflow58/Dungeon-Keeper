using System;
using UnityEngine;

public class DungeonBoard : MonoBehaviour
{
    [SerializeField] private int width = 48;
    [SerializeField] private int height = 32;
    [SerializeField] private int cavernSize = 6;
    [SerializeField] private Vector2Int entranceTile = new Vector2Int(24, 31); // The door, on the top edge above the cavern

    private TileState[,] tiles;

    public int Width => width;
    public int Height => height;
    public int CavernSize => cavernSize;

    /// <summary>
    /// Where the dungeon's door is. Must agree with HeroSpawner's entranceTile. Takes effect on the next
    /// InitializeBoard (the board is generated at runtime).
    /// </summary>
    public Vector2Int EntranceTile { get => entranceTile; set => entranceTile = value; }

    /// <summary>True for the door tile (TileState.Entrance).</summary>
    public bool IsEntrance(Vector2Int tile) => GetTile(tile.x, tile.y) == TileState.Entrance;

    public event Action<int, int, TileState> OnTileChanged;

    private void Awake()
    {
        tiles ??= InitializeBoard();
    }

    public TileState[,] InitializeBoard()
    {
        tiles = new TileState[width, height];

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

        // 3. The door. It sits on the board edge, outside the cavern, so the carve order doesn't interact.
        if (IsInBounds(entranceTile.x, entranceTile.y))
        {
            tiles[entranceTile.x, entranceTile.y] = TileState.Entrance;
        }

        return tiles;
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    /// <summary>
    /// Terrain a unit can stand on and path through: in-bounds Floor. The Entrance isn't walkable: only a hero
    /// stands on it (spawned there; Pathfinder's start-tile exception lets him step off it).
    /// </summary>
    public bool IsWalkable(int x, int y) => IsInBounds(x, y) && GetTile(x, y) == TileState.Floor;

    /// <summary>Undesignated diggable terrain: in-bounds Rock. (Dig work items are Designated tiles; the Entrance isn't diggable.)</summary>
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
