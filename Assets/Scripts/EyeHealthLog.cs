using System.Linq;
using UnityEngine;
using TMPro;
using System;
using System.Collections;

public class EyeHealthLogUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI dateTimeText;
    [SerializeField] private TextMeshProUGUI symptomsText;
    [SerializeField] private TextMeshProUGUI patternDetectedText;
    [SerializeField] private TextMeshProUGUI recommendationOneText;
    [SerializeField] private TextMeshProUGUI recommendationTwoText;
    [SerializeField] private TextMeshProUGUI recommendationThreeText;

    [Header("Analysis Info")]
    [SerializeField] private TextMeshProUGUI analysisTypeText;
    [SerializeField] private TextMeshProUGUI surveyCountText;
    [SerializeField] private GameObject analysisInfoPanel;

    [Header("Auto Refresh")]
    [SerializeField] private bool autoRefreshOnAnalysis = true;
    [SerializeField] private float refreshInterval = 5f; // Check for updates every 5 seconds

    private EyeHealthLog _currentLog;
    private Coroutine _refreshCoroutine;
    private int _currentUserId;
    
    // Store live analysis results separately
    private string _currentPatterns = "";
    private string _currentRecommendations = "";
    private float _currentAvgScreenTime = 0f;
    
    // PERFORMANCE OPTIMIZATION: Cache and throttling
    private float _lastDateTimeUpdate = 0f;
    private float _dateTimeUpdateInterval = 1f; // Update time every second instead of every minute
    private bool _isProcessingRefresh = false;
    private DateTime _lastLogLoadTime = DateTime.MinValue;
    private float _logLoadCooldown = 2f; // Don't load logs more than once every 2 seconds
    private int _cachedSurveyCount = -1; // Cache survey count
    private float _lastSurveyCountUpdate = 0f;
    private float _surveyCountCacheDuration = 10f; // Cache survey count for 10 seconds

    private void Start()
    {
        // PERFORMANCE FIX: Use Update() for time instead of coroutine
        if (dateTimeText != null)
        {
            UpdateDateTime(); // Initial update
        }

        // Wait for DB to be ready
        if (DatabaseManager.Instance != null)
        {
            DatabaseManager.Instance.OnDatabaseReady += InitializeUI;
        }
        else
        {
            Debug.LogError("[EyeHealthLogUI] DatabaseManager instance not found!");
        }

        // Subscribe to analysis events
        if (EyePatternProcessor.Instance != null)
        {
            EyePatternProcessor.Instance.OnAnalysisComplete += OnAnalysisComplete;
            EyePatternProcessor.Instance.OnAnalysisFailed += OnAnalysisFailed;
        }
        else
        {
            Debug.LogWarning("[EyeHealthLogUI] EyePatternProcessor instance not found. Analysis updates won't work.");
        }

        // Listen for user login
        SessionManager.OnSessionStarted += OnUserLoggedIn;
        
        // If already logged in, initialize
        if (SessionManager.Instance?.IsLoggedIn == true)
        {
            OnUserLoggedIn(SessionManager.Instance.CurrentUser);
        }
    }
    
    private void Update()
    {
        // PERFORMANCE FIX: Update time every second instead of coroutine
        if (dateTimeText != null && Time.time - _lastDateTimeUpdate >= _dateTimeUpdateInterval)
        {
            _lastDateTimeUpdate = Time.time;
            dateTimeText.text = DateTime.Now.ToString("MMMM dd, yyyy - hh:mm tt");
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        SessionManager.OnSessionStarted -= OnUserLoggedIn;
        
        if (EyePatternProcessor.Instance != null)
        {
            EyePatternProcessor.Instance.OnAnalysisComplete -= OnAnalysisComplete;
            EyePatternProcessor.Instance.OnAnalysisFailed -= OnAnalysisFailed;
        }
        
        if (_refreshCoroutine != null)
            StopCoroutine(_refreshCoroutine);
    }

    private void OnUserLoggedIn(User user)
    {
        if (user == null) return;
        
        _currentUserId = user.id;
        Debug.Log($"[EyeHealthLogUI] User logged in: {user.id}");
        
        // Clear previous analysis and cache
        _currentPatterns = "";
        _currentRecommendations = "";
        _currentAvgScreenTime = 0f;
        _cachedSurveyCount = -1; // Invalidate cache
        
        // Load data for this user
        LoadLatestLog();
        
        // Start auto-refresh if enabled
        if (autoRefreshOnAnalysis && _refreshCoroutine == null)
        {
            _refreshCoroutine = StartCoroutine(AutoRefreshRoutine());
        }
        
        // Trigger analysis when UI loads for a logged-in user
        StartCoroutine(TriggerAnalysisWithDelay(1f));
    }

    private void InitializeUI()
    {
        Debug.Log("[EyeHealthLogUI] Database ready, initializing UI...");
        
        // Hide analysis info panel initially
        if (analysisInfoPanel != null)
            analysisInfoPanel.SetActive(false);
            
        // Load data if user is already logged in
        if (SessionManager.Instance?.IsLoggedIn == true)
        {
            OnUserLoggedIn(SessionManager.Instance.CurrentUser);
        }
    }

    private IEnumerator TriggerAnalysisWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (EyePatternProcessor.Instance != null && _currentUserId > 0)
        {
            Debug.Log($"[EyeHealthLogUI] Triggering analysis for user {_currentUserId}");
            EyePatternProcessor.Instance.TriggerAnalysisForCurrentUser();
        }
    }

    private IEnumerator AutoRefreshRoutine()
    {
        // PERFORMANCE FIX: Don't run if not needed
        if (!autoRefreshOnAnalysis || !gameObject.activeInHierarchy)
            yield break;
            
        while (gameObject.activeInHierarchy && autoRefreshOnAnalysis)
        {
            yield return new WaitForSeconds(refreshInterval);
            
            // Check for new analysis data (trigger fresh analysis)
            if (_currentUserId > 0 && EyePatternProcessor.Instance != null && 
                !_isProcessingRefresh && gameObject.activeInHierarchy)
            {
                EyePatternProcessor.Instance.TriggerAnalysisForCurrentUser();
            }
        }
    }

    private void LoadLatestLog()
    {
        // PERFORMANCE FIX: Throttle log loading
        if ((DateTime.Now - _lastLogLoadTime).TotalSeconds < _logLoadCooldown && _currentLog != null)
        {
            return; // Too soon to load again
        }
        
        _lastLogLoadTime = DateTime.Now;
        
        if (SessionManager.Instance == null || !SessionManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("[EyeHealthLogUI] No user logged in. Cannot load logs.");
            SetNoLogsText();
            return;
        }

        // 1. Load latest log (symptoms only from database)
        _currentLog = DatabaseManager.Instance.GetLatestWithUser<EyeHealthLog>();

        if (_currentLog != null)
        {
            // Display symptoms from database
            DisplayLog(_currentLog);
            
            // 2. Display live analysis results if available
            if (!string.IsNullOrEmpty(_currentPatterns) && !string.IsNullOrEmpty(_currentRecommendations))
            {
                UpdateLiveAnalysisUI();
            }
            else
            {
                // Trigger fresh analysis if we don't have live results yet
                if (EyePatternProcessor.Instance != null)
                {
                    EyePatternProcessor.Instance.TriggerAnalysisForCurrentUser();
                }
            }
        }
        else
        {
            SetNoLogsText();
        }
    }

    private void DisplayLog(EyeHealthLog log)
    {
        if (log == null) return;
        
        // Format screen time nicely
        string screenTimeText = FormatScreenTime((float)log.screen_time_hours);
        
        // PERFORMANCE FIX: Only update if text changed
        if (symptomsText != null && symptomsText.text != $"😵 {log.symptoms} | After {screenTimeText} screen time")
        {
            symptomsText.text = $"😵 {log.symptoms} | After {screenTimeText} screen time";
        }
        
        // Display pattern analysis from live results (NOT from database)
        if (patternDetectedText != null)
        {
            string newPatternText = !string.IsNullOrEmpty(_currentPatterns) 
                ? _currentPatterns 
                : "Analyzing patterns...";
                
            if (patternDetectedText.text != newPatternText)
            {
                patternDetectedText.text = newPatternText;
            }
        }
        
        // Display recommendations from live results (NOT from database)
        if (!string.IsNullOrEmpty(_currentRecommendations))
        {
            UpdateRecommendationsUI(_currentRecommendations);
        }
        else
        {
            ClearRecommendations();
        }
        
        // Update analysis info based on live results
        UpdateAnalysisInfo();
    }

    private void UpdateLiveAnalysisUI()
    {
        if (patternDetectedText != null && !string.IsNullOrEmpty(_currentPatterns) && 
            patternDetectedText.text != _currentPatterns)
        {
            patternDetectedText.text = _currentPatterns;
        }
        
        if (!string.IsNullOrEmpty(_currentRecommendations))
        {
            UpdateRecommendationsUI(_currentRecommendations);
        }
        
        UpdateAnalysisInfo();
    }

    private void UpdateAnalysisInfo()
    {
        if (analysisInfoPanel == null) return;
        
        // Count user's surveys for info display (with caching)
        int surveyCount = GetCachedSurveyCount(_currentUserId);
        
        // Show analysis info panel if we have live analysis
        bool hasAnalysis = !string.IsNullOrEmpty(_currentPatterns) && 
                          !string.IsNullOrEmpty(_currentRecommendations);
        
        // PERFORMANCE FIX: Only update if state changed
        if (analysisInfoPanel.activeSelf != hasAnalysis)
        {
            analysisInfoPanel.SetActive(hasAnalysis);
        }
        
        if (hasAnalysis)
        {
            // Determine analysis type based on survey count
            string analysisType = surveyCount == 1 ? "Single Survey Analysis" : "Pattern Analysis";
            
            if (analysisTypeText != null && analysisTypeText.text != analysisType)
                analysisTypeText.text = analysisType;
            
            if (surveyCountText != null)
            {
                string newCountText = $"Based on {surveyCount} survey{(surveyCount != 1 ? "s" : "")}";
                if (surveyCountText.text != newCountText)
                    surveyCountText.text = newCountText;
            }
        }
    }

    private int GetCachedSurveyCount(int userId)
    {
        // Check cache first
        if (_cachedSurveyCount >= 0 && Time.time - _lastSurveyCountUpdate < _surveyCountCacheDuration)
        {
            return _cachedSurveyCount;
        }
        
        try
        {
            var userLogs = DatabaseManager.Instance.GetAllWithUser<EyeHealthLog>(userId);
            _cachedSurveyCount = userLogs?.Count ?? 0;
            _lastSurveyCountUpdate = Time.time;
            return _cachedSurveyCount;
        }
        catch
        {
            return 0;
        }
    }

    private void UpdateRecommendationsUI(string recommendations)
    {
        if (string.IsNullOrEmpty(recommendations))
        {
            ShowDefaultRecommendations();
            return;
        }

        // Split by pipe (as returned by processor) or newline
        string[] tips = recommendations.Contains('\n') 
            ? recommendations.Split('\n') 
            : recommendations.Split('|');

        // PERFORMANCE FIX: Only update if text changed
        if (recommendationOneText != null)
        {
            string newText1 = tips.Length > 0 ? FormatRecommendation(tips[0]) : "";
            if (recommendationOneText.text != newText1)
                recommendationOneText.text = newText1;
        }
        
        if (recommendationTwoText != null)
        {
            string newText2 = tips.Length > 1 ? FormatRecommendation(tips[1]) : "";
            if (recommendationTwoText.text != newText2)
                recommendationTwoText.text = newText2;
        }
        
        if (recommendationThreeText != null)
        {
            string newText3 = tips.Length > 2 ? FormatRecommendation(tips[2]) : "";
            if (recommendationThreeText.text != newText3)
                recommendationThreeText.text = newText3;
        }
    }

    private string FormatRecommendation(string recommendation)
    {
        if (string.IsNullOrEmpty(recommendation))
            return "";
            
        // Clean up the recommendation text
        string clean = recommendation.Trim();
        
        // Remove any "Title: " prefix if present and too long
        if (clean.Contains(":") && clean.IndexOf(":") < 20)
        {
            clean = clean.Substring(clean.IndexOf(":") + 1).Trim();
        }
        
        // Capitalize first letter
        if (clean.Length > 0)
        {
            clean = char.ToUpper(clean[0]) + clean.Substring(1);
        }
        
        // Truncate if too long
        if (clean.Length > 80)
        {
            clean = clean.Substring(0, 77) + "...";
        }
        
        return clean;
    }

    private string FormatScreenTime(float hours)
    {
        int h = Mathf.FloorToInt(hours);
        int m = Mathf.FloorToInt((hours - h) * 60f);
        
        if (h > 0 && m > 0)
            return $"{h}h {m}m";
        else if (h > 0)
            return $"{h}h";
        else
            return $"{m}m";
    }

    private void ShowDefaultRecommendations()
    {
        // PERFORMANCE FIX: Only update if text changed
        if (recommendationOneText != null && recommendationOneText.text != "Take regular breaks every 20-30 minutes")
            recommendationOneText.text = "Take regular breaks every 20-30 minutes";
            
        if (recommendationTwoText != null && recommendationTwoText.text != "Adjust screen brightness to match room lighting")
            recommendationTwoText.text = "Adjust screen brightness to match room lighting";
            
        if (recommendationThreeText != null && recommendationThreeText.text != "Blink frequently to keep eyes moist")
            recommendationThreeText.text = "Blink frequently to keep eyes moist";
    }

    private void ClearRecommendations()
    {
        // PERFORMANCE FIX: Only update if not already empty
        if (recommendationOneText != null && !string.IsNullOrEmpty(recommendationOneText.text))
            recommendationOneText.text = "";
            
        if (recommendationTwoText != null && !string.IsNullOrEmpty(recommendationTwoText.text))
            recommendationTwoText.text = "";
            
        if (recommendationThreeText != null && !string.IsNullOrEmpty(recommendationThreeText.text))
            recommendationThreeText.text = "";
    }

    private void SetNoLogsText()
    {
        if (symptomsText != null && symptomsText.text != "No surveys completed yet.")
            symptomsText.text = "No surveys completed yet.";
            
        if (patternDetectedText != null && patternDetectedText.text != "-")
            patternDetectedText.text = "-";
            
        ClearRecommendations();
        
        if (analysisInfoPanel != null && analysisInfoPanel.activeSelf)
            analysisInfoPanel.SetActive(false);
    }

    // Event Handlers for Analysis Updates
    private void OnAnalysisComplete(string patterns, string recommendations, float avgScreenTime)
    {
        Debug.Log("[EyeHealthLogUI] Live analysis received!");
        Debug.Log($"Patterns: {patterns}");
        Debug.Log($"Recommendations: {recommendations}");
        Debug.Log($"Avg Screen Time: {avgScreenTime}");
        
        // Store live analysis results
        _currentPatterns = patterns;
        _currentRecommendations = recommendations;
        _currentAvgScreenTime = avgScreenTime;
        
        // Update the UI with live analysis results
        if (patternDetectedText != null)
            patternDetectedText.text = patterns;
            
        UpdateRecommendationsUI(recommendations);
        
        // Refresh the display to show symptoms + live analysis
        LoadLatestLog();
        
        Debug.Log("[EyeHealthLogUI] Displaying live analysis results (not saved to DB)");
    }

    private void OnAnalysisFailed(string error)
    {
        Debug.LogWarning($"[EyeHealthLogUI] Analysis failed: {error}");
        
        if (patternDetectedText != null)
            patternDetectedText.text = "Analysis failed. Please try again.";
        
        // Show default recommendations
        ShowDefaultRecommendations();
    }

    // Public Methods for UI Buttons
    public void RefreshData()
    {
        if (_isProcessingRefresh) return;
        
        Debug.Log("[EyeHealthLogUI] Manually refreshing data...");
        _isProcessingRefresh = true;
        
        try
        {
            LoadLatestLog();
            
            if (EyePatternProcessor.Instance != null && _currentUserId > 0)
            {
                EyePatternProcessor.Instance.TriggerAnalysisForCurrentUser();
            }
        }
        finally
        {
            _isProcessingRefresh = false;
        }
    }

    public void ForceAnalysis()
    {
        if (EyePatternProcessor.Instance != null && _currentUserId > 0)
        {
            Debug.Log("[EyeHealthLogUI] Forcing fresh analysis...");
            
            // Clear previous results for fresh start
            _currentPatterns = "";
            _currentRecommendations = "";
            _currentAvgScreenTime = 0f;
            _cachedSurveyCount = -1; // Invalidate cache
            
            EyePatternProcessor.Instance.TriggerAnalysisForCurrentUser();
        }
        else
        {
            Debug.LogWarning("[EyeHealthLogUI] Cannot force analysis - processor or user not available");
        }
    }

    public void ShowSurveyHistory()
    {
        // You could implement a history view here
        Debug.Log("[EyeHealthLogUI] Survey history requested");
        
        try
        {
            var allLogs = DatabaseManager.Instance.GetAllWithUser<EyeHealthLog>(_currentUserId);
            if (allLogs != null)
            {
                Debug.Log($"[EyeHealthLogUI] User has {allLogs.Count} total surveys");
                // Only show first 5 for performance
                foreach (var log in allLogs.OrderByDescending(x => x.date).Take(5))
                {
                    Debug.Log($"  - #{log.id}: {log.date} - {log.symptoms}");
                }
                if (allLogs.Count > 5)
                {
                    Debug.Log($"  ... and {allLogs.Count - 5} more");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EyeHealthLogUI] Error getting history: {ex.Message}");
        }
    }
    
    // PERFORMANCE FIX: Removed the infinite coroutine UpdateDateTime()
    private void UpdateDateTime()
    {
        if (dateTimeText != null)
        {
            dateTimeText.text = DateTime.Now.ToString("MMMM dd, yyyy - hh:mm tt");
            _lastDateTimeUpdate = Time.time;
        }
    }
}