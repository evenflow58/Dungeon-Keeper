using UnityEngine;
using Shape = CreatureModel.Shape;
using Part = CreatureModel.Part;

/// <summary>
/// Recipes for the dungeon's props (ticket #77): placeables and the Heart, built through CreatureModel's kit (its
/// Build / CreatePart / MaterialFor / MeshFor) so every model in the world shares one material and mesh cache.
/// Footprint recipes take s, the placeable's size in tiles; every recipe stands on the root's ground point.
/// </summary>
public static class PropModel
{
    // ---- Lair cot: a bed laid across the tile ----

    /// <summary>Top of the mattress above the ground: what a cot's occupants rest on.</summary>
    public const float CotSurfaceHeight = 0.20f;

    public static Part[] LairCotRecipe(float s, Color frame, Color mattress, Color pillow) => new[]
    {
        new Part("Frame",    Shape.Box, new Vector3(0f, 0.06f, 0f),         new Vector3(s, 0.12f, 0.62f * s), frame),
        new Part("Mattress", Shape.Box, new Vector3(0f, 0.16f, 0f),         new Vector3(0.92f * s, 0.08f, 0.54f * s), mattress),
        new Part("Pillow",   Shape.Box, new Vector3(-0.32f * s, 0.235f, 0f), new Vector3(0.22f * s, 0.07f, 0.42f * s), pillow),
    };

    // ---- Mushroom plot: a soil mound with a cluster of mushrooms at its back (pips sit on its front) ----

    /// <summary>Height of the mound's crown above the ground.</summary>
    public const float MoundHeight = 0.12f;

    public static Part[] MushroomPlotRecipe(float s, Color soil, Color stem, Color cap)
    {
        // Three mushrooms in the back half (+Z), so the food pips on the front arc stay in view of the camera.
        Vector3[] spots = { new Vector3(-0.16f * s, 0f, 0.14f * s), new Vector3(0.14f * s, 0f, 0.18f * s), new Vector3(0f, 0f, 0.02f * s) };
        float[] heights = { 0.16f, 0.12f, 0.2f };
        var parts = new Part[1 + spots.Length * 2];
        parts[0] = new Part("Mound", Shape.Sphere, Vector3.zero, new Vector3(s, MoundHeight * 2f, s), soil); // Half sunk: the crown shows
        for (int i = 0; i < spots.Length; i++)
        {
            float h = heights[i];
            parts[1 + i * 2] = new Part($"Stem{i}", Shape.Cylinder, spots[i] + new Vector3(0f, MoundHeight * 0.6f + h * 0.5f, 0f), new Vector3(0.06f, h * 0.5f, 0.06f), stem);
            parts[2 + i * 2] = new Part($"Cap{i}", Shape.Sphere, spots[i] + new Vector3(0f, MoundHeight * 0.6f + h, 0f), new Vector3(0.2f, 0.1f, 0.2f), cap);
        }
        return parts;
    }

    /// <summary>
    /// Where food pip i of n sits: spread evenly along the mound's front arc (facing the camera), resting on the
    /// mound's surface at that radius.
    /// </summary>
    public static Vector3 PipPosition(int i, int n, float s, float ringFraction, float pipDiameter)
    {
        float t = n <= 1 ? 0.5f : i / (float)(n - 1);
        float angle = Mathf.Lerp(-160f, -20f, t) * Mathf.Deg2Rad; // −Z is the front, toward the camera
        float r = ringFraction * s * 0.5f;
        float halfWidth = s * 0.5f;
        float surface = MoundHeight * Mathf.Sqrt(Mathf.Max(0f, 1f - (r / halfWidth) * (r / halfWidth)));
        return new Vector3(Mathf.Cos(angle) * r, surface + pipDiameter * 0.35f, Mathf.Sin(angle) * r);
    }

    // ---- Spike trap: a flat plate (the spikes are SpikeTrap's, so it can raise and retract them) ----

    public const float TrapPlateHeight = 0.04f;

    public static Part[] SpikeTrapPlateRecipe(float s, Color plate) => new[]
    {
        new Part("Plate", Shape.Box, new Vector3(0f, TrapPlateHeight * 0.5f, 0f), new Vector3(s, TrapPlateHeight, s), plate),
    };

    /// <summary>A grid × grid of cone spikes over the plate, bases on the spike root (which sits on the plate top).</summary>
    public static Part[] SpikeGridRecipe(float s, int grid, float spikeHeight, float spikeWidth, Color spike)
    {
        var parts = new Part[grid * grid];
        float step = s / grid;
        for (int x = 0; x < grid; x++)
            for (int z = 0; z < grid; z++)
                parts[x * grid + z] = new Part($"Spike{x}{z}", Shape.Cone,
                    new Vector3((x - (grid - 1) * 0.5f) * step, 0f, (z - (grid - 1) * 0.5f) * step),
                    new Vector3(spikeWidth, spikeHeight, spikeWidth), spike);
        return parts;
    }

    // ---- The Heart: a faceted crystal on a low stone plinth ----

    public static Part[] HeartRecipe(float height, float width, Color crystal, Color plinth, float plinthHeight) => new[]
    {
        new Part("Plinth",  Shape.Cylinder,   new Vector3(0f, plinthHeight * 0.5f, 0f), new Vector3(0.85f, plinthHeight * 0.5f, 0.85f), plinth),
        // Turned 45° so a facet edge faces the camera: it reads as a gem, not a box.
        new Part("Crystal", Shape.Octahedron, new Vector3(0f, plinthHeight, 0f), new Vector3(width, height - plinthHeight, width), crystal, new Vector3(0f, 45f, 0f)),
    };
}
