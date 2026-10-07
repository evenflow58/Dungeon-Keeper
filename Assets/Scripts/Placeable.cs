using UnityEngine;

public enum PlaceableType
{
    LairCot,
    MushroomPlot,
    SpikeTrap
}

/// <summary>
/// A placed item occupying one board tile. Placement never changes the tile's TileState:
/// the tile stays Floor and walkable; occupancy lives in PlacementManager's registry.
/// </summary>
[DisallowMultipleComponent]
public class Placeable : MonoBehaviour
{
    public PlaceableType Type { get; private set; }
    public Vector2Int Tile { get; private set; }

    /// <summary>Lair Cot rest-spot claim (used by goblins in Epic #4). Always false for other types.</summary>
    public bool IsClaimed { get; private set; }

    /// <summary>Height of the body's center above the ground point: decorations (pips, spikes) stand on it too.</summary>
    public float StandHeight { get; private set; }

    private SpriteRenderer bodySprite;

    /// <summary>
    /// Sets type and tile and builds the placeholder sprite. Called once by PlacementManager on creation.
    /// </summary>
    public void Initialize(PlaceableType type, Vector2Int tile, Color color, float size)
    {
        Type = type;
        Tile = tile;
        CreateBody(color, size);
    }

    /// <summary>Claims a Lair Cot. Fails if this isn't a cot or it's already claimed.</summary>
    public bool TryClaim()
    {
        if (Type != PlaceableType.LairCot || IsClaimed) return false;
        IsClaimed = true;
        return true;
    }

    /// <summary>Releases a claim. No-op when unclaimed.</summary>
    public void Release()
    {
        IsClaimed = false;
    }

    private void CreateBody(Color color, float size)
    {
        if (bodySprite != null) return;

        var go = new GameObject(Type + "Body");
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(size, size, 1f);
        StandHeight = size * 0.5f;
        go.transform.localPosition = new Vector3(0f, StandHeight, 0f); // Standing on the ground point
        bodySprite = go.AddComponent<SpriteRenderer>();

        const int res = 16;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = Type + "_Texture";
        var pixels = new Color[res * res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = Type + "_Sprite";
        bodySprite.sprite = sprite;
        bodySprite.color = color;
        bodySprite.sortingOrder = 1; // Above tiles (0); below the imp and drag previews (2)
    }
}
