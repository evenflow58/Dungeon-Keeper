using UnityEngine;

/// <summary>How big and how dark a ground shadow disc is. Serialized on each owner so it stays tunable.</summary>
[System.Serializable]
public struct GroundShadowStyle
{
    public float Diameter; // In tiles
    [Range(0f, 1f)] public float Opacity;

    public GroundShadowStyle(float diameter, float opacity)
    {
        Diameter = diameter;
        Opacity = opacity;
    }
}

/// <summary>
/// A soft dark disc lying on the ground under a sprite body (ticket #75). Sprite shaders have no shadow-caster
/// pass, so sprite bodies can't cast real-time shadows under the 3D renderer; the disc anchors them to their tile
/// instead, until models (stories 3–4) bring real shadows. It lies flat on the XZ plane just above the ground
/// point, parented to the owner's root (not the raised body), so it moves and hides with the owner.
/// </summary>
public static class GroundShadow
{
    public const string ObjectName = "GroundShadow";
    private const int TextureSize = 32;

    private static Sprite discSprite;

    public static SpriteRenderer Create(Transform root, GroundShadowStyle style, float lift)
    {
        var go = new GameObject(ObjectName);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, lift, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Flat on the ground, facing up
        go.transform.localScale = new Vector3(style.Diameter, style.Diameter, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DiscSprite();
        sr.color = new Color(0f, 0f, 0f, style.Opacity);
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return sr;
    }

    // A white disc whose alpha falls off smoothly toward the rim (the renderer color supplies black + opacity).
    private static Sprite DiscSprite()
    {
        if (discSprite != null) return discSprite;

        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false) { name = "GroundShadow_Texture" };
        var pixels = new Color[TextureSize * TextureSize];
        float radius = TextureSize * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                float t = Mathf.Clamp01((r - 0.45f) / 0.55f); // Solid core to 45% of the radius, then a smooth fade
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, 1f - t * t * (3f - 2f * t));
            }
        }
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        discSprite = Sprite.Create(tex, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), TextureSize);
        discSprite.name = "GroundShadow_Sprite";
        return discSprite;
    }
}
