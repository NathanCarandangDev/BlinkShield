using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Unity ↔ Android bridge for BlinkShield ScreenTime tracking.
/// Maintains live sync, AFK detection, background resilience, and correct break logic.
/// Now includes weekly data support for Progress Manager UI and scheduling control APIs.
/// </summary>
public class DeviceScreenTime : MonoBehaviour
{
    public static DeviceScreenTime Instance { get; private set; }

    [Tooltip("How often to poll plugin for updates (seconds).")]
    public float refreshInterval = 1f;

    [SerializeField] private string userId = "";

    private long totalScreenSecondsToday;
    private int eyeScoreToday = 100;
    private int breaksTakenToday;

    // 🕒 Track when the last day was updated
    private DateTime lastKnownDate;

    // 📊 Weekly data for Progress Manager
    private WeeklyScreenTimeData weeklyData = new WeeklyScreenTimeData();
    private bool weeklyDataLoaded = false;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaClass pluginClass;
    private AndroidJavaObject activity;
#endif

    private Coroutine updateRoutine;
    private float lastRefreshTime;

    // Events for UI updates - explicitly initialized to avoid null reference issues
    public event Action OnDailyDataUpdated = delegate { };
    public event Action OnWeeklyDataUpdated = delegate { };
    public event Action OnDatabaseStatusUpdated = delegate { }; // NEW: Added this event

    [System.Serializable]
    public class DailyData
    {
        public string date;
        public int screenTime; // minutes
        public int breaks;
        public int eyeScore;

        public DailyData()
        {
            date = "";
            screenTime = 0;
            breaks = 0;
            eyeScore = 100;
        }
    }

    [System.Serializable]
    public class WeeklyScreenTimeData
    {
        public List<DailyData> days = new List<DailyData>();
        public int totalScreenTime = 0;
        public int averageScreenTime = 0;

        public void Clear()
        {
            days.Clear();
            totalScreenTime = 0;
            averageScreenTime = 0;
        }
    }

    // ONE ROW PER DAY Database tracking variables
    private bool _hasTodayRecord = false;
    private float _todayScreenTimeFromDB = 0f;
    private bool _shouldSyncToday = false;
    private string _databaseStatus = "";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        lastKnownDate = DateTime.Today; // 🌅 initialize day tracker

#if UNITY_ANDROID && !UNITY_EDITOR
        InitializePlugin();
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void InitializePlugin()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            pluginClass = new AndroidJavaClass("com.blinkshield.screentimetracker.ScreenTimePlugin");
            Debug.Log("[DeviceScreenTime] Plugin bridge initialized.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DeviceScreenTime] Failed to initialize plugin: {ex}");
        }
    }
#endif

    /// <summary>
    /// Initialize plugin for a specific user and start tracking.
    /// </summary>
    public void InitializeForUser(string newUserId)
    {
        if (string.IsNullOrEmpty(newUserId))
        {
            Debug.LogWarning("[DeviceScreenTime] InitializeForUser() called with empty user ID.");
            return;
        }

        userId = newUserId;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass == null || activity == null)
            InitializePlugin();

        Debug.Log($"[DeviceScreenTime] Starting tracking service for user: {userId}");
        SafeCallStatic("startTracking", activity, userId);

        UpdateScreenTimeData();
        LoadWeeklyData(); // Load weekly data on initialization
        
        // Load ONE ROW PER DAY status
        UpdateDatabaseStatus();
#endif

        StartUpdateRoutine();
    }

    private void StartUpdateRoutine()
    {
        if (updateRoutine != null) 
            StopCoroutine(updateRoutine);
        updateRoutine = StartCoroutine(UpdateDataRoutine());
    }

    private IEnumerator UpdateDataRoutine()
    {
        while (true)
        {
            // 🌄 Detect day change and reset
            if (DateTime.Today != lastKnownDate)
            {
                Debug.Log("[DeviceScreenTime] 🌅 New day detected — resetting daily stats");
                ResetToday();
                lastKnownDate = DateTime.Today;
                
                // Reload weekly data when day changes
                LoadWeeklyData();
                
                // Update database status
                UpdateDatabaseStatus();
            }

            UpdateScreenTimeData();
            
            // Update database status periodically
            if (Time.time % 30f < 0.1f) // Every 30 seconds
            {
                UpdateDatabaseStatus();
            }
            
            yield return new WaitForSeconds(refreshInterval);
        }
    }

    /// <summary>
    /// Refreshes local cached stats from the plugin.
    /// </summary>
    public void UpdateScreenTimeData()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(userId) || pluginClass == null || activity == null) 
            return;

        try
        {
            totalScreenSecondsToday = SafeCallStatic<long>("getTodayScreenTime", activity, userId) / 1000L;
            breaksTakenToday = SafeCallStatic<int>("getTodayBreaks", activity, userId);
            eyeScoreToday = SafeCallStatic<int>("getTodayEyeScore", activity, userId);

            if (Time.time - lastRefreshTime > 10f)
            {
                Debug.Log($"[DeviceScreenTime] ⏱ {totalScreenSecondsToday}s | 🧘 Breaks: {breaksTakenToday} | 👁️ EyeScore: {eyeScoreToday}");
                lastRefreshTime = Time.time;
            }

            // Notify subscribers of data update - use try/catch to prevent event exceptions from breaking the update loop
            SafeInvokeDailyDataUpdated();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error updating data: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// Update ONE ROW PER DAY database status
    /// </summary>
    private void UpdateDatabaseStatus()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(userId) || pluginClass == null || activity == null) 
            return;

        try
        {
            // Check if we have a record for today
            _hasTodayRecord = SafeCallStatic<bool>("hasTodayRecord", activity, userId);
            
            // Get today's screen time from database
            _todayScreenTimeFromDB = SafeCallStatic<float>("getTodayScreenTimeFromDatabase", activity, userId);
            
            // Check if we should sync today
            _shouldSyncToday = SafeCallStatic<bool>("shouldSyncToday", activity, userId);
            
            // Get database status string
            _databaseStatus = SafeCallStatic<string>("getDatabaseStatus", activity, userId);
            
            // Notify subscribers
            SafeInvokeDatabaseStatusUpdated();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error updating database status: {e.Message}");
            _databaseStatus = $"Error: {e.Message}";
        }
#endif
    }

    /// <summary>
    /// Safely invoke the daily data updated event with exception handling
    /// </summary>
    private void SafeInvokeDailyDataUpdated()
    {
        try
        {
            OnDailyDataUpdated?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DeviceScreenTime] Error in OnDailyDataUpdated event handler: {e}");
        }
    }

    /// <summary>
    /// Safely invoke the weekly data updated event with exception handling
    /// </summary>
    private void SafeInvokeWeeklyDataUpdated()
    {
        try
        {
            OnWeeklyDataUpdated?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DeviceScreenTime] Error in OnWeeklyDataUpdated event handler: {e}");
        }
    }

    /// <summary>
    /// Safely invoke the database status updated event with exception handling
    /// </summary>
    private void SafeInvokeDatabaseStatusUpdated()
    {
        try
        {
            OnDatabaseStatusUpdated?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DeviceScreenTime] Error in OnDatabaseStatusUpdated event handler: {e}");
        }
    }

    /// <summary>
    /// Load weekly data for Progress Manager UI
    /// </summary>
    public void LoadWeeklyData()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(userId) || pluginClass == null || activity == null) 
            return;

        try
        {
            // Method 1: Get JSON and parse it
            string weeklyJson = SafeCallStatic<string>("getWeeklyDataJson", activity, userId);
            if (!string.IsNullOrEmpty(weeklyJson))
            {
                ParseWeeklyJsonData(weeklyJson);
            }

            // Method 2: Get pre-parsed arrays (alternative approach)
            int[] screenTimes = SafeCallStatic<int[]>("getWeeklyScreenTimes", activity, userId);
            int[] breaks = SafeCallStatic<int[]>("getWeeklyBreaks", activity, userId);
            
            if (screenTimes != null && screenTimes.Length == 7)
            {
                UpdateWeeklyDataFromArrays(screenTimes, breaks);
            }

            // Get weekly totals
            weeklyData.totalScreenTime = SafeCallStatic<int>("getWeeklyTotalScreenTime", activity, userId);
            weeklyData.averageScreenTime = SafeCallStatic<int>("getWeeklyAverageScreenTime", activity, userId);

            weeklyDataLoaded = true;
            Debug.Log($"[DeviceScreenTime] Weekly data loaded. Total: {weeklyData.totalScreenTime}m, Avg: {weeklyData.averageScreenTime}m");

            // Notify subscribers
            SafeInvokeWeeklyDataUpdated();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error loading weekly data: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// Parse JSON weekly data from plugin
    /// </summary>
    private void ParseWeeklyJsonData(string jsonData)
    {
        try
        {
            weeklyData.Clear();

            // Simple JSON parsing (you might want to use a proper JSON parser for complex data)
            if (jsonData.StartsWith("[") && jsonData.EndsWith("]"))
            {
                // Remove brackets and split by objects
                string cleanJson = jsonData.Trim('[', ']');
                string[] dayEntries = cleanJson.Split(new[] { "}," }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < dayEntries.Length && i < 7; i++)
                {
                    string dayEntry = dayEntries[i].Trim();
                    if (!dayEntry.EndsWith("}")) 
                        dayEntry += "}";

                    // Simple parsing - in production, use JsonUtility or Newtonsoft.Json
                    var dailyData = ParseDailyData(dayEntry);
                    if (dailyData != null)
                    {
                        weeklyData.days.Add(dailyData);
                    }
                }

                Debug.Log($"[DeviceScreenTime] Parsed {weeklyData.days.Count} days from weekly data");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DeviceScreenTime] Error parsing weekly JSON: {e.Message}");
        }
    }

    /// <summary>
    /// Simple JSON parser for daily data (replace with proper JSON library in production)
    /// </summary>
    private DailyData ParseDailyData(string json)
    {
        try
        {
            DailyData data = new DailyData();

            // Very basic parsing - consider using JsonUtility in real implementation
            if (json.Contains("\"date\""))
            {
                int start = json.IndexOf("\"date\"") + 7;
                int end = json.IndexOf("\"", start);
                if (start > 6 && end > start)
                    data.date = json.Substring(start, end - start);
            }

            if (json.Contains("\"screen_time\""))
            {
                int start = json.IndexOf("\"screen_time\"") + 13;
                int end = json.IndexOf(",", start);
                if (end == -1) 
                    end = json.IndexOf("}", start);
                if (start > 12 && end > start)
                {
                    string value = json.Substring(start, end - start).Trim();
                    int.TryParse(value, out data.screenTime);
                }
            }

            if (json.Contains("\"breaks\""))
            {
                int start = json.IndexOf("\"breaks\"") + 8;
                int end = json.IndexOf(",", start);
                if (end == -1) 
                    end = json.IndexOf("}", start);
                if (start > 7 && end > start)
                {
                    string value = json.Substring(start, end - start).Trim();
                    int.TryParse(value, out data.breaks);
                }
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error parsing daily data: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Update weekly data from pre-parsed arrays
    /// </summary>
    private void UpdateWeeklyDataFromArrays(int[] screenTimes, int[] breaks)
    {
        weeklyData.Clear();

        string[] dayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        
        for (int i = 0; i < 7; i++)
        {
            DailyData day = new DailyData
            {
                date = dayNames[i],
                screenTime = screenTimes[i],
                breaks = breaks[i]
            };
            weeklyData.days.Add(day);
        }
    }

    /// <summary>
    /// Force refresh of weekly data (call from UI when needed)
    /// </summary>
    public void RefreshWeeklyData()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        SafeCallStatic("forceDatabaseSync", activity, userId);
#endif
        LoadWeeklyData();
    }

    /* =========================================================
       ========== SCHEDULING CONTROL APIS ======================
       ========================================================= */

    /// <summary>
    /// NEW: Force immediate midnight reset for current user (for testing/UI)
    /// </summary>
    public void ForceMidnightReset()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(userId) || pluginClass == null || activity == null) 
            return;

        try
        {
            SafeCallStatic("forceMidnightReset", activity, userId);
            Debug.Log($"[DeviceScreenTime] Forced midnight reset for user: {userId}");
            
            // Refresh data after reset
            UpdateScreenTimeData();
            LoadWeeklyData();
            UpdateDatabaseStatus();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error forcing midnight reset: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// NEW: Force immediate midnight reset for all users (admin/testing)
    /// </summary>
    public void ForceMidnightResetAllUsers()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass == null || activity == null) 
            return;

        try
        {
            SafeCallStatic("forceMidnightResetAllUsers", activity);
            Debug.Log("[DeviceScreenTime] Forced midnight reset for all users");
            
            // Refresh data after reset
            UpdateScreenTimeData();
            LoadWeeklyData();
            UpdateDatabaseStatus();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error forcing midnight reset for all users: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// NEW: Reinitialize scheduling for current user
    /// Useful when app resumes or user re-authenticates
    /// </summary>
    public void ReinitializeScheduling()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(userId) || pluginClass == null || activity == null) 
            return;

        try
        {
            SafeCallStatic("reinitializeScheduling", activity, userId);
            Debug.Log($"[DeviceScreenTime] Scheduling reinitialized for user: {userId}");
            
            // Update database status after reinitialization
            UpdateDatabaseStatus();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error reinitializing scheduling: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// NEW: Get scheduling status for debugging
    /// </summary>
    public string GetSchedulingStatus()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass == null || activity == null) 
            return "Plugin not initialized";

        try
        {
            return SafeCallStatic<string>("getSchedulingStatus", activity);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error getting scheduling status: {e.Message}");
            return $"Error: {e.Message}";
        }
#else
        return "Not available in editor";
#endif
    }

    /// <summary>
    /// NEW: Emergency cleanup - stop all tracking and clear everything
    /// </summary>
    public void EmergencyCleanup()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass == null || activity == null) 
            return;

        try
        {
            SafeCallStatic("emergencyCleanup", activity);
            Debug.Log("[DeviceScreenTime] Emergency cleanup completed");
            
            // Clear local cache
            totalScreenSecondsToday = 0;
            breaksTakenToday = 0;
            eyeScoreToday = 100;
            weeklyData.Clear();
            weeklyDataLoaded = false;
            
            // Reset ONE ROW PER DAY status
            _hasTodayRecord = false;
            _todayScreenTimeFromDB = 0f;
            _shouldSyncToday = false;
            _databaseStatus = "Emergency cleanup completed";
            
            // Notify UI of reset
            SafeInvokeDailyDataUpdated();
            SafeInvokeWeeklyDataUpdated();
            SafeInvokeDatabaseStatusUpdated();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error during emergency cleanup: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// NEW: Get all currently tracked users
    /// </summary>
    public string[] GetTrackedUsers()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass == null || activity == null) 
            return new string[0];

        try
        {
            return SafeCallStatic<string[]>("getTrackedUsers", activity);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error getting tracked users: {e.Message}");
            return new string[0];
        }
#else
        return new string[0];
#endif
    }

    /// <summary>
    /// NEW: Check if user is currently being tracked
    /// </summary>
    public bool IsUserTracked(string checkUserId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass == null || activity == null) 
            return false;

        try
        {
            return SafeCallStatic<bool>("isUserTracked", activity, checkUserId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error checking if user is tracked: {e.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// NEW: Initialize Android tracking with existing time from other devices
    /// </summary>
    public void InitializeWithExistingTime(float remoteMinutes)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass == null || activity == null) 
            return;

        try
        {
            SafeCallStatic("initializeWithExistingTime", activity, userId, remoteMinutes);
            Debug.Log($"[DeviceScreenTime] Initialized with {remoteMinutes:F0}m from other devices");
            
            // Update local data
            UpdateScreenTimeData();
            UpdateDatabaseStatus();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error initializing with existing time: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// NEW: Trigger manual database write
    /// </summary>
    public void TriggerManualDatabaseWrite()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(userId) || pluginClass == null || activity == null) 
            return;

        try
        {
            SafeCallStatic("triggerManualDatabaseWrite", activity, userId);
            Debug.Log($"[DeviceScreenTime] Triggered manual database write");
            
            // Update database status after write
            UpdateDatabaseStatus();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeviceScreenTime] Error triggering manual database write: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// Check whether the device is currently active.
    /// Always returns true on non-Android platforms.
    /// </summary>
    public bool IsDeviceActive()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginClass != null && activity != null)
            return SafeCallStatic<bool>("isDeviceActive", activity);
#endif
        return true;
    }

    private T SafeCallStatic<T>(string method, params object[] args)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            return pluginClass.CallStatic<T>(method, args);
        }
        catch (AndroidJavaException ex)
        {
            Debug.LogWarning($"[DeviceScreenTime] CallStatic<{typeof(T).Name}>({method}) failed: {ex.Message}");
        }
#endif
        return default;
    }

    private void SafeCallStatic(string method, params object[] args)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            pluginClass.CallStatic(method, args);
        }
        catch (AndroidJavaException ex)
        {
            Debug.LogWarning($"[DeviceScreenTime] CallStatic({method}) failed: {ex.Message}");
        }
#endif
    }

    // === Public getters ===
    public int GetTodaySeconds() => (int)totalScreenSecondsToday;
    public int GetTodayMinutes() => Mathf.RoundToInt(totalScreenSecondsToday / 60f);
    public float GetTodayHours() => totalScreenSecondsToday / 3600f;
    public int GetEyeScoreToday() => eyeScoreToday;
    public int GetBreaksToday() => breaksTakenToday;

    // === Weekly data getters ===
    public WeeklyScreenTimeData GetWeeklyData() => weeklyData;
    public bool IsWeeklyDataLoaded() => weeklyDataLoaded;
    public int GetWeeklyTotalMinutes() => weeklyData.totalScreenTime;
    public int GetWeeklyAverageMinutes() => weeklyData.averageScreenTime;
    public List<DailyData> GetDailyData() => weeklyData.days;

    /// <summary>
    /// Get screen time for a specific day of week (0=Monday, 6=Sunday)
    /// </summary>
    public int GetScreenTimeForDay(int dayIndex)
    {
        if (weeklyData.days != null && dayIndex >= 0 && dayIndex < weeklyData.days.Count)
            return weeklyData.days[dayIndex].screenTime;
        return 0;
    }

    /// <summary>
    /// Get breaks for a specific day of week (0=Monday, 6=Sunday)
    /// </summary>
    public int GetBreaksForDay(int dayIndex)
    {
        if (weeklyData.days != null && dayIndex >= 0 && dayIndex < weeklyData.days.Count)
            return weeklyData.days[dayIndex].breaks;
        return 0;
    }

    // === ONE ROW PER DAY Database Getters ===
    public bool HasTodayRecord() => _hasTodayRecord;
    public float GetTodayScreenTimeFromDatabase() => _todayScreenTimeFromDB;
    public bool ShouldSyncToday() => _shouldSyncToday;
    public string GetDatabaseStatus() => _databaseStatus;

    public void ResetToday()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!string.IsNullOrEmpty(userId))
        {
            SafeCallStatic("resetToday", activity, userId);
            Debug.Log($"[DeviceScreenTime] Daily stats reset for {userId}");
        }
#endif
        // Unity cache will automatically refresh in next UpdateDataRoutine()
        totalScreenSecondsToday = 0;
        breaksTakenToday = 0;
        eyeScoreToday = 100;
        
        // Reset ONE ROW PER DAY status
        _hasTodayRecord = false;
        _todayScreenTimeFromDB = 0f;
        _shouldSyncToday = false;
        _databaseStatus = "Reset today";
        
        // Refresh weekly data after reset
        LoadWeeklyData();
        SafeInvokeDatabaseStatusUpdated();
    }

    private void OnApplicationPause(bool pause)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!pause && !string.IsNullOrEmpty(userId))
        {
            Debug.Log("[DeviceScreenTime] App resumed — ensuring ScreenTimeService is active.");
            SafeCallStatic("startTracking", activity, userId);
            
            // Reinitialize scheduling on resume
            ReinitializeScheduling();
            
            // Refresh data when app resumes
            UpdateScreenTimeData();
            LoadWeeklyData();
            UpdateDatabaseStatus();
        }
#endif
    }

    private void OnApplicationQuit()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!string.IsNullOrEmpty(userId))
        {
            Debug.Log("[DeviceScreenTime] App exited — service continues in background.");
            SafeCallStatic("startTracking", activity, userId);
        }
#endif
    }

    /// <summary>
    /// Clear all event subscribers - useful for cleanup when destroying the object
    /// </summary>
    public void ClearEventSubscribers()
    {
        OnDailyDataUpdated = delegate { };
        OnWeeklyDataUpdated = delegate { };
        OnDatabaseStatusUpdated = delegate { };
    }

    private void OnDestroy()
    {
        if (updateRoutine != null)
            StopCoroutine(updateRoutine);
        
        // Clear event subscribers to prevent memory leaks
        ClearEventSubscribers();
    }
}