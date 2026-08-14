using UnityEngine;

/// <summary>
/// Keeps the camera above and behind a target at a fixed tilt, hole.io style.
/// </summary>
public class TopDownCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [Tooltip("Camera pitch in degrees. 90 = straight down; 50-60 gives the angled hole.io look.")]
    [Range(20f, 90f)][SerializeField] private float pitch = 55f;
    [Tooltip("Distance from the target along the tilted view direction.")]
    [SerializeField] private float distance = 22f;
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private Vector3 planarOffset = Vector3.zero;

    public Transform Target
    {
        get => target;
        set => target = value;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // Yaw stays at 0 so screen-up remains world +Z and the existing WASD/joystick mapping still reads naturally.
        Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
        Vector3 desiredPosition = target.position + planarOffset - (rotation * Vector3.forward) * distance;

        transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);
        transform.rotation = rotation;
    }
}
