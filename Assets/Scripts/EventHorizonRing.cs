using System.Collections;
using UnityEngine;

/// <summary>
/// Draws the "point of no return" boundary at the black hole's event horizon and
/// periodically fires fast-rising energy pulses off that ring for visual flair.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class EventHorizonRing : MonoBehaviour
{
    [SerializeField] private BlackHoleController blackHole;
    [SerializeField] private int segments = 64;
    [SerializeField] private float ringHeight = 0.1f;
    [SerializeField] private float ringWidth = 0.15f;
    [SerializeField] private Color ringColor = new Color(1f, 0.15f, 0.05f, 1f);

    [Header("Rising Pulse Effect")]
    [SerializeField] private float pulseInterval = 0.8f;
    [SerializeField] private float pulseDuration = 0.5f;
    [SerializeField] private float pulseRiseHeight = 4f;
    [Tooltip("Exponent < 1 makes the pulse shoot up fast at the start, then ease off.")]
    [SerializeField] private float pulseRiseCurveExponent = 0.4f;

    private LineRenderer _lineRenderer;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        ConfigureLineRenderer(_lineRenderer, ringWidth, ringColor);
    }

    private void Start()
    {
        StartCoroutine(PulseLoop());
    }

    private void Update()
    {
        if (blackHole == null)
        {
            return;
        }
        DrawCircle(_lineRenderer, blackHole.EventHorizonRadius, ringHeight);
    }

    private void DrawCircle(LineRenderer lr, float radius, float height)
    {
        lr.positionCount = segments;
        lr.loop = true;
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius));
        }
    }

    private void ConfigureLineRenderer(LineRenderer lr, float width, Color color)
    {
        lr.useWorldSpace = false;
        lr.widthMultiplier = width;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        lr.numCapVertices = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private IEnumerator PulseLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(pulseInterval);
        while (true)
        {
            yield return wait;
            if (blackHole != null)
            {
                SpawnPulse(blackHole.EventHorizonRadius);
            }
        }
    }

    private void SpawnPulse(float baseRadius)
    {
        GameObject pulseGO = new GameObject("EventHorizonPulse");
        pulseGO.transform.SetParent(transform, false);
        LineRenderer pulseLine = pulseGO.AddComponent<LineRenderer>();
        ConfigureLineRenderer(pulseLine, ringWidth * 0.8f, ringColor);
        StartCoroutine(AnimatePulse(pulseGO, pulseLine, baseRadius));
    }

    private IEnumerator AnimatePulse(GameObject pulseGO, LineRenderer pulseLine, float baseRadius)
    {
        float elapsed = 0f;
        while (elapsed < pulseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / pulseDuration);
            float fastRise = Mathf.Pow(t, pulseRiseCurveExponent);
            float height = Mathf.Lerp(ringHeight, pulseRiseHeight, fastRise);
            float radius = Mathf.Lerp(baseRadius, baseRadius * 1.15f, t);
            DrawCircle(pulseLine, radius, height);

            float alpha = 1f - t;
            Color faded = new Color(ringColor.r, ringColor.g, ringColor.b, alpha);
            pulseLine.startColor = faded;
            pulseLine.endColor = faded;

            yield return null;
        }
        Destroy(pulseGO);
    }
}
