using UnityEngine;

/// <summary>
/// Turns an AI-authored spawn decision into an actual star in the arena: a scaled, tinted sphere
/// placed directly at its resting height (every star in this level runs with gravity off, so
/// nothing here waits to fall) and tagged so BlackHoleController and GameManager treat it exactly
/// like a hand-placed one.
/// </summary>
public class SpawnerManager : MonoBehaviour
{
    [SerializeField] private GameObject basePlanetPrefab;
    [SerializeField] private Transform spawnAreaCenter;
    [Tooltip("Random spawn position is picked within this distance of spawnAreaCenter, on the XZ plane.")]
    [SerializeField] private float spawnAreaRadius = 12f;
    [Tooltip("A spawn point this close to the black hole or another star is rejected and re-rolled, so nothing appears already touching the hole or buried inside something else.")]
    [SerializeField] private float minClearance = 1.5f;
    [SerializeField] private int maxPlacementAttempts = 12;

    // Whatever the AI asks for still has to fit inside the size ladder the hole's calibration was
    // tuned against, or it just sits there uneatable forever - clamp defensively, since a model
    // won't always respect the range asked for in the prompt.
    [SerializeField] private float minScale = 0.3f;
    [SerializeField] private float maxScale = 3f;

    private BlackHoleController _hole;

    private void Awake()
    {
        _hole = FindFirstObjectByType<BlackHoleController>();
    }

    public GameObject SpawnCustomObject(string objectType, float scaleMultiplier, string colorHex, int pointValue)
    {
        if (basePlanetPrefab == null)
        {
            Debug.LogError("SpawnerManager: no basePlanetPrefab assigned.");
            return null;
        }

        float scale = Mathf.Clamp(scaleMultiplier, minScale, maxScale);
        Vector3 center = spawnAreaCenter != null ? spawnAreaCenter.position : Vector3.zero;
        Vector3 spawnPos = FindClearSpawnPosition(center, scale);

        GameObject newObj = Instantiate(basePlanetPrefab, spawnPos, Quaternion.identity);
        newObj.name = "Custom_" + (string.IsNullOrEmpty(objectType) ? "Star" : objectType);
        newObj.tag = "Planet";
        newObj.transform.localScale = Vector3.one * scale;

        if (ColorUtility.TryParseHtmlString(colorHex, out Color parsedColor))
        {
            Renderer rend = newObj.GetComponent<Renderer>();
            if (rend != null)
            {
                // First access instances the material, so this never recolors every other star
                // that happens to share the prefab's material asset.
                rend.material.color = parsedColor;
            }
        }

        Rigidbody rb = newObj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = newObj.AddComponent<Rigidbody>();
        }
        rb.useGravity = false; // matches every other star in the level - nothing here rests on a floor collider
        rb.mass = scale * 2f;

        if (newObj.GetComponent<Collider>() == null)
        {
            newObj.AddComponent<SphereCollider>();
        }

        SpawnedStarInfo info = newObj.AddComponent<SpawnedStarInfo>();
        info.Initialize(objectType, colorHex, pointValue);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterStarSpawned();
        }

        return newObj;
    }

    private Vector3 FindClearSpawnPosition(Vector3 center, float scale)
    {
        Vector3 best = center;
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * spawnAreaRadius;
            Vector3 candidate = new Vector3(center.x + offset.x, scale * 0.5f, center.z + offset.y);
            best = candidate;

            if (_hole != null)
            {
                float holeDistance = Vector2.Distance(
                    new Vector2(candidate.x, candidate.z),
                    new Vector2(_hole.transform.position.x, _hole.transform.position.z));
                if (holeDistance < _hole.EventHorizonRadius + scale * 0.5f + minClearance)
                {
                    continue; // would land already touching the event horizon
                }
            }

            if (!Physics.CheckSphere(candidate, scale * 0.5f + minClearance, ~0, QueryTriggerInteraction.Ignore))
            {
                return candidate;
            }
        }

        return best; // every attempt was crowded - drop it in the last rolled spot rather than never spawning
    }
}
