using UnityEngine;
using System;
using System.Collections;
using System.Runtime.InteropServices;

/// <summary>
/// PC-specific screen time tracking (Windows/Mac standalone builds)
/// ONLY tracks screen time - ScreenTimeManager handles saving to database
/// Implements ONE ROW PER DAY logic with 5-minute minimum threshold
/// NEVER writes directly to database — only reports metrics
/// </summary>
public class PCScreenTime : MonoBehaviour
{
    public static PCScreenTime Instance { get; private set; }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
#endif

    [Header("Settings")]
    [SerializeField] private float updateInterval = 1f;
    [SerializeField] private int breakThresholdMinutes = 5;
    [SerializeField] private float minScreenTimeForSave = 5f; // ONE ROW PER DAY minimum

    // Tracking state
    private float timer = 0f;
    private DateTime lastDateChecked;
    private float todayActiveMinutes = 0f;
    private bool isTracking = false;
    private DateTime lastUpdateTime;
    private int breaksTaken = 0;
    private int eyeScore = 100;
    
    // Break tracking
    private DateTime lastActivityTime;
    private bool wasIdle = false;

    // Atomic reset guard
    private bool isProcessingReset = false;
    private DateTime pendingResetDate = DateTime.MinValue;

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        lastActivityTime = DateTime.Now;
        lastDateChecked = DateTime.Today;
        lastUpdateTime = DateTime.Now;
        Debug.Log("[PCScreenTime] Initialized");
    }

    private void Update()
    {
        if (!isTracking) return;
        
        // Detect midnight — schedule atomic reset
        if (!isProcessingReset && DateTime.Today != lastDateChecked)
        {
            pendingResetDate = DateTime.Today;
            isProcessingReset = true;
            Debug.Log($"[PCScreenTime] Midnight detected: {lastDateChecked:yyyy-MM-dd} → {DateTime.Today:yyyy-MM-dd}");
            return;
        }
        
        // Process reset if scheduled
        if (isProcessingReset && pendingResetDate != DateTime.MinValue)
        {
            HandleNewDayAtomic();
            return;
        }
        
        // Regular tracking update
        timer += Time.deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f;
            UpdateScreenTime();
            CheckForBreaks();
            UpdateEyeScore();
        }
    }

    private void OnApplicationQuit()
    {
        if (isTracking)
        {
            Debug.Log($"[PCScreenTime] App quitting - performing final update");
            ForceUpdate();
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && isTracking)
        {
            Debug.Log($"[PCScreenTime] App pausing - final update");
            ForceUpdate();
        }
        else if (!pauseStatus && isTracking)
        {
            // Resume tracking
            lastActivityTime = DateTime.Now;
            wasIdle = false;
            lastUpdateTime = DateTime.Now;
            Debug.Log($"[PCScreenTime] App resumed - tracking restarted");
        }
    }

    #endregion

    #region Public API - Called by ScreenTimeManager

    public void StartTracking(float existingMinutesToday = 0f)
    {
        if (isTracking)
        {
            Debug.Log($"[PCScreenTime] Already tracking");
            return;
        }
        
        // Handle new day if needed
        if (DateTime.Today != lastDateChecked)
        {
            HandleNewDayAtomic();
        }

        Debug.Log($"[PCScreenTime] Starting tracking with existing: {existingMinutesToday:F1}m");

        // Resume from existing time (e.g., from DB or other devices)
        todayActiveMinutes = Mathf.Max(0f, existingMinutesToday);
        lastDateChecked = DateTime.Today;
        lastUpdateTime = DateTime.Now;
        lastActivityTime = DateTime.Now;
        isTracking = true;
        breaksTaken = 0;
        eyeScore = CalculateEyeScore();
        wasIdle = false;

        Debug.Log($"[PCScreenTime] Tracking started at {todayActiveMinutes:F1}m");
    }

    public (float minutes, int breaks, int eyeScore) StopTracking()
    {
        if (!isTracking)
        {
            Debug.Log($"[PCScreenTime] Not tracking");
            return (0, 0, 100);
        }

        ForceUpdate();

        var result = (todayActiveMinutes, breaksTaken, eyeScore);
        isTracking = false;

        Debug.Log($"[PCScreenTime] Tracking stopped: {todayActiveMinutes:F1}m, breaks: {breaksTaken}, eye score: {eyeScore}");
        return result;
    }

    public void ForceUpdate()
    {
        if (!isTracking) return;
        UpdateScreenTime();
        CheckForBreaks();
        UpdateEyeScore();
        Debug.Log($"[PCScreenTime] Force update: {todayActiveMinutes:F1}m, breaks: {breaksTaken}, eye score: {eyeScore}");
    }

    public void ResetForNewDay()
    {
        Debug.Log($"[PCScreenTime] External new-day reset requested");
        HandleNewDayAtomic();
    }

    #endregion

    #region Core Tracking Logic

    private void UpdateScreenTime()
    {
        if (!isTracking || isProcessingReset) return;

        double idleMinutes = GetIdleMinutes();

        if (idleMinutes < breakThresholdMinutes)
        {
            // User is active → add updateInterval as active time
            todayActiveMinutes += updateInterval / 60f; // convert seconds to minutes
            lastActivityTime = DateTime.Now;
            wasIdle = false;
        }
        else if (!wasIdle)
        {
            wasIdle = true;
            Debug.Log($"[PCScreenTime] Idle detected after {idleMinutes:F1}m");
        }

        lastUpdateTime = DateTime.Now;
    }

    private double GetIdleMinutes()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            LASTINPUTINFO lii = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO))
            };

            if (GetLastInputInfo(ref lii))
            {
                uint idleTime = ((uint)Environment.TickCount - lii.dwTime);
                return idleTime / 60000.0; // ms → minutes
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PCScreenTime] GetLastInputInfo failed: {ex.Message}");
        }
#endif
        // Fallback: use app focus as proxy (non-Windows or error)
        return Application.isFocused ? 0 : (DateTime.Now - lastActivityTime).TotalMinutes;
    }

    private void CheckForBreaks()
    {
        if (!isTracking || isProcessingReset) return;

        double idleMinutes = GetIdleMinutes();

        if (idleMinutes >= breakThresholdMinutes && !wasIdle)
        {
            breaksTaken++;
            wasIdle = true;
            Debug.Log($"[PCScreenTime] Break awarded after {idleMinutes:F1}m idle! Total: {breaksTaken}");
        }
        else if (idleMinutes < breakThresholdMinutes && wasIdle)
        {
            wasIdle = false;
            Debug.Log($"[PCScreenTime] Device active again");
        }
    }

    private void UpdateEyeScore()
    {
        if (isProcessingReset) return;
        int newScore = CalculateEyeScore();
        if (newScore != eyeScore)
        {
            eyeScore = newScore;
            Debug.Log($"[PCScreenTime] Eye score updated: {eyeScore}");
        }
    }

    private int CalculateEyeScore()
    {
        // Max screen time before 0% = 10 hours (600 minutes)
        float maxMinutes = 600f;
        float percent = Mathf.Clamp01(todayActiveMinutes / maxMinutes);
        return Mathf.Clamp(100 - Mathf.RoundToInt(percent * 100), 0, 100);
    }

    #endregion

    #region Daily Reset (Atomic)

    private void HandleNewDayAtomic()
    {
        if (isProcessingReset) return;

        isProcessingReset = true;
        try
        {
            DateTime newDate = pendingResetDate != DateTime.MinValue ? pendingResetDate : DateTime.Today;
            float prevMinutes = todayActiveMinutes;

            Debug.Log($"[PCScreenTime] ATOMIC New day: {lastDateChecked:yyyy-MM-dd} → {newDate:yyyy-MM-dd}");
            Debug.Log($"[PCScreenTime] Previous day: {prevMinutes:F1}m, breaks: {breaksTaken}");

            // Reset internal state
            todayActiveMinutes = 0f;
            lastDateChecked = newDate;
            breaksTaken = 0;
            eyeScore = 100;
            wasIdle = false;
            lastUpdateTime = DateTime.Now;
            lastActivityTime = DateTime.Now;
            timer = 0f;

            Debug.Log($"[PCScreenTime] Reset complete: {prevMinutes:F1}m → 0m");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PCScreenTime] Atomic reset failed: {ex.Message}");
        }
        finally
        {
            isProcessingReset = false;
            pendingResetDate = DateTime.MinValue;
        }
    }

    #endregion

    #region Data Getters (for ScreenTimeManager)

    public float GetTodayActiveHours() => todayActiveMinutes / 60f;

    public int GetTodayBreaks() => breaksTaken;

    public int GetEyeScoreToday() => eyeScore;

    public float GetTodayScreenTimeMinutes() => todayActiveMinutes;

    public bool IsDeviceActive() => GetIdleMinutes() < breakThresholdMinutes;

    public bool IsTracking() => isTracking;

    public DateTime GetLastDateChecked() => lastDateChecked;

    // ✅ FIXED: Renamed to avoid duplicate - Report whether a DB record exists for today
    public bool HasTodayDatabaseRecord()
    {
        if (DatabaseManager.Instance == null || !DatabaseManager.Instance.IsLoggedIn)
            return false;
        return DatabaseManager.Instance.HasTodayRecord();
    }

    // ✅ FIXED: Renamed to avoid duplicate - Return actual DB value for debugging/status
    public float GetTodayScreenTimeFromDatabase()
    {
        if (DatabaseManager.Instance == null || !DatabaseManager.Instance.IsLoggedIn)
            return 0f;
        return DatabaseManager.Instance.GetTodayScreenTimeFromDB();
    }

    // ✅ FIXED: Unified status string matching Android format
    public string GetDatabaseStatus()
    {
        bool hasRecord = HasTodayDatabaseRecord();
        float dbMinutes = GetTodayScreenTimeFromDatabase();
        bool shouldSync = HasEnoughScreenTimeForDatabaseWrite();
        
        return $"ONE ROW PER DAY Status for User {DatabaseManager.Instance?.CurrentUserId}:\n" +
               $"Today Record: {(hasRecord ? "YES" : "NO")}\n" +
               $"Today Minutes: {dbMinutes:F0}m\n" +
               $"Today Breaks: {breaksTaken}\n" +
               $"Should Sync: {(shouldSync ? "YES (≥5m or has breaks)" : "NO")}\n" +
               $"Total Records: N/A (PC uses live tracking)\n" +
               $"This Week Records: N/A";
    }

    // ✅ FIXED: Renamed to avoid duplicate - Check if we have enough screen time for database write
    public bool HasEnoughScreenTimeForDatabaseWrite()
    {
        return todayActiveMinutes >= minScreenTimeForSave || breaksTaken > 0;
    }

    #endregion

    #region Database Integration Methods

    /// <summary>
    /// Trigger a manual database write (for PC platform compatibility)
    /// </summary>
    public void TriggerManualDatabaseWrite()
    {
        Debug.Log($"[PCScreenTime] Manual DB write requested: {todayActiveMinutes:F1}m, {breaksTaken} breaks");
        
        if (DatabaseManager.Instance != null && DatabaseManager.Instance.IsLoggedIn)
        {
            // Use UpdateDailyMetrics to ensure proper ONE ROW PER DAY logic
            DatabaseManager.Instance.UpdateDailyMetrics(
                Mathf.RoundToInt(todayActiveMinutes),
                breaksTaken,
                eyeScore
            );
        }
    }

    /// <summary>
    /// Initialize with existing time from other devices
    /// </summary>
    public void InitializeWithExistingTime(float existingMinutes)
    {
        if (existingMinutes > 0)
        {
            todayActiveMinutes = existingMinutes;
            Debug.Log($"[PCScreenTime] Initialized with {existingMinutes:F0}m from other devices");
        }
    }

    /// <summary>
    /// Reinitialize scheduling (PC doesn't need scheduling, just track)
    /// </summary>
    public void ReinitializeScheduling()
    {
        Debug.Log("[PCScreenTime] Reinitialized tracking");
        // Just ensure tracking is active
        if (!isTracking)
        {
            StartTracking(todayActiveMinutes);
        }
    }

    /// <summary>
    /// Force midnight reset
    /// </summary>
    public void ForceMidnightReset()
    {
        Debug.Log("[PCScreenTime] Force midnight reset");
        HandleNewDayAtomic();
    }

    /// <summary>
    /// Emergency cleanup
    /// </summary>
    public void EmergencyCleanup()
    {
        Debug.Log("[PCScreenTime] Emergency cleanup");
        // Reset tracking
        todayActiveMinutes = 0f;
        breaksTaken = 0;
        eyeScore = 100;
        wasIdle = false;
        lastDateChecked = DateTime.Today;
        lastUpdateTime = DateTime.Now;
        lastActivityTime = DateTime.Now;
    }

    #endregion

    #region Debug Methods

    [ContextMenu("Force New Day Reset")]
    public void DebugForceNewDay() => HandleNewDayAtomic();

    [ContextMenu("Add 10 Minutes")]
    public void DebugAdd10Minutes()
    {
        if (isTracking)
        {
            todayActiveMinutes += 10f;
            UpdateEyeScore();
            Debug.Log($"[PCScreenTime] DEBUG: Added 10m → {todayActiveMinutes:F1}m");
        }
    }

    [ContextMenu("Reset to Zero")]
    public void DebugResetToZero()
    {
        todayActiveMinutes = 0f;
        breaksTaken = 0;
        eyeScore = 100;
        Debug.Log($"[PCScreenTime] DEBUG: Reset to zero");
    }

    [ContextMenu("Debug Idle Time")]
    public void DebugIdleTime()
    {
        double idle = GetIdleMinutes();
        bool active = IsDeviceActive();
        Debug.Log($"[PCScreenTime] DEBUG - Idle: {idle:F1}m, Active: {active}");
    }

    #endregion
}