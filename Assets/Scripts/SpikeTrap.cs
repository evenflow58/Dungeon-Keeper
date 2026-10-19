using UnityEngine;

/// <summary>
/// Spike Trap behavior: Armed or Spent. An armed trap fires once when a hero steps on its tile
/// (TryTrigger), dealing Damage and becoming Spent. A spent trap never re-fires until the imp rearms it.
/// Visual (ticket #77): a grid of steel cone spikes on the trap's plate, raised while Armed and retracted
/// nearly flat while Spent, switched instantly by every state change.
/// Powered rearm (#142): a built, spent trap on a powered grid rearms itself over poweredRearmSeconds, progress kept on
/// the trap. Losing power pauses the progress (kept) and hands the trap back to the imp's rearm pool; while powered it is
/// not an imp candidate. An imp already channelling a rearm when power arrives finishes it, and the self-rearm stays
/// paused meanwhile, so the two never advance the same trap together. With no power (no grid, no conduit), as before.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class SpikeTrap : MonoBehaviour
{
    [Header("Trap")]
    [SerializeField] private int damage = 1; // Provisional: hero HP lands with Epic #5
    [SerializeField] private float poweredRearmSeconds = 4f; // #142: self-rearm time on a powered grid (the power table's, set at placement)

    [Header("Spikes (code-built, ticket #77)")]
    [SerializeField] private Color spikeColor = new Color(0.85f, 0.87f, 0.90f, 1f); // Light steel
    [SerializeField] private int spikeGrid = 3;                                     // spikeGrid x spikeGrid cones
    [SerializeField] private float spikeHeight = 0.34f;                             // Tiles, when raised
    [SerializeField] private float spikeWidth = 0.14f;                              // Cone base diameter, tiles
    [SerializeField] private float retractedScale = 0.12f;                          // Spent: spikes squashed to this fraction, sunk into the plate
    [SerializeField] private float spikeSmoothness = 0.6f;                          // Polished steel
    [SerializeField] private float spikeSpread = 0.8f;                              // Grid span as a fraction of the plate

    public int Damage { get => damage; set => damage = value; }
    public float PoweredRearmSeconds { get => poweredRearmSeconds; set => poweredRearmSeconds = value; }

    /// <summary>Self-rearm progress 0–1 (#142). Kept through a brownout; reset when the trap re-arms by any means.</summary>
    public float PoweredRearmProgress { get; private set; }

    /// <summary>True while an imp is channelling a rearm on this trap (ImpDigger sets it): self-rearm pauses.</summary>
    public bool ImpRearming { get; set; }

    /// <summary>On a grid that covers its demand (#142): its PowerNode's IsPowered. False with no node.</summary>
    public bool IsPowered => TryGetComponent(out PowerNode node) && node.IsPowered;

    /// <summary>
    /// The self-rearm owns this trap (#142): built, spent and powered. Such a trap is not an imp rearm candidate.
    /// </summary>
    public bool SelfRearmOwns => IsBuilt && !IsArmed && IsPowered;

    public Vector2Int Tile => GetComponent<Placeable>().Tile;

    public bool IsArmed { get; private set; }

    /// <summary>
    /// False while a construction site (#91): unarmed, untriggerable, and not rearmable — and not a spent trap
    /// either, so rearm work must exclude it explicitly (ImpDigger.TrySelectWork does).
    /// </summary>
    public bool IsBuilt => GetComponent<Placeable>().IsBuilt;

    /// <summary>True while the spikes are raised (Armed); false while retracted into the plate (Spent).</summary>
    public bool SpikesVisible => spikes != null && spikesRaised;

    /// <summary>The spike grid's root: its Y scale is 1 when raised, retractedScale when spent.</summary>
    public Transform Spikes => spikes;

    private Transform spikes;
    private bool spikesRaised;
    private float spikeBaseY; // The plate's top, where raised spikes stand

    /// <summary>Called once by PlacementManager right after Placeable.Initialize.</summary>
    public void Initialize(int spikeDamage)
    {
        damage = spikeDamage;
        IsArmed = IsBuilt; // A site starts unarmed; OnConstructed arms it
        RefreshVisual();
    }

    /// <summary>Placeable.CompleteConstruction calls this: the finished trap starts Armed.</summary>
    public void OnConstructed()
    {
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

    /// <summary>Returns the trap to Armed. Idempotent. No-op on a site: only construction finishes a trap.</summary>
    public void Rearm()
    {
        if (!IsBuilt) return;
        IsArmed = true;
        PoweredRearmProgress = 0f;
        RefreshVisual();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances the powered self-rearm by deltaTime (game time): only for a built, spent trap that's powered and not being
    /// rearmed by an imp; reaching full calls Rearm. Called from Update; public so tests can step it.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsBuilt || IsArmed || ImpRearming || !IsPowered) return; // Paused, progress kept
        PoweredRearmProgress += poweredRearmSeconds > 0f ? deltaTime / poweredRearmSeconds : 1f;
        if (PoweredRearmProgress >= 1f - RearmCompleteTolerance) Rearm();
    }

    // Progress is a sum of deltaTime / seconds steps; absorb float rounding so a rearm lands on its full duration.
    private const float RearmCompleteTolerance = 1e-4f;

    private void RefreshVisual()
    {
        if (spikes == null) spikes = CreateSpikes();
        spikes.gameObject.SetActive(IsBuilt); // A site shows only its materials pile
        spikesRaised = IsArmed;
        spikes.localScale = new Vector3(1f, IsArmed ? 1f : retractedScale, 1f);
        // Spent: the squashed spikes also sink below the plate's top, so a spent trap reads as a bare plate.
        spikes.localPosition = new Vector3(0f, IsArmed ? spikeBaseY : spikeBaseY - spikeHeight * retractedScale - 0.002f, 0f);
    }

    // The grid stands on the plate's top surface, sized to the placeable's footprint.
    private Transform CreateSpikes()
    {
        Placeable placeable = GetComponent<Placeable>();
        float size = placeable != null && placeable.Size > 0f ? placeable.Size : 1f; // A full tile when uninitialized
        float plateTop = placeable != null ? placeable.SurfaceHeight : PropModel.TrapPlateHeight;

        var root = new GameObject("Spikes").transform;
        root.SetParent(transform, false);
        spikeBaseY = plateTop;
        root.localPosition = new Vector3(0f, plateTop, 0f);
        foreach (CreatureModel.Part part in PropModel.SpikeGridRecipe(size * spikeSpread, spikeGrid, spikeHeight, spikeWidth, spikeColor))
            CreatureModel.CreatePart(root, part, spikeSmoothness);
        return root;
    }
}
