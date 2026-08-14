using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Floating on-screen drag joystick: appears wherever the player first touches within its input
/// area, and eases back to its static home position when released. Exposes a static normalized
/// Direction that BlackHoleController blends with keyboard input each frame.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private float handleRange = 80f;
    [Tooltip("How long the joystick takes to ease back to its resting spot after release.")]
    [SerializeField] private float returnDuration = 0.15f;

    public static Vector2 Direction { get; private set; }

    private RectTransform _parent;
    private Vector2 _homeAnchoredPosition;
    private Coroutine _returnRoutine;

    private void Awake()
    {
        _parent = background.parent as RectTransform;
        _homeAnchoredPosition = background.anchoredPosition;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }

        // Re-center the joystick under the finger, so it always appears exactly where touched.
        if (_parent != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            background.anchoredPosition = localPoint;
        }

        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            Vector2 clamped = Vector2.ClampMagnitude(localPoint, handleRange);
            handle.anchoredPosition = clamped;
            Direction = clamped / handleRange;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Direction = Vector2.zero;
        handle.anchoredPosition = Vector2.zero;

        if (isActiveAndEnabled)
        {
            _returnRoutine = StartCoroutine(ReturnHome());
        }
        else
        {
            background.anchoredPosition = _homeAnchoredPosition;
        }
    }

    private void OnDisable()
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }

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
