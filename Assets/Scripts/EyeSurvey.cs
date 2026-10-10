using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SQLite4Unity3d;

public class EyeSurvey : MonoBehaviour
{
    [Header("Survey Panel")]
    public GameObject surveyPanel;

    [Header("Survey Toggles")]
    public Toggle dryEyesToggle;
    public Toggle blurryVisionToggle;
    public Toggle headacheToggle;
    public Toggle dizzinessToggle;
    public Toggle glareToggle;
    public Toggle noneToggle;

    [Header("Blurry Vision Sub-Options")]
    public GameObject blurrySubContainer; // Parent GameObject for sub-toggles
    public Toggle blurryNearToggle;
    public Toggle blurryFarToggle;
    public Toggle blurryBothToggle;

    [Header("Headache Sub-Options")]
    public GameObject headacheSubContainer; // Parent GameObject for sub-toggles
    public Toggle headacheMildToggle;
    public Toggle headacheModerateToggle;
    public Toggle headacheSevereToggle;

    [Header("Progress Indicator (Optional)")]
    public Image progressFill;
    public TMPro.TMP_Text progressText;

    [Header("Analysis Notification (Optional)")]
    [SerializeField] private GameObject analysisNotification;
    [SerializeField] private TMPro.TMP_Text analysisNotificationText;
    [SerializeField] private float notificationDuration = 3f;

    private SQLiteConnection _db;
    private ScreenTimeManager _screenTimeManager;
    private SettingsManager _settingsManager;
    private NotificationManager _notificationManager;
    private Coroutine _surveyCoroutine;
    
    // Track screen time accumulation
    private float _lastScreenTimeCheck = 0f; // Last recorded screen time in hours
    private float _screenTimeAccumulator = 0f; // How much screen time accumulated since last survey
    private float _currentSurveyIntervalHours = 1f; // Default 1 hour (from settings)

    private void Awake()
    {
        string dbPath = Path.Combine(Application.persistentDataPath, "blinkshieldlocal.db");
        _db = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
        _db.CreateTable<EyeHealthLog>();
    }

    private void Start()
    {
        _screenTimeManager = Object.FindFirstObjectByType<ScreenTimeManager>();
        _settingsManager = Object.FindFirstObjectByType<SettingsManager>();
        _notificationManager = Object.FindFirstObjectByType<NotificationManager>();

        // Use static events instead of instance events
        SessionManager.OnSessionStarted += OnUserReady;

        if (SessionManager.Instance?.IsLoggedIn == true)
            OnUserReady(SessionManager.Instance.CurrentUser);

        if (surveyPanel != null)
            surveyPanel.SetActive(false);

        // Initialize analysis notification
        if (analysisNotification != null)
            analysisNotification.SetActive(false);

        // Bind main toggles to update sub-option visibility dynamically
        blurryVisionToggle.onValueChanged.AddListener(_ => ToggleSubContainer(blurrySubContainer, blurryVisionToggle.isOn));
        headacheToggle.onValueChanged.AddListener(_ => ToggleSubContainer(headacheSubContainer, headacheToggle.isOn));

        // Initially collapse sub-containers
        ToggleSubContainer(blurrySubContainer, false, instant: true);
        ToggleSubContainer(headacheSubContainer, false, instant: true);
        
        // Initialize with settings
        if (_settingsManager != null)
        {
            _currentSurveyIntervalHours = _settingsManager.GetEyeSurveyIntervalMinutes() / 60f;
            Debug.Log($"[EyeSurvey] Initial survey interval: {_currentSurveyIntervalHours} hours");
        }
    }

    private void OnDestroy()
    {
        // Use static events instead of instance events
        SessionManager.OnSessionStarted -= OnUserReady;

        if (_surveyCoroutine != null)
            StopCoroutine(_surveyCoroutine);
    }

    private void OnUserReady(User user)
    {
        if (_settingsManager == null) 
        {
            Debug.LogError("[EyeSurvey] SettingsManager not found!");
            return;
        }

        if (_surveyCoroutine != null)
            StopCoroutine(_surveyCoroutine);

        // Reset accumulators for new user
        _lastScreenTimeCheck = 0f;
        _screenTimeAccumulator = 0f;
        _currentSurveyIntervalHours = _settingsManager.GetEyeSurveyIntervalMinutes() / 60f;
        
        Debug.Log($"[EyeSurvey] User {user?.id} ready. Survey interval: {_currentSurveyIntervalHours} hours");

        // Start monitoring screen time for survey
        _surveyCoroutine = StartCoroutine(MonitorScreenTimeForSurvey());
    }

    // Monitor actual screen time for surveys
    private IEnumerator MonitorScreenTimeForSurvey()
    {
        Debug.Log("[EyeSurvey] Starting screen time monitor for surveys");
        
        // Wait a bit for ScreenTimeManager to initialize
        yield return new WaitForSeconds(2f);
        
        // Get initial screen time
        if (_screenTimeManager != null)
        {
            float initialScreenTime = _screenTimeManager.GetTodayScreenTimeInMinutes() / 60f;
            _lastScreenTimeCheck = initialScreenTime;
            Debug.Log($"[EyeSurvey] Initial screen time: {initialScreenTime:F2}h");
        }
        
        while (true)
        {
            yield return new WaitForSeconds(30f); // Check every 30 seconds
            
            if (_screenTimeManager == null || _settingsManager == null)
            {
                Debug.LogWarning("[EyeSurvey] Missing managers, waiting...");
                continue;
            }
            
            // Get current screen time from ScreenTimeManager
            float currentScreenTimeHours = _screenTimeManager.GetTodayScreenTimeInMinutes() / 60f;
            
            // Calculate how much screen time has been added since last check
            float screenTimeDelta = currentScreenTimeHours - _lastScreenTimeCheck;
            
            if (screenTimeDelta > 0)
            {
                // Only accumulate when screen time increases (user is actually using screen)
                _screenTimeAccumulator += screenTimeDelta;
                _lastScreenTimeCheck = currentScreenTimeHours;
                
                // Update progress UI if available
                UpdateProgressUI();
                
                Debug.Log($"[EyeSurvey] Screen time: {currentScreenTimeHours:F2}h (+{screenTimeDelta:F2}h), " +
                         $"Accumulated: {_screenTimeAccumulator:F2}h, Target: {_currentSurveyIntervalHours}h");
                
                // Check if enough screen time has accumulated
                if (_screenTimeAccumulator >= _currentSurveyIntervalHours)
                {
                    ShowSurvey();
                    _screenTimeAccumulator = 0f; // Reset for next interval
                }
            }
            
            // Check if survey interval changed in settings
            float newIntervalHours = _settingsManager.GetEyeSurveyIntervalMinutes() / 60f;
            if (Mathf.Abs(newIntervalHours - _currentSurveyIntervalHours) > 0.01f)
            {
                _currentSurveyIntervalHours = newIntervalHours;
                Debug.Log($"[EyeSurvey] Interval updated to: {_currentSurveyIntervalHours} hours");
                UpdateProgressUI();
            }
        }
    }
    
    // Update progress UI (optional)
    private void UpdateProgressUI()
    {
        if (progressFill != null)
        {
            float progress = Mathf.Clamp01(_screenTimeAccumulator / _currentSurveyIntervalHours);
            progressFill.fillAmount = progress;
        }
        
        if (progressText != null)
        {
            float remainingHours = Mathf.Max(0, _currentSurveyIntervalHours - _screenTimeAccumulator);
            progressText.text = $"Next survey in: {remainingHours:F1}h";
        }
    }

    // Show survey method
    private void ShowSurvey()
    {
        if (surveyPanel != null && SessionManager.Instance?.IsLoggedIn == true)
        {
            surveyPanel.SetActive(true);
            Debug.Log($"[EyeSurvey] Survey displayed after {_currentSurveyIntervalHours}h of screen time at {System.DateTime.Now}");
            _notificationManager?.ScheduleEyeSurveyNotification(0);
            
            // Reset progress UI when survey is shown
            UpdateProgressUI();
        }
        else
        {
            Debug.LogWarning("[EyeSurvey] Cannot show survey - panel null or user not logged in");
        }
    }

    // Smooth expand/collapse of sub-toggle container
    private void ToggleSubContainer(GameObject container, bool show, bool instant = false)
    {
        if (container == null) return;

        container.SetActive(true); // Always active for animation

        LayoutElement layout = container.GetComponent<LayoutElement>();
        if (layout == null)
            layout = container.AddComponent<LayoutElement>();

        float targetHeight = show ? GetPreferredHeight(container) : 0f;

        if (instant)
        {
            layout.minHeight = targetHeight;
            layout.preferredHeight = targetHeight;
        }
        else
        {
            StartCoroutine(AnimateHeight(layout, targetHeight, 0.25f));
        }

        // Reset toggles if hiding
        if (!show)
        {
            foreach (Toggle t in container.GetComponentsInChildren<Toggle>())
                t.isOn = false;
        }
    }

    private float GetPreferredHeight(GameObject container)
    {
        var layoutGroup = container.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup != null)
        {
            float height = 0f;
            foreach (RectTransform child in container.transform)
                if (child.gameObject.activeSelf)
                    height += LayoutUtility.GetPreferredHeight(child) + layoutGroup.spacing;
            return height;
        }
        return LayoutUtility.GetPreferredHeight(container.GetComponent<RectTransform>());
    }

    private IEnumerator AnimateHeight(LayoutElement layout, float targetHeight, float duration)
    {
        float startHeight = layout.preferredHeight;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float newHeight = Mathf.Lerp(startHeight, targetHeight, elapsed / duration);
            layout.preferredHeight = newHeight;
            LayoutRebuilder.ForceRebuildLayoutImmediate(layout.GetComponent<RectTransform>());
            yield return null;
        }
        layout.preferredHeight = targetHeight;
        LayoutRebuilder.ForceRebuildLayoutImmediate(layout.GetComponent<RectTransform>());
    }

    private int CurrentUserId => SessionManager.Instance?.CurrentUser?.id ?? 0;

    // UPDATED: SubmitSurvey - ONLY saves symptoms, no analysis
    public void SubmitSurvey()
    {
        if (CurrentUserId == 0)
        {
            Debug.LogError("❌ Cannot submit survey — no user logged in yet.");
            return;
        }

        float screenTimeHours = 0f;
        if (_screenTimeManager != null)
        {
            int totalMinutes = _screenTimeManager.GetTodayScreenTimeInMinutes();
            screenTimeHours = totalMinutes / 60f;
        }

        List<string> selectedSymptoms = BuildSymptomList();

        EyeHealthLog log = new EyeHealthLog
        {
            user_id = CurrentUserId,
            screen_time_hours = screenTimeHours,
            date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            symptoms = string.Join(", ", selectedSymptoms),
            
            // IMPORTANT: Leave these EMPTY - analysis happens live!
            pattern_detected = "",  // Empty - not saved to DB
            ai_recommendation = ""  // Empty - not saved to DB
        };

        _db.Insert(log);

        Debug.Log($"✅ Survey #{log.id} saved with SYMPTOMS ONLY: {log.symptoms}");
        Debug.Log($"   Analysis fields left empty for real-time processing");

        // Show notification
        ShowAnalysisNotification("Survey submitted! Analysis will update automatically.");
        
        // Clear EyePatternProcessor cache so fresh analysis is triggered
        if (EyePatternProcessor.Instance != null)
        {
            EyePatternProcessor.Instance.ClearUserCache(CurrentUserId);
        }
        
        // Trigger fresh analysis for UI
        if (EyePatternProcessor.Instance != null && CurrentUserId > 0)
        {
            EyePatternProcessor.Instance.TriggerAnalysisForCurrentUser();
            Debug.Log($"[EyeSurvey] Triggered fresh analysis after survey submission");
        }

        ResetToggles();

        if (surveyPanel != null)
            surveyPanel.SetActive(false);
            
        // After submitting survey, reset the screen time check
        // so we start counting fresh for the next survey
        if (_screenTimeManager != null)
        {
            _lastScreenTimeCheck = _screenTimeManager.GetTodayScreenTimeInMinutes() / 60f;
        }
    }

    // Process analysis after survey submission (REAL-TIME)
    private IEnumerator ProcessSurveyAnalysisRealTime()
    {
        yield return new WaitForSeconds(0.5f); // Small delay for database consistency
        
        if (EyePatternProcessor.Instance == null)
        {
            Debug.LogWarning("[EyeSurvey] Pattern processor not available for real-time analysis");
            yield break;
        }
        
        // Count user's total surveys for notification message
        int surveyCount = CountUserSurveys(CurrentUserId);
        Debug.Log($"[EyeSurvey] User has {surveyCount} total surveys");
        
        string notificationMessage = "";
        
        if (surveyCount == 1)
        {
            // First survey - notify user
            notificationMessage = "First survey submitted! Check Eye Health Log for analysis.";
        }
        else if (surveyCount == 2)
        {
            // Second survey - pattern analysis becomes available
            notificationMessage = "Second survey submitted! Pattern analysis now available.";
        }
        else
        {
            // Multiple surveys
            notificationMessage = $"Survey submitted! Analysis updated with {surveyCount} surveys.";
        }
        
        // Show notification to user
        if (!string.IsNullOrEmpty(notificationMessage))
        {
            ShowAnalysisNotification(notificationMessage);
        }
        
        // The actual analysis happens in EyePatternProcessor
        // and is displayed live in EyeHealthLogUI
    }
    
    // Count user's surveys
    private int CountUserSurveys(int userId)
    {
        try
        {
            return _db.Table<EyeHealthLog>().Count(x => x.user_id == userId);
        }
        catch (System.Exception)
        {
            return 0;
        }
    }
    
    // Show analysis notification
    private void ShowAnalysisNotification(string message)
    {
        if (analysisNotification == null || analysisNotificationText == null)
        {
            Debug.Log($"[EyeSurvey] {message}");
            return;
        }
        
        analysisNotificationText.text = message;
        analysisNotification.SetActive(true);
        
        StartCoroutine(HideNotificationAfterDelay(notificationDuration));
    }
    
    private IEnumerator HideNotificationAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (analysisNotification != null)
            analysisNotification.SetActive(false);
    }

    private List<string> BuildSymptomList()
    {
        List<string> symptoms = new List<string>();

        if (noneToggle.isOn)
        {
            symptoms.Add("None");
            return symptoms;
        }

        if (dryEyesToggle.isOn) symptoms.Add("Dry Eyes");

        if (blurryVisionToggle.isOn)
        {
            symptoms.Add("Blurry Vision");
            if (blurryNearToggle.isOn) symptoms.Add("Blurry Vision - Near");
            if (blurryFarToggle.isOn) symptoms.Add("Blurry Vision - Far");
            if (blurryBothToggle.isOn) symptoms.Add("Blurry Vision - Both");
        }

        if (headacheToggle.isOn)
        {
            symptoms.Add("Headache");
            if (headacheMildToggle.isOn) symptoms.Add("Headache - Mild");
            if (headacheModerateToggle.isOn) symptoms.Add("Headache - Moderate");
            if (headacheSevereToggle.isOn) symptoms.Add("Headache - Severe");
        }

        if (dizzinessToggle.isOn) symptoms.Add("Dizziness");
        if (glareToggle.isOn) symptoms.Add("Glare");

        return symptoms;
    }

    private void ResetToggles()
    {
        dryEyesToggle.isOn = false;
        blurryVisionToggle.isOn = false;
        headacheToggle.isOn = false;
        dizzinessToggle.isOn = false;
        glareToggle.isOn = false;
        noneToggle.isOn = false;

        ToggleSubContainer(blurrySubContainer, false, instant: true);
        ToggleSubContainer(headacheSubContainer, false, instant: true);
    }
    
    // Check if EyePatternProcessor is available
    public bool IsAnalysisAvailable()
    {
        return EyePatternProcessor.Instance != null;
    }
    
    // Get current user's survey count
    public int GetCurrentUserSurveyCount()
    {
        if (CurrentUserId == 0) return 0;
        return CountUserSurveys(CurrentUserId);
    }
    
    // Manually trigger analysis (for debugging or testing)
    [ContextMenu("Debug: Trigger Analysis Now")]
    public void DebugTriggerAnalysis()
    {
        if (EyePatternProcessor.Instance != null && CurrentUserId > 0)
        {
            EyePatternProcessor.Instance.TriggerAnalysisForCurrentUser();
            Debug.Log($"[EyeSurvey] Debug: Triggered analysis for user #{CurrentUserId}");
        }
        else
        {
            Debug.LogWarning("[EyeSurvey] Debug: Cannot trigger analysis");
        }
    }
    
    // Public method to manually trigger survey (for debugging)
    [ContextMenu("Debug: Show Survey Now")]
    public void DebugShowSurveyNow()
    {
        ShowSurvey();
    }
    
    // Enhanced debug check
    [ContextMenu("Debug: Check Survey Status")]
    public void DebugCheckSurveyStatus()
    {
        if (_screenTimeManager != null)
        {
            float currentScreenTime = _screenTimeManager.GetTodayScreenTimeInMinutes() / 60f;
            Debug.Log($"[DEBUG] Current screen time: {currentScreenTime:F2}h");
            Debug.Log($"[DEBUG] Last check: {_lastScreenTimeCheck:F2}h");
            Debug.Log($"[DEBUG] Accumulated: {_screenTimeAccumulator:F2}h");
            Debug.Log($"[DEBUG] Target interval: {_currentSurveyIntervalHours}h");
            Debug.Log($"[DEBUG] Remaining: {Mathf.Max(0, _currentSurveyIntervalHours - _screenTimeAccumulator):F2}h");
        }
        else
        {
            Debug.Log("[DEBUG] ScreenTimeManager not found!");
        }
        
        // Survey count info
        if (CurrentUserId > 0)
        {
            int surveyCount = CountUserSurveys(CurrentUserId);
            Debug.Log($"[DEBUG] User #{CurrentUserId} has {surveyCount} surveys");
            
            bool canDoPatternAnalysis = surveyCount >= 2;
            Debug.Log($"[DEBUG] Pattern analysis possible: {canDoPatternAnalysis}");
            
            if (EyePatternProcessor.Instance != null)
            {
                Debug.Log($"[DEBUG] EyePatternProcessor is available");
                
                // Check cache status
                var cachedResult = EyePatternProcessor.Instance.GetCachedAnalysis(CurrentUserId);
                Debug.Log($"[DEBUG] Cached analysis available: {!string.IsNullOrEmpty(cachedResult.patterns)}");
                Debug.Log($"[DEBUG] Cached patterns: {cachedResult.patterns}");
            }
            else
            {
                Debug.Log($"[DEBUG] EyePatternProcessor NOT available");
            }
        }
    }
    
    // NEW: Get user's symptoms history
    public List<string> GetUserSymptomsHistory(int userId, int maxEntries = 5)
    {
        try
        {
            return _db.Table<EyeHealthLog>()
                .Where(x => x.user_id == userId)
                .OrderByDescending(x => x.date)
                .Take(maxEntries)
                .Select(x => x.symptoms)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}