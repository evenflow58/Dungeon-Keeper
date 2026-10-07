using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class CreatureModelTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
    }

    private static Bounds WorldBounds(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    private static string[] PartNames(GameObject model) =>
        model.GetComponentsInChildren<MeshRenderer>().Select(r => r.gameObject.name).ToArray();

    // --- Recipes through the one builder (#84: the detailed toyetic designs) ---

    private static GameObject BuildGoblin(Transform parent) =>
        CreatureModel.Build(parent, "GoblinModel", CreatureModel.GoblinRecipe(0.8f, CreatureModel.GoblinPalette.Default), 0.35f);
    private static GameObject BuildImp(Transform parent) =>
        CreatureModel.Build(parent, "ImpModel", CreatureModel.ImpRecipe(0.6f, CreatureModel.ImpPalette.Default), 0.35f);
    private static GameObject BuildHero(Transform parent) =>
        CreatureModel.Build(parent, "HeroModel", CreatureModel.HeroRecipe(0.85f, CreatureModel.HeroPalette.Default), 0.35f);

    [Test]
    public void Goblin_IsTheFullReferenceDesign()
    {
        root = new GameObject("Goblin");
        CollectionAssert.AreEqual(new[]
        {
            "BootLeft", "ToeLeft", "WrapLeft", "BootRight", "ToeRight", "WrapRight",
            "Shorts", "Vest", "Belt", "Buckle", "ArmLeft", "ArmRight", "HandLeft", "HandRight",
            "Head", "Muzzle", "Nose", "Brow", "EyeLeft", "EyeRight", "EyeShineLeft", "EyeShineRight",
            "BlushLeft", "BlushRight", "MouthLeft", "MouthMid", "MouthRight", "Fang",
            "EarLeft", "EarRight", "InnerEarLeft", "InnerEarRight",
        }, PartNames(BuildGoblin(root.transform)));
    }

    [Test]
    public void Imp_KeepsHornsAndPickaxe_WithAFaceBootsAndLoincloth()
    {
        root = new GameObject("Imp");
        CollectionAssert.AreEqual(new[]
        {
            "BootLeft", "CuffLeft", "BootRight", "CuffRight", "Body", "Belt", "Loincloth", "HandLeft", "HandRight",
            "Head", "Muzzle", "Brow", "EyeLeft", "EyeRight", "EyeShineLeft", "EyeShineRight", "Fang",
            "HornLeft", "HornRight", "PickHandle", "PickHead",
        }, PartNames(BuildImp(root.transform)));
    }

    [Test]
    public void Hero_KeepsHelmetCrestAndShield_WithVisorPauldronsGauntletsAndTabard()
    {
        root = new GameObject("Hero");
        CollectionAssert.AreEqual(new[]
        {
            "BootLeft", "BootRight", "Body", "Tabard", "Belt", "PauldronLeft", "PauldronRight", "ArmLeft", "ArmRight",
            "GauntletLeft", "GauntletRight", "Helmet", "Visor", "Crest", "Shield", "ShieldBoss",
        }, PartNames(BuildHero(root.transform)));
    }

    [Test]
    public void EachCreature_HasAFace_OnTheCameraSide()
    {
        root = new GameObject("Owners");
        GameObject goblin = BuildGoblin(root.transform), imp = BuildImp(root.transform), hero = BuildHero(root.transform);

        foreach (GameObject model in new[] { goblin, imp })
        {
            Transform head = model.transform.Find("Head");
            foreach (string eye in new[] { "EyeLeft", "EyeRight", "EyeShineLeft", "EyeShineRight" })
            {
                Transform part = model.transform.Find(eye);
                Assert.IsNotNull(part, model.name + " " + eye);
                Assert.Less(part.localPosition.z, head.localPosition.z, model.name + " " + eye + " in front of the head (toward the camera)");
            }
        }
        Transform visor = hero.transform.Find("Visor");
        Assert.IsNotNull(visor, "The hero's face is a visor slit");
        Assert.Less(visor.localPosition.z, hero.transform.Find("Helmet").localPosition.z);
    }

    [Test]
    public void Parts_ShareOneLitMaterialPerColor_AndCastAndReceiveShadows()
    {
        root = new GameObject("Goblin");
        CreatureModel.GoblinPalette palette = CreatureModel.GoblinPalette.Default;
        GameObject model = BuildGoblin(root.transform);

        MeshRenderer head = model.transform.Find("Head").GetComponent<MeshRenderer>();
        MeshRenderer ear = model.transform.Find("EarLeft").GetComponent<MeshRenderer>();
        MeshRenderer vest = model.transform.Find("Vest").GetComponent<MeshRenderer>();
        Assert.AreSame(head.sharedMaterial, ear.sharedMaterial, "Same color, same material");
        Assert.AreNotSame(head.sharedMaterial, vest.sharedMaterial, "The vest is its own color");
        Assert.AreSame(CreatureModel.MaterialFor(palette.Skin, 0.35f), head.sharedMaterial, "Keyed by color (and smoothness)");
        Assert.AreSame(CreatureModel.MaterialFor(palette.Vest, 0.35f), vest.sharedMaterial);
        Assert.AreEqual(CreatureModel.LitShaderName, head.sharedMaterial.shader.name);

        // Per-part smoothness: eyes are the glossiest thing on the model; the model's sheen elsewhere.
        Material eye = model.transform.Find("EyeLeft").GetComponent<MeshRenderer>().sharedMaterial;
        Assert.Greater(eye.GetFloat("_Smoothness"), head.sharedMaterial.GetFloat("_Smoothness"));
        Assert.AreSame(eye, model.transform.Find("EyeRight").GetComponent<MeshRenderer>().sharedMaterial);

        foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>())
        {
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode, r.name);
            Assert.IsTrue(r.receiveShadows, r.name);
        }
    }

    [Test]
    public void Palettes_CarryTheDesignColors()
    {
        CreatureModel.GoblinPalette g = CreatureModel.GoblinPalette.Default;
        Assert.AreEqual(0x60 / 255f, g.Skin.r, 1e-4f); Assert.AreEqual(0xa0 / 255f, g.Skin.g, 1e-4f); Assert.AreEqual(0x20 / 255f, g.Skin.b, 1e-4f);
        Assert.AreEqual(0x80 / 255f, g.Vest.r, 1e-4f); Assert.AreEqual(0x40 / 255f, g.Vest.g, 1e-4f); Assert.AreEqual(0f, g.Vest.b, 1e-4f);
        Assert.AreEqual(0xf2 / 255f, g.Buckle.r, 1e-4f); Assert.AreEqual(0xb1 / 255f, g.Buckle.g, 1e-4f); Assert.AreEqual(0x34 / 255f, g.Buckle.b, 1e-4f);
        Assert.AreEqual(new Color(0.85f, 0.2f, 0.2f), CreatureModel.ImpPalette.Default.Skin, "Imp red kept");
        Assert.AreEqual(new Color(0.78f, 0.83f, 0.92f), CreatureModel.HeroPalette.Default.Armor, "Pale steel kept");
    }

    [Test]
    public void Models_StandOnTheGroundPoint_FillTheirHeight_AndKeepTheirOrder()
    {
        root = new GameObject("Owners");
        root.transform.position = new Vector3(2.5f, 0f, -3.5f);
        Bounds imp = WorldBounds(BuildImp(root.transform));
        Bounds goblin = WorldBounds(BuildGoblin(root.transform));
        Bounds hero = WorldBounds(BuildHero(root.transform));

        foreach (Bounds b in new[] { imp, goblin, hero })
            Assert.AreEqual(0f, b.min.y, 0.02f, "Feet at the ground point");

        // Extremities (horn tips, ear tips, crest) define the top, at the serialized height.
        Assert.AreEqual(0.6f, imp.max.y, 0.04f, "Imp: horn tips");
        Assert.AreEqual(0.8f, goblin.max.y, 0.04f, "Goblin: ear tips");
        Assert.AreEqual(0.85f, hero.max.y, 0.04f, "Hero: crest");
        Assert.Less(imp.max.y, goblin.max.y, "Imp smallest");
        Assert.Less(goblin.max.y, hero.max.y, "Hero tallest");
        Assert.Greater(goblin.size.x, hero.size.x, "Goblin widest: its ears");
    }

    [Test]
    public void Cone_FacesOutward()
    {
        Mesh cone = CreatureModel.MeshFor(CreatureModel.Shape.Cone);
        Vector3[] v = cone.vertices;
        Vector3[] n = cone.normals;
        int[] t = cone.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            Assert.Greater(Vector3.Dot(cross, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]), 0f, $"Triangle {i / 3} faces outward");
        }
        Assert.AreEqual(0f, cone.bounds.min.y, 1e-5f, "Base on its origin");
        Assert.AreEqual(1f, cone.bounds.max.y, 1e-5f, "Tip at y = 1");
    }

    // --- The creatures use it (staged bare, as the behavior tests do) ---

    [Test]
    public void Imp_Goblin_Hero_BuildAModel_NotASprite_Once()
    {
        root = new GameObject("Owners");
        var imp = new GameObject("TestImp").AddComponent<Imp>();
        var goblin = new GameObject("TestGoblin").AddComponent<Goblin>();
        var hero = new GameObject("TestHero").AddComponent<Hero>();
        imp.transform.SetParent(root.transform);
        goblin.transform.SetParent(root.transform);
        hero.transform.SetParent(root.transform);

        imp.CreateModel();
        goblin.CreateModel();
        hero.CreateModel();

        foreach (var (owner, model, name) in new[] { (imp.transform, imp.Model, "ImpModel"), (goblin.transform, goblin.Model, "GoblinModel"), (hero.transform, hero.Model, "HeroModel") })
        {
            Assert.IsNotNull(model, name);
            Assert.AreEqual(name, model.name);
            Assert.AreSame(owner, model.transform.parent, "Under the creature root");
            Assert.AreEqual(Vector3.zero, model.transform.localPosition, "On the ground point");
            Assert.AreEqual(0, owner.GetComponentsInChildren<SpriteRenderer>().Length, "No sprite body, no ground disc");
        }

        GameObject first = goblin.Model;
        goblin.CreateModel();
        Assert.AreSame(first, goblin.Model, "Built once");
        Assert.AreEqual(1, goblin.transform.childCount);
    }
}
