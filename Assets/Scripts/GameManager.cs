using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Session Settings")]
    [SerializeField] private float timeLimit = 60f; // זמן מוגבל בסשן (שניות)

    private float _currentTime;
    private int _totalStars;
    private int _remainingStars;
    private int _score;
    private bool _isGameEnded = false;

    // Events שה-UI או מערכות אחרות יכולים להקשיב אליהם
    public event Action<float> OnTimeChanged;
    public event Action OnGameWon;
    public event Action OnGameOver;

    // remaining, eaten
    public event Action<int, int> OnStarsProgressChanged;
    public event Action<int> OnScoreChanged;

    public int Score => _score;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // ספירת כל הכוכבים בזירה בתחילת המשחק
        var starsList = GameObject.FindGameObjectsWithTag("Planet");
        _totalStars = starsList.Length;
        _remainingStars = _totalStars;

        _currentTime = timeLimit;
        _isGameEnded = false;

        OnStarsProgressChanged?.Invoke(_remainingStars, _totalStars - _remainingStars);
    }

    private void Update()
    {
        if (_isGameEnded) return;

        // ניהול הטיימר
        if (_currentTime > 0f)
        {
            _currentTime -= Time.deltaTime;
            OnTimeChanged?.Invoke(_currentTime);

            if (_currentTime <= 0f)
            {
                _currentTime = 0f;
                TriggerGameOver();
            }
        }
    }

    // פונקציה שהחור השחור יקרא לה ברגע שכוכב נבלע סופית
    public void RegisterStarConsumed(int points = 0)
    {
        if (_isGameEnded) return;

        _remainingStars--;
        OnStarsProgressChanged?.Invoke(_remainingStars, _totalStars - _remainingStars);

        if (points != 0)
        {
            _score += points;
            OnScoreChanged?.Invoke(_score);
        }

        // בדיקת תנאי ניצחון: האם כל הכוכבים נבלעו?
        if (_remainingStars <= 0)
        {
            TriggerGameWin();
        }
    }

    // כוכב שנוצר בזמן ריצה (למשל דרך הצ'אט) - מוסיף אותו לספירה הכוללת כדי שתנאי הניצחון יישאר נכון
    public void RegisterStarSpawned()
    {
        if (_isGameEnded) return;

        _totalStars++;
        _remainingStars++;
        OnStarsProgressChanged?.Invoke(_remainingStars, _totalStars - _remainingStars);
    }

    private void TriggerGameWin()
    {
        _isGameEnded = true;
        Debug.Log("ניצחת! כל הכוכבים נבלעו.");
        OnGameWon?.Invoke();
        // כאן אפשר לעצור את הזמן במשחק: Time.timeScale = 0f;
    }

    private void TriggerGameOver()
    {
        _isGameEnded = true;
        Debug.Log("נגמר הזמן! הפסדת.");
        OnGameOver?.Invoke();
        // Time.timeScale = 0f;
    }
}