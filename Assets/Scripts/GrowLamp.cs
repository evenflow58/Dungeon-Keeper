using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Grow-Lamp (#143): a consumer whose effect reaches the tiles around it. While its PowerNode is powered (which
/// already means enabled, connected and covered), Mushroom Plots within a Chebyshev radius grow faster. Lamps don't stack:
/// a plot takes the best single multiplier of the lit lamps covering it, and with none it grows at ×1. The effect is
/// live — MushroomPlot asks PlacementManager each growth tick, so power flickers and toggles change growth at once.
/// The coverage rule is a static helper over plain data so tests drive it without a scene.
/// Its glass glows while it's lit and goes dull when it isn't, so a dark lamp reads at a glance.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class GrowLamp : MonoBehaviour
{
    [Header("Glow (feel)")]
    [SerializeField] private Color litColor = new Color(1.00f, 0.86f, 0.45f, 1f);   // Warm light
    [SerializeField] private Color unlitColor = new Color(0.36f, 0.34f, 0.30f, 1f); // Dull glass
    [SerializeField] private float glowIntensity = 1.6f;
    [SerializeField] private float glassSmoothness = 0.6f;

    private bool? shownLit;

    /// <summary>One lamp as the coverage rule sees it: where it stands and whether it's lit (powered and enabled).</summary>
    public readonly struct LampState
    {
        public readonly Vector2Int Tile;
        public readonly bool Lit;

        public LampState(Vector2Int tile, bool lit)
        {
            Tile = tile;
            Lit = lit;
        }
    }

    public Vector2Int Tile => GetComponent<Placeable>().Tile;
    public bool IsBuilt => GetComponent<Placeable>().IsBuilt;

    /// <summary>Lit: built, and its node powered (PowerNode.IsPowered is false for a disabled node). False with no node.</summary>
    public bool IsLit => IsBuilt && TryGetComponent(out PowerNode node) && node.IsPowered;

    public LampState State => new LampState(Tile, IsLit);

    private void Update()
    {
        RefreshGlow();
    }

    /// <summary>Shows the glass lit or dull from IsLit; swaps the material only when that changes.</summary>
    public void RefreshGlow()
    {
        bool lit = IsLit;
        if (shownLit == lit) return;
        Transform glass = CreatureModel.FindPart(GetComponent<Placeable>().Model, "Glass");
        if (glass == null || !glass.TryGetComponent(out MeshRenderer glassRenderer)) return;
        glassRenderer.sharedMaterial = lit
            ? CreatureModel.EmissiveMaterialFor(litColor, glowIntensity, glassSmoothness)
            : CreatureModel.MaterialFor(unlitColor, glassSmoothness);
        shownLit = lit;
    }

    /// <summary>Chebyshev distance: diagonal steps count as one, so a radius covers a square.</summary>
    public static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    /// <summary>Whether a tile is within radius of a lamp's tile (the lamp's own tile included).</summary>
    public static bool Covers(Vector2Int lampTile, Vector2Int tile, int radius) => Chebyshev(lampTile, tile) <= radius;

    /// <summary>
    /// The growth multiplier at a tile: the best single multiplier among lit lamps within radius (no stacking), else 1.
    /// A multiplier under 1 is never applied (a lamp never slows growth).
    /// </summary>
    public static float MultiplierAt(IEnumerable<LampState> lamps, Vector2Int tile, int radius, float multiplier)
    {
        float best = 1f;
        if (lamps == null) return best;
        foreach (LampState lamp in lamps)
            if (lamp.Lit && Covers(lamp.Tile, tile, radius)) best = Mathf.Max(best, multiplier);
        return best;
    }

    /// <summary>How many of the tiles are within radius of the lamp (the card's "Covers n plots").</summary>
    public static int CountCovered(Vector2Int lampTile, IEnumerable<Vector2Int> tiles, int radius)
    {
        int n = 0;
        if (tiles == null) return n;
        foreach (Vector2Int tile in tiles)
            if (Covers(lampTile, tile, radius)) n++;
        return n;
    }
}
