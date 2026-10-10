using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    [Header("Account Settings")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private TMP_Text emailText;
    [SerializeField] private TMP_InputField currentPasswordInput;
    [SerializeField] private TMP_InputField newPasswordInput;
    [SerializeField] private TMP_InputField confirmPasswordInput;
    [SerializeField] private Button changePasswordButton;
    [SerializeField] private Button logoutButton;

    [Header("Sync Settings")]
    [SerializeField] private TMP_Text lastSyncTimeText;
    [SerializeField] private Button syncDataButton;

    [Header("Personalization")]
    [SerializeField] private TMP_Dropdown reminderIntervalDropdown;
    [SerializeField] private TMP_Dropdown eyeSurveyDropdown;

    [Header("EyeSurvey Settings")]
    [SerializeField] private GameObject pageSurvey;
    // REMOVED: eyeSurveyTimer - Now handled by EyeSurvey.cs
    private int eyeSurveyIntervalMinutes = 60; // Default 1 hour

    private string SettingsURL => ServerConfig.BaseURL + "user_settings.php";
    private string PasswordChangeURL => ServerConfig.BaseURL + "change_password.php";

    #region Lifecycle
    private void OnEnable()
    {
        // FIXED: Use static events instead of instance events
        SessionManager.OnSessionStarted += HandleSessionStarted;

        // Always load nickname/email immediately from session or DB
        LoadCurrentUser();
    }

    private void OnDisable()
    {
        // FIXED: Use static events instead of instance events
        SessionManager.OnSessionStarted -= HandleSessionStarted;
    }

    private void Start()
    {
        changePasswordButton?.onClick.AddListener(OnChangePasswordClicked);
        syncDataButton?.onClick.AddListener(OnSyncDataClicked);
        logoutButton?.onClick.AddListener(OnLogoutClicked);

        InitializeDropdowns();
        InitializeEyeSurveyDropdown();

        if (reminderIntervalDropdown != null)
            reminderIntervalDropdown.onValueChanged.AddListener(OnReminderIntervalChanged);
    }

    private void HandleSessionStarted(User user)
    {
        LoadCurrentUser();
    }
    #endregion

    #region Load Current User
    public void LoadCurrentUser()
    {
        // Get user from session first, fallback to DB
        var user = SessionManager.Instance?.CurrentUser ?? DatabaseManager.Instance?.GetCurrentUser();

        // Populate only nickname and email
        nicknameText.text = user?.nickname ?? "-";
        emailText.text = user?.email ?? "-";
    }
    #endregion

    #region Dropdowns
    private void InitializeDropdowns()
    {
        InitializeReminderIntervalDropdown();
        InitializeEyeSurveyDropdown();
    }

    private void InitializeReminderIntervalDropdown()
    {
        if (reminderIntervalDropdown == null) return;

        List<string> options = GenerateReminderIntervals();
        reminderIntervalDropdown.ClearOptions();
        reminderIntervalDropdown.AddOptions(options);

        // Load saved interval or default to 15 minutes
        var settings = DatabaseManager.Instance.GetUserSettings();
        int savedMinutes = settings?.reminder_interval_min ?? 15;
        
        // Find the index for saved minutes
        int savedIndex = FindReminderIntervalIndex(savedMinutes);
        reminderIntervalDropdown.value = Mathf.Max(0, savedIndex);
        
        Debug.Log($"[SettingsManager] Reminder interval initialized to {savedMinutes} minutes (index: {savedIndex})");
    }

    private List<string> GenerateReminderIntervals()
    {
        List<string> intervals = new();
        for (int h = 0; h <= 3; h++)
        {
            for (int m = 0; m < 60; m += 5)
            {
                if (h == 3 && m > 0) break;
                if (h == 0 && m == 0) continue;
                intervals.Add(h > 0
                    ? $"{h} hour{(h > 1 ? "s" : "")}{(m > 0 ? $", {m} minutes" : "")}"
                    : $"{m} minutes");
            }
        }
        return intervals;
    }

    private int FindReminderIntervalIndex(int minutes)
    {
        if (reminderIntervalDropdown == null) return 0;
        
        for (int i = 0; i < reminderIntervalDropdown.options.Count; i++)
        {
            string optionText = reminderIntervalDropdown.options[i].text;
            int optionMinutes = ParseMinutesFromText(optionText);
            if (optionMinutes == minutes)
                return i;
        }
        return 2; // Default to 15 minutes (index 2) if not found
    }

    private void OnReminderIntervalChanged(int index)
    {
        if (reminderIntervalDropdown == null) return;

        string selectedText = reminderIntervalDropdown.options[index].text;
        int minutes = ParseMinutesFromText(selectedText);

        Debug.Log($"[SettingsManager] Break reminder interval changed to {minutes} minutes (screen time based)");

        // Save to database
        var settings = DatabaseManager.Instance.GetUserSettings() ?? new UserSettings();
        settings.reminder_interval_min = minutes;
        DatabaseManager.Instance.SaveOrUpdateUserSettings(settings);

        // Update NotificationManager with new interval
        NotificationManager nm = FindFirstObjectByType<NotificationManager>();
        if (nm != null)
        {
            nm.UpdateReminderInterval(minutes);
            Debug.Log($"[SettingsManager] Updated NotificationManager with {minutes} minute interval");
        }
        else
        {
            Debug.LogWarning("[SettingsManager] NotificationManager not found!");
        }
    }

    private int ParseMinutesFromText(string text)
    {
        int totalMinutes = 0;
        if (string.IsNullOrEmpty(text)) return 15;

        string[] parts = text.Split(',');
        foreach (string part in parts)
        {
            string p = part.Trim();
            if (p.Contains("hour") && int.TryParse(p.Split(' ')[0], out int h))
                totalMinutes += h * 60;
            else if (p.Contains("minute") && int.TryParse(p.Split(' ')[0], out int m))
                totalMinutes += m;
        }
        return totalMinutes > 0 ? totalMinutes : 15;
    }
    #endregion

    #region EyeSurvey
    private void InitializeEyeSurveyDropdown()
    {
        if (eyeSurveyDropdown == null) return;

        List<string> options = new() { "1 Hour", "2 Hours", "3 Hours" };
        eyeSurveyDropdown.ClearOptions();
        eyeSurveyDropdown.AddOptions(options);

        var settings = DatabaseManager.Instance.GetUserSettings();
        int savedIndex = (settings != null)
            ? Mathf.Clamp(settings.survey_interval_hr - 1, 0, options.Count - 1)
            : 0;

        eyeSurveyDropdown.value = savedIndex;
        eyeSurveyDropdown.onValueChanged.AddListener(OnEyeSurveyDropdownChanged);

        eyeSurveyIntervalMinutes = GetEyeSurveyIntervalMinutes();
    }

    private void OnEyeSurveyDropdownChanged(int index)
    {
        int hours = index + 1;
        var settings = DatabaseManager.Instance.GetUserSettings() ?? new UserSettings();
        settings.survey_interval_hr = hours;
        DatabaseManager.Instance.SaveOrUpdateUserSettings(settings);

        eyeSurveyIntervalMinutes = hours * 60;
        
        Debug.Log($"[SettingsManager] Eye survey interval changed to {hours} hour(s) ({eyeSurveyIntervalMinutes} minutes screen time)");
    }

    public int GetEyeSurveyIntervalMinutes()
    {
        // Return the selected interval in minutes
        if (eyeSurveyDropdown == null) return 60;
        
        return eyeSurveyDropdown.value switch
        {
            0 => 60,   // 1 hour
            1 => 120,  // 2 hours
            2 => 180,  // 3 hours
            _ => 60    // default
        };
    }

    // REMOVED: The Update() method timer logic
    // EyeSurvey.cs now handles the timing based on actual screen time
    #endregion

    #region Sync
    private void OnSyncDataClicked() => StartCoroutine(SyncData());

    private IEnumerator SyncData()
    {
        var user = SessionManager.Instance?.CurrentUser ?? DatabaseManager.Instance.GetCurrentUser();
        if (user == null) yield break;

        var localSettings = DatabaseManager.Instance.GetUserSettings();
        if (localSettings != null)
        {
            string json = JsonUtility.ToJson(localSettings);
            using var uploadRequest = new UnityWebRequest(SettingsURL, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            uploadRequest.SetRequestHeader("Content-Type", "application/json");
            yield return uploadRequest.SendWebRequest();
        }

        using var downloadRequest = UnityWebRequest.Get($"{SettingsURL}?action=settings&subaction=get_settings");
        yield return downloadRequest.SendWebRequest();

        if (downloadRequest.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(downloadRequest.downloadHandler.text))
        {
            try
            {
                var serverSettings = JsonUtility.FromJson<UserSettings>(downloadRequest.downloadHandler.text);
                if (serverSettings != null)
                {
                    DatabaseManager.Instance.SaveOrUpdateUserSettings(serverSettings);
                    
                    // Update both dropdowns from server settings
                    if (reminderIntervalDropdown != null)
                    {
                        int index = FindReminderIntervalIndex(serverSettings.reminder_interval_min);
                        reminderIntervalDropdown.value = index;
                    }
                    
                    if (eyeSurveyDropdown != null && serverSettings.survey_interval_hr > 0)
                    {
                        eyeSurveyDropdown.value = Mathf.Clamp(serverSettings.survey_interval_hr - 1, 0, 2);
                    }
                    
                    if (lastSyncTimeText != null)
                        lastSyncTimeText.text = "Last sync: " + DateTime.Now.ToString("MMM dd, yyyy HH:mm");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SettingsManager] Sync error: {ex.Message}");
            }
        }
    }
    #endregion

    #region Password Change
    private void OnChangePasswordClicked()
    {
        if (currentPasswordInput == null || newPasswordInput == null || confirmPasswordInput == null) return;

        string currentPassword = currentPasswordInput.text;
        string newPassword = newPasswordInput.text;
        string confirmPassword = confirmPasswordInput.text;

        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword)) return;
        if (newPassword != confirmPassword) return;
        if (newPassword.Length < 8) return;

        StartCoroutine(ChangePassword(currentPassword, newPassword));
    }

    private IEnumerator ChangePassword(string currentPassword, string newPassword)
    {
        var user = SessionManager.Instance?.CurrentUser ?? DatabaseManager.Instance.GetCurrentUser();
        if (user == null) yield break;

        var data = new { user_id = user.id, current_password = currentPassword, new_password = newPassword };
        string json = JsonUtility.ToJson(data);

        using var request = new UnityWebRequest(PasswordChangeURL, "POST")
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        request.SetRequestHeader("Content-Type", "application/json");
        yield return request.SendWebRequest();
    }
    #endregion

    #region Helpers
    private void SetDropdownValue(TMP_Dropdown dropdown, string value)
    {
        if (dropdown == null) return;
        int index = dropdown.options.FindIndex(option => option.text == value);
        if (index >= 0) dropdown.value = index;
    }

    private void OnLogoutClicked()
    {
        nicknameText.text = "-";
        emailText.text = "-";
        SessionManager.Instance?.Logout();
    }
    #endregion
    
    #region Debug Methods
    [ContextMenu("Debug: Show Current Settings")]
    public void DebugShowCurrentSettings()
    {
        var settings = DatabaseManager.Instance?.GetUserSettings();
        if (settings != null)
        {
            Debug.Log($"[SettingsManager] Current settings:");
            Debug.Log($"- Break Reminder Interval: {settings.reminder_interval_min} minutes (screen time)");
            Debug.Log($"- Eye Survey Interval: {settings.survey_interval_hr} hours (screen time)");
            
            if (reminderIntervalDropdown != null)
            {
                string selectedReminder = reminderIntervalDropdown.options[reminderIntervalDropdown.value].text;
                Debug.Log($"- Selected Break Reminder: {selectedReminder}");
            }
            
            if (eyeSurveyDropdown != null)
            {
                string selectedSurvey = eyeSurveyDropdown.options[eyeSurveyDropdown.value].text;
                Debug.Log($"- Selected Eye Survey: {selectedSurvey}");
            }
        }
        else
        {
            Debug.Log("[SettingsManager] No settings found");
        }
    }
    #endregion
}