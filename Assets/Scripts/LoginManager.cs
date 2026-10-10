using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Handles authentication, session bootstrapping, and UI transitions post-login.
/// Integrates with DatabaseManager, SessionManager, and SyncManager.
/// Only triggers sync on login, logout, and local data changes.
/// </summary>
public class LoginManager : MonoBehaviour
{
    [Header("Input Fields")]
    [SerializeField] private TMP_InputField inputNickname;
    [SerializeField] private TMP_InputField inputPassword;

    [Header("Password Toggle UI")]
    [SerializeField] private Button eyeButton;
    [SerializeField] private Sprite eyeOpenSprite;
    [SerializeField] private Sprite eyeClosedSprite;
    private Image eyeButtonImage;

    [Header("UI Feedback")]
    [SerializeField] private TextMeshProUGUI feedbackText;

    [Header("Redirect Pages")]
    [SerializeField] private GameObject homePage;

    private const string TAG = "[LOGIN]";
    private string LoginURL => "https://blinkshield.alwaysdata.net/BlinkShield/login.php";

    private void Awake()
    {
        SetupPasswordField();
        SetupPasswordToggle();
        StartCoroutine(InitializeLoginSystem());
    }

    #region Initialization
    private IEnumerator InitializeLoginSystem()
    {
        yield return new WaitUntil(() =>
            DatabaseManager.Instance != null &&
            DatabaseManager.Instance.IsDatabaseReady()
        );

        Debug.Log($"{TAG} Local DB initialized. Ready for login input.");
    }
    #endregion

    #region Password Visibility
    private void SetupPasswordField()
    {
        if (inputPassword == null) return;
        inputPassword.contentType = TMP_InputField.ContentType.Password;
        inputPassword.ForceLabelUpdate();
    }

    private void SetupPasswordToggle()
    {
        if (eyeButton == null) return;

        eyeButtonImage = eyeButton.GetComponent<Image>();
        if (eyeButtonImage != null && eyeClosedSprite != null)
            eyeButtonImage.sprite = eyeClosedSprite;

        var trigger = eyeButton.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerDown, () => TogglePasswordVisibility(true));
        AddTrigger(trigger, EventTriggerType.PointerUp, () => TogglePasswordVisibility(false));
        AddTrigger(trigger, EventTriggerType.PointerExit, () => TogglePasswordVisibility(false));
    }

    private void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => callback());
        trigger.triggers.Add(entry);
    }

    private void TogglePasswordVisibility(bool visible)
    {
        if (inputPassword == null) return;

        inputPassword.contentType = visible
            ? TMP_InputField.ContentType.Standard
            : TMP_InputField.ContentType.Password;

        inputPassword.ForceLabelUpdate();
        if (eyeButtonImage != null)
            eyeButtonImage.sprite = visible ? eyeOpenSprite : eyeClosedSprite;
    }
    #endregion

    #region Login Flow
    public void OnLoginButtonClick()
    {
        if (!ValidateInputs(out string nickname, out string password))
            return;

        StartCoroutine(AuthenticateUser(nickname, password));
    }

    private bool ValidateInputs(out string nickname, out string password)
    {
        nickname = inputNickname?.text.Trim() ?? "";
        password = inputPassword?.text ?? "";

        if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(password))
        {
            ShowFeedback("Please enter both nickname and password.");
            return false;
        }

        return true;
    }

    private IEnumerator AuthenticateUser(string nickname, string password)
    {
        ShowFeedback("Logging in...");
        
        WWWForm form = new WWWForm();
        form.AddField("nickname", nickname);
        form.AddField("password", password);

        using (UnityWebRequest www = UnityWebRequest.Post(LoginURL, form))
        {
            Debug.Log($"{TAG} Sending POST → {LoginURL}");
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{TAG} Network error: {www.error}");
                ShowFeedback("Network error. Please check your connection.");
                yield break;
            }

            string rawResponse = www.downloadHandler.text.Trim();
            Debug.Log($"{TAG} Response: {rawResponse}");

            if (!TryParseResponse(rawResponse, out LoginResponse response))
            {
                ShowFeedback("Invalid response from server.");
                yield break;
            }

            yield return HandleLoginResponse(response);
        }
    }

    private bool TryParseResponse(string rawJson, out LoginResponse response)
    {
        response = null;
        try
        {
            response = JsonUtility.FromJson<LoginResponse>(rawJson);
            return response != null && !string.IsNullOrEmpty(response.status);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{TAG} JSON Parse Error: {ex.Message}\nRaw: {rawJson}");
            return false;
        }
    }

    private IEnumerator HandleLoginResponse(LoginResponse response)
    {
        if (!response.status.Equals("success", System.StringComparison.OrdinalIgnoreCase))
        {
            ShowFeedback(response.message ?? "Login failed.");
            yield break;
        }

        ShowFeedback("Login successful! Setting up session...");

        // Wait for critical managers to be ready
        yield return new WaitUntil(() =>
            DatabaseManager.Instance != null &&
            SessionManager.Instance != null &&
            SyncManager.Instance != null
        );

        // Convert and store user data
        var user = ConvertToUser(response.user);
        DatabaseManager.Instance.SyncRemoteUserToLocal(user);
        
        // Start the session
        SessionManager.Instance.StartSession(user);
        
        // Wait for session to be fully initialized
        yield return WaitForSessionReady();

        // ✅ UPDATED: Use SyncManager's OnUserLogin method which handles all sync logic
        yield return StartCoroutine(PerformLoginSync(user.id));

        // Load user settings
        SettingsManager settingsManager = FindFirstObjectByType<SettingsManager>();
        if (settingsManager != null)
        {
            settingsManager.LoadCurrentUser();
        }

        // ✅ Complete login setup with initial sync
        yield return SessionManager.Instance.CompleteLoginSetup();

        Debug.Log($"{TAG} Login pipeline completed. Redirecting...");
        ShowFeedback("Welcome back!");
        
        // Small delay to show welcome message
        yield return new WaitForSeconds(1f);
        
        RedirectToHome();
    }

    /// <summary>
    /// UPDATED: Use SyncManager's built-in login sync logic
    /// </summary>
    private IEnumerator PerformLoginSync(int userId)
    {
        if (SyncManager.Instance == null)
        {
            Debug.LogError($"{TAG} SyncManager is null during login sync");
            yield break;
        }

        // Notify SyncManager about user login (this triggers the sync logic)
        var user = DatabaseManager.Instance.GetUserById(userId);
        if (user != null)
        {
            SyncManager.Instance.OnUserLogin(user);
        }
        else
        {
            Debug.LogError($"{TAG} User not found in database: {userId}");
            yield break;
        }

        // Wait for SyncManager to complete initial sync
        float timeout = 30f; // Increased timeout for sync
        float startTime = Time.time;
        
        // Wait for user data to be pulled
        while (Time.time - startTime < timeout && !SyncManager.Instance.UserDataPulled)
        {
            Debug.Log($"{TAG} Waiting for user data sync...");
            yield return new WaitForSeconds(1f);
        }

        if (SyncManager.Instance.UserDataPulled)
        {
            Debug.Log($"{TAG} User data sync completed successfully");
            
            // Refresh user data from local database after sync
            var updatedUser = DatabaseManager.Instance.GetCurrentUser();
            if (updatedUser != null)
            {
                Debug.Log($"{TAG} User points after sync: {updatedUser.points}");
                Debug.Log($"{TAG} User achievements count: {DatabaseManager.Instance.GetUserAchievements(userId)?.Count ?? 0}");
                Debug.Log($"{TAG} User wardrobe count: {DatabaseManager.Instance.GetUserWardrobe()?.Count ?? 0}");
            }
        }
        else
        {
            Debug.LogError($"{TAG} User data sync timed out after {timeout} seconds");
            ShowFeedback("Note: Unable to sync latest data. Using local cache.");
        }
    }

    /// <summary>
    /// UPDATED: Fallback method for direct data sync (if needed)
    /// </summary>
    private IEnumerator FallbackSync(int userId)
    {
        Debug.Log($"{TAG} Attempting fallback sync...");
        
        // Try to pull global data first
        bool globalDataLoaded = false;
        yield return StartCoroutine(SyncManager.Instance.LoadGlobalData((success) => {
            globalDataLoaded = success;
        }));

        if (!globalDataLoaded)
        {
            Debug.LogWarning($"{TAG} Global data not loaded, but continuing with user sync");
        }

        // Try to pull user data
        bool userDataPulled = false;
        yield return StartCoroutine(SyncManager.Instance.PullAllUserData((success) => {
            userDataPulled = success;
        }));

        if (userDataPulled)
        {
            Debug.Log($"{TAG} Fallback sync completed successfully");
        }
        else
        {
            Debug.LogError($"{TAG} Fallback sync failed");
            ShowFeedback("Unable to sync with server. Using offline mode.");
        }
    }

    /// <summary>
    /// UPDATED: Alternative sync method that uses DatabaseManager's methods
    /// </summary>
    private IEnumerator AlternativeSyncMethod(int userId)
    {
        Debug.Log($"{TAG} Using alternative sync method...");
        
        // Use DatabaseManager's proxy methods that call SyncManager
        bool syncCompleted = false;
        bool syncSuccess = false;
        string syncMessage = "";
        
        yield return StartCoroutine(DatabaseManager.Instance.ProbeUserCoroutine(userId, (userExists, hasData, message) => {
            syncCompleted = true;
            syncMessage = message;
            
            if (userExists && hasData)
            {
                // User exists and has data - pull from server
                StartCoroutine(PullUserDataFromServer(userId));
                syncSuccess = true;
            }
            else if (userExists && !hasData)
            {
                // User exists but no data - push local data
                StartCoroutine(PushLocalDataToServer(userId));
                syncSuccess = true;
            }
            else
            {
                // New user - push local data
                StartCoroutine(PushLocalDataToServer(userId));
                syncSuccess = true;
            }
        }));

        // Wait for sync to complete
        float timeout = 20f;
        float startTime = Time.time;
        
        while (!syncCompleted && Time.time - startTime < timeout)
        {
            yield return null;
        }

        if (syncSuccess)
        {
            Debug.Log($"{TAG} Alternative sync completed: {syncMessage}");
        }
        else
        {
            Debug.LogError($"{TAG} Alternative sync failed or timed out");
        }
    }

    private IEnumerator PullUserDataFromServer(int userId)
    {
        Debug.Log($"{TAG} Pulling user data from server...");
        
        bool pullCompleted = false;
        bool pullSuccess = false;
        
        yield return StartCoroutine(DatabaseManager.Instance.PullUserDataSmartCoroutine(userId, (success, message) => {
            pullCompleted = true;
            pullSuccess = success;
            Debug.Log($"{TAG} Pull result: {message}");
        }));

        // Wait for pull to complete
        yield return new WaitUntil(() => pullCompleted);
        
        if (pullSuccess)
        {
            Debug.Log($"{TAG} User data pulled successfully");
        }
        else
        {
            Debug.LogWarning($"{TAG} User data pull failed");
        }
    }

    private IEnumerator PushLocalDataToServer(int userId)
    {
        Debug.Log($"{TAG} Pushing local data to server...");
        
        bool pushCompleted = false;
        bool pushSuccess = false;
        
        // Use SyncManager's ForceSync
        yield return StartCoroutine(SyncManager.Instance.ForceSyncCoroutine((success, message) => {
            pushCompleted = true;
            pushSuccess = success;
            Debug.Log($"{TAG} Push result: {message}");
        }));

        // Wait for push to complete
        yield return new WaitUntil(() => pushCompleted);
        
        if (pushSuccess)
        {
            Debug.Log($"{TAG} Local data pushed successfully");
        }
        else
        {
            Debug.LogWarning($"{TAG} Local data push failed");
        }
    }

    private IEnumerator WaitForSessionReady()
    {
        if (SessionManager.Instance == null)
        {
            Debug.LogError($"{TAG} SessionManager is null during session wait");
            yield break;
        }

        // Wait for session to be fully ready with timeout
        float timeout = 10f;
        float elapsed = 0f;
        
        while (elapsed < timeout && !SessionManager.Instance.IsSessionFullyReady())
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!SessionManager.Instance.IsSessionFullyReady())
        {
            Debug.LogError($"{TAG} Session not ready after {timeout} seconds timeout");
            ShowFeedback("Session setup timed out. Please try again.");
            yield break;
        }

        if (SessionManager.Instance.DEV_MODE)
            Debug.Log($"{TAG} Session ready after {elapsed:F1} seconds");
    }

    private User ConvertToUser(LoginResponse.UserData data)
    {
        return new User
        {
            id = data.id,
            nickname = data.nickname ?? "Unknown",
            email = data.email ?? "",
            password = data.password ?? "",
            profile_picture = data.profile_picture ?? "",
            created_at = data.created_at ?? "",
            updated_at = data.updated_at ?? "",
            is_active = data.is_active,
            is_verified = data.is_verified,
            verification_code = data.verification_code ?? "",
            points = data.points,
            screentime_minutes = data.screentime_minutes,
            breaks_taken = data.breaks_taken,
            health_score = data.health_score
        };
    }
    #endregion

    #region Navigation
    private void RedirectToHome()
    {
        if (UIManager.Instance != null && homePage != null)
        {
            UIManager.Instance.ShowPage(homePage);
            Debug.Log($"{TAG} Redirected to Home via UIManager.");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("HomeScene");
            Debug.Log($"{TAG} Redirected to HomeScene.");
        }
    }
    #endregion

    #region Feedback
    private void ShowFeedback(string message)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            
            // Auto-clear success messages after delay
            if (message.Contains("successful") || message.Contains("Welcome"))
            {
                StartCoroutine(ClearFeedbackAfterDelay(3f));
            }
        }
        else
        {
            Debug.LogWarning($"{TAG} Feedback: {message}");
        }
    }

    private IEnumerator ClearFeedbackAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (feedbackText != null)
            feedbackText.text = "";
    }
    #endregion

    #region Utility Methods
    public void ClearInputFields()
    {
        if (inputNickname != null)
            inputNickname.text = "";
        if (inputPassword != null)
            inputPassword.text = "";
        if (feedbackText != null)
            feedbackText.text = "";
    }

    public void FocusNicknameField()
    {
        if (inputNickname != null)
            inputNickname.Select();
    }
    #endregion

    #region Response Model
    [System.Serializable]
    private class LoginResponse
    {
        public string status;
        public string message;
        public UserData user;

        [System.Serializable]
        public class UserData
        {
            public int id;
            public string nickname;
            public string email;
            public string password;
            public string profile_picture;
            public string created_at;
            public string updated_at;
            public int is_active;
            public int is_verified;
            public string verification_code;
            public int points;
            public float screentime_minutes;
            public int breaks_taken;
            public int health_score;
        }
    }
    #endregion

    #region Test Login Methods (for debugging)
    
    /// <summary>
    /// UPDATED: Test login with sync validation
    /// </summary>
    [ContextMenu("Test Login with Sync")]
    public void TestLoginWithSync()
    {
        Debug.Log($"{TAG} === TESTING LOGIN WITH SYNC ===");
        
        // Set test credentials
        inputNickname.text = "testuser";
        inputPassword.text = "password123";
        
        // Trigger login
        OnLoginButtonClick();
    }

    /// <summary>
    /// UPDATED: Debug sync status after login
    /// </summary>
    public void DebugPostLoginSyncStatus()
    {
        if (!SessionManager.Instance.IsLoggedIn)
        {
            Debug.LogError($"{TAG} No user logged in");
            return;
        }

        int userId = SessionManager.Instance.CurrentUser.id;
        Debug.Log($"{TAG} === POST-LOGIN SYNC STATUS ===");
        Debug.Log($"{TAG} User ID: {userId}");
        
        if (SyncManager.Instance != null)
        {
            // UPDATED: Use GetStatus() instead of GetSmartSyncStatus()
            Debug.Log($"{TAG} Sync Status:\n{SyncManager.Instance.GetStatus()}");
            Debug.Log($"{TAG} Is Syncing: {SyncManager.Instance.IsSyncing}");
            Debug.Log($"{TAG} Last Successful Sync: {SyncManager.Instance.LastSuccessfulSync}");
            Debug.Log($"{TAG} Global Data Loaded: {SyncManager.Instance.GlobalDataLoaded}");
            Debug.Log($"{TAG} User Data Pulled: {SyncManager.Instance.UserDataPulled}");
        }
        else
        {
            Debug.LogError($"{TAG} SyncManager not available");
        }

        // Check local data
        var user = DatabaseManager.Instance.GetCurrentUser();
        if (user != null)
        {
            Debug.Log($"{TAG} Local User Points: {user.points}");
            Debug.Log($"{TAG} Local Achievements: {DatabaseManager.Instance.GetUserAchievements(userId)?.Count ?? 0}");
            Debug.Log($"{TAG} Local Wardrobe: {DatabaseManager.Instance.GetUserWardrobe()?.Count ?? 0}");
        }
    }

    /// <summary>
    /// UPDATED: Test force sync manually
    /// </summary>
    [ContextMenu("Test Force Sync")]
    public void TestForceSync()
    {
        if (!SessionManager.Instance.IsLoggedIn)
        {
            Debug.LogError($"{TAG} No user logged in");
            return;
        }

        Debug.Log($"{TAG} === TESTING FORCE SYNC ===");
        
        if (SyncManager.Instance != null)
        {
            SyncManager.Instance.ForceSync();
            Debug.Log($"{TAG} Force sync initiated");
        }
        else
        {
            Debug.LogError($"{TAG} SyncManager not available");
        }
    }

    /// <summary>
    /// UPDATED: Test global data sync
    /// </summary>
    [ContextMenu("Test Global Data Sync")]
    public void TestGlobalDataSync()
    {
        Debug.Log($"{TAG} === TESTING GLOBAL DATA SYNC ===");
        
        if (SyncManager.Instance != null)
        {
            StartCoroutine(SyncManager.Instance.LoadGlobalData((success) => {
                Debug.Log($"{TAG} Global data sync result: {success}");
            }));
        }
        else
        {
            Debug.LogError($"{TAG} SyncManager not available");
        }
    }

    #endregion
}