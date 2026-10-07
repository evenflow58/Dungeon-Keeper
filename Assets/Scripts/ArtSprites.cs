using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime loader for art sprites (Art pass, ticket #70). Sprites live under Assets/Resources/Art/... and are
/// loaded by their Resources path (no extension), e.g. "Art/Characters/goblin". A missing asset returns null —
/// never throws — and every consumer falls back to its code-created placeholder, so the game and the EditMode
/// suite run the same with the art absent.
///
/// Conventions for art sprites: Texture Type Sprite (2D and UI), Sprite Mode Single, pivot at the figure's feet
/// (bottom-center, 0.5/0), Filter Mode Point, Compression None, 100 pixels per unit. On-screen size is set in
/// code by a serialized height in tiles (ScaleForHeight), not by pixels per unit.
/// </summary>
public static class ArtSprites
{
    public const string GoblinPath = "Art/Characters/goblin";

    // Only hits are cached: a sprite added later (or restored after a rename) loads on the next request.
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // The project enters Play without a domain reload, so statics survive between sessions: start each
    // session with an empty cache, or a sprite renamed/moved since the last session would keep loading.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() => ClearCache();

    /// <summary>Forgets every cached sprite; the next Load goes back to Resources.</summary>
    public static void ClearCache() => cache.Clear();

    /// <summary>The sprite at a Resources path, or null when there's no sprite there.</summary>
    public static Sprite Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (cache.TryGetValue(path, out Sprite cached) && cached != null) return cached;

        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) cache[path] = sprite;
        return sprite;
    }

    /// <summary>
    /// Uniform scale that makes a sprite spriteHeightUnits tall (in world units at its own pixels per unit)
    /// display heightTiles tall; width follows the sprite's aspect. 1 for a degenerate height.
    /// </summary>
    public static float ScaleForHeight(float spriteHeightUnits, float heightTiles) =>
        spriteHeightUnits > 0f ? heightTiles / spriteHeightUnits : 1f;
}
