using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;

public class EyePatternProcessor : MonoBehaviour
{
    // Singleton pattern for easy access
    public static EyePatternProcessor Instance { get; private set; }
    
    [Header("Configuration")]
    [SerializeField] private bool autoAnalyzeOnStart = true;
    [SerializeField] private int minSurveysForPatternAnalysis = 2;
    [SerializeField] private float analysisCheckInterval = 3600f; // Check every hour
    [SerializeField] private float headacheThresholdHours = 2f; // Minimum screen time for headache pattern
    [SerializeField] private int maxDaysToAnalyze = 7; // Analyze data from last 7 days
    
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;
    
    // Events for UI updates
    public event Action<string, string, float> OnAnalysisComplete;
    public event Action<string> OnAnalysisFailed;
    
    private Coroutine periodicAnalysisCoroutine;
    private Dictionary<int, (string patterns, string recommendations, float avgScreenTime)> _userAnalysisCache = new();
    
    // PERFORMANCE OPTIMIZATION: Cache and throttling
    private Dictionary<int, (List<EyeHealthLog> logs, DateTime timestamp)> _userLogsCache = new();
    private float _logsCacheDuration = 300f; // Cache logs for 5 minutes
    
    // FIXED: Changed from List<EyeTip> to List<string>
    private Dictionary<string, List<string>> _tipsByCategoryCache = new Dictionary<string, List<string>>();
    private DateTime _lastTipsCacheTime = DateTime.MinValue;
    private float _tipsCacheDuration = 600f; // Cache tips for 10 minutes
    private bool _isAnalyzing = false;
    private Coroutine _currentAnalysisCoroutine = null;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        if (autoAnalyzeOnStart)
        {
            StartCoroutine(InitializeWithDelay());
        }
    }
    
    private IEnumerator InitializeWithDelay()
    {
        // Wait for database and session manager to be ready
        yield return new WaitForSeconds(2f);
        
        if (DatabaseManager.Instance != null)
        {
            DatabaseManager.Instance.OnDatabaseReady += () => {
                if (enableDebugLogs) Debug.Log("[EyePatternProcessor] Database ready, starting analysis monitoring...");
                StartPeriodicAnalysisCheck();
            };
        }
        else
        {
            if (enableDebugLogs) Debug.LogError("[EyePatternProcessor] DatabaseManager not found!");
        }
    }
    
    private void StartPeriodicAnalysisCheck()
    {
        if (periodicAnalysisCoroutine != null)
            StopCoroutine(periodicAnalysisCoroutine);
        
        periodicAnalysisCoroutine = StartCoroutine(PeriodicAnalysisCheck());
    }
    
    private IEnumerator PeriodicAnalysisCheck()
    {
        // PERFORMANCE FIX: Add safety checks
        if (!gameObject.activeInHierarchy)
            yield break;
            
        while (gameObject.activeInHierarchy)
        {
            yield return new WaitForSeconds(analysisCheckInterval);
            
            if (SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn && gameObject.activeInHierarchy)
            {
                int userId = SessionManager.Instance.CurrentUser.id;
                
                // Check if we should analyze user's data
                if (ShouldAnalyzeUserData(userId))
                {
                    if (enableDebugLogs) Debug.Log("[EyePatternProcessor] Triggering periodic analysis...");
                    TriggerAnalysisForUser(userId);
                }
            }
            
            yield return null;
        }
    }
    
    private bool ShouldAnalyzeUserData(int userId)
    {
        // Check if we have cached results that are stale (older than 1 hour)
        if (_userAnalysisCache.TryGetValue(userId, out var cachedResult))
        {
            // Check if user has new surveys since last analysis
            DateTime lastWeek = DateTime.Now.AddDays(-7);
            var userLogs = GetUserLogsCached(userId, lastWeek);
            
            if (userLogs.Count > 0)
            {
                // Check if we have a recent survey (last 30 minutes)
                var latestLog = userLogs.FirstOrDefault();
                if (latestLog != null && DateTime.TryParse(latestLog.date, out DateTime logDate))
                {
                    TimeSpan timeSinceLog = DateTime.Now - logDate;
                    if (timeSinceLog.TotalMinutes < 30)
                    {
                        // User has recent data, should analyze
                        return true;
                    }
                }
            }
            
            // Use cached results for now
            return false;
        }
        
        // No cache, check if user has data
        return HasUserData(userId);
    }
    
    public void TriggerAnalysisForUser(int userId)
    {
        // PERFORMANCE FIX: Prevent multiple concurrent analyses
        if (_isAnalyzing)
        {
            if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Already analyzing, skipping user {userId}");
            return;
        }
        
        if (_currentAnalysisCoroutine != null)
        {
            StopCoroutine(_currentAnalysisCoroutine);
        }
        
        _currentAnalysisCoroutine = StartCoroutine(RunAnalysisForUserAsync(userId));
    }
    
    private IEnumerator RunAnalysisForUserAsync(int userId)
    {
        if (_isAnalyzing) yield break;
        
        _isAnalyzing = true;
        
        if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Starting async analysis for user {userId}");
        
        // Wait for database to be ready
        int maxWaitTime = 10;
        int waitCount = 0;
        
        while (DatabaseManager.Instance == null || !DatabaseManager.Instance.IsDatabaseReady())
        {
            if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Waiting for database... ({waitCount}/{maxWaitTime})");
            yield return new WaitForSeconds(1f);
            waitCount++;
            
            if (waitCount >= maxWaitTime)
            {
                if (enableDebugLogs) Debug.LogError("[EyePatternProcessor] Database timeout!");
                OnAnalysisFailed?.Invoke("Database timeout");
                _isAnalyzing = false;
                yield break;
            }
        }
        
        // Perform analysis
        var result = AnalyzeUserDataOptimized(userId);
        
        // Cache the results (in memory only, not in database)
        _userAnalysisCache[userId] = result;
        
        // Trigger event with analysis results
        OnAnalysisComplete?.Invoke(result.patterns, result.recommendations, result.avgScreenTime);
        
        if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Analysis complete for user {userId}");
        
        _isAnalyzing = false;
        _currentAnalysisCoroutine = null;
    }
    
    // MAIN PUBLIC METHOD: Analyzes user data and returns results (doesn't save to DB)
    public (string patterns, string recommendations, float avgScreenTime) AnalyzeUserData(int userId)
    {
        return AnalyzeUserDataOptimized(userId);
    }
    
    // PERFORMANCE OPTIMIZED VERSION
    private (string patterns, string recommendations, float avgScreenTime) AnalyzeUserDataOptimized(int userId)
    {
        if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Analyzing user #{userId} data");
        
        if (DatabaseManager.Instance == null)
        {
            string errorMsg = "Analysis failed: Database not available";
            if (enableDebugLogs) Debug.LogError($"[EyePatternProcessor] {errorMsg}");
            return (errorMsg, "Please check database connection", 0f);
        }
        
        if (userId <= 0)
        {
            string errorMsg = "Analysis failed: Invalid user ID";
            if (enableDebugLogs) Debug.LogWarning($"[EyePatternProcessor] {errorMsg}");
            return (errorMsg, "Please log in again", 0f);
        }

        try
        {
            // Get user's recent surveys (last X days) - using cache
            DateTime startDate = DateTime.Now.AddDays(-maxDaysToAnalyze);
            var userLogs = GetUserLogsCached(userId, startDate);
            
            if (userLogs.Count == 0)
            {
                string noDataMessage = "No survey data found.";
                string noDataRecommendation = "Complete a survey to get personalized insights.";
                if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] {noDataMessage}");
                return (noDataMessage, noDataRecommendation, 0f);
            }
            
            if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Found {userLogs.Count} logs for user {userId}");

            // Determine analysis type based on survey count
            if (userLogs.Count == 1)
            {
                // Single survey analysis
                return AnalyzeSingleSurveyOptimized(userLogs[0]);
            }
            else
            {
                // Pattern analysis
                return AnalyzePatternsOptimized(userLogs);
            }
        }
        catch (Exception ex)
        {
            string errorMsg = $"Analysis error: {ex.Message}";
            if (enableDebugLogs) Debug.LogError($"[EyePatternProcessor] {errorMsg}\n{ex.StackTrace}");
            return (errorMsg, "Please try again later", 0f);
        }
    }
    
    // PERFORMANCE OPTIMIZED: Single survey analysis
    private (string patterns, string recommendations, float avgScreenTime) AnalyzeSingleSurveyOptimized(EyeHealthLog survey)
    {
        if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Single survey analysis for log #{survey.id}");
        
        if (survey == null || string.IsNullOrEmpty(survey.symptoms))
        {
            return ("No symptoms reported", GetGeneralRecommendations((float)survey.screen_time_hours, 1), 0f);
        }
        
        List<string> symptoms = ParseSymptomsOptimized(survey.symptoms);
        
        // Check for "None" selection
        if (symptoms.Contains("None") || symptoms.Count == 0)
        {
            return ("No symptoms reported", GetGeneralRecommendations((float)survey.screen_time_hours, 1), (float)survey.screen_time_hours);
        }
        
        // For single survey: Just categorize symptoms and give immediate tips
        List<string> detectedCategories = new List<string>();
        List<string> recommendations = new List<string>();
        
        // Check each symptom
        foreach (string symptom in symptoms)
        {
            string category = GetSymptomCategory(symptom);
            if (!string.IsNullOrEmpty(category) && !detectedCategories.Contains(category))
            {
                detectedCategories.Add(category);
                
                // Get tips for this category (using cache)
                var tips = GetTipsForCategoryCached(category, 1);
                recommendations.AddRange(tips);
            }
        }
        
        // Format results
        string patterns = symptoms.Count == 1 
            ? $"Reported: {symptoms[0]}" 
            : $"Reported {symptoms.Count} symptoms";
        
        string finalRecommendations = recommendations.Count > 0
            ? string.Join("\n", recommendations.Take(2)) // Max 2 tips for single survey
            : GetGeneralRecommendations((float)survey.screen_time_hours, 1);
        
        if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Single survey analysis: {patterns}");
        
        return (patterns, finalRecommendations, (float)survey.screen_time_hours);
    }
    
    // PERFORMANCE OPTIMIZED: Pattern analysis
    private (string patterns, string recommendations, float avgScreenTime) AnalyzePatternsOptimized(List<EyeHealthLog> surveys)
    {
        if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Pattern analysis for {surveys.Count} surveys");
        
        // Analyze patterns across all surveys
        Dictionary<string, int> symptomFrequency = new Dictionary<string, int>();
        Dictionary<string, List<float>> symptomScreenTimes = new Dictionary<string, List<float>>();
        float totalScreenTime = 0f;
        
        foreach (var survey in surveys)
        {
            totalScreenTime += (float)survey.screen_time_hours;
            
            List<string> symptoms = ParseSymptomsOptimized(survey.symptoms);
            foreach (string symptom in symptoms)
            {
                if (symptom == "None") continue;
                
                string cleanSymptom = symptom.Trim();
                if (symptomFrequency.ContainsKey(cleanSymptom))
                    symptomFrequency[cleanSymptom]++;
                else
                    symptomFrequency[cleanSymptom] = 1;
                
                // Track screen time for this symptom
                if (!symptomScreenTimes.ContainsKey(cleanSymptom))
                    symptomScreenTimes[cleanSymptom] = new List<float>();
                symptomScreenTimes[cleanSymptom].Add((float)survey.screen_time_hours);
            }
        }
        
        float avgScreenTime = totalScreenTime / surveys.Count;
        
        // Find patterns (symptoms that appear in multiple surveys)
        List<string> patterns = new List<string>();
        List<string> recommendations = new List<string>();
        HashSet<string> addedCategories = new HashSet<string>();
        
        foreach (var kvp in symptomFrequency)
        {
            string symptom = kvp.Key;
            int frequency = kvp.Value;
            
            // For pattern analysis, we want symptoms that appear in at least 2 surveys
            if (frequency >= 2)
            {
                string category = GetSymptomCategory(symptom);
                float avgSymptomScreenTime = symptomScreenTimes.ContainsKey(symptom) 
                    ? symptomScreenTimes[symptom].Average() 
                    : 0f;
                
                string patternText = GetPatternText(symptom, frequency, surveys.Count, avgSymptomScreenTime);
                patterns.Add(patternText);
                
                // Get recommendations for this category (only once per category)
                if (!string.IsNullOrEmpty(category) && !addedCategories.Contains(category))
                {
                    addedCategories.Add(category);
                    var tips = GetTipsForCategoryCached(category, 2);
                    recommendations.AddRange(tips);
                }
            }
        }
        
        // Handle case with no clear patterns
        if (patterns.Count == 0)
        {
            if (avgScreenTime > 4f)
            {
                patterns.Add($"Consistent screen time: {avgScreenTime:F1}h average");
                recommendations.Add("Consider more frequent breaks during long sessions");
            }
            else
            {
                patterns.Add("No recurring patterns detected yet");
                recommendations.Add("Keep tracking for better insights");
            }
        }
        
        // Check for "None" responses
        int noneCount = surveys.Count(s => s.symptoms != null && s.symptoms.Contains("None"));
        if (noneCount >= surveys.Count / 2) // At least half of surveys had no symptoms
        {
            patterns.Add("Frequently no symptoms reported");
            if (avgScreenTime < 3f)
            {
                recommendations.Add("Great job maintaining healthy screen habits!");
            }
        }
        
        // Add general tips if needed
        if (recommendations.Count < 2)
        {
            recommendations.Add(GetGeneralRecommendations(avgScreenTime, surveys.Count));
        }
        
        // Format results
        string finalPatterns = patterns.Count > 0 
            ? string.Join(" • ", patterns)
            : "Analyzing patterns...";
        
        string finalRecommendations = string.Join("\n", recommendations.Distinct().Take(3));
        
        if (enableDebugLogs)
        {
            Debug.Log($"[EyePatternProcessor] Pattern analysis complete:");
            Debug.Log($"  Surveys: {surveys.Count}");
            Debug.Log($"  Patterns: {finalPatterns}");
            Debug.Log($"  Recommendations: {finalRecommendations}");
            Debug.Log($"  Avg Screen Time: {avgScreenTime:F1}h");
        }
        
        return (finalPatterns, finalRecommendations, avgScreenTime);
    }
    
    // PERFORMANCE OPTIMIZED: Get user logs with caching
    private List<EyeHealthLog> GetUserLogsCached(int userId, DateTime startDate)
    {
        string cacheKey = $"{userId}_{startDate:yyyyMMdd}";
        
        // Check cache first
        if (_userLogsCache.TryGetValue(userId, out var cached) && 
            (DateTime.Now - cached.timestamp).TotalSeconds < _logsCacheDuration)
        {
            // Filter cached logs by date
            var filteredLogs = cached.logs
                .Where(log => 
                {
                    if (DateTime.TryParse(log.date, out DateTime logDate))
                    {
                        return logDate >= startDate;
                    }
                    return false;
                })
                .OrderByDescending(x => 
                {
                    DateTime.TryParse(x.date, out DateTime logDate);
                    return logDate;
                })
                .ToList();
            
            return filteredLogs;
        }
        
        try
        {
            // Use the new GetAllWithUser method from DatabaseManager
            var allUserLogs = DatabaseManager.Instance.GetAllWithUser<EyeHealthLog>(userId);
            
            if (allUserLogs == null || allUserLogs.Count == 0)
            {
                // Cache empty result to prevent repeated queries
                _userLogsCache[userId] = (new List<EyeHealthLog>(), DateTime.Now);
                return new List<EyeHealthLog>();
            }
            
            // Cache all logs for this user
            _userLogsCache[userId] = (allUserLogs, DateTime.Now);
            
            // Filter by date and sort
            var filteredLogs = allUserLogs
                .Where(log => 
                {
                    if (DateTime.TryParse(log.date, out DateTime logDate))
                    {
                        return logDate >= startDate;
                    }
                    return false;
                })
                .OrderByDescending(x => 
                {
                    DateTime.TryParse(x.date, out DateTime logDate);
                    return logDate;
                })
                .ToList();
            
            return filteredLogs;
        }
        catch (Exception ex)
        {
            if (enableDebugLogs) Debug.LogError($"[EyePatternProcessor] Error getting user logs: {ex.Message}");
            return new List<EyeHealthLog>();
        }
    }
    
    // Original method for backward compatibility
    private List<EyeHealthLog> GetUserLogs(int userId, DateTime startDate)
    {
        return GetUserLogsCached(userId, startDate);
    }
    
    // PERFORMANCE OPTIMIZED: Parse symptoms
    private List<string> ParseSymptomsOptimized(string symptomsText)
    {
        if (string.IsNullOrEmpty(symptomsText))
            return new List<string>();
        
        // Use simple split for better performance
        return symptomsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .ToList();
    }
    
    // Original method for backward compatibility
    private List<string> ParseSymptoms(string symptomsText)
    {
        return ParseSymptomsOptimized(symptomsText);
    }
    
    private string GetSymptomCategory(string symptom)
    {
        // PERFORMANCE FIX: Use switch statement for faster matching
        string lowerSymptom = symptom.ToLower();
        
        if (lowerSymptom.Contains("headache"))
            return "headache";
        if (lowerSymptom.Contains("dry"))
            return "dry eyes";
        if (lowerSymptom.Contains("blurry"))
            return "blurry vision";
        if (lowerSymptom.Contains("dizziness"))
            return "dizziness";
        if (lowerSymptom.Contains("glare"))
            return "glare";
        
        return "general";
    }
    
    private string GetPatternText(string symptom, int frequency, int totalSurveys, float avgScreenTime)
    {
        string baseText = $"{symptom} in {frequency}/{totalSurveys} surveys";
        
        // Add context based on symptom type
        string lowerSymptom = symptom.ToLower();
        
        if (lowerSymptom.Contains("headache") && avgScreenTime > headacheThresholdHours)
        {
            return $"{baseText} (often after {avgScreenTime:F1}h+ screen time)";
        }
        
        if (lowerSymptom.Contains("dry") && avgScreenTime > 3f)
        {
            return $"{baseText} (during long {avgScreenTime:F1}h sessions)";
        }
        
        return baseText;
    }
    
    // PERFORMANCE OPTIMIZED: Get tips with caching
    private List<string> GetTipsForCategoryCached(string category, int count)
    {
        string categoryKey = category.ToLower();
        
        // Check if tips cache is stale
        if ((DateTime.Now - _lastTipsCacheTime).TotalSeconds > _tipsCacheDuration)
        {
            _tipsByCategoryCache.Clear();
        }
        
        // Check cache first
        if (_tipsByCategoryCache.TryGetValue(categoryKey, out var cachedTips))
        {
            return cachedTips.Take(count).ToList();
        }
        
        List<string> tips = new List<string>();
        
        try
        {
            var dbTips = DatabaseManager.Instance.GetAll<EyeTip>()
                .Where(t => t.category != null && 
                           t.category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.created_at)
                .Take(count)
                .ToList();
            
            foreach (var tip in dbTips)
            {
                string tipText = $"{tip.title}: {tip.content}";
                if (!string.IsNullOrEmpty(tip.link))
                {
                    tipText += $" | {tip.link}";
                }
                tips.Add(tipText);
            }
            
            // Cache the tips for this category
            if (tips.Count > 0)
            {
                _tipsByCategoryCache[categoryKey] = tips;
                _lastTipsCacheTime = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            if (enableDebugLogs) Debug.LogWarning($"[EyePatternProcessor] Tips query error: {ex.Message}");
        }
        
        // Fallback if no tips found
        if (tips.Count == 0)
        {
            tips.Add(GetFallbackTip(category));
        }
        
        return tips;
    }
    
    // Original method for backward compatibility
    private List<string> GetTipsForCategory(string category, int count)
    {
        return GetTipsForCategoryCached(category, count);
    }
    
    private string GetFallbackTip(string category)
    {
        // PERFORMANCE FIX: Use dictionary for faster lookup
        var fallbackTips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "headache", "Headache relief: Take a 5-minute break and look into the distance" },
            { "dry eyes", "Dry eyes: Blink consciously 10 times rapidly" },
            { "blurry vision", "Blurry vision: Follow the 20-20-20 rule every 20 minutes" },
            { "dizziness", "Dizziness: Stand up slowly and stretch your neck" },
            { "glare", "Glare sensitivity: Adjust screen brightness to match room lighting" }
        };
        
        if (fallbackTips.TryGetValue(category, out string tip))
            return tip;
        
        return "General: Take regular screen breaks";
    }
    
    private string GetGeneralRecommendations(float screenTime, int surveyCount)
    {
        if (screenTime > 6f)
            return surveyCount == 1 
                ? "Consider reducing daily screen time" 
                : "Consistently high screen time - consider setting limits";
        
        if (screenTime > 3f)
            return "Remember to take regular breaks during long sessions";
        
        return "Good screen time management";
    }
    
    private bool HasUserData(int userId)
    {
        try
        {
            DateTime weekAgo = DateTime.Now.AddDays(-7);
            var userLogs = GetUserLogsCached(userId, weekAgo);
            return userLogs.Count > 0;
        }
        catch (Exception ex)
        {
            if (enableDebugLogs) Debug.LogError($"[EyePatternProcessor] Error checking data: {ex.Message}");
            return false;
        }
    }
    
    public bool HasEnoughDataForPatternAnalysis(int userId)
    {
        try
        {
            DateTime weekAgo = DateTime.Now.AddDays(-7);
            var userLogs = GetUserLogsCached(userId, weekAgo);
            bool hasEnough = userLogs.Count >= minSurveysForPatternAnalysis;
            
            if (enableDebugLogs) 
                Debug.Log($"[EyePatternProcessor] User {userId} has {userLogs.Count}/{minSurveysForPatternAnalysis} surveys: {hasEnough}");
            
            return hasEnough;
        }
        catch (Exception ex)
        {
            if (enableDebugLogs) Debug.LogError($"[EyePatternProcessor] Error checking pattern data: {ex.Message}");
            return false;
        }
    }
    
    public int GetUserSurveyCount(int userId)
    {
        try
        {
            DateTime weekAgo = DateTime.Now.AddDays(-7);
            var userLogs = GetUserLogsCached(userId, weekAgo);
            return userLogs.Count;
        }
        catch
        {
            return 0;
        }
    }
    
    // Public method for manual trigger
    public void TriggerAnalysisForCurrentUser()
    {
        if (SessionManager.Instance == null || !SessionManager.Instance.IsLoggedIn)
        {
            if (enableDebugLogs) Debug.LogWarning("[EyePatternProcessor] No user logged in for analysis.");
            return;
        }
        
        int userId = SessionManager.Instance.CurrentUser.id;
        TriggerAnalysisForUser(userId);
    }
    
    // NEW: Get cached analysis for user (for UI display)
    public (string patterns, string recommendations, float avgScreenTime) GetCachedAnalysis(int userId)
    {
        if (_userAnalysisCache.TryGetValue(userId, out var cachedResult))
        {
            return cachedResult;
        }
        
        // If no cache, return empty results
        return ("No analysis available", "Complete a survey to get insights", 0f);
    }
    
    // NEW: Clear cache for user (when new survey is submitted)
    public void ClearUserCache(int userId)
    {
        if (_userAnalysisCache.ContainsKey(userId))
        {
            _userAnalysisCache.Remove(userId);
            if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Cleared analysis cache for user {userId}");
        }
        
        // Also clear user logs cache
        if (_userLogsCache.ContainsKey(userId))
        {
            _userLogsCache.Remove(userId);
            if (enableDebugLogs) Debug.Log($"[EyePatternProcessor] Cleared logs cache for user {userId}");
        }
    }
    
    // NEW: Clear all caches (call on logout)
    public void ClearAllCaches()
    {
        _userAnalysisCache.Clear();
        _userLogsCache.Clear();
        _tipsByCategoryCache.Clear();
        
        if (enableDebugLogs) Debug.Log("[EyePatternProcessor] Cleared all caches");
    }
    
    // NEW: Analyze specific survey (for immediate feedback) - no DB save
    public (string patterns, string recommendations) AnalyzeSingleSurveyImmediate(EyeHealthLog survey)
    {
        var result = AnalyzeSingleSurveyOptimized(survey);
        return (result.patterns, result.recommendations);
    }
    
    // Clean up on destroy
    private void OnDestroy()
    {
        if (periodicAnalysisCoroutine != null)
            StopCoroutine(periodicAnalysisCoroutine);
            
        if (_currentAnalysisCoroutine != null)
            StopCoroutine(_currentAnalysisCoroutine);
    }
    
    // NEW: Get analysis summary for UI
    public string GetAnalysisSummary(int userId)
    {
        var result = AnalyzeUserDataOptimized(userId);
        return $"Patterns: {result.patterns}\nAvg Screen Time: {result.avgScreenTime:F1}h";
    }
    
    // NEW: Format screen time helper
    public static string FormatScreenTime(float hours)
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
    
    // NEW: Performance monitoring
    public string GetCacheStats()
    {
        return $"Analysis Cache: {_userAnalysisCache.Count} users, " +
               $"Logs Cache: {_userLogsCache.Count} users, " +
               $"Tips Cache: {_tipsByCategoryCache.Count} categories";
    }
}