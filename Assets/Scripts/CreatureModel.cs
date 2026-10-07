using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one builder for code-built creature models (ticket #76): a model is a root with primitive parts, each a
/// MeshRenderer with a URP Lit material keyed by (color, smoothness) and shared across every creature, casting
/// and receiving shadows. Creatures differ only by recipe — a list of parts whose positions/scales are fractions of
/// the creature's serialized height and whose colors come from the creature's own serialized fields.
/// The model root sits at the creature's ground point; every recipe keeps its lowest part's base at y = 0.
/// </summary>
public static class CreatureModel
{
    public const string LitShaderName = "Universal Render Pipeline/Lit";

    public enum Shape { Sphere, Capsule, Cylinder, Box, Cone, Octahedron }

    /// <summary>One primitive part: local position/rotation/scale under the model root, and its color.</summary>
    public struct Part
    {
        public string Name;
        public Shape Shape;
        public Vector3 Position;
        public Vector3 Euler;
        public Vector3 Scale;
        public Color Color;
        public float? Smoothness; // Per-part override (eyes, metal); null = the model's smoothness

        public Part(string name, Shape shape, Vector3 position, Vector3 scale, Color color, Vector3 euler = default, float? smoothness = null)
        {
            Name = name;
            Shape = shape;
            Position = position;
            Scale = scale;
            Color = color;
            Euler = euler;
            Smoothness = smoothness;
        }
    }

    private static readonly Dictionary<(Color, float), Material> materials = new Dictionary<(Color, float), Material>();
    private static readonly Dictionary<(Color, float, float), Material> emissiveMaterials = new Dictionary<(Color, float, float), Material>();
    private static readonly Dictionary<Shape, Mesh> meshes = new Dictionary<Shape, Mesh>();

    /// <summary>Builds the model under root: a "name" object at the ground point holding one object per part.</summary>
    public static GameObject Build(Transform root, string name, IList<Part> parts, float smoothness)
    {
        var model = new GameObject(name);
        model.transform.SetParent(root, false);

        foreach (Part part in parts) CreatePart(model.transform, part, smoothness);
        return model;
    }

    /// <summary>One part under parent: its mesh, a shared lit material for its color, casting and receiving shadows.</summary>
    public static MeshRenderer CreatePart(Transform parent, Part part, float smoothness)
    {
        var go = new GameObject(part.Name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = part.Position;
        go.transform.localRotation = Quaternion.Euler(part.Euler);
        go.transform.localScale = part.Scale;
        go.AddComponent<MeshFilter>().sharedMesh = MeshFor(part.Shape);
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = MaterialFor(part.Color, part.Smoothness ?? smoothness);
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        meshRenderer.receiveShadows = true;
        return meshRenderer;
    }

    /// <summary>A shared emissive lit material (the color glows at intensity): one per (color, intensity, smoothness).</summary>
    public static Material EmissiveMaterialFor(Color color, float intensity, float smoothness)
    {
        if (emissiveMaterials.TryGetValue((color, intensity, smoothness), out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find(LitShaderName) ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        var material = new Material(shader) { name = $"Emissive {ColorUtility.ToHtmlStringRGB(color)} x{intensity:0.##}" };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * intensity); // HDR: intensity > 1 glows past the lit surface
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        emissiveMaterials[(color, intensity, smoothness)] = material;
        return material;
    }

    /// <summary>The shared lit material for a color at a smoothness: one per pair, created on first use.</summary>
    public static Material MaterialFor(Color color, float smoothness)
    {
        if (materials.TryGetValue((color, smoothness), out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find(LitShaderName) ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        var material = new Material(shader) { name = $"Creature {ColorUtility.ToHtmlStringRGB(color)} s{smoothness:0.##}" };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness); // Toy sheen: specular highlights and reflections stay on
        materials[(color, smoothness)] = material;
        return material;
    }

    /// <summary>The shared mesh for a shape: Unity's built-in primitives, plus a code-built cone (Unity has none).</summary>
    public static Mesh MeshFor(Shape shape)
    {
        if (meshes.TryGetValue(shape, out Mesh cached) && cached != null) return cached;

        Mesh mesh = shape == Shape.Cone ? BuildCone()
            : shape == Shape.Octahedron ? BuildOctahedron()
            : BuiltinPrimitiveMesh(shape);
        meshes[shape] = mesh;
        return mesh;
    }

    private static Mesh BuiltinPrimitiveMesh(Shape shape)
    {
        PrimitiveType type = shape == Shape.Sphere ? PrimitiveType.Sphere
            : shape == Shape.Capsule ? PrimitiveType.Capsule
            : shape == Shape.Cylinder ? PrimitiveType.Cylinder
            : PrimitiveType.Cube;
        GameObject temp = GameObject.CreatePrimitive(type); // Borrow the built-in mesh, discard the object
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Object.Destroy(temp);
        else Object.DestroyImmediate(temp);
        return mesh;
    }

    // A cone with its base (radius 0.5) on the local origin and its tip at y = 1, so scale.y is its length.
    private static Mesh BuildCone()
    {
        const int segments = 16;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        float slope = Mathf.Atan2(0.5f, 1f); // Side normal tilt

        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            Vector3 n0 = new Vector3(Mathf.Cos(a0) * Mathf.Cos(slope), Mathf.Sin(slope), Mathf.Sin(a0) * Mathf.Cos(slope));
            Vector3 n1 = new Vector3(Mathf.Cos(a1) * Mathf.Cos(slope), Mathf.Sin(slope), Mathf.Sin(a1) * Mathf.Cos(slope));
            Vector3 nTip = ((n0 + n1) * 0.5f).normalized;

            // Side: base edge to tip, wound clockwise seen from outside.
            int s = vertices.Count;
            vertices.Add(p0); vertices.Add(Vector3.up); vertices.Add(p1);
            normals.Add(n0); normals.Add(nTip); normals.Add(n1);
            triangles.Add(s); triangles.Add(s + 1); triangles.Add(s + 2);

            // Base: a fan facing down.
            int b = vertices.Count;
            vertices.Add(Vector3.zero); vertices.Add(p0); vertices.Add(p1);
            normals.Add(Vector3.down); normals.Add(Vector3.down); normals.Add(Vector3.down);
            triangles.Add(b); triangles.Add(b + 1); triangles.Add(b + 2);
        }

        var mesh = new Mesh { name = "CreatureCone" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A faceted double pyramid: bottom tip on the local origin, the square equator (corners at ±0.5 on X/Z) at
    // y = 0.5, top tip at y = 1. Each face has its own vertices and flat normal, so the facets catch light.
    private static Mesh BuildOctahedron()
    {
        var corners = new[] { new Vector3(0.5f, 0.5f, 0f), new Vector3(0f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0f, 0.5f, -0.5f) };
        Vector3 top = Vector3.up, bottom = Vector3.zero;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        void AddFacet(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int s = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            triangles.Add(s); triangles.Add(s + 1); triangles.Add(s + 2);
        }

        for (int i = 0; i < 4; i++)
        {
            Vector3 c0 = corners[i], c1 = corners[(i + 1) % 4];
            AddFacet(c0, top, c1);    // Upper facet, clockwise seen from outside
            AddFacet(c1, bottom, c0); // Lower facet
        }

        var mesh = new Mesh { name = "Octahedron" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---- palettes: one serializable group of colors per creature (shown as one foldout in the inspector) ----

    // "#rrggbb" → Color in plain C#: palettes are field initializers on MonoBehaviours, where Unity's native APIs
    // (ColorUtility included) may not be called.
    private static Color Hex(string hex)
    {
        int rgb = System.Convert.ToInt32(hex.TrimStart('#'), 16);
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>The goblin's design palette (#84, the approved toyetic reference).</summary>
    [System.Serializable]
    public struct GoblinPalette
    {
        public Color Skin, InnerEar, BrowAndWraps, Nose, Vest, Shorts, BootsAndBelt, Buckle, Blush, Eyes, Mouth, Fang;

        public static GoblinPalette Default => new GoblinPalette
        {
            Skin = Hex("#60a020"), InnerEar = Hex("#a4d65e"), BrowAndWraps = Hex("#2e6b1a"), Nose = Hex("#4c9428"),
            Vest = Hex("#804000"), Shorts = Hex("#602000"), BootsAndBelt = Hex("#402000"), Buckle = Hex("#f2b134"),
            Blush = Hex("#f49ac1"), Eyes = Hex("#0b0b0b"), Mouth = Hex("#1d2b12"), Fang = Color.white,
        };
    }

    /// <summary>The imp's palette: its sprite-era red skin and dark horns, with leather, cloth and a pickaxe.</summary>
    [System.Serializable]
    public struct ImpPalette
    {
        public Color Skin, Horns, Brow, Boots, Loincloth, Eyes, Fang, PickHandle, PickHead;

        public static ImpPalette Default => new ImpPalette
        {
            Skin = new Color(0.85f, 0.2f, 0.2f), Horns = new Color(0.35f, 0.08f, 0.06f), Brow = new Color(0.55f, 0.10f, 0.08f),
            Boots = new Color(0.24f, 0.11f, 0.06f), Loincloth = new Color(0.40f, 0.20f, 0.09f), Eyes = Hex("#0b0b0b"),
            Fang = Color.white, PickHandle = new Color(0.50f, 0.33f, 0.18f), PickHead = new Color(0.62f, 0.64f, 0.68f),
        };
    }

    /// <summary>The hero's palette: pale steel armor and blue heraldry (the triangle's colors), dark steel and leather.</summary>
    [System.Serializable]
    public struct HeroPalette
    {
        public Color Armor, Accent, DarkSteel, Visor, Belt;

        public static HeroPalette Default => new HeroPalette
        {
            Armor = new Color(0.78f, 0.83f, 0.92f), Accent = new Color(0.20f, 0.36f, 0.72f),
            DarkSteel = new Color(0.32f, 0.34f, 0.40f), Visor = new Color(0.05f, 0.05f, 0.07f), Belt = new Color(0.32f, 0.20f, 0.10f),
        };
    }

    // ---- recipes: positions/scales are fractions of the creature's height h (feet at y = 0, top ≈ h); −Z faces the camera ----

    private const float EyeGloss = 0.9f;   // Eyes and their highlights: the glossiest thing on a model
    private const float MetalGloss = 0.6f; // Buckles, fangs, the shield boss

    private static Vector3 V(float x, float y, float z, float h) => new Vector3(x, y, z) * h;

    /// <summary>
    /// The goblin, to the approved toyetic reference: boots with toes and leg wraps, shorts, a brown vest with a
    /// belt and gold buckle, arms with oversized hands, a face (muzzle, nose, brow, glossy eyes with highlights,
    /// blush, a grin with one fang) and tall ears with lighter inner ears. Ear tips define the top.
    /// </summary>
    public static Part[] GoblinRecipe(float h, GoblinPalette p) => new[]
    {
        new Part("BootLeft",  Shape.Box, V(-0.10f, 0.04f, -0.01f, h), V(0.13f, 0.08f, 0.16f, h), p.BootsAndBelt),
        new Part("ToeLeft",   Shape.Box, V(-0.10f, 0.03f, -0.09f, h), V(0.11f, 0.06f, 0.07f, h), p.BootsAndBelt),
        new Part("WrapLeft",  Shape.Cylinder, V(-0.10f, 0.12f, 0f, h), V(0.10f, 0.03f, 0.10f, h), p.BrowAndWraps),
        new Part("BootRight", Shape.Box, V(0.10f, 0.04f, -0.01f, h),  V(0.13f, 0.08f, 0.16f, h), p.BootsAndBelt),
        new Part("ToeRight",  Shape.Box, V(0.10f, 0.03f, -0.09f, h),  V(0.11f, 0.06f, 0.07f, h), p.BootsAndBelt),
        new Part("WrapRight", Shape.Cylinder, V(0.10f, 0.12f, 0f, h),  V(0.10f, 0.03f, 0.10f, h), p.BrowAndWraps),
        new Part("Shorts",    Shape.Box, V(0f, 0.205f, 0f, h), V(0.30f, 0.15f, 0.22f, h), p.Shorts),
        new Part("Vest",      Shape.Sphere, V(0f, 0.34f, 0f, h), V(0.44f, 0.30f, 0.34f, h), p.Vest),
        new Part("Belt",      Shape.Cylinder, V(0f, 0.26f, 0f, h), V(0.40f, 0.025f, 0.31f, h), p.BootsAndBelt),
        new Part("Buckle",    Shape.Box, V(0f, 0.26f, -0.165f, h), V(0.07f, 0.06f, 0.02f, h), p.Buckle, default, MetalGloss),
        new Part("ArmLeft",   Shape.Capsule, V(-0.22f, 0.32f, 0f, h), V(0.08f, 0.09f, 0.08f, h), p.Skin, new Vector3(0f, 0f, -30f)),
        new Part("ArmRight",  Shape.Capsule, V(0.22f, 0.32f, 0f, h),  V(0.08f, 0.09f, 0.08f, h), p.Skin, new Vector3(0f, 0f, 30f)),
        new Part("HandLeft",  Shape.Sphere, V(-0.27f, 0.25f, -0.02f, h), V(0.14f, 0.12f, 0.14f, h), p.Skin),
        new Part("HandRight", Shape.Sphere, V(0.27f, 0.25f, -0.02f, h),  V(0.14f, 0.12f, 0.14f, h), p.Skin),
        new Part("Head",      Shape.Sphere, V(0f, 0.63f, 0f, h), V(0.45f, 0.38f, 0.40f, h), p.Skin),
        new Part("Muzzle",    Shape.Sphere, V(0f, 0.55f, -0.15f, h), V(0.28f, 0.16f, 0.16f, h), p.Skin),
        new Part("Nose",      Shape.Sphere, V(0f, 0.585f, -0.225f, h), V(0.07f, 0.06f, 0.06f, h), p.Nose),
        new Part("Brow",      Shape.Box, V(0f, 0.71f, -0.17f, h), V(0.30f, 0.04f, 0.06f, h), p.BrowAndWraps, new Vector3(-10f, 0f, 0f)),
        new Part("EyeLeft",   Shape.Sphere, V(-0.09f, 0.66f, -0.17f, h), V(0.08f, 0.08f, 0.08f, h), p.Eyes, default, EyeGloss),
        new Part("EyeRight",  Shape.Sphere, V(0.09f, 0.66f, -0.17f, h),  V(0.08f, 0.08f, 0.08f, h), p.Eyes, default, EyeGloss),
        new Part("EyeShineLeft",  Shape.Sphere, V(-0.075f, 0.675f, -0.205f, h), V(0.025f, 0.025f, 0.025f, h), Color.white, default, EyeGloss),
        new Part("EyeShineRight", Shape.Sphere, V(0.105f, 0.675f, -0.205f, h),  V(0.025f, 0.025f, 0.025f, h), Color.white, default, EyeGloss),
        new Part("BlushLeft",  Shape.Sphere, V(-0.16f, 0.58f, -0.155f, h), V(0.08f, 0.04f, 0.03f, h), p.Blush),
        new Part("BlushRight", Shape.Sphere, V(0.16f, 0.58f, -0.155f, h),  V(0.08f, 0.04f, 0.03f, h), p.Blush),
        // The grin: three thin angled bars (corners up) rather than a new torus mesh.
        new Part("MouthLeft",  Shape.Box, V(-0.06f, 0.515f, -0.218f, h), V(0.05f, 0.012f, 0.012f, h), p.Mouth, new Vector3(0f, 0f, -20f)),
        new Part("MouthMid",   Shape.Box, V(0f, 0.505f, -0.224f, h),     V(0.08f, 0.012f, 0.012f, h), p.Mouth),
        new Part("MouthRight", Shape.Box, V(0.06f, 0.515f, -0.218f, h),  V(0.05f, 0.012f, 0.012f, h), p.Mouth, new Vector3(0f, 0f, 20f)),
        new Part("Fang",       Shape.Cone, V(0.03f, 0.502f, -0.226f, h), V(0.025f, 0.04f, 0.025f, h), p.Fang, new Vector3(180f, 0f, 0f), MetalGloss),
        new Part("EarLeft",       Shape.Cone, V(-0.19f, 0.74f, 0f, h), V(0.16f, 0.42f, 0.05f, h), p.Skin, new Vector3(0f, 0f, 55f)),
        new Part("EarRight",      Shape.Cone, V(0.19f, 0.74f, 0f, h),  V(0.16f, 0.42f, 0.05f, h), p.Skin, new Vector3(0f, 0f, -55f)),
        new Part("InnerEarLeft",  Shape.Cone, V(-0.20f, 0.745f, -0.02f, h), V(0.09f, 0.30f, 0.03f, h), p.InnerEar, new Vector3(0f, 0f, 55f)),
        new Part("InnerEarRight", Shape.Cone, V(0.20f, 0.745f, -0.02f, h),  V(0.09f, 0.30f, 0.03f, h), p.InnerEar, new Vector3(0f, 0f, -55f)),
    };

    /// <summary>
    /// The imp, in the goblin's language: boots with cuffs, a belt and loincloth, hands, a face (muzzle, brow, glossy
    /// eyes with highlights, one fang), its horns and its pickaxe. Horn tips define the top.
    /// </summary>
    public static Part[] ImpRecipe(float h, ImpPalette p) => new[]
    {
        new Part("BootLeft",  Shape.Box, V(-0.09f, 0.04f, -0.01f, h), V(0.11f, 0.08f, 0.15f, h), p.Boots),
        new Part("CuffLeft",  Shape.Cylinder, V(-0.09f, 0.10f, 0f, h), V(0.09f, 0.02f, 0.09f, h), p.Boots),
        new Part("BootRight", Shape.Box, V(0.09f, 0.04f, -0.01f, h),  V(0.11f, 0.08f, 0.15f, h), p.Boots),
        new Part("CuffRight", Shape.Cylinder, V(0.09f, 0.10f, 0f, h),  V(0.09f, 0.02f, 0.09f, h), p.Boots),
        new Part("Body",      Shape.Capsule, V(0f, 0.30f, 0f, h), V(0.32f, 0.20f, 0.28f, h), p.Skin),
        new Part("Belt",      Shape.Cylinder, V(0f, 0.20f, 0f, h), V(0.34f, 0.02f, 0.30f, h), p.Boots),
        new Part("Loincloth", Shape.Box, V(0f, 0.14f, -0.14f, h), V(0.12f, 0.10f, 0.02f, h), p.Loincloth),
        new Part("HandLeft",  Shape.Sphere, V(-0.22f, 0.30f, -0.02f, h), V(0.12f, 0.10f, 0.12f, h), p.Skin),
        new Part("HandRight", Shape.Sphere, V(0.22f, 0.30f, -0.02f, h),  V(0.12f, 0.10f, 0.12f, h), p.Skin),
        new Part("Head",      Shape.Sphere, V(0f, 0.62f, 0f, h), V(0.42f, 0.36f, 0.38f, h), p.Skin),
        new Part("Muzzle",    Shape.Sphere, V(0f, 0.555f, -0.15f, h), V(0.24f, 0.13f, 0.14f, h), p.Skin),
        new Part("Brow",      Shape.Box, V(0f, 0.69f, -0.16f, h), V(0.28f, 0.035f, 0.05f, h), p.Brow, new Vector3(-10f, 0f, 0f)),
        new Part("EyeLeft",   Shape.Sphere, V(-0.085f, 0.65f, -0.165f, h), V(0.075f, 0.075f, 0.075f, h), p.Eyes, default, EyeGloss),
        new Part("EyeRight",  Shape.Sphere, V(0.085f, 0.65f, -0.165f, h),  V(0.075f, 0.075f, 0.075f, h), p.Eyes, default, EyeGloss),
        new Part("EyeShineLeft",  Shape.Sphere, V(-0.07f, 0.665f, -0.2f, h), V(0.022f, 0.022f, 0.022f, h), Color.white, default, EyeGloss),
        new Part("EyeShineRight", Shape.Sphere, V(0.10f, 0.665f, -0.2f, h),  V(0.022f, 0.022f, 0.022f, h), Color.white, default, EyeGloss),
        new Part("Fang",      Shape.Cone, V(0.03f, 0.51f, -0.21f, h), V(0.025f, 0.04f, 0.025f, h), p.Fang, new Vector3(180f, 0f, 0f), MetalGloss),
        new Part("HornLeft",  Shape.Cone, V(-0.12f, 0.76f, 0f, h), V(0.08f, 0.24f, 0.08f, h), p.Horns, new Vector3(0f, 0f, 22f)),
        new Part("HornRight", Shape.Cone, V(0.12f, 0.76f, 0f, h),  V(0.08f, 0.24f, 0.08f, h), p.Horns, new Vector3(0f, 0f, -22f)),
        new Part("PickHandle", Shape.Cylinder, V(0.30f, 0.40f, -0.08f, h), V(0.05f, 0.30f, 0.05f, h), p.PickHandle, new Vector3(0f, 0f, -18f)),
        new Part("PickHead",   Shape.Box, V(0.393f, 0.685f, -0.08f, h), V(0.36f, 0.07f, 0.07f, h), p.PickHead, new Vector3(0f, 0f, -18f)),
    };

    /// <summary>
    /// The hero, in the same language: boots, an armored body with a blue tabard and a belt, shoulder pauldrons,
    /// armored arms and gauntlets, a helmet with a dark visor slit (a knight's face) and a crest, and a round shield
    /// with a center boss. The crest tip defines the top.
    /// </summary>
    public static Part[] HeroRecipe(float h, HeroPalette p) => new[]
    {
        new Part("BootLeft",  Shape.Box, V(-0.09f, 0.045f, -0.01f, h), V(0.12f, 0.09f, 0.17f, h), p.DarkSteel),
        new Part("BootRight", Shape.Box, V(0.09f, 0.045f, -0.01f, h),  V(0.12f, 0.09f, 0.17f, h), p.DarkSteel),
        new Part("Body",      Shape.Capsule, V(0f, 0.33f, 0f, h), V(0.36f, 0.22f, 0.30f, h), p.Armor),
        new Part("Tabard",    Shape.Box, V(0f, 0.30f, -0.15f, h), V(0.16f, 0.30f, 0.02f, h), p.Accent),
        new Part("Belt",      Shape.Cylinder, V(0f, 0.30f, 0f, h), V(0.38f, 0.02f, 0.32f, h), p.Belt),
        new Part("PauldronLeft",  Shape.Sphere, V(-0.19f, 0.52f, 0f, h), V(0.16f, 0.12f, 0.16f, h), p.Armor),
        new Part("PauldronRight", Shape.Sphere, V(0.19f, 0.52f, 0f, h),  V(0.16f, 0.12f, 0.16f, h), p.Armor),
        new Part("ArmLeft",   Shape.Capsule, V(-0.21f, 0.40f, 0f, h), V(0.08f, 0.08f, 0.08f, h), p.Armor, new Vector3(0f, 0f, -15f)),
        new Part("ArmRight",  Shape.Capsule, V(0.21f, 0.40f, 0f, h),  V(0.08f, 0.08f, 0.08f, h), p.Armor, new Vector3(0f, 0f, 15f)),
        new Part("GauntletLeft",  Shape.Sphere, V(-0.23f, 0.30f, -0.02f, h), V(0.10f, 0.09f, 0.10f, h), p.DarkSteel),
        new Part("GauntletRight", Shape.Sphere, V(0.23f, 0.30f, -0.02f, h),  V(0.10f, 0.09f, 0.10f, h), p.DarkSteel),
        new Part("Helmet",    Shape.Sphere, V(0f, 0.70f, 0f, h), V(0.36f, 0.34f, 0.34f, h), p.Armor),
        new Part("Visor",     Shape.Box, V(0f, 0.69f, -0.165f, h), V(0.22f, 0.035f, 0.03f, h), p.Visor, default, EyeGloss),
        new Part("Crest",     Shape.Box, V(0f, 0.93f, 0f, h), V(0.06f, 0.14f, 0.28f, h), p.Accent),
        // The shield, held at his front-left with its face to the camera; a boss at its center.
        new Part("Shield",     Shape.Cylinder, V(-0.20f, 0.34f, -0.18f, h), V(0.32f, 0.025f, 0.32f, h), p.Accent, new Vector3(90f, 0f, 0f)),
        new Part("ShieldBoss", Shape.Sphere, V(-0.20f, 0.34f, -0.205f, h), V(0.08f, 0.08f, 0.04f, h), p.Armor, default, MetalGloss),
    };
}
