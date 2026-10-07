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

    // --- Recipes through the one builder ---

    [Test]
    public void Goblin_HasBodyBellyHeadAndLongEars()
    {
        root = new GameObject("Goblin");
        GameObject model = CreatureModel.Build(root.transform, "GoblinModel",
            CreatureModel.GoblinRecipe(0.8f, Color.green, Color.gray), 0.35f);
        CollectionAssert.AreEqual(new[] { "Body", "Belly", "Head", "EarLeft", "EarRight" }, PartNames(model));
    }

    [Test]
    public void Imp_HasBodyHeadHornsAndAPickaxe()
    {
        root = new GameObject("Imp");
        GameObject model = CreatureModel.Build(root.transform, "ImpModel",
            CreatureModel.ImpRecipe(0.6f, Color.red, Color.black, Color.yellow, Color.gray), 0.35f);
        CollectionAssert.AreEqual(new[] { "Body", "Head", "HornLeft", "HornRight", "PickHandle", "PickHead" }, PartNames(model));
    }

    [Test]
    public void Hero_HasBodyHelmetCrestAndShield()
    {
        root = new GameObject("Hero");
        GameObject model = CreatureModel.Build(root.transform, "HeroModel",
            CreatureModel.HeroRecipe(0.85f, Color.white, Color.blue), 0.35f);
        CollectionAssert.AreEqual(new[] { "Body", "Helmet", "Crest", "Shield" }, PartNames(model));
    }

    [Test]
    public void Parts_ShareOneLitMaterialPerColor_AndCastAndReceiveShadows()
    {
        root = new GameObject("Goblin");
        Color skin = new Color(0.45f, 0.8f, 0.35f), belly = new Color(0.33f, 0.62f, 0.25f);
        GameObject model = CreatureModel.Build(root.transform, "GoblinModel", CreatureModel.GoblinRecipe(0.8f, skin, belly), 0.35f);

        MeshRenderer body = model.transform.Find("Body").GetComponent<MeshRenderer>();
        MeshRenderer head = model.transform.Find("Head").GetComponent<MeshRenderer>();
        MeshRenderer bellyPart = model.transform.Find("Belly").GetComponent<MeshRenderer>();
        Assert.AreSame(body.sharedMaterial, head.sharedMaterial, "Same color, same material");
        Assert.AreNotSame(body.sharedMaterial, bellyPart.sharedMaterial);
        Assert.AreSame(CreatureModel.MaterialFor(skin, 0.35f), body.sharedMaterial, "Keyed by color (and smoothness)");
        Assert.AreEqual(CreatureModel.LitShaderName, body.sharedMaterial.shader.name);
        Assert.AreEqual(0.35f, body.sharedMaterial.GetFloat("_Smoothness"), 1e-6f, "Toy sheen");

        foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>())
        {
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode, r.name);
            Assert.IsTrue(r.receiveShadows, r.name);
        }
    }

    [Test]
    public void Models_StandOnTheGroundPoint_AndKeepTheirHeightOrder()
    {
        root = new GameObject("Owners");
        root.transform.position = new Vector3(2.5f, 0f, -3.5f);
        Bounds imp = WorldBounds(CreatureModel.Build(root.transform, "Imp", CreatureModel.ImpRecipe(0.6f, Color.red, Color.black, Color.yellow, Color.gray), 0.35f));
        Bounds goblin = WorldBounds(CreatureModel.Build(root.transform, "Goblin", CreatureModel.GoblinRecipe(0.8f, Color.green, Color.gray), 0.35f));
        Bounds hero = WorldBounds(CreatureModel.Build(root.transform, "Hero", CreatureModel.HeroRecipe(0.85f, Color.white, Color.blue), 0.35f));

        foreach (Bounds b in new[] { imp, goblin, hero })
            Assert.AreEqual(0f, b.min.y, 0.02f, "Base at the ground point");

        // Bodies stand roughly at their serialized heights; horns/ears/crest may poke a little past.
        Assert.AreEqual(0.6f, imp.max.y, 0.1f);
        Assert.AreEqual(0.8f, goblin.max.y, 0.1f);
        Assert.AreEqual(0.85f, hero.max.y, 0.1f);
        Assert.Less(imp.max.y, goblin.max.y, "Imp smallest");
        Assert.Less(goblin.max.y, hero.max.y, "Hero tallest");
        Assert.Greater(goblin.size.x, hero.size.x * 0.9f, "Goblin wide: its ears");
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
