using UnityEngine;

/// <summary>
/// Tags a star that was created at runtime by the chat/AI spawner, carrying the metadata the AI
/// decided on so BlackHoleController can award the right score when it gets swallowed. Absence of
/// this component just means "a level-authored star" - those award zero points, same as today.
/// </summary>
public class SpawnedStarInfo : MonoBehaviour
{
    public string ObjectType { get; private set; }
    public string ColorHex { get; private set; }
    public int PointValue { get; private set; }

    public void Initialize(string objectType, string colorHex, int pointValue)
    {
        ObjectType = objectType;
        ColorHex = colorHex;
        PointValue = pointValue;
    }
}
