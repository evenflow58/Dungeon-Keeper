using UnityEngine;

/// <summary>
/// Spike Trap behavior: Armed or Spent. An armed trap fires once when a hero steps on its tile
/// (TryTrigger), dealing Damage and becoming Spent. A spent trap never re-fires until the imp rearms it.
/// Visual (ticket #77): a grid of steel cone spikes on the trap's plate, raised while Armed and retracted
/// nearly flat while Spent, switched instantly by every state change.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class SpikeTrap : MonoBehaviour
{
    [Header("Trap")]
    [SerializeField] private int damage = 1; // Provisional: hero HP lands with Epic #5

    [Header("Spikes (code-built, ticket #77)")]
    [SerializeField] private Color spikeColor = new Color(0.85f, 0.87f, 0.90f, 1f); // Light steel
    [SerializeField] private int spikeGrid = 3;                                     // spikeGrid x spikeGrid cones
    [SerializeField] private float spikeHeight = 0.3f;                              // Tiles, when raised
    [SerializeField] private float spikeWidth = 0.14f;                              // Cone base diameter, tiles
    [SerializeField] private float retractedScale = 0.12f;                          // Spent: spikes squashed to this fraction
    [SerializeField] private float spikeSmoothness = 0.6f;                          // Polished steel
    [SerializeField] private float spikeSpread = 0.8f;                              // Grid span as a fraction of the plate

    public int Damage { get => damage; set => damage = value; }

    public Vector2Int Tile => GetComponent<Placeable>().Tile;

    public bool IsArmed { get; private set; }

    /// <summary>True while the spikes are raised (Armed); false while retracted into the plate (Spent).</summary>
    public bool SpikesVisible => spikes != null && spikesRaised;

    /// <summary>The spike grid's root: its Y scale is 1 when raised, retractedScale when spent.</summary>
    public Transform Spikes => spikes;

    private Transform spikes;
    private bool spikesRaised;

    /// <summary>Called once by PlacementManager right after Placeable.Initialize.</summary>
    public void Initialize(int spikeDamage)
    {
        damage = spikeDamage;
        IsArmed = true;
        RefreshVisual();
    }

    /// <summary>
    /// The step-on seam a hero calls when it enters this tile. Armed: deals Damage, becomes Spent, returns true.
    /// Spent: deals nothing, returns false.
    /// </summary>
    public bool TryTrigger(out int damageDealt)
    {
        if (!IsArmed)
        {
            damageDealt = 0;
            return false;
        }

        damageDealt = damage;
        IsArmed = false;
        RefreshVisual();
        return true;
    }

    /// <summary>Returns the trap to Armed. Idempotent.</summary>
    public void Rearm()
    {
        IsArmed = true;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (spikes == null) spikes = CreateSpikes();
        spikesRaised = IsArmed;
        spikes.localScale = new Vector3(1f, IsArmed ? 1f : retractedScale, 1f);
    }

    // The grid stands on the plate's top surface, sized to the placeable's footprint.
    private Transform CreateSpikes()
    {
        Placeable placeable = GetComponent<Placeable>();
        float size = placeable != null && placeable.Size > 0f ? placeable.Size : 1f; // A full tile when uninitialized
        float plateTop = placeable != null ? placeable.SurfaceHeight : PropModel.TrapPlateHeight;

        var root = new GameObject("Spikes").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, plateTop, 0f);
        foreach (CreatureModel.Part part in PropModel.SpikeGridRecipe(size * spikeSpread, spikeGrid, spikeHeight, spikeWidth, spikeColor))
            CreatureModel.CreatePart(root, part, spikeSmoothness);
        return root;
    }
}
