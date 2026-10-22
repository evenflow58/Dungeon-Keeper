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

    // ---- Imp wheel (#142): a spoked wheel on a timber stand, its face toward the camera; the operating imp stands beside it ----

    /// <summary>Height of the wheel's hub above the ground, for a placeable of size s.</summary>
    public static float WheelHubHeight(float s) => WheelRadius(s) + 0.07f;

    public static float WheelRadius(float s) => 0.34f * s;

    public static Part[] ImpWheelRecipe(float s, Color frame, Color wheel, Color iron)
    {
        const int rimSegments = 8;
        const int spokes = 4;
        float r = WheelRadius(s);
        float hub = WheelHubHeight(s);
        float segment = 2f * r * Mathf.Sin(Mathf.PI / rimSegments) * 1.12f; // A little overlap so the rim reads closed
        var parts = new Part[4 + rimSegments + spokes];
        parts[0] = new Part("Base", Shape.Box, new Vector3(0f, 0.03f, 0.06f * s), new Vector3(0.9f * s, 0.06f, 0.45f * s), frame);
        parts[1] = new Part("Post", Shape.Box, new Vector3(0f, (hub + 0.06f) * 0.5f, 0.16f * s), new Vector3(0.09f, hub + 0.06f, 0.07f), frame);
        // Cylinders stand on Y: turned 90° about X, their axis runs front-to-back through the wheel.
        parts[2] = new Part("Axle", Shape.Cylinder, new Vector3(0f, hub, 0.08f * s), new Vector3(0.05f, 0.1f * s, 0.05f), iron, new Vector3(90f, 0f, 0f));
        parts[3] = new Part("Hub", Shape.Cylinder, new Vector3(0f, hub, -0.01f), new Vector3(0.11f, 0.03f, 0.11f), iron, new Vector3(90f, 0f, 0f));
        for (int i = 0; i < rimSegments; i++)
        {
            float angle = i * 360f / rimSegments;
            float rad = angle * Mathf.Deg2Rad;
            parts[4 + i] = new Part($"Rim{i}", Shape.Box, new Vector3(r * Mathf.Cos(rad), hub + r * Mathf.Sin(rad), 0f),
                new Vector3(segment, 0.05f, 0.06f), wheel, new Vector3(0f, 0f, angle + 90f));
        }
        for (int i = 0; i < spokes; i++)
            parts[4 + rimSegments + i] = new Part($"Spoke{i}", Shape.Box, new Vector3(0f, hub, 0f),
                new Vector3(2f * r, 0.035f, 0.04f), wheel, new Vector3(0f, 0f, i * 180f / spokes));
        return parts;
    }

    // ---- Furnace generator (#143): a stout stone body on an iron plinth, a glowing firebox door, a chimney ----

    public static Part[] FurnaceRecipe(float s, Color body, Color iron, Color fire)
    {
        float front = -0.28f * s; // The body's camera-facing (−Z) face
        return new[]
        {
            new Part("Plinth",  Shape.Box,      new Vector3(0f, 0.03f, 0f),                 new Vector3(0.8f * s, 0.06f, 0.7f * s), iron),
            new Part("Body",    Shape.Box,      new Vector3(0f, 0.27f, 0f),                 new Vector3(0.66f * s, 0.42f, 0.56f * s), body),
            new Part("Band",    Shape.Box,      new Vector3(0f, 0.40f, 0f),                 new Vector3(0.68f * s, 0.04f, 0.58f * s), iron),
            new Part("Door",    Shape.Box,      new Vector3(0f, 0.2f, front - 0.006f),      new Vector3(0.3f * s, 0.2f, 0.02f), iron),
            new Part("Fire",    Shape.Box,      new Vector3(0f, 0.17f, front - 0.018f),     new Vector3(0.24f * s, 0.08f, 0.01f), fire),
            // A cylinder is 2 units tall: scale.y is half its height. This one stands from the body's top to ~0.98.
            new Part("Chimney", Shape.Cylinder, new Vector3(0.18f * s, 0.73f, 0.14f * s),   new Vector3(0.12f, 0.25f, 0.12f), iron),
            new Part("Cap",     Shape.Cylinder, new Vector3(0.18f * s, 0.99f, 0.14f * s),   new Vector3(0.17f, 0.02f, 0.17f), iron),
        };
    }

    // ---- Grow-lamp (#143): an iron post on a round foot, a glass lamp head under a flat cap ----

    public static Part[] GrowLampRecipe(float s, Color post, Color glass) => new[]
    {
        new Part("Foot",  Shape.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.3f * s, 0.02f, 0.3f * s), post),
        new Part("Post",  Shape.Cylinder, new Vector3(0f, 0.34f, 0f), new Vector3(0.05f, 0.3f, 0.05f), post),
        new Part("Glass", Shape.Sphere,   new Vector3(0f, 0.74f, 0f), new Vector3(0.26f, 0.26f, 0.26f), glass),
        new Part("Cap",   Shape.Cylinder, new Vector3(0f, 0.875f, 0f), new Vector3(0.14f, 0.015f, 0.14f), post),
    };

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

    /// <summary>Wheel materials (#142): stand timbers, a loose rim section, and a parts crate.</summary>
    public static Part[] WheelMaterialsRecipe(float s, Color wood, Color crate, Color wheel) => new[]
    {
        new Part("Beam0", Shape.Box, new Vector3(0f, 0.025f, -0.1f * s),          new Vector3(0.85f * s, 0.05f, 0.1f * s), wood, new Vector3(0f, 6f, 0f)),
        new Part("Beam1", Shape.Box, new Vector3(0.03f * s, 0.075f, -0.08f * s),   new Vector3(0.8f * s, 0.05f, 0.1f * s), wood, new Vector3(0f, -8f, 0f)),
        new Part("Rim",   Shape.Box, new Vector3(-0.15f * s, 0.02f, 0.2f * s),     new Vector3(0.45f * s, 0.04f, 0.08f), wheel, new Vector3(0f, 25f, 0f)),
        new Part("Crate", Shape.Box, new Vector3(0.26f * s, 0.1f, 0.2f * s),       new Vector3(0.2f, 0.2f, 0.2f), crate, new Vector3(0f, -12f, 0f)),
    };

    /// <summary>Furnace materials (#143): two cut stone blocks, a chimney section lying on its side, a parts crate.</summary>
    public static Part[] FurnaceMaterialsRecipe(float s, Color stone, Color iron, Color crate) => new[]
    {
        new Part("Block0",  Shape.Box,      new Vector3(-0.14f * s, 0.07f, -0.08f * s), new Vector3(0.3f * s, 0.14f, 0.26f * s), stone, new Vector3(0f, 10f, 0f)),
        new Part("Block1",  Shape.Box,      new Vector3(-0.1f * s, 0.2f, -0.06f * s),   new Vector3(0.26f * s, 0.12f, 0.22f * s), stone, new Vector3(0f, -14f, 0f)),
        new Part("Chimney", Shape.Cylinder, new Vector3(0.12f * s, 0.06f, 0.24f * s),   new Vector3(0.11f, 0.2f, 0.11f), iron, new Vector3(0f, 20f, 90f)),
        new Part("Crate",   Shape.Box,      new Vector3(0.28f * s, 0.1f, -0.2f * s),    new Vector3(0.2f, 0.2f, 0.2f), crate, new Vector3(0f, -8f, 0f)),
    };

    /// <summary>Lamp materials (#143): the post lying on the floor, the glass head beside it, a small crate.</summary>
    public static Part[] LampMaterialsRecipe(float s, Color post, Color glass, Color crate) => new[]
    {
        new Part("Post",  Shape.Cylinder, new Vector3(-0.05f * s, 0.04f, -0.1f * s), new Vector3(0.05f, 0.3f, 0.05f), post, new Vector3(0f, 15f, 90f)),
        new Part("Glass", Shape.Sphere,   new Vector3(0.22f * s, 0.08f, 0.16f * s),  new Vector3(0.16f, 0.16f, 0.16f), glass),
        new Part("Crate", Shape.Box,      new Vector3(-0.24f * s, 0.08f, 0.2f * s),  new Vector3(0.16f, 0.16f, 0.16f), crate, new Vector3(0f, 18f, 0f)),
    };

    /// <summary>An order's ground outline (#104): a thin square frame around the footprint, just above the floor.</summary>
    public static Part[] OrderOutlineRecipe(float s, float thickness, Color color)
    {
        float h = s * 0.5f, y = 0.012f;
        Vector3 along = new Vector3(s + thickness, 0.02f, thickness), across = new Vector3(thickness, 0.02f, s + thickness);
        return new[]
        {
            new Part("Front", Shape.Box, new Vector3(0f, y, -h), along, color),
            new Part("Back",  Shape.Box, new Vector3(0f, y, h),  along, color),
            new Part("Left",  Shape.Box, new Vector3(-h, y, 0f), across, color),
            new Part("Right", Shape.Box, new Vector3(h, y, 0f),  across, color),
        };
    }

    // ---- Accumulator (#145): a stone basin ringed by four prongs, a mana crystal hovering in its bowl ----

    public static Part[] AccumulatorRecipe(float s, Color stone, Color crystal)
    {
        float p = 0.27f * s; // Prong offset from the centre
        return new[]
        {
            // A cylinder is 2 units tall: scale.y is half its height.
            new Part("Base",    Shape.Cylinder,   new Vector3(0f, 0.04f, 0f),  new Vector3(0.78f * s, 0.04f, 0.78f * s), stone),
            new Part("Bowl",    Shape.Cylinder,   new Vector3(0f, 0.11f, 0f),  new Vector3(0.58f * s, 0.03f, 0.58f * s), Color.Lerp(stone, Color.black, 0.35f)),
            new Part("Prong0",  Shape.Box,        new Vector3(p, 0.2f, p),     new Vector3(0.07f, 0.32f, 0.07f), stone, new Vector3(0f, 45f, 0f)),
            new Part("Prong1",  Shape.Box,        new Vector3(-p, 0.2f, p),    new Vector3(0.07f, 0.32f, 0.07f), stone, new Vector3(0f, 45f, 0f)),
            new Part("Prong2",  Shape.Box,        new Vector3(p, 0.2f, -p),    new Vector3(0.07f, 0.32f, 0.07f), stone, new Vector3(0f, 45f, 0f)),
            new Part("Prong3",  Shape.Box,        new Vector3(-p, 0.2f, -p),   new Vector3(0.07f, 0.32f, 0.07f), stone, new Vector3(0f, 45f, 0f)),
            new Part("Crystal", Shape.Octahedron, new Vector3(0f, 0.17f, 0f),  new Vector3(0.18f, 0.3f, 0.18f), crystal, new Vector3(0f, 45f, 0f)),
        };
    }

    // ---- Mana Container (#145): a squat banded tank on a plinth, a glass gauge on its face ----

    public static Part[] ManaContainerRecipe(float s, Color body, Color band, Color glass)
    {
        float front = -0.31f * s; // The tank's camera-facing (−Z) side
        return new[]
        {
            new Part("Plinth", Shape.Box,      new Vector3(0f, 0.03f, 0f),            new Vector3(0.82f * s, 0.06f, 0.82f * s), band),
            new Part("Tank",   Shape.Cylinder, new Vector3(0f, 0.3f, 0f),             new Vector3(0.62f * s, 0.24f, 0.62f * s), body),
            new Part("Band0",  Shape.Cylinder, new Vector3(0f, 0.16f, 0f),            new Vector3(0.65f * s, 0.025f, 0.65f * s), band),
            new Part("Band1",  Shape.Cylinder, new Vector3(0f, 0.44f, 0f),            new Vector3(0.65f * s, 0.025f, 0.65f * s), band),
            new Part("Lid",    Shape.Cylinder, new Vector3(0f, 0.56f, 0f),            new Vector3(0.48f * s, 0.03f, 0.48f * s), band),
            new Part("Gauge",  Shape.Box,      new Vector3(0f, 0.3f, front - 0.01f),  new Vector3(0.1f, 0.22f, 0.02f), glass),
        };
    }

    /// <summary>Accumulator materials (#145): two cut stones, a crystal shard lying on its side, a crate.</summary>
    public static Part[] AccumulatorMaterialsRecipe(float s, Color stone, Color crystal, Color crate) => new[]
    {
        new Part("Stone0",  Shape.Box,        new Vector3(-0.16f * s, 0.06f, -0.06f * s), new Vector3(0.28f * s, 0.12f, 0.24f * s), stone, new Vector3(0f, 12f, 0f)),
        new Part("Stone1",  Shape.Box,        new Vector3(-0.12f * s, 0.17f, -0.04f * s), new Vector3(0.22f * s, 0.1f, 0.2f * s), stone, new Vector3(0f, -10f, 0f)),
        new Part("Shard",   Shape.Octahedron, new Vector3(0.18f * s, 0.06f, 0.2f * s),    new Vector3(0.12f, 0.2f, 0.12f), crystal, new Vector3(0f, 30f, 80f)),
        new Part("Crate",   Shape.Box,        new Vector3(0.26f * s, 0.09f, -0.22f * s),  new Vector3(0.18f, 0.18f, 0.18f), crate, new Vector3(0f, -14f, 0f)),
    };

    /// <summary>Container materials (#145): a stack of tank staves, a loose band, a crate.</summary>
    public static Part[] ManaContainerMaterialsRecipe(float s, Color body, Color band, Color crate) => new[]
    {
        new Part("Stave0", Shape.Box,      new Vector3(-0.04f * s, 0.02f, -0.12f * s), new Vector3(0.8f * s, 0.04f, 0.1f * s), body, new Vector3(0f, 6f, 0f)),
        new Part("Stave1", Shape.Box,      new Vector3(0.02f * s, 0.06f, -0.1f * s),   new Vector3(0.76f * s, 0.04f, 0.1f * s), body, new Vector3(0f, -7f, 0f)),
        new Part("Band",   Shape.Cylinder, new Vector3(-0.14f * s, 0.015f, 0.18f * s), new Vector3(0.36f * s, 0.015f, 0.36f * s), band),
        new Part("Crate",  Shape.Box,      new Vector3(0.26f * s, 0.1f, 0.2f * s),     new Vector3(0.2f, 0.2f, 0.2f), crate, new Vector3(0f, 16f, 0f)),
    };

    // ---- Combination Chamber (#146): a round stone vessel on a plinth, an inlet on the left (input port), an outlet on
    // the right (output port), and a glowing core where the mana combines (the Core part glows) ----

    public const string ChamberCorePart = "Core";

    public static Part[] CombinationChamberRecipe(float s, Color stone, Color band, Color core)
    {
        float side = 0.36f * s;
        return new[]
        {
            new Part("Plinth", Shape.Box,        new Vector3(0f, 0.03f, 0f),       new Vector3(0.84f * s, 0.06f, 0.84f * s), band),
            new Part("Vessel", Shape.Sphere,     new Vector3(0f, 0.24f, 0f),       new Vector3(0.6f * s, 0.42f, 0.6f * s), stone),
            new Part("Collar", Shape.Cylinder,   new Vector3(0f, 0.42f, 0f),       new Vector3(0.36f * s, 0.03f, 0.36f * s), band),
            new Part("Inlet",  Shape.Box,        new Vector3(-side, 0.16f, 0f),    new Vector3(0.2f * s, 0.1f, 0.12f), band),
            new Part("Outlet", Shape.Box,        new Vector3(side, 0.16f, 0f),     new Vector3(0.2f * s, 0.1f, 0.12f), band),
            new Part(ChamberCorePart, Shape.Octahedron, new Vector3(0f, 0.52f, 0f), new Vector3(0.16f, 0.24f, 0.16f), core, new Vector3(0f, 45f, 0f)),
        };
    }

    /// <summary>Chamber materials (#146): two curved stone slabs, a band, the core crystal lying loose, a crate.</summary>
    public static Part[] CombinationChamberMaterialsRecipe(float s, Color stone, Color band, Color core, Color crate) => new[]
    {
        new Part("Slab0", Shape.Box,        new Vector3(-0.12f * s, 0.04f, -0.1f * s), new Vector3(0.5f * s, 0.08f, 0.24f * s), stone, new Vector3(0f, 12f, 0f)),
        new Part("Slab1", Shape.Box,        new Vector3(-0.08f * s, 0.12f, 0.14f * s), new Vector3(0.46f * s, 0.08f, 0.22f * s), stone, new Vector3(0f, -9f, 0f)),
        new Part("Band",  Shape.Cylinder,   new Vector3(0.2f * s, 0.015f, -0.24f * s), new Vector3(0.3f * s, 0.015f, 0.3f * s), band),
        new Part("Shard", Shape.Octahedron, new Vector3(0.28f * s, 0.06f, 0.08f * s),  new Vector3(0.1f, 0.18f, 0.1f), core, new Vector3(0f, 20f, 80f)),
        new Part("Crate", Shape.Box,        new Vector3(0.3f * s, 0.1f, 0.28f * s),    new Vector3(0.18f, 0.18f, 0.18f), crate, new Vector3(0f, -12f, 0f)),
    };

    // ---- Fire Vent (#145): a cracked rock mound around a glowing fissure (the Ember part glows) ----

    public static Part[] FireVentRecipe(Color rock, Color ember) => new[]
    {
        new Part("Mound", Shape.Sphere, new Vector3(0f, 0f, 0f),          new Vector3(0.86f, 0.26f, 0.86f), rock), // Half sunk
        new Part("Lip0",  Shape.Box,    new Vector3(0.18f, 0.1f, 0.05f),  new Vector3(0.22f, 0.08f, 0.14f), rock, new Vector3(0f, 25f, 12f)),
        new Part("Lip1",  Shape.Box,    new Vector3(-0.17f, 0.1f, -0.04f), new Vector3(0.2f, 0.08f, 0.15f), rock, new Vector3(0f, -20f, -14f)),
        new Part("Ember", Shape.Box,    new Vector3(0f, 0.12f, 0f),       new Vector3(0.34f, 0.03f, 0.08f), ember, new Vector3(0f, 30f, 0f)),
    };

    public const string FireVentGlowPart = "Ember";

    // ---- Spring (#150): a ring of stones around a round, glowing pool (the Pool part glows) ----

    public static Part[] SpringRecipe(Color stone, Color pool) => new[]
    {
        new Part("Rim",    Shape.Cylinder, new Vector3(0f, 0.04f, 0f),       new Vector3(0.78f, 0.04f, 0.78f), stone),
        new Part("Stone0", Shape.Sphere,   new Vector3(0.3f, 0.07f, 0.12f),   new Vector3(0.2f, 0.14f, 0.18f), stone),
        new Part("Stone1", Shape.Sphere,   new Vector3(-0.26f, 0.07f, 0.2f),  new Vector3(0.18f, 0.12f, 0.2f), stone),
        new Part("Stone2", Shape.Sphere,   new Vector3(-0.1f, 0.07f, -0.3f),  new Vector3(0.22f, 0.13f, 0.16f), stone),
        new Part("Pool",   Shape.Cylinder, new Vector3(0f, 0.085f, 0f),      new Vector3(0.52f, 0.01f, 0.52f), pool),
    };

    public const string SpringGlowPart = "Pool";

    // ---- The Heart: a faceted crystal on a low stone plinth ----

    public static Part[] HeartRecipe(float height, float width, Color crystal, Color plinth, float plinthHeight) => new[]
    {
        new Part("Plinth",  Shape.Cylinder,   new Vector3(0f, plinthHeight * 0.5f, 0f), new Vector3(0.85f, plinthHeight * 0.5f, 0.85f), plinth),
        // Turned 45° so a facet edge faces the camera: it reads as a gem, not a box.
        new Part("Crystal", Shape.Octahedron, new Vector3(0f, plinthHeight, 0f), new Vector3(width, height - plinthHeight, width), crystal, new Vector3(0f, 45f, 0f)),
    };
}
