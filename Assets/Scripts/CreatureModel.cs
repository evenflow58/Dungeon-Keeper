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

    public enum Shape { Sphere, Capsule, Cylinder, Box, Cone }

    /// <summary>One primitive part: local position/rotation/scale under the model root, and its color.</summary>
    public struct Part
    {
        public string Name;
        public Shape Shape;
        public Vector3 Position;
        public Vector3 Euler;
        public Vector3 Scale;
        public Color Color;

        public Part(string name, Shape shape, Vector3 position, Vector3 scale, Color color, Vector3 euler = default)
        {
            Name = name;
            Shape = shape;
            Position = position;
            Scale = scale;
            Color = color;
            Euler = euler;
        }
    }

    private static readonly Dictionary<(Color, float), Material> materials = new Dictionary<(Color, float), Material>();
    private static readonly Dictionary<Shape, Mesh> meshes = new Dictionary<Shape, Mesh>();

    /// <summary>Builds the model under root: a "name" object at the ground point holding one object per part.</summary>
    public static GameObject Build(Transform root, string name, IList<Part> parts, float smoothness)
    {
        var model = new GameObject(name);
        model.transform.SetParent(root, false);

        foreach (Part part in parts)
        {
            var go = new GameObject(part.Name);
            go.transform.SetParent(model.transform, false);
            go.transform.localPosition = part.Position;
            go.transform.localRotation = Quaternion.Euler(part.Euler);
            go.transform.localScale = part.Scale;
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(part.Shape);
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = MaterialFor(part.Color, smoothness);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
        }
        return model;
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

        Mesh mesh = shape == Shape.Cone ? BuildCone() : BuiltinPrimitiveMesh(shape);
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

    // ---- recipes: proportions as fractions of the creature's height h; feet at y = 0 ----

    /// <summary>The goblin: stockiest. Squat body, darker belly, oversized head, long ears out to the sides.</summary>
    public static Part[] GoblinRecipe(float h, Color skin, Color belly) => new[]
    {
        new Part("Body",  Shape.Capsule, new Vector3(0f, 0.275f * h, 0f),  new Vector3(0.52f, 0.275f, 0.48f) * h, skin),
        new Part("Belly", Shape.Sphere,  new Vector3(0f, 0.25f * h, -0.17f * h), new Vector3(0.36f, 0.32f, 0.16f) * h, belly),
        new Part("Head",  Shape.Sphere,  new Vector3(0f, 0.72f * h, 0f),   new Vector3(0.54f, 0.52f, 0.52f) * h, skin),
        new Part("EarLeft",  Shape.Cone, new Vector3(-0.22f * h, 0.76f * h, 0f), new Vector3(0.14f, 0.42f, 0.06f) * h, skin, new Vector3(0f, 0f, 68f)),
        new Part("EarRight", Shape.Cone, new Vector3(0.22f * h, 0.76f * h, 0f),  new Vector3(0.14f, 0.42f, 0.06f) * h, skin, new Vector3(0f, 0f, -68f)),
    };

    /// <summary>The imp: smallest. Slim body, big head, two small horns, a pickaxe held at its right side.</summary>
    public static Part[] ImpRecipe(float h, Color skin, Color horns, Color handle, Color pickHead) => new[]
    {
        new Part("Body", Shape.Capsule, new Vector3(0f, 0.27f * h, 0f), new Vector3(0.40f, 0.27f, 0.38f) * h, skin),
        new Part("Head", Shape.Sphere,  new Vector3(0f, 0.72f * h, 0f), new Vector3(0.50f, 0.48f, 0.48f) * h, skin),
        new Part("HornLeft",  Shape.Cone, new Vector3(-0.13f * h, 0.90f * h, 0f), new Vector3(0.10f, 0.20f, 0.10f) * h, horns, new Vector3(0f, 0f, 22f)),
        new Part("HornRight", Shape.Cone, new Vector3(0.13f * h, 0.90f * h, 0f),  new Vector3(0.10f, 0.20f, 0.10f) * h, horns, new Vector3(0f, 0f, -22f)),
        new Part("PickHandle", Shape.Cylinder, new Vector3(0.32f * h, 0.42f * h, -0.08f * h), new Vector3(0.06f, 0.34f, 0.06f) * h, handle, new Vector3(0f, 0f, -18f)),
        new Part("PickHead",   Shape.Box,      new Vector3(0.43f * h, 0.74f * h, -0.08f * h), new Vector3(0.40f, 0.08f, 0.08f) * h, pickHead, new Vector3(0f, 0f, -18f)),
    };

    /// <summary>The hero: tallest. Upright armored body, helmet with a crest, a round shield on his left.</summary>
    public static Part[] HeroRecipe(float h, Color armor, Color accent) => new[]
    {
        new Part("Body",   Shape.Capsule,  new Vector3(0f, 0.32f * h, 0f),  new Vector3(0.40f, 0.32f, 0.36f) * h, armor),
        new Part("Helmet", Shape.Sphere,   new Vector3(0f, 0.76f * h, 0f),  new Vector3(0.40f, 0.38f, 0.38f) * h, armor),
        new Part("Crest",  Shape.Box,      new Vector3(0f, 0.95f * h, 0f),  new Vector3(0.07f, 0.10f, 0.30f) * h, accent),
        // A round shield held at his front-left, its face toward the camera (a disc edge-on would read as a sliver).
        new Part("Shield", Shape.Cylinder, new Vector3(-0.20f * h, 0.34f * h, -0.18f * h), new Vector3(0.34f, 0.025f, 0.34f) * h, accent, new Vector3(90f, 0f, 0f)),
    };
}
