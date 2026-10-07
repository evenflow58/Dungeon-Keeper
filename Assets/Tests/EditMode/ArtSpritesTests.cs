using NUnit.Framework;
using UnityEngine;

public class ArtSpritesTests
{
    private const string MissingPath = "Art/Characters/__no_such_sprite__";

    private GameObject managerGo;
    private GameObject goblinGo;
    private Goblin goblin;

    [SetUp]
    public void SetUp()
    {
        // Bare goblin, staged like GoblinTests (Start() doesn't run in EditMode).
        managerGo = new GameObject("TestDungeonManager");
        var board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;

        goblinGo = new GameObject("TestGoblin");
        goblin = goblinGo.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.PlaceOnTile(new Vector2Int(23, 15));
    }

    [TearDown]
    public void TearDown()
    {
        if (goblinGo != null) Object.DestroyImmediate(goblinGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    [Test]
    public void Load_MissingAsset_ReturnsNullWithoutThrowing()
    {
        Sprite sprite = null;
        Assert.DoesNotThrow(() => sprite = ArtSprites.Load(MissingPath));
        Assert.IsNull(sprite);
        Assert.IsNull(ArtSprites.Load(null));
        Assert.IsNull(ArtSprites.Load(""));
    }

    [Test]
    public void Goblin_ArtAbsent_FallsBackToTheRoundToken()
    {
        goblin.ArtSpritePath = MissingPath;

        goblin.CreateBody();

        Assert.IsNotNull(goblin.Body, "Body built");
        Assert.IsNotNull(goblin.Body.sprite, "Fallback sprite assigned");
        Assert.AreEqual("Goblin_Sprite", goblin.Body.sprite.name, "The code-created token");
        Assert.AreEqual(new Vector3(0.75f, 0.75f, 1f), goblin.Body.transform.localScale, "Token sizing unchanged");
        Assert.AreEqual(2, goblin.Body.sortingOrder);
    }

    [Test]
    public void ScaleForHeight_GoblinMaster_IsAboutAThirteenthAndKeepsAspect()
    {
        // 980x1488 master at 100 px/unit is 9.80 x 14.88 units; shown 1.15 tiles tall.
        float scale = ArtSprites.ScaleForHeight(14.88f, 1.15f);

        Assert.AreEqual(1.15f / 14.88f, scale, 1e-6f);
        Assert.AreEqual(1.15f, 14.88f * scale, 1e-5f, "Height in tiles");
        Assert.AreEqual(0.757f, 9.80f * scale, 1e-3f, "Width follows the ~0.66 aspect");
        Assert.AreEqual(1f, ArtSprites.ScaleForHeight(0f, 1.15f), "Degenerate height leaves scale alone");
    }

    [Test]
    public void Goblin_DefaultsPointAtTheGoblinArt()
    {
        Assert.AreEqual("Art/Characters/goblin", goblin.ArtSpritePath);
        Assert.AreEqual(1.15f, goblin.SpriteHeightTiles);
    }

    [Test]
    public void Goblin_ArtPresent_UsesItFeetDownAtTheConfiguredHeight()
    {
        Sprite art = ArtSprites.Load(ArtSprites.GoblinPath);
        if (art == null) Assert.Ignore("Goblin art not in the project; the fallback test covers this case.");

        goblin.CreateBody();

        Assert.AreSame(art, goblin.Body.sprite);
        Assert.AreEqual(0f, art.pivot.y, 1e-3f, "Pivot at the feet (bottom)");
        Assert.AreEqual(art.rect.width * 0.5f, art.pivot.x, 1e-3f, "Pivot horizontally centered");
        Assert.AreEqual(1.15f, goblin.Body.bounds.size.y, 1e-3f, "Shown 1.15 tiles tall");
        Assert.AreEqual(goblin.transform.position.y, goblin.Body.bounds.min.y, 1e-3f, "Feet on the goblin's ground point");
        Assert.AreEqual(2, goblin.Body.sortingOrder);
    }
}
