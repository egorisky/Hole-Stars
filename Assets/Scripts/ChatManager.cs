using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Takes a player's free-text prompt, asks an LLM to turn it into a concrete spawn decision, and
/// hands the parsed result to SpawnerManager. The API key never lives in a tracked file: it's
/// read at runtime from secrets/openai_api_key.txt next to the project (see .gitignore) so it
/// can't end up baked into the committed scene or script.
/// </summary>
public class ChatManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button submitButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private SpawnerManager spawnerManager;
    [SerializeField] private string model = "gpt-4o-mini";

    private const string ApiEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string SystemPrompt =
        "You are a game generator for a Hole.io style game. Analyze the player's request and " +
        "output ONLY a JSON object with these exact fields: objectType ('asteroid' or 'planet'), " +
        "scaleMultiplier (float between 0.5 and 3.0), colorHex (string hex code like #FF0000), " +
        "and pointValue (int).";

    private string _apiKey;
    private bool _requestInFlight;
    private UnityAction<string> _onInputSubmit;

    [Serializable] private class ChatMessage { public string role; public string content; }
    [Serializable] private class ResponseFormat { public string type; }

    [Serializable]
    private class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public ResponseFormat response_format;
    }

    [Serializable] private class ChoiceMessage { public string content; }
    [Serializable] private class Choice { public ChoiceMessage message; }
    [Serializable] private class ChatCompletionResponse { public Choice[] choices; }

    [Serializable]
    private class SpawnDecision
    {
        public string objectType;
        public float scaleMultiplier;
        public string colorHex;
        public int pointValue;
    }

    private void Awake()
    {
        _apiKey = LoadApiKey();
    }

    private void OnEnable()
    {
        if (submitButton != null)
        {
            submitButton.onClick.AddListener(OnSubmitButtonClicked);
        }

        if (inputField != null)
        {
            _onInputSubmit = _ => OnSubmitButtonClicked();
            inputField.onSubmit.AddListener(_onInputSubmit);
        }
    }

    private void OnDisable()
    {
        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(OnSubmitButtonClicked);
        }

        if (inputField != null && _onInputSubmit != null)
        {
            inputField.onSubmit.RemoveListener(_onInputSubmit);
        }
    }

    private static string LoadApiKey()
    {
        // Kept out of every tracked file on purpose: an Inspector-assigned value gets serialized
        // straight into the (committed) scene file. A real shipped game can't do this at all - a
        // client build can always be inspected for a bundled key, local file or not; production
        // needs a server-side proxy that holds the key and never ships it to the player.
        string path = Path.Combine(Application.dataPath, "..", "secrets", "openai_api_key.txt");
        if (!File.Exists(path))
        {
            return null;
        }

        string key = File.ReadAllText(path).Trim();
        return string.IsNullOrEmpty(key) ? null : key;
    }

    public void OnSubmitButtonClicked()
    {
        if (_requestInFlight)
        {
            return;
        }

        string playerPrompt = inputField != null ? inputField.text : null;
        if (string.IsNullOrWhiteSpace(playerPrompt))
        {
            return;
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            SetStatus("No API key found - put it in secrets/openai_api_key.txt.");
            return;
        }

        if (inputField != null)
        {
            inputField.text = "";
        }

        StartCoroutine(SendPromptToAI(playerPrompt));
    }

    private IEnumerator SendPromptToAI(string prompt)
    {
        _requestInFlight = true;
        SetInteractable(false);
        SetStatus("Thinking...");

        var body = new ChatRequest
        {
            model = model,
            messages = new[]
            {
                new ChatMessage { role = "system", content = SystemPrompt },
                new ChatMessage { role = "user", content = prompt },
            },
            response_format = new ResponseFormat { type = "json_object" },
        };

        // JsonUtility escapes the prompt correctly; a hand-built JSON string breaks the moment a
        // player types a quote or a newline into the chat box.
        string jsonBody = JsonUtility.ToJson(body);

        using (UnityWebRequest request = new UnityWebRequest(ApiEndpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + _apiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("AI request failed: " + request.error + "\n" + request.downloadHandler.text);
                SetStatus("Request failed: " + request.error);
            }
            else
            {
                HandleResponse(request.downloadHandler.text);
            }
        }

        _requestInFlight = false;
        SetInteractable(true);
    }

    private void HandleResponse(string rawResponse)
    {
        SpawnDecision decision;
        try
        {
            ChatCompletionResponse completion = JsonUtility.FromJson<ChatCompletionResponse>(rawResponse);
            string content = completion != null && completion.choices != null && completion.choices.Length > 0
                ? completion.choices[0].message.content
                : null;

            if (string.IsNullOrEmpty(content))
            {
                Debug.LogError("AI response had no usable content: " + rawResponse);
                SetStatus("The AI didn't return anything usable.");
                return;
            }

            decision = JsonUtility.FromJson<SpawnDecision>(content);
        }
        catch (ArgumentException e)
        {
            Debug.LogError("Could not parse AI response: " + e.Message + "\n" + rawResponse);
            SetStatus("Couldn't understand the AI's response.");
            return;
        }

        if (decision == null || decision.scaleMultiplier <= 0f || string.IsNullOrEmpty(decision.colorHex))
        {
            Debug.LogError("AI response missing required fields: " + rawResponse);
            SetStatus("The AI's answer was missing something - try again.");
            return;
        }

        if (spawnerManager == null)
        {
            Debug.LogError("ChatManager has no SpawnerManager assigned.");
            return;
        }

        spawnerManager.SpawnCustomObject(decision.objectType, decision.scaleMultiplier, decision.colorHex, decision.pointValue);
        SetStatus("Spawned a " + (string.IsNullOrEmpty(decision.objectType) ? "star" : decision.objectType) + ".");
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SetInteractable(bool interactable)
    {
        if (submitButton != null)
        {
            submitButton.interactable = interactable;
        }
        if (inputField != null)
        {
            inputField.interactable = interactable;
        }
    }
}
