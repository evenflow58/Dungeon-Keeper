using System;
using UnityEngine;

public class DungeonBoard : MonoBehaviour
{
    [SerializeField] private int width = 48;
    [SerializeField] private int height = 32;
    [SerializeField] private int cavernSize = 6;

    private TileState[,] tiles;

    public int Width => width;
    public int Height => height;
    public int CavernSize => cavernSize;

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

        return tiles;
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

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
