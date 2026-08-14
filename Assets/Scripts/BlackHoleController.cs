using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls a player-driven black hole: WASD movement on the XZ plane,
/// gravitational attraction of tagged "Planet" rigidbodies, and consumption-based growth.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BlackHoleController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float moveAcceleration = 15f;

    [Header("Gravity Well")]
    [Tooltip("Radius within which planets are affected by the black hole's gravity.")]
    [SerializeField] private float pullRadius = 5f;
    [Tooltip("Base strength of the gravitational pull.")]
    [SerializeField] private float gravityStrength = 25f;
    [Tooltip("Higher values make the pull increase more sharply as objects approach the center.")]
    [SerializeField] private float gravityExponent = 2f;
    [Tooltip("Minimum distance used in the falloff calculation, to avoid division blow-ups at the center.")]
    [SerializeField] private float minFalloffDistance = 0.5f;
    [SerializeField] private LayerMask planetLayerMask = ~0;

    [Header("Velocity Control")]
    [Tooltip("Drag applied to planets while inside the pull radius, to prevent them overshooting the center.")]
    [SerializeField] private float capturedDrag = 2f;
    [Tooltip("Max speed allowed for a planet as it nears the event horizon.")]
    [SerializeField] private float maxApproachSpeed = 12f;

    [Header("Consumption & Growth")]
    [Tooltip("Radius of the event horizon: the point of no return. Drives both gameplay and the flat disc's visual size.")]
    [SerializeField] private float eventHorizonRadius = 1f;
    [SerializeField] private float eventHorizonGrowthPerConsume = 0.06f;
    [SerializeField] private float pullRadiusGrowthPerConsume = 0.15f;
    [SerializeField] private string planetTag = "Planet";

    [Header("Growth Calibration")]
    [Tooltip("On startup, measure every star in the level and derive the growth per star so the hole ends up exactly big enough for the largest one. Overrides the growth values above.")]
    [SerializeField] private bool autoCalibrateGrowth = true;
    [Tooltip("Also shrink the starting hole to fit the smallest star, so there is room to grow. Without this the hole may already be bigger than most stars at the start.")]
    [SerializeField] private bool calibrateStartRadius = true;
    [Tooltip("Slack on the calibrated start and end sizes, so a star of exactly that width still fits.")]
    [SerializeField] private float calibrationMargin = 1.05f;
    [Tooltip("Extra slack on the starting hole only, on top of the margin above. At 1 the hole starts barely wider than the smallest star, which reads as tiny; above 1 it starts with some presence and simply grows in smaller steps to still finish at the largest star.")]
    [SerializeField] private float startRadiusMargin = 1.6f;
    [Tooltip("How much of the level may still be uneaten when the hole first becomes wide enough for the largest star. At 0 the biggest star only opens up once every single other star is gone - drive into it before that and nothing happens at all, which reads as broken. At 0.3 it opens up with roughly a third of the stars to spare.")]
    [Range(0f, 0.9f)]
    [SerializeField] private float growthReserve = 0.3f;
    [Tooltip("Leave stars wider than the hole alone - no pull, no swallow - until it has grown enough for them.")]
    [SerializeField] private bool onlySwallowSmallerStars = true;

    [Header("Hole.io Style Visual")]
    [Tooltip("The pit interior under the hole (a cylinder using the HolePit material). Scaled to match the hole.")]
    [SerializeField] private Transform pitVisual;
    [Tooltip("How deep the pit under the floor is, in world units.")]
    [SerializeField] private float holeDepth = 8f;
    [Tooltip("Pit radius as a multiple of the hole radius. A star can be almost as wide as the hole and can start falling right at the rim, so the cutout can briefly reach close to 2x the hole radius - this needs enough headroom to stay outside that, or the tube wall clips through the star.")]
    [SerializeField] private float pitRadiusMultiplier = 2.3f;
    [Tooltip("How quickly a falling star slides towards the middle of the pit, in units per second.")]
    [SerializeField] private float consumeCenteringSpeed = 4f;
    [Tooltip("How far a consumed star drops before it's destroyed. Keep this at or below holeDepth so it vanishes out of sight.")]
    [SerializeField] private float consumeFallDepth = 7f;
    [Tooltip("Downward acceleration applied to a star once it crosses the event horizon.")]
    [SerializeField] private float consumeFallGravity = 25f;
    [SerializeField] private Color consumeBurstColor = new Color(1f, 0.55f, 0.15f);
    [SerializeField] private int consumeBurstParticleCount = 14;

    private static readonly int HoleCenterId = Shader.PropertyToID("_HoleCenter");
    private static readonly int HoleRadiusId = Shader.PropertyToID("_HoleRadius");



    private Rigidbody _rigidbody;
    private Vector3 _inputDirection;
    private Vector3 _currentVelocity;
    private readonly Collider[] _overlapBuffer = new Collider[64];

    // Measured once at startup: how wide each star is, so the size gate costs nothing per frame.
    private readonly Dictionary<Collider, float> _starRadii = new Dictionary<Collider, float>();

    // Stars currently mid-fall. Tracked so the floor's cutout can widen to cover whatever is
    // actually falling right now, instead of clipping a wide star against the solid floor near the rim.
    private readonly List<FallingStar> _fallingStars = new List<FallingStar>();

    private struct FallingStar
    {
        public Transform Star;
        public float Radius;
    }

    public float EventHorizonRadius => eventHorizonRadius;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.useGravity = false;
        _rigidbody.freezeRotation = true;

        // Physics ticks at 50 Hz while a phone renders at 60-120, so without interpolation the
        // hole visibly steps forward in bursts once it is moving fast.
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        // The hole is now a real gap in the floor, not a scaled disc, so the root stays at
        // unit scale — otherwise it would also distort the event horizon ring and the pit.
        transform.localScale = Vector3.one;
        UpdateHoleVisual();

        // The hole's own collider must never physically block stars — consumption is
        // purely distance-based (see ApplyGravityWell). A solid collider here would
        // bounce stars away before they ever cross the event horizon.
        Collider ownCollider = GetComponent<Collider>();
        if (ownCollider != null)
        {
            ownCollider.isTrigger = true;
        }

    }

    private void Start()
    {
        MeasureStars();

        if (autoCalibrateGrowth)
        {
            CalibrateGrowth();
        }

        EnsureProgressPossible();
        UpdateHoleVisual();
    }

    /// <summary>Records the width of every star in the level, once, before anything is eaten.</summary>
    private void MeasureStars()
    {
        _starRadii.Clear();

        foreach (GameObject star in GameObject.FindGameObjectsWithTag(planetTag))
        {
            Collider starCollider = star.GetComponent<Collider>();
            if (starCollider == null)
            {
                continue;
            }

            // World-space bounds, so the star's scale is already baked in.
            Vector3 extents = starCollider.bounds.extents;
            _starRadii[starCollider] = Mathf.Max(extents.x, extents.z);
        }
    }

    /// <summary>
    /// Sizes the hole's progression to the level: it starts just big enough for the smallest star,
    /// and every star - including the biggest - becomes edible with stars still left over, so the
    /// player is never left shoving at something that refuses to react.
    /// </summary>
    private void CalibrateGrowth()
    {
        if (_starRadii.Count == 0)
        {
            Debug.LogWarning($"BlackHoleController: no '{planetTag}' stars found, keeping the growth values from the inspector.");
            return;
        }

        List<float> sorted = new List<float>(_starRadii.Values);
        sorted.Sort();
        float smallest = sorted[0];
        float largest = sorted[sorted.Count - 1];

        // Preserve the designed relationship between the gravity well and the hole, so the well
        // scales along with it instead of staying huge around a tiny starting hole.
        float pullToHoleRatio = pullRadius / Mathf.Max(eventHorizonRadius, 0.0001f);

        if (calibrateStartRadius)
        {
            // The start margin is deliberately generous so the hole doesn't read as a speck, but on
            // a level where every star is a similar size it could cover everything immediately -
            // clamp it so there is always something left to grow into.
            eventHorizonRadius = Mathf.Min(smallest * calibrationMargin * Mathf.Max(1f, startRadiusMargin),
                                           largest * calibrationMargin);
        }

        // The i-th smallest star has i smaller stars to feed on before it, so the growth step has
        // to be at least (what it needs) / (how many meals come first). The reserve shortens that
        // budget on purpose: the star opens up a few consumes early instead of exactly on the last
        // one, which is what stops the biggest star from being gated behind hunting every speck.
        // Whichever star is hungriest sets the step for all of them.
        float growthPerConsume = 0f;
        for (int i = 1; i < sorted.Count; i++)
        {
            float needed = sorted[i] * calibrationMargin - eventHorizonRadius;
            if (needed <= 0f)
            {
                continue;
            }

            int mealsAvailable = Mathf.Max(1, Mathf.FloorToInt(i * (1f - growthReserve)));
            growthPerConsume = Mathf.Max(growthPerConsume, needed / mealsAvailable);
        }

        eventHorizonGrowthPerConsume = growthPerConsume;
        pullRadius = eventHorizonRadius * pullToHoleRatio;
        pullRadiusGrowthPerConsume = growthPerConsume * pullToHoleRatio;

        int mealsUntilLargest = growthPerConsume > 0f
            ? Mathf.CeilToInt((largest * calibrationMargin - eventHorizonRadius) / growthPerConsume)
            : 0;

        Debug.Log($"BlackHoleController calibrated for {_starRadii.Count} stars: " +
                  $"radius {eventHorizonRadius:0.000} (+{growthPerConsume:0.000} per star), " +
                  $"smallest star {smallest:0.000}, largest {largest:0.000}, " +
                  $"largest becomes edible after {mealsUntilLargest} of {_starRadii.Count - 1} other stars.");
    }

    /// <summary>
    /// Last-resort guard against a stalemate: if every star still standing is too wide for the
    /// hole, nothing can ever be eaten again and the level is unwinnable. Calibration is supposed
    /// to make this impossible - this exists so a hand-tuned level can't soft-lock.
    /// </summary>
    private void EnsureProgressPossible()
    {
        if (!onlySwallowSmallerStars || _starRadii.Count == 0)
        {
            return;
        }

        float smallestRemaining = float.MaxValue;
        foreach (float radius in _starRadii.Values)
        {
            smallestRemaining = Mathf.Min(smallestRemaining, radius);
        }

        if (smallestRemaining <= eventHorizonRadius)
        {
            return;
        }

        float rescuedRadius = smallestRemaining * calibrationMargin;
        float pullToHoleRatio = pullRadius / Mathf.Max(eventHorizonRadius, 0.0001f);

        Debug.LogWarning($"BlackHoleController: every remaining star is wider than the hole " +
                         $"({eventHorizonRadius:0.000}), so nothing could ever be swallowed again. " +
                         $"Growing to {rescuedRadius:0.000} to fit the smallest one.");

        eventHorizonRadius = rescuedRadius;
        pullRadius = eventHorizonRadius * pullToHoleRatio;
    }

    private void Update()
    {
        HandleMovementInput();
    }

    private void HandleMovementInput()
    {
        float horizontal = 0f;
        float vertical = 0f;

        // The virtual joystick takes priority over keyboard input while it's being dragged.
        Vector2 joystickInput = VirtualJoystick.Direction;
        if (joystickInput.sqrMagnitude > 0.0001f)
        {
            horizontal = joystickInput.x;
            vertical = joystickInput.y;
        }
        else
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) vertical -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) vertical += 1f;
            }
        }

        Vector3 inputDirection = new Vector3(horizontal, 0f, vertical);
        if (inputDirection.sqrMagnitude > 1f)
        {
            inputDirection.Normalize();
        }

        // Only sample input here. Acceleration happens on the physics tick, so a frame rate that
        // differs from the fixed timestep can't make the hole surge.
        _inputDirection = inputDirection;
    }

    private void LateUpdate()
    {
        // The hole moves every frame, so the floor shader needs its position after movement.
        PushHoleToShaders();
    }

    private void FixedUpdate()
    {
        ApplyMovement();
        ApplyGravityWell();
    }

    private void ApplyMovement()
    {
        Vector3 targetVelocity = _inputDirection * moveSpeed;
        _currentVelocity = Vector3.MoveTowards(_currentVelocity, targetVelocity, moveAcceleration * Time.fixedDeltaTime);

        Vector3 newPosition = _rigidbody.position + _currentVelocity * Time.fixedDeltaTime;
        newPosition.y = _rigidbody.position.y; // stay locked to the XZ plane
        _rigidbody.MovePosition(newPosition);
    }

    private void ApplyGravityWell()
    {
        Vector3 center = _rigidbody.position;
        int hitCount = Physics.OverlapSphereNonAlloc(center, pullRadius, _overlapBuffer, planetLayerMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _overlapBuffer[i];
            if (hit == null || !hit.CompareTag(planetTag))
            {
                continue;
            }

            Rigidbody planetBody = hit.attachedRigidbody;
            if (planetBody == null)
            {
                continue;
            }

            // Wider than the hole: leave it entirely alone - not even pulled - until the hole has
            // grown into it. Otherwise a star too big to swallow would park itself on the mouth.
            if (onlySwallowSmallerStars && StarRadius(hit) > eventHorizonRadius)
            {
                continue;
            }

            // The hole is a gap in the floor, so everything about the well is planar. Including
            // the height difference here would tilt the pull downwards and drag stars under the
            // floor on their way in.
            Vector3 toCenter = center - planetBody.position;
            toCenter.y = 0f;
            float distance = toCenter.magnitude;

            // Consume the planet once it crosses the event horizon.
            if (distance <= eventHorizonRadius)
            {
                Consume(planetBody.gameObject);
                continue;
            }

            AttractPlanet(planetBody, toCenter, distance);
        }
    }

    private float StarRadius(Collider starCollider)
    {
        if (_starRadii.TryGetValue(starCollider, out float radius))
        {
            return radius;
        }

        // Spawned after startup, so it missed the initial sweep: measure and remember it now.
        Vector3 extents = starCollider.bounds.extents;
        radius = Mathf.Max(extents.x, extents.z);
        _starRadii[starCollider] = radius;
        return radius;
    }

    private void AttractPlanet(Rigidbody planetBody, Vector3 toCenter, float distance)
    {
        // Pin the star to its resting height: nothing about being pulled should move it
        // vertically, and star-on-star shoves shouldn't push it through the floor either.
        planetBody.constraints |= RigidbodyConstraints.FreezePositionY;

        Vector3 direction = toCenter / distance;

        // Exponential falloff: force grows sharply as the planet nears the center.
        float falloffDistance = Mathf.Max(distance, minFalloffDistance);
        float forceMagnitude = gravityStrength / Mathf.Pow(falloffDistance, gravityExponent);

        planetBody.AddForce(direction * forceMagnitude, ForceMode.Acceleration);

        // Increase drag near the black hole so planets don't slingshot past the center at high velocity.
        float proximityFactor = 1f - Mathf.Clamp01(distance / pullRadius);
        planetBody.linearDamping = Mathf.Lerp(0f, capturedDrag, proximityFactor);

        // Clamp velocity as the planet approaches the event horizon to prevent tunnelling.
        float speedLimit = Mathf.Lerp(maxApproachSpeed, maxApproachSpeed * 0.15f, proximityFactor);
        if (planetBody.linearVelocity.sqrMagnitude > speedLimit * speedLimit)
        {
            planetBody.linearVelocity = planetBody.linearVelocity.normalized * speedLimit;
        }
    }

    private void Consume(GameObject planet)
    {
        // Stop physics immediately so this star is skipped by future OverlapSphere queries this frame.
        Rigidbody planetBody = planet.GetComponent<Rigidbody>();
        if (planetBody != null)
        {
            planetBody.linearVelocity = Vector3.zero;
            planetBody.isKinematic = true;
        }

        Collider planetCollider = planet.GetComponent<Collider>();
        if (planetCollider != null)
        {
            planetCollider.enabled = false;
        }

        float starRadius = planetCollider != null ? StarRadius(planetCollider) : eventHorizonRadius;

        // Off the books before growing, so the stalemate check only weighs stars still in play.
        if (planetCollider != null)
        {
            _starRadii.Remove(planetCollider);
        }

        Grow();
        SpawnConsumeBurst(planet.transform.position);
        StartCoroutine(FallIntoHoleRoutine(planet.transform, starRadius));
    }

    private IEnumerator FallIntoHoleRoutine(Transform star, float starRadius)
    {
        FallingStar entry = new FallingStar { Star = star, Radius = starRadius };
        _fallingStars.Add(entry);

        try
        {
            // A plain drop: the star falls through the gap in the floor, accelerating as it goes.
            // No spin, no stretch — it keeps the orientation and size it had on the surface.
            float fallSpeed = 0f;
            float depth = 0f;

            while (depth < consumeFallDepth)
            {
                fallSpeed += consumeFallGravity * Time.deltaTime;
                float step = fallSpeed * Time.deltaTime;
                depth += step;

                // Straight down, plus a gentle slide towards the middle of the tube, so a star that
                // went in at the rim doesn't graze the pit wall on the way down.
                Vector3 position = star.position + Vector3.down * step;
                Vector3 holeCenter = transform.position;
                Vector2 flat = Vector2.MoveTowards(
                    new Vector2(position.x, position.z),
                    new Vector2(holeCenter.x, holeCenter.z),
                    consumeCenteringSpeed * Time.deltaTime);

                star.position = new Vector3(flat.x, position.y, flat.y);
                yield return null;
            }
        }
        finally
        {
            _fallingStars.Remove(entry);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterStarConsumed();
        }

        if (SoundEffectPlayer.Instance != null)
        {
            SoundEffectPlayer.Instance.Play();
        }

        Destroy(star.gameObject);
    }

    private void SpawnConsumeBurst(Vector3 position)
    {
        GameObject burstGO = new GameObject("ConsumeBurst");
        burstGO.transform.position = position;

        ParticleSystem ps = burstGO.AddComponent<ParticleSystem>();

        // A fresh system plays on awake, and Unity refuses to retune duration mid-playback.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = 0.3f;
        main.startSpeed = 5f;
        main.startSize = 0.12f;
        main.startColor = consumeBurstColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)consumeBurstParticleCount) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        ParticleSystemRenderer psRenderer = burstGO.GetComponent<ParticleSystemRenderer>();
        psRenderer.material = new Material(Shader.Find("Sprites/Default"));

        ps.Play();
        Destroy(burstGO, main.duration + main.startLifetime.constantMax + 0.1f);
    }

    private void Grow()
    {
        eventHorizonRadius += eventHorizonGrowthPerConsume;
        pullRadius += pullRadiusGrowthPerConsume;
        EnsureProgressPossible();
        UpdateHoleVisual();
    }

    /// <summary>
    /// Resizes the pit to match the event horizon. The hole in the floor itself is cut by
    /// HoleFloor.shader, which reads the globals pushed in <see cref="PushHoleToShaders"/>.
    /// </summary>
    private void UpdateHoleVisual()
    {
        if (pitVisual != null)
        {
            // Wider than the cut in the floor, so a star falling in at the rim stays inside the
            // tube instead of being sliced by the far wall. The floor hides the extra width.
            float diameter = eventHorizonRadius * pitRadiusMultiplier * 2f;

            // The built-in cylinder is 2 units tall, so half the depth gives the right Y scale.
            pitVisual.localScale = new Vector3(diameter, holeDepth * 0.5f, diameter);

            // Drop the mouth a hair below the floor so the rim never fights it for depth.
            pitVisual.localPosition = new Vector3(0f, -holeDepth * 0.5f - 0.02f, 0f);
        }

        PushHoleToShaders();
    }

    private void PushHoleToShaders()
    {
        Vector3 center = transform.position;
        Shader.SetGlobalVector(HoleCenterId, new Vector4(center.x, center.y, center.z, 0f));
        Shader.SetGlobalFloat(HoleRadiusId, eventHorizonRadius);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UpdateHoleVisual();
    }

    // In edit mode LateUpdate never runs, so keep the scene-view preview of the cut in sync
    // while the hole is dragged around.
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            PushHoleToShaders();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, pullRadius);

        Gizmos.color = Color.black;
        Gizmos.DrawWireSphere(transform.position, eventHorizonRadius);
    }
#endif
}
