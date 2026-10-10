using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;

public class FeedbackManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField feedbackInput;
    [SerializeField] private Button submitFeedbackButton;
    [SerializeField] private TMP_Text feedbackStatusText;

    private string FeedbackURL
    {
        get
        {
            string baseURL = ServerConfig.BaseURL;
            if (!baseURL.EndsWith("/"))
                baseURL += "/";
            return baseURL + "submit_feedback.php";
        }
    }

    private void Start()
    {
        submitFeedbackButton?.onClick.AddListener(OnSubmitFeedbackClicked);

        // FIXED: Use static events instead of instance events
        SessionManager.OnSessionStarted += OnSessionStarted;
        SessionManager.OnSessionEnded += OnSessionEnded;

        if (SessionManager.Instance?.IsLoggedIn == true)
            EnableFeedback();
        else
            DisableFeedback("Please log in to submit feedback.");
    }

    private void OnDestroy()
    {
        // FIXED: Use static events instead of instance events
        SessionManager.OnSessionStarted -= OnSessionStarted;
        SessionManager.OnSessionEnded -= OnSessionEnded;
    }

    private void OnSessionStarted(User user)
    {
        EnableFeedback();
        ShowFeedback($"Welcome {user.nickname}! You can now submit feedback.");
    }

    private void OnSessionEnded()
    {
        DisableFeedback("Please log in to submit feedback.");
    }

    private void EnableFeedback()
    {
        // FIXED: Added null checks
        if (submitFeedbackButton != null)
            submitFeedbackButton.interactable = true;
        if (feedbackInput != null)
            feedbackInput.interactable = true;
    }

    private void DisableFeedback(string message)
    {
        // FIXED: Added null checks
        if (submitFeedbackButton != null)
            submitFeedbackButton.interactable = false;
        if (feedbackInput != null)
            feedbackInput.interactable = false;
        ShowFeedback(message);
    }

    private void OnSubmitFeedbackClicked()
    {
        string message = feedbackInput.text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            ShowFeedback("Please enter your feedback before submitting.");
            return;
        }

        // FIXED: Added length validation
        if (message.Length > 1000)
        {
            ShowFeedback("Feedback must be less than 1000 characters.");
            return;
        }

        var currentUser = SessionManager.Instance?.CurrentUser;
        if (currentUser == null || currentUser.id == 0 || string.IsNullOrEmpty(currentUser.nickname))
        {
            ShowFeedback("User ID and nickname are required to submit feedback.");
            return;
        }

        // FIXED: Debug user data
        Debug.Log($"[FeedbackManager] User Data - ID: {currentUser.id}, Nickname: {currentUser.nickname}, Email: {currentUser.email ?? "null"}");

        StartCoroutine(PostFeedback(currentUser.id, currentUser.nickname, currentUser.email, message));
    }

    private IEnumerator PostFeedback(int userId, string nickname, string email, string message)
    {
        // FIXED: Use serializable class instead of anonymous type
        var feedbackData = new FeedbackRequest
        {
            user_id = userId,
            nickname = nickname,
            email = email ?? "",
            message = message
        };

        string json = JsonUtility.ToJson(feedbackData);
        string url = FeedbackURL;

        // FIXED: Validate JSON before sending
        if (string.IsNullOrEmpty(json) || json == "{}")
        {
            ShowFeedback("Error: Failed to serialize feedback data. Please try again.");
            yield break;
        }

        Debug.Log($"[FeedbackManager] POST {url}\nPayload: {json}");

        // FIXED: Disable button during submission
        if (submitFeedbackButton != null)
            submitFeedbackButton.interactable = false;

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Accept", "application/json");

            yield return www.SendWebRequest();

            Debug.Log($"[FeedbackManager] Response Code: {www.responseCode}");
            Debug.Log($"[FeedbackManager] Raw Response: {www.downloadHandler.text}");

            if (www.result != UnityWebRequest.Result.Success)
            {
                // FIXED: Better error messages based on response code
                if (www.responseCode == 401) // Unauthorized
                    ShowFeedback("Session expired. Please log in again.");
                else if (www.responseCode >= 500) // Server error
                    ShowFeedback("Server error. Please try again later.");
                else
                    ShowFeedback("Network error: " + www.error);
            }
            else
            {
                try
                {
                    // FIXED: Check if response is not empty
                    if (string.IsNullOrEmpty(www.downloadHandler.text))
                    {
                        ShowFeedback("Server returned empty response. Please try again.");
                        yield break;
                    }

                    var response = JsonUtility.FromJson<ServerResponse>(www.downloadHandler.text);
                    if (response != null)
                    {
                        ShowFeedback(response.message);
                        if (response.status == "success")
                        {
                            if (feedbackInput != null)
                                feedbackInput.text = "";
                        }
                    }
                    else
                    {
                        ShowFeedback("Failed to parse server response.");
                    }
                }
                catch (System.Exception e)
                {
                    // FIXED: More detailed error information
                    ShowFeedback($"Error parsing server response: {e.Message}");
                }
            }
        }

        // FIXED: Re-enable button when done
        if (submitFeedbackButton != null)
            submitFeedbackButton.interactable = true;
    }

    private void ShowFeedback(string message)
    {
        Debug.Log("[FeedbackManager] " + message);
        if (feedbackStatusText != null)
            feedbackStatusText.text = message;
    }

    // FIXED: Added serializable class for request data
    [System.Serializable]
    private class FeedbackRequest
    {
        public int user_id;
        public string nickname;
        public string email;
        public string message;
    }

    [System.Serializable]
    private class ServerResponse
    {
        public string status;
        public string message;
    }
}