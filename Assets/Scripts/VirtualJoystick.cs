using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Floating on-screen drag joystick: appears wherever the player first touches within its input
/// area, and eases back to its static home position when released. Exposes a static normalized
/// Direction that BlackHoleController blends with keyboard input each frame.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [Tooltip("Finger travel from the touch point, in canvas units, that counts as full tilt. Leave at 0 to use the background's radius, so the stick is exactly as sensitive as it looks.")]
    [SerializeField] private float handleRange = 0f;
    [Tooltip("Travel shorter than this fraction of the range reads as no input, so a resting thumb doesn't drift the hole. Past it the remaining travel is stretched back over the full 0-1 range.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float deadZone = 0.12f;
    [Tooltip("How long the joystick takes to ease back to its resting spot after release.")]
    [SerializeField] private float returnDuration = 0.15f;

    public static Vector2 Direction { get; private set; }

    private const int NoPointer = int.MinValue;

    private RectTransform _parent;
    private Vector2 _homeAnchoredPosition;
    private Coroutine _returnRoutine;
    private float _range;
    private float _handleTravel;

    // The stick belongs to the finger that pressed it: a second touch elsewhere on screen must not
    // steal it, and lifting that second finger must not zero the direction mid-drag.
    private int _activePointerId = NoPointer;

    private void Awake()
    {
        _parent = background.parent as RectTransform;
        _homeAnchoredPosition = background.anchoredPosition;

        float backgroundRadius = Mathf.Min(background.rect.width, background.rect.height) * 0.5f;
        _range = handleRange > 0f ? handleRange : backgroundRadius;

        // The knob is clamped to stay inside the ring, but the finger still gets the ring's full
        // radius to work with - otherwise the stick hits full speed well before it looks maxed out.
        float handleRadius = handle != null ? Mathf.Min(handle.rect.width, handle.rect.height) * 0.5f : 0f;
        _handleTravel = Mathf.Max(0f, backgroundRadius - handleRadius);

        Direction = Vector2.zero;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_activePointerId != NoPointer)
        {
            return;
        }

        _activePointerId = eventData.pointerId;

        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }

        MoveUnderPointer(eventData);
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _activePointerId)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            return;
        }

        Vector2 offset = Vector2.ClampMagnitude(localPoint, _range);
        if (handle != null)
        {
            handle.anchoredPosition = offset / _range * _handleTravel;
        }

        float magnitude = offset.magnitude / _range;
        if (magnitude <= deadZone)
        {
            Direction = Vector2.zero;
            return;
        }

        // Rescale past the dead zone so the first responsive notch of travel isn't a crawl and
        // full tilt still means full speed.
        Direction = offset.normalized * Mathf.InverseLerp(deadZone, 1f, magnitude);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _activePointerId)
        {
            return;
        }

        ReleaseStick();
    }

    private void Update()
    {
        // Pointer-up goes missing more often than it should: the finger slides off the edge of the
        // screen, the editor loses focus mid-drag, another system swallows the event. The stick
        // would then stay locked at whatever tilt it had and drive the hole around on its own, so
        // the device's own press state gets the final say.
        if (_activePointerId != NoPointer && CanTrustDeviceState() && !IsAnyPointerPressed())
        {
            ReleaseStick();
        }
    }

    private void ReleaseStick()
    {
        _activePointerId = NoPointer;
        Direction = Vector2.zero;
        if (handle != null)
        {
            handle.anchoredPosition = Vector2.zero;
        }

        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }

        if (isActiveAndEnabled)
        {
            _returnRoutine = StartCoroutine(ReturnHome());
        }
        else
        {
            background.anchoredPosition = _homeAnchoredPosition;
        }
    }

    /// <summary>Only second-guess the event system on platforms whose pointers we can actually read.</summary>
    private static bool CanTrustDeviceState()
    {
        return Mouse.current != null || Touchscreen.current != null || Pen.current != null;
    }

    private static bool IsAnyPointerPressed()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            return true;
        }

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed)
                {
                    return true;
                }
            }
        }

        Pen pen = Pen.current;
        return pen != null && pen.tip.isPressed;
    }

    private void OnDisable()
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }

        _activePointerId = NoPointer;
        Direction = Vector2.zero;
        if (handle != null)
        {
            handle.anchoredPosition = Vector2.zero;
        }
        if (background != null)
        {
            background.anchoredPosition = _homeAnchoredPosition;
        }
    }

    /// <summary>Re-centers the ring under the finger, so the stick always appears exactly where touched.</summary>
    private void MoveUnderPointer(PointerEventData eventData)
    {
        if (_parent == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            return;
        }

        // localPoint is measured from the parent's pivot, while anchoredPosition is measured from
        // the background's own anchor. They only coincide when the background is center-anchored,
        // so convert rather than assuming - otherwise the ring lands half a screen off.
        Rect parentRect = _parent.rect;
        Vector2 anchorOrigin = new Vector2(
            parentRect.x + parentRect.width * (background.anchorMin.x + background.anchorMax.x) * 0.5f,
            parentRect.y + parentRect.height * (background.anchorMin.y + background.anchorMax.y) * 0.5f);

        background.anchoredPosition = localPoint - anchorOrigin;
    }

    private IEnumerator ReturnHome()
    {
        Vector2 start = background.anchoredPosition;
        float elapsed = 0f;

        // Unscaled time, so the joystick still eases back even if the game is paused via timeScale.
        while (elapsed < returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            background.anchoredPosition = Vector2.Lerp(start, _homeAnchoredPosition, elapsed / returnDuration);
            yield return null;
        }

        background.anchoredPosition = _homeAnchoredPosition;
        _returnRoutine = null;
    }
}
