using UnityEngine;
using TMPro; // במידה ואתה משתמש ב-TextMeshPro

public class UIManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI timerText; // או UnityEngine.UI.Text רגיל
    [SerializeField] private TextMeshProUGUI starsLeftText;
    [SerializeField] private TextMeshProUGUI candiesEatenText;

    private void Start()
    {
        // וידוא שהפאנלים סגורים בתחילת המשחק
        if (winPanel) winPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTimeChanged += UpdateTimerDisplay;
            GameManager.Instance.OnGameWon += ShowWinScreen;
            GameManager.Instance.OnGameOver += ShowGameOverScreen;
            GameManager.Instance.OnStarsProgressChanged += UpdateStarsProgressDisplay;
        }
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTimeChanged -= UpdateTimerDisplay;
            GameManager.Instance.OnGameWon -= ShowWinScreen;
            GameManager.Instance.OnGameOver -= ShowGameOverScreen;
            GameManager.Instance.OnStarsProgressChanged -= UpdateStarsProgressDisplay;
        }
    }

    private void UpdateTimerDisplay(float timeRemaining)
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(timeRemaining);
            timerText.text = seconds.ToString();
        }
    }

    private void UpdateStarsProgressDisplay(int remaining, int eaten)
    {
        if (starsLeftText != null)
        {
            starsLeftText.text = remaining.ToString();
        }
        if (candiesEatenText != null)
        {
            candiesEatenText.text = eaten.ToString();
        }
    }

    private void ShowWinScreen()
    {
        if (winPanel != null) winPanel.SetActive(true);
    }

    private void ShowGameOverScreen()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
    }
}