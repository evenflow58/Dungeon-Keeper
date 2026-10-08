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
    /// Where food pip i of n sits: a straight row across the mound's front (toward the camera), so all of the
    /// spacing is left-right on screen (depth is foreshortened), resting on the mound's surface at that spot.
    /// frontFraction places the row between the center (0) and the front rim (1).
    /// </summary>
    public static Vector3 PipPosition(int i, int n, float s, float frontFraction, float pipDiameter, float pipSpacing)
    {
        float halfWidth = s * 0.5f;
        float x = (i - (n - 1) * 0.5f) * pipSpacing;
        float z = -frontFraction * halfWidth;                 // −Z is the front, toward the camera
        float rr = (x * x + z * z) / (halfWidth * halfWidth); // Mound is a half-sunk ellipsoid
        float surface = MoundHeight * Mathf.Sqrt(Mathf.Max(0f, 1f - rr));
        return new Vector3(x, surface + pipDiameter * 0.35f, z);
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

    // ---- Construction sites (#91): a materials pile per type, shown until the placeable is built ----

    /// <summary>Cot materials: a loose stack of frame planks, a crate, and a folded bolt of mattress cloth.</summary>
    public static Part[] CotMaterialsRecipe(float s, Color wood, Color crate, Color cloth) => new[]
    {
        new Part("Plank0", Shape.Box, new Vector3(0f, 0.02f, -0.12f * s),         new Vector3(0.9f * s, 0.04f, 0.12f * s), wood, new Vector3(0f, 8f, 0f)),
        new Part("Plank1", Shape.Box, new Vector3(0.02f * s, 0.06f, -0.1f * s),   new Vector3(0.88f * s, 0.04f, 0.12f * s), wood, new Vector3(0f, -6f, 0f)),
        new Part("Plank2", Shape.Box, new Vector3(-0.05f * s, 0.02f, 0.03f * s),  new Vector3(0.85f * s, 0.04f, 0.12f * s), wood, new Vector3(0f, 3f, 0f)),
        new Part("Crate",  Shape.Box, new Vector3(0.24f * s, 0.11f, 0.22f * s),   new Vector3(0.22f, 0.22f, 0.22f), crate, new Vector3(0f, 20f, 0f)),
        new Part("Cloth",  Shape.Box, new Vector3(-0.2f * s, 0.03f, 0.24f * s),   new Vector3(0.32f * s, 0.06f, 0.2f * s), cloth, new Vector3(0f, -10f, 0f)),
    };

    /// <summary>Plot materials: a heap of fresh soil with spawn caps on it, and two sacks beside it.</summary>
    public static Part[] PlotMaterialsRecipe(float s, Color soil, Color sack, Color cap) => new[]
    {
        new Part("Soil",   Shape.Sphere, new Vector3(0f, 0f, 0.06f * s),             new Vector3(0.6f * s, 0.18f, 0.5f * s), soil), // Half sunk
        new Part("Spawn0", Shape.Sphere, new Vector3(0.06f * s, 0.085f, 0.1f * s),   new Vector3(0.09f, 0.05f, 0.09f), cap),
        new Part("Spawn1", Shape.Sphere, new Vector3(-0.08f * s, 0.075f, 0.16f * s), new Vector3(0.07f, 0.04f, 0.07f), cap),
        new Part("Sack0",  Shape.Sphere, new Vector3(-0.24f * s, 0.09f, -0.16f * s), new Vector3(0.22f, 0.18f, 0.2f), sack),
        new Part("Sack1",  Shape.Sphere, new Vector3(0.24f * s, 0.08f, -0.12f * s),  new Vector3(0.2f, 0.16f, 0.18f), sack, new Vector3(0f, 30f, 0f)),
    };

    /// <summary>Trap materials: two plate sections stacked askew, loose spikes lying on the floor, a parts crate.</summary>
    public static Part[] TrapMaterialsRecipe(float s, Color plate, Color spike, Color crate) => new[]
    {
        new Part("PlateA", Shape.Box,  new Vector3(-0.06f * s, 0.012f, 0.04f * s), new Vector3(0.6f * s, 0.024f, 0.6f * s), plate, new Vector3(0f, 12f, 0f)),
        new Part("PlateB", Shape.Box,  new Vector3(0.05f * s, 0.036f, -0.02f * s), new Vector3(0.45f * s, 0.024f, 0.45f * s), plate, new Vector3(0f, -20f, 0f)),
        // Cones lie on their sides (base at the part's origin, pointing along −X / turned).
        new Part("Spike0", Shape.Cone, new Vector3(0.42f * s, 0.05f, 0.28f * s),  new Vector3(0.1f, 0.26f, 0.1f), spike, new Vector3(0f, 0f, 90f)),
        new Part("Spike1", Shape.Cone, new Vector3(0.4f * s, 0.05f, 0.08f * s),   new Vector3(0.1f, 0.26f, 0.1f), spike, new Vector3(0f, 25f, 90f)),
        new Part("Crate",  Shape.Box,  new Vector3(-0.3f * s, 0.08f, -0.28f * s), new Vector3(0.16f, 0.16f, 0.16f), crate, new Vector3(0f, -15f, 0f)),
    };

    // ---- The Heart: a faceted crystal on a low stone plinth ----

    public static Part[] HeartRecipe(float height, float width, Color crystal, Color plinth, float plinthHeight) => new[]
    {
        new Part("Plinth",  Shape.Cylinder,   new Vector3(0f, plinthHeight * 0.5f, 0f), new Vector3(0.85f, plinthHeight * 0.5f, 0.85f), plinth),
        // Turned 45° so a facet edge faces the camera: it reads as a gem, not a box.
        new Part("Crystal", Shape.Octahedron, new Vector3(0f, plinthHeight, 0f), new Vector3(width, height - plinthHeight, width), crystal, new Vector3(0f, 45f, 0f)),
    };
}
