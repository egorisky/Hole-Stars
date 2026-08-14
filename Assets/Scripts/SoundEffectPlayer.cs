using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pooled one-shot sound effect player. Keeps a set of AudioSources alive and reuses
/// whichever one is idle, so rapid-fire effects never allocate an AudioSource at runtime.
/// </summary>
public class SoundEffectPlayer : MonoBehaviour
{
    public static SoundEffectPlayer Instance { get; private set; }

    [Header("Clips")]
    [Tooltip("Clips from the SoundEffect folder. One is picked at random each time the sound plays.")]
    [SerializeField] private AudioClip[] clips;

    [Header("Pool")]
    [SerializeField] private int poolSize = 8;
    [Tooltip("Allow the pool to grow past its initial size when every source is already busy.")]
    [SerializeField] private bool allowPoolGrowth = true;

    [Header("Playback")]
    [Range(0f, 1f)][SerializeField] private float volume = 1f;
    [Tooltip("Random pitch range, so repeated plays don't sound mechanically identical.")]
    [SerializeField] private float minPitch = 0.94f;
    [SerializeField] private float maxPitch = 1.06f;
    [Tooltip("Requests arriving within this many seconds of the previous one are ignored, so several stars consumed on the same frame don't stack into a distorted blast.")]
    [SerializeField] private float minRetriggerInterval = 0.04f;

    private readonly List<AudioSource> _pool = new List<AudioSource>();
    private int _nextIndex;
    private float _lastPlayTime = float.NegativeInfinity;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        for (int i = 0; i < poolSize; i++)
        {
            CreateSource();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Plays a random clip from the assigned clips.</summary>
    public void Play()
    {
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        AudioClip clip = clips.Length == 1 ? clips[0] : clips[Random.Range(0, clips.Length)];
        Play(clip);
    }

    /// <summary>Plays a specific clip through the pool.</summary>
    public void Play(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        // Unscaled time, so throttling still behaves if the game pauses via timeScale.
        if (Time.unscaledTime - _lastPlayTime < minRetriggerInterval)
        {
            return;
        }

        AudioSource source = GetAvailableSource();
        if (source == null)
        {
            return;
        }

        _lastPlayTime = Time.unscaledTime;

        source.clip = clip;
        source.volume = volume;
        source.pitch = Random.Range(minPitch, maxPitch);
        source.Play();
    }

    private AudioSource GetAvailableSource()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            AudioSource candidate = _pool[i];
            if (candidate != null && !candidate.isPlaying)
            {
                return candidate;
            }
        }

        if (allowPoolGrowth)
        {
            return CreateSource();
        }

        if (_pool.Count == 0)
        {
            return null;
        }

        // Every source is busy and growth is disabled: steal the least recently started one.
        AudioSource stolen = _pool[_nextIndex];
        _nextIndex = (_nextIndex + 1) % _pool.Count;
        return stolen;
    }

    private AudioSource CreateSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f; // 2D: the hole is always on screen, no need for positional audio.
        _pool.Add(source);
        return source;
    }
}