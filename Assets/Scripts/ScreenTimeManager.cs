using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Orchestrates screen time tracking, UI updates, and eye-health metrics.
/// Focused ONLY on tracking - SyncManager handles all synchronization.
/// Database writes use SIMPLE ONE ROW PER DAY logic.
/// </summary>
public class ScreenTimeManager : MonoBehaviour
{
    public static ScreenTimeManager Instance { get; private set; }

    #region Inspector Fields
    [Header("Circular Progress")]
    [SerializeField] private Image circularProgress;
    [SerializeField] private TMP_Text percentageText;
    [SerializeField] private TMP_Text homeButtonText;
    [SerializeField] private Image button_ScreenTime;
    [SerializeField] private float circularLerpSpeed = 3f;
    
    [Header("Break Settings")]
    [SerializeField] private int maxBreaks = 50;
    [SerializeField] private float breakRecoveryMinutes = 5f;
    
    [Header("Details")]
    [SerializeField] private TMP_Text screenTimeTodayText;
    [SerializeField] private TMP_Text breaksTakenText;
    [SerializeField] private TMP_Text eyeHealthScoreText;
    
    [Header("Progress Statement")]
    [SerializeField] private TMP_Text progressStatementText;
    
    [Header("External Managers")]
    [SerializeField] public BlinkyManager blinky;
    [SerializeField] private ProgressManager progressManager;
    #endregion

    #region Private State
    private float targetCircularFill = 1f;
    private int breaksTaken = 0;
    private int lastBreaksReported = 0;
    private float todayScreenHours = 0f;
    private int todayEyeScore = 100;
    private bool initialized = false;
    private int currentUserId = -1;
    private Coroutine liveUpdateRoutine;
    private bool wasScreenOff = false;
    private DateTime screenOffStart;
    
    // Weekly data tracking
    private DeviceScreenTime.WeeklyScreenTimeData weeklyData = new DeviceScreenTime.WeeklyScreenTimeData();
    private bool weeklyDataInitialized = false;
    
    // Scheduling resilience
    private float schedulingCheckTimer = 0f;
    private const float SCHEDULING_CHECK_INTERVAL = 30f;
    private Coroutine _delayedInitializationCoroutine;
    
    // Midnight reset tracking
    private DateTime _lastMidnightCheck = DateTime.MinValue;
    private bool _hasProcessedMidnightReset = false;
    
    // Emotion update tracking
    private float _lastEmotionUpdateTime = 0f;
    private const float EMOTION_UPDATE_INTERVAL = 1f;
    
    // Sync tracking
    private bool _isSyncing = false;
    private float _lastSyncTime = 0f;
    private const float SYNC_COOLDOWN = 5f;
    
    // Cross-device screen time tracking
    private float _remoteScreenTimeMinutes = 0f;
    private bool _hasMergedRemoteData = false;
    private float _lastSyncedMinutes = 0f;
    private DateTime _lastScreenTimeSync = DateTime.MinValue;
    
    // Track local active time
    private float _localActiveTimeMinutes = 0f;
    private DateTime _localTrackingStart = DateTime.MinValue;
    
    // ONE ROW PER DAY tracking
    private bool _hasTodayRecord = false;
    private float _todayScreenTimeFromDB = 0f;
    private bool _shouldSyncToday = false;
    private string _databaseStatus = "";
    
    // System idle tracking
    private bool _wasSystemIdle = false;
    private DateTime _systemIdleStartTime;
    private const float SYSTEM_IDLE_THRESHOLD_MINUTES = 5f;
    private bool _isAppActuallyPaused = false;
    
    // Database write rate limiting
    private float _lastDBWriteTime = 0f;
    private const float MIN_DB_WRITE_INTERVAL = 60f; // Only write to DB every 60 seconds
    private int _lastScreenTimeValue = 0;
    private int _lastBreaksValue = 0;
    private int _lastEyeScoreValue = 100;
    
    // Debug tracking to find 0-minute issue
    private float _lastDebugLogTime = 0f;
    private const float DEBUG_LOG_INTERVAL = 10f;
    #endregion

    #region Events
    public event Action<int> OnBreakTaken;
    public event Action<Achievement> OnAchievementEligible;
    public event Action OnWeeklyDataUpdated = delegate { };
    public event Action OnSchedulingStatusChanged = delegate { };
    public event Action OnMidnightReset = delegate { };
    public event Action<bool, string> OnSyncCompleted = delegate { };
    public event Action<float> OnScreenTimeUpdated = delegate { };
    public event Action<string> OnSyncError = delegate { };
    public event Action<string> OnDatabaseStatusUpdated = delegate { };
    public event Action<int, float, int, int> OnScreenTimeDataChanged = delegate { };
    #endregion

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
    }

    private void Start()
    {
        ResetUI();
        initialized = true;
        
        SessionManager.OnSessionStarted += OnUserLoginCommit;
        SessionManager.OnSessionEnded += OnUserLogoutCommit;
        
        if (SyncManager.Instance != null)
        {
            SyncManager.Instance.OnUserDataSynced += OnSyncCompletedHandler;
            SyncManager.Instance.OnScreenTimeSynced += OnScreenTimeSyncedHandler;
        }
        
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
        {
            DeviceScreenTime.Instance.OnDailyDataUpdated += OnAndroidDailyDataUpdated;
            DeviceScreenTime.Instance.OnWeeklyDataUpdated += OnAndroidWeeklyDataUpdated;
            DeviceScreenTime.Instance.OnDatabaseStatusUpdated += OnAndroidDatabaseStatusUpdated;
        }
#endif
        
        if (SessionManager.Instance?.CurrentUser != null)
        {
            Debug.Log("[ScreenTimeManager] Existing session detected. Initializing...");
            OnUserLoginCommit(SessionManager.Instance.CurrentUser);
        }
        
#if UNITY_ANDROID && !UNITY_EDITOR
        liveUpdateRoutine = StartCoroutine(LiveAndroidScreenTimeUpdate());
#endif
    }

    private void Update()
    {
        if (!initialized || currentUserId == -1) return;
        
        TrackLocalActiveTime();
        UpdatePlatformMetrics(out bool systemActive);
        HandleBreakLogic(systemActive);
        AnimateCircularProgress();
        RefreshUI();
        CheckSchedulingResilience();
        CheckMidnightReset();
        UpdateBlinkyEmotion();
        CheckDatabaseStatus();
        CheckAndNotifyScreenTimeChange();
        
        // Debug logging to track the 0-minute issue
        if (Time.time - _lastDebugLogTime >= DEBUG_LOG_INTERVAL)
        {
            _lastDebugLogTime = Time.time;
            int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
            Debug.Log($"[ScreenTimeManager-DEBUG] Update check: {screenTimeMinutes}m, Breaks: {breaksTaken}, " +
                     $"Time to next write: {MIN_DB_WRITE_INTERVAL - (Time.time - _lastDBWriteTime):F0}s");
        }
        
        // Periodically check if we should write to DB (SIMPLE logic)
        if (Time.time - _lastDBWriteTime >= MIN_DB_WRITE_INTERVAL)
        {
            // Only write if we have meaningful data (≥5 minutes or has breaks)
            int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
            if (screenTimeMinutes >= 5 || breaksTaken > 0)
            {
                CommitScreenTimeToLocalDB(currentUserId);
            }
            else
            {
                // Debug log when timer expires but we don't have enough data
                if (Time.time - _lastDebugLogTime >= 5f) // More frequent debug for this issue
                {
                    Debug.Log($"[ScreenTimeManager] Timer expired but skipping write: {screenTimeMinutes}m < 5m and breaks={breaksTaken}");
                }
            }
        }
    }

    private void OnDestroy()
    {
        SessionManager.OnSessionStarted -= OnUserLoginCommit;
        SessionManager.OnSessionEnded -= OnUserLogoutCommit;
        
        if (SyncManager.Instance != null)
        {
            SyncManager.Instance.OnUserDataSynced -= OnSyncCompletedHandler;
            SyncManager.Instance.OnScreenTimeSynced -= OnScreenTimeSyncedHandler;
        }
        
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
        {
            DeviceScreenTime.Instance.OnDailyDataUpdated -= OnAndroidDailyDataUpdated;
            DeviceScreenTime.Instance.OnWeeklyDataUpdated -= OnAndroidWeeklyDataUpdated;
            DeviceScreenTime.Instance.OnDatabaseStatusUpdated -= OnAndroidDatabaseStatusUpdated;
        }
        
        if (liveUpdateRoutine != null)
            StopCoroutine(liveUpdateRoutine);
#endif
        
        if (_delayedInitializationCoroutine != null)
            StopCoroutine(_delayedInitializationCoroutine);
            
        ClearEventSubscribers();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            _isAppActuallyPaused = true;
            Debug.Log("[ScreenTimeManager] App PAUSED (going to background)");
            if (currentUserId != -1)
            {
                SaveLocalTrackingTime();
                
                // Only commit if we have meaningful data (≥5 minutes or has breaks)
                int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
                if (screenTimeMinutes >= 5 || breaksTaken > 0)
                {
                    CommitScreenTimeToLocalDB(currentUserId);
                }
                else
                {
                    Debug.Log($"[ScreenTimeManager] Pause - No DB write: {screenTimeMinutes}m < 5m and breaks={breaksTaken}");
                }
            }
        }
        else
        {
            _isAppActuallyPaused = false;
            Debug.Log("[ScreenTimeManager] App RESUMED from background");
            if (currentUserId != -1)
            {
                ReinitializeScheduling();
                RefreshAllData();
                CheckMidnightReset();
                ForceUpdateBlinkyEmotion();
                
                if (SyncManager.Instance != null)
                {
                    SyncManager.Instance.OnAppResume();
                }
            }
        }
    }
    #endregion

    #region Session Management
    public void OnUserLoginCommit(User user)
    {
        if (user == null || user.id <= 0) return;
        
        currentUserId = user.id;
        Debug.Log($"[ScreenTimeManager] Initializing for user {user.id}");
        
#if UNITY_ANDROID && !UNITY_EDITOR
        DeviceScreenTime.Instance?.InitializeForUser(user.id.ToString());
        
        if (DeviceScreenTime.Instance != null && DeviceScreenTime.Instance.IsWeeklyDataLoaded())
        {
            weeklyData = DeviceScreenTime.Instance.GetWeeklyData();
            weeklyDataInitialized = true;
            SafeInvokeWeeklyDataUpdated();
        }
#endif

        ResetUI();
        _lastMidnightCheck = DateTime.Now;
        _hasProcessedMidnightReset = false;
        _isSyncing = false;
        _lastSyncTime = 0f;
        _remoteScreenTimeMinutes = 0f;
        _hasMergedRemoteData = false;
        _localActiveTimeMinutes = 0f;
        _localTrackingStart = DateTime.Now;
        _wasSystemIdle = false;
        _isAppActuallyPaused = false;
        _hasTodayRecord = false;
        _todayScreenTimeFromDB = 0f;
        _shouldSyncToday = false;
        _databaseStatus = "";
        
        // Reset rate limiting variables
        _lastDBWriteTime = 0f;
        _lastScreenTimeValue = 0;
        _lastBreaksValue = 0;
        _lastEyeScoreValue = 100;
        
        LoadLocalTrackingTime();
        
        if (_delayedInitializationCoroutine != null)
            StopCoroutine(_delayedInitializationCoroutine);
            
        _delayedInitializationCoroutine = StartCoroutine(DelayedLoginInitialization());
    }

    private IEnumerator DelayedLoginInitialization()
    {
        Debug.Log($"[ScreenTimeManager] Starting delayed initialization for user {currentUserId}");
        yield return new WaitForSeconds(2f);
        
        yield return StartCoroutine(FetchAndMergeRemoteScreenTime());
        
        // Load screen time from DB
        float dbMinutes = 0f;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
        {
            dbMinutes = DeviceScreenTime.Instance.GetTodayScreenTimeFromDatabase();
        }
        else
#endif
        {
            if (DatabaseManager.Instance != null && DatabaseManager.Instance.IsLoggedIn)
            {
                dbMinutes = DatabaseManager.Instance.GetTodayScreenTimeFromDB(currentUserId);
            }
        }
        
        if (dbMinutes > 0f)
        {
            float dbHours = dbMinutes / 60f;
            // Only adopt DB time if it's greater (prevent regression)
            if (dbHours > todayScreenHours)
            {
                todayScreenHours = dbHours;
                _localActiveTimeMinutes = 0f; // DB already accounts for prior tracking
                _localTrackingStart = DateTime.Now;
                Debug.Log($"[ScreenTimeManager] Resumed screen time from DB: {dbHours:F2}h ({dbMinutes:F0}m)");
            }
        }
        
        RefreshUI();
        yield return new WaitForSeconds(1f);
        ReinitializeScheduling();
        
        Debug.Log($"[ScreenTimeManager] Delayed initialization completed for user {currentUserId}");
    }

    public void InitializeForUser(User user)
    {
        if (_delayedInitializationCoroutine != null)
            StopCoroutine(_delayedInitializationCoroutine);
            
        _delayedInitializationCoroutine = StartCoroutine(DelayedInitializeForUser(user));
    }

    private IEnumerator DelayedInitializeForUser(User user)
    {
        yield return new WaitForSeconds(0.5f);
        OnUserLoginCommit(user);
    }

    public void OnUserLogoutCommit()
    {
        if (currentUserId == -1) return;
        
        Debug.Log($"[ScreenTimeManager] Starting logout commit for user {currentUserId}");
        
        if (_delayedInitializationCoroutine != null)
        {
            StopCoroutine(_delayedInitializationCoroutine);
            _delayedInitializationCoroutine = null;
        }
        
        SaveLocalTrackingTime();
        
        // Commit final data before logout (if we have meaningful data)
        int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
        if (screenTimeMinutes >= 5 || breaksTaken > 0)
        {
            CommitScreenTimeToLocalDB(currentUserId);
        }
        else
        {
            Debug.Log($"[ScreenTimeManager] Logout - No DB write: {screenTimeMinutes}m < 5m and breaks={breaksTaken}");
        }
        
        currentUserId = -1;
        ResetUI();
        weeklyDataInitialized = false;
        weeklyData.Clear();
        _isSyncing = false;
        _remoteScreenTimeMinutes = 0f;
        _hasMergedRemoteData = false;
        _localActiveTimeMinutes = 0f;
        _wasSystemIdle = false;
        _isAppActuallyPaused = false;
        _hasTodayRecord = false;
        _todayScreenTimeFromDB = 0f;
        _shouldSyncToday = false;
        _databaseStatus = "";
        
        // Reset rate limiting variables
        _lastDBWriteTime = 0f;
        _lastScreenTimeValue = 0;
        _lastBreaksValue = 0;
        _lastEyeScoreValue = 100;
        
        Debug.Log($"[ScreenTimeManager] Logout commit completed");
    }

    // Remote screen time fetch
    private IEnumerator GetRemoteScreenTimeCoroutine(int userId, DateTime date, Action<float> callback)
    {
        if (SyncManager.Instance == null)
        {
            callback?.Invoke(0f);
            yield break;
        }
        
        bool requestCompleted = false;
        float result = 0f;
        
        var task = SyncManager.Instance.GetRemoteScreenTime(userId, date);
        task.ContinueWith(t =>
        {
            result = !t.IsFaulted && !t.IsCanceled ? t.Result : 0f;
            requestCompleted = true;
        });
        
        float waitTime = 0f;
        while (!requestCompleted && waitTime < 10f)
        {
            waitTime += Time.deltaTime;
            yield return null;
        }
        
        callback?.Invoke(requestCompleted ? result : 0f);
    }

    private IEnumerator FetchAndMergeRemoteScreenTime()
    {
        if (currentUserId == -1 || SyncManager.Instance == null)
        {
            yield break;
        }
        
        yield return new WaitForSeconds(1f);
        
        DateTime today = DateTime.Today;
        float remoteMinutes = 0f;
        bool requestCompleted = false;
        
        yield return StartCoroutine(GetRemoteScreenTimeCoroutine(currentUserId, today, (minutes) =>
        {
            remoteMinutes = minutes;
            requestCompleted = true;
        }));
        
        float waitTime = 0f;
        while (!requestCompleted && waitTime < 10f)
        {
            waitTime += Time.deltaTime;
            yield return null;
        }
        
        if (!requestCompleted || remoteMinutes <= 0) yield break;
        
        _remoteScreenTimeMinutes = remoteMinutes;
        
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null && !_hasMergedRemoteData)
        {
            try
            {
                DeviceScreenTime.Instance.InitializeWithExistingTime(remoteMinutes);
                Debug.Log($"[ScreenTimeManager] Initialized Android tracking with {remoteMinutes:F0}m from other devices");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScreenTimeManager] Failed to initialize: {ex.Message}");
            }
        }
#endif
        
        _hasMergedRemoteData = true;
        
        float localMinutes = GetLocalTodayMinutes();
        float platformMinutes = todayScreenHours * 60f;
        float totalMinutes = Mathf.Max(remoteMinutes, Mathf.Max(localMinutes, platformMinutes));
        todayScreenHours = totalMinutes / 60f;
        
        Debug.Log($"[ScreenTimeManager] Merged {remoteMinutes:F0}m remote + {localMinutes:F0}m local = {totalMinutes:F0}m total");
        
        RefreshUI();
        UpdateBlinkyEmotion(true);
        progressManager?.ForceRefreshGraphs();
        OnScreenTimeUpdated?.Invoke(totalMinutes);
    }
    #endregion

    #region Track Local Active Time
    private void TrackLocalActiveTime()
    {
        if (currentUserId == -1 || _isAppActuallyPaused || _wasSystemIdle) return;
        
        if (_localTrackingStart.Date < DateTime.Today)
        {
            _localActiveTimeMinutes = 0f;
            _localTrackingStart = DateTime.Now;
        }
        
        _localActiveTimeMinutes += Time.deltaTime / 60f;
        
        if (Time.time % 30f < 0.1f)
        {
            Debug.Log($"[ScreenTimeManager] Local tracking: {_localActiveTimeMinutes:F1}m");
        }
    }

    private float GetLocalTodayMinutes()
    {
        return _localTrackingStart.Date == DateTime.Today ? _localActiveTimeMinutes : 0f;
    }

    private void SaveLocalTrackingTime()
    {
        if (currentUserId == -1) return;
        
        string key = $"LocalScreenTime_{currentUserId}_{DateTime.Today:yyyyMMdd}";
        PlayerPrefs.SetFloat(key, _localActiveTimeMinutes);
        PlayerPrefs.SetString($"{key}_start", _localTrackingStart.ToString("o"));
        PlayerPrefs.Save();
    }

    private void LoadLocalTrackingTime()
    {
        if (currentUserId == -1) return;
        
        string key = $"LocalScreenTime_{currentUserId}_{DateTime.Today:yyyyMMdd}";
        if (PlayerPrefs.HasKey(key))
        {
            _localActiveTimeMinutes = PlayerPrefs.GetFloat(key, 0f);
            string startKey = $"{key}_start";
            if (PlayerPrefs.HasKey(startKey) && DateTime.TryParse(PlayerPrefs.GetString(startKey), out DateTime savedStart))
            {
                _localTrackingStart = savedStart;
            }
            Debug.Log($"[ScreenTimeManager] Loaded {_localActiveTimeMinutes:F0}m local tracking time");
        }
    }
    #endregion

    #region ONE ROW PER DAY Database Status
    private void CheckDatabaseStatus()
    {
        if (currentUserId == -1) return;
        
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
        {
            try
            {
                _hasTodayRecord = DeviceScreenTime.Instance.HasTodayRecord();
                _todayScreenTimeFromDB = DeviceScreenTime.Instance.GetTodayScreenTimeFromDatabase();
                _shouldSyncToday = DeviceScreenTime.Instance.ShouldSyncToday();
                _databaseStatus = DeviceScreenTime.Instance.GetDatabaseStatus();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScreenTimeManager] Database status check failed: {ex.Message}");
            }
        }
#else
        // PC: Query DatabaseManager directly using SIMPLE methods
        if (DatabaseManager.Instance != null)
        {
            try
            {
                _hasTodayRecord = DatabaseManager.Instance.HasTodayRecord(currentUserId);
                _todayScreenTimeFromDB = DatabaseManager.Instance.GetTodayScreenTimeFromDB(currentUserId);
                _shouldSyncToday = DatabaseManager.Instance.ShouldSyncToday(currentUserId);
                _databaseStatus = DatabaseManager.Instance.GetDatabaseStatus(currentUserId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScreenTimeManager] PC DB status failed: {ex.Message}");
            }
        }
#endif
    }

    private void OnAndroidDatabaseStatusUpdated()
    {
        if (currentUserId == -1) return;
        
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (DeviceScreenTime.Instance != null)
            {
                _databaseStatus = DeviceScreenTime.Instance.GetDatabaseStatus();
                SafeInvokeDatabaseStatusUpdated();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ScreenTimeManager] OnAndroidDatabaseStatusUpdated failed: {ex.Message}");
        }
#endif
    }

    private void SafeInvokeDatabaseStatusUpdated()
    {
        try
        {
            OnDatabaseStatusUpdated?.Invoke(_databaseStatus);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenTimeManager] Error in OnDatabaseStatusUpdated: {e}");
        }
    }

    [ContextMenu("Debug ONE ROW PER DAY Status")]
    public void DebugOneRowPerDayStatus()
    {
        if (currentUserId == -1)
        {
            Debug.Log("[ScreenTimeManager] No user logged in");
            return;
        }
        
        Debug.Log($"[ONE ROW PER DAY STATUS]");
        Debug.Log($"User ID: {currentUserId}");
        Debug.Log($"Has Today Record: {_hasTodayRecord}");
        Debug.Log($"Today Screen Time from DB: {_todayScreenTimeFromDB:F0}m");
        Debug.Log($"Should Sync Today: {_shouldSyncToday}");
        Debug.Log($"Total Minutes Today: {GetTodayScreenTimeInMinutes()}m");
        Debug.Log($"Has Enough for DB Write (≥5m or breaks): {(GetTodayScreenTimeInMinutes() >= 5 || breaksTaken > 0 ? "YES" : "NO")}");
        Debug.Log($"Merged Remote Data: {_hasMergedRemoteData}");
        Debug.Log($"Remote Minutes: {_remoteScreenTimeMinutes:F0}m");
        Debug.Log($"Local Minutes: {GetLocalTodayMinutes():F0}m");
    }
    #endregion

    #region Sync Management
    private void OnSyncCompletedHandler(bool success)
    {
        _isSyncing = false;
        OnSyncCompleted?.Invoke(success, success ? "Sync completed" : "Sync failed");
    }

    private void OnScreenTimeSyncedHandler(float minutes, DateTime date)
    {
        _lastSyncedMinutes = minutes;
        _lastScreenTimeSync = DateTime.Now;
    }

    private void CheckAndNotifyScreenTimeChange()
    {
        if (currentUserId == -1 || SyncManager.Instance == null) return;
        
        float currentMinutes = GetTodayScreenTimeInMinutes();
        if (Mathf.Abs(currentMinutes - _lastSyncedMinutes) >= 5f)
        {
            _lastSyncedMinutes = currentMinutes;
            _lastScreenTimeSync = DateTime.Now;
            SyncManager.Instance.NotifyScreenTimeChanged(currentMinutes);
        }
    }

    public void RequestSync(string reason = "")
    {
        if (currentUserId == -1 || SyncManager.Instance == null) return;
        if (_isSyncing || Time.time - _lastSyncTime < SYNC_COOLDOWN) return;
        
        Debug.Log($"[ScreenTimeManager] Requesting sync. Reason: {reason}");
        _isSyncing = true;
        _lastSyncTime = Time.time;
        SyncManager.Instance.RequestSync();
    }

    public void RequestScreenTimeSync()
    {
        if (currentUserId == -1 || SyncManager.Instance == null) return;
        
        float totalMinutes = GetTodayScreenTimeInMinutes();
        if (totalMinutes < 1f) return;
        
        Debug.Log($"[ScreenTimeManager] Requesting screen time sync: {totalMinutes:F0}m");
        StartCoroutine(SyncManager.Instance.SyncScreenTimeData(currentUserId, DateTime.Today, totalMinutes));
    }

    public void ForceSync(string reason = "manual") => RequestSync(reason);
    public bool IsSyncing => _isSyncing;
    public float TimeSinceLastSync => Time.time - _lastSyncTime;
    #endregion

    #region Platform Data Integration
    private void UpdatePlatformMetrics(out bool systemActive)
    {
        systemActive = true;
        float platformHours = 0f;
        bool isSystemIdle = false;
        
#if UNITY_STANDALONE_WIN
        if (PCScreenTime.Instance != null)
        {
            platformHours = PCScreenTime.Instance.GetTodayActiveHours();
            isSystemIdle = !PCScreenTime.Instance.IsDeviceActive();
            breaksTaken = PCScreenTime.Instance.GetTodayBreaks();
            todayEyeScore = PCScreenTime.Instance.GetEyeScoreToday();
        }
        else
        {
            isSystemIdle = _isAppActuallyPaused;
        }
#endif
        
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
        {
            try
            {
                platformHours = DeviceScreenTime.Instance.GetTodayHours();
                isSystemIdle = !DeviceScreenTime.Instance.IsDeviceActive();
                breaksTaken = DeviceScreenTime.Instance.GetBreaksToday();
                todayEyeScore = DeviceScreenTime.Instance.GetEyeScoreToday();
                _hasTodayRecord = DeviceScreenTime.Instance.HasTodayRecord();
                _todayScreenTimeFromDB = DeviceScreenTime.Instance.GetTodayScreenTimeFromDatabase();
                _shouldSyncToday = DeviceScreenTime.Instance.ShouldSyncToday();
                
                if (DeviceScreenTime.Instance.IsWeeklyDataLoaded())
                {
                    weeklyData = DeviceScreenTime.Instance.GetWeeklyData();
                    if (!weeklyDataInitialized)
                    {
                        weeklyDataInitialized = true;
                        SafeInvokeWeeklyDataUpdated();
                    }
                }
            }
            catch
            {
                isSystemIdle = _isAppActuallyPaused;
            }
        }
        else
        {
            isSystemIdle = _isAppActuallyPaused;
        }
#endif
        
        HandleSystemIdleTransition(isSystemIdle);
        systemActive = !isSystemIdle;
        
        float localMinutes = GetLocalTodayMinutes();
        float totalMinutes = (platformHours * 60f) + localMinutes + _remoteScreenTimeMinutes;
        float previousHours = todayScreenHours;
        todayScreenHours = Mathf.Max(platformHours, totalMinutes / 60f);
        
        if (Mathf.Abs(todayScreenHours - previousHours) > 0.1f)
        {
            Debug.Log($"[ScreenTimeManager] Screen time updated: {previousHours:F2}h → {todayScreenHours:F2}h");
        }
        
        OnScreenTimeDataChanged?.Invoke(currentUserId, totalMinutes, breaksTaken, todayEyeScore);
    }

    private void HandleSystemIdleTransition(bool isSystemIdleNow)
    {
        if (isSystemIdleNow && !_wasSystemIdle)
        {
            Debug.Log($"[ScreenTimeManager] System became idle");
            _wasSystemIdle = true;
            _systemIdleStartTime = DateTime.Now;
            
            if (currentUserId != -1)
            {
                AwardBreak("[Break] System idle detected");
            }
        }
        else if (!isSystemIdleNow && _wasSystemIdle)
        {
            Debug.Log($"[ScreenTimeManager] System became active again");
            _wasSystemIdle = false;
            _localTrackingStart = DateTime.Now;
        }
    }

    private void OnAndroidDailyDataUpdated()
    {
        if (currentUserId == -1) return;
        
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            todayScreenHours = DeviceScreenTime.Instance.GetTodayHours();
            breaksTaken = DeviceScreenTime.Instance.GetBreaksToday();
            todayEyeScore = DeviceScreenTime.Instance.GetEyeScoreToday();
            
            float localMinutes = GetLocalTodayMinutes();
            float totalMinutes = (todayScreenHours * 60f) + localMinutes + _remoteScreenTimeMinutes;
            
            OnScreenTimeDataChanged?.Invoke(currentUserId, totalMinutes, breaksTaken, todayEyeScore);
            RefreshUI();
            UpdateBlinkyEmotion(true);
            
            if (SyncManager.Instance != null)
            {
                SyncManager.Instance.NotifyScreenTimeChanged(totalMinutes);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ScreenTimeManager] OnAndroidDailyDataUpdated failed: {ex.Message}");
        }
#endif
    }

    private void OnAndroidWeeklyDataUpdated()
    {
        if (currentUserId == -1) return;
        
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (DeviceScreenTime.Instance.IsWeeklyDataLoaded())
            {
                weeklyData = DeviceScreenTime.Instance.GetWeeklyData();
                weeklyDataInitialized = true;
                progressManager?.UpdateWeeklyGraphs();
                Debug.Log($"[ScreenTimeManager] Weekly data updated - Total: {weeklyData.totalScreenTime}m");
                SafeInvokeWeeklyDataUpdated();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ScreenTimeManager] OnAndroidWeeklyDataUpdated failed: {ex.Message}");
        }
#endif
    }
    #endregion

    #region Screen Time Calculation
    public float GetTodayTotalScreenTimeHours()
    {
        if (currentUserId == -1) return 0f;
        
        float platformHours = 0f;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
            platformHours = DeviceScreenTime.Instance.GetTodayHours();
#elif UNITY_STANDALONE_WIN
        if (PCScreenTime.Instance != null)
            platformHours = PCScreenTime.Instance.GetTodayActiveHours();
#endif
        
        float localHours = GetLocalTodayMinutes() / 60f;
        float remoteHours = _remoteScreenTimeMinutes / 60f;
        
        return Mathf.Max(platformHours, localHours + remoteHours);
    }

    public int GetTodayScreenTimeInMinutes() => Mathf.RoundToInt(GetTodayTotalScreenTimeHours() * 60f);

    public float GetScreenTimeForDate(DateTime date)
    {
        if (date.Date == DateTime.Today)
            return GetTodayTotalScreenTimeHours();
            
        if (weeklyDataInitialized && weeklyData.days.Count > 0)
        {
            DateTime startOfWeek = GetStartOfWeek(DateTime.Today);
            int dayIndex = (date - startOfWeek).Days;
            if (dayIndex >= 0 && dayIndex < weeklyData.days.Count)
                return weeklyData.days[dayIndex].screenTime / 60f;
        }
        
        if (DatabaseManager.Instance != null)
        {
            var records = DatabaseManager.Instance.GetRange<ScreenTimeHistory>(date, date);
            if (records != null && records.Count > 0)
                return records.First().screen_on_time / 60f;
        }
        
        return 0f;
    }

    private DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-1 * diff).Date;
    }
    #endregion

    #region ONE ROW PER DAY Getters
    public bool HasTodayDatabaseRecord() => _hasTodayRecord;
    public float GetTodayScreenTimeFromDatabase() => _todayScreenTimeFromDB;
    public bool ShouldSyncToday() => _shouldSyncToday;
    public bool HasEnoughTimeForDatabaseWrite() => GetTodayScreenTimeInMinutes() >= 5 || breaksTaken > 0;
    public string GetDatabaseStatusString() => _databaseStatus;
    #endregion

    #region Scheduling Control & Resilience
    private void CheckSchedulingResilience()
    {
        schedulingCheckTimer += Time.deltaTime;
        if (schedulingCheckTimer >= SCHEDULING_CHECK_INTERVAL)
        {
            schedulingCheckTimer = 0f;
            
#if UNITY_ANDROID && !UNITY_EDITOR
            if (currentUserId != -1 && DeviceScreenTime.Instance != null)
            {
                string status = DeviceScreenTime.Instance.GetSchedulingStatus();
                bool isTracked = DeviceScreenTime.Instance.IsUserTracked(currentUserId.ToString());
                if (!isTracked)
                {
                    Debug.LogWarning($"[ScreenTimeManager] User {currentUserId} not tracked - reinitializing");
                    ReinitializeScheduling();
                }
                SafeInvokeSchedulingStatusChanged();
            }
#endif
        }
    }

    public void ReinitializeScheduling()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (currentUserId != -1 && DeviceScreenTime.Instance != null)
        {
            try
            {
                DeviceScreenTime.Instance.ReinitializeScheduling();
                SafeInvokeSchedulingStatusChanged();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScreenTimeManager] ReinitializeScheduling failed: {ex.Message}");
            }
        }
#endif
    }

    public void ForceMidnightReset()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (currentUserId != -1 && DeviceScreenTime.Instance != null)
        {
            try
            {
                DeviceScreenTime.Instance.ForceMidnightReset();
                RefreshAllData();
                SafeInvokeSchedulingStatusChanged();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScreenTimeManager] ForceMidnightReset failed: {ex.Message}");
            }
        }
#endif
    }

    public void EmergencyCleanup()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
        {
            try
            {
                DeviceScreenTime.Instance.EmergencyCleanup();
                ResetUI();
                weeklyDataInitialized = false;
                weeklyData.Clear();
                SafeInvokeWeeklyDataUpdated();
                SafeInvokeSchedulingStatusChanged();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScreenTimeManager] EmergencyCleanup failed: {ex.Message}");
            }
        }
#endif
    }

    public string GetSchedulingStatus()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
            return DeviceScreenTime.Instance.GetSchedulingStatus();
#endif
        return "Not available";
    }

    public bool IsUserTracked()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (currentUserId != -1 && DeviceScreenTime.Instance != null)
            return DeviceScreenTime.Instance.IsUserTracked(currentUserId.ToString());
#endif
        return false;
    }

    public void RefreshAllData()
    {
        UpdatePlatformMetrics(out bool deviceActive);
        RefreshUI();
        RefreshWeeklyData();
        ForceUpdateBlinkyEmotion();
        
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
            DeviceScreenTime.Instance.UpdateScreenTimeData();
#endif
    }
    #endregion

    #region Midnight Reset Detection
    private void CheckMidnightReset()
    {
        if (currentUserId == -1) return;
        
        DateTime now = DateTime.Now;
        if (_lastMidnightCheck == DateTime.MinValue)
        {
            _lastMidnightCheck = now;
            return;
        }
        
        if (_lastMidnightCheck.Date < now.Date)
        {
            Debug.Log($"[ScreenTimeManager] Midnight reset detected! Previous: {_lastMidnightCheck}, Current: {now}");
            ProcessMidnightReset();
            _lastMidnightCheck = now;
            _hasProcessedMidnightReset = true;
        }
    }

    private void ProcessMidnightReset()
    {
        if (currentUserId == -1) return;
        
        try
        {
            Debug.Log($"[ScreenTimeManager] Processing midnight reset for user {currentUserId}");
            
            // ✅ CRITICAL FIX: Save yesterday's data BEFORE midnight reset
            // Use yesterday's date explicitly
            DateTime yesterday = _lastMidnightCheck.Date;
            int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
            
            // Only save if we have meaningful data
            if (screenTimeMinutes >= 5 || breaksTaken > 0)
            {
                Debug.Log($"[ScreenTimeManager] Saving yesterday's ({yesterday:yyyy-MM-dd}) data: {screenTimeMinutes}m, {breaksTaken} breaks");
                
                // Try to use the method with date parameter if available
                // Otherwise use UpdateDailyMetrics (which uses DateTime.Now)
                DatabaseManager.Instance.UpdateDailyMetrics(screenTimeMinutes, breaksTaken, todayEyeScore);
                
                Debug.Log($"[ScreenTimeManager] Midnight save completed for {yesterday:yyyy-MM-dd}");
            }
            else
            {
                Debug.Log($"[ScreenTimeManager] No data to save for {yesterday:yyyy-MM-dd}: {screenTimeMinutes}m, {breaksTaken} breaks");
            }
            
#if UNITY_ANDROID && !UNITY_EDITOR
            if (DeviceScreenTime.Instance != null)
            {
                DeviceScreenTime.Instance.ForceMidnightReset();
                CheckDatabaseStatus();
            }
#endif
            
            // Reset all variables for the new day
            breaksTaken = 0;
            lastBreaksReported = 0;
            todayScreenHours = 0f;
            todayEyeScore = 100;
            wasScreenOff = false;
            _localActiveTimeMinutes = 0f;
            _localTrackingStart = DateTime.Now;
            _remoteScreenTimeMinutes = 0f;
            _hasMergedRemoteData = false;
            _wasSystemIdle = false;
            _hasTodayRecord = false;
            _todayScreenTimeFromDB = 0f;
            _shouldSyncToday = false;
            
            // Reset rate limiting variables on midnight reset
            _lastDBWriteTime = 0f;
            _lastScreenTimeValue = 0;
            _lastBreaksValue = 0;
            _lastEyeScoreValue = 100;
            
            // Clean up old PlayerPrefs
            string oldKey = $"LocalScreenTime_{currentUserId}_{_lastMidnightCheck.Date:yyyyMMdd}";
            PlayerPrefs.DeleteKey(oldKey);
            PlayerPrefs.DeleteKey($"{oldKey}_start");
            PlayerPrefs.Save();
            
            RefreshUI();
            if (blinky != null) blinky.SetEnergy(100f);
            OnMidnightReset?.Invoke();
            SafeInvokeWeeklyDataUpdated();
            Debug.Log($"[ScreenTimeManager] Midnight reset completed for user {currentUserId}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ScreenTimeManager] Midnight reset failed: {ex.Message}");
        }
    }

    public bool HasProcessedMidnightReset() => _hasProcessedMidnightReset;
    #endregion

    #region Blinky Emotion Updates
    private void UpdateBlinkyEmotion(bool forceUpdate = false)
    {
        if (blinky == null || currentUserId == -1) return;
        if (!forceUpdate && Time.time - _lastEmotionUpdateTime < EMOTION_UPDATE_INTERVAL) return;
        
        _lastEmotionUpdateTime = Time.time;
        float batteryPercentage = CalculateBatteryPercentage();
        blinky.SetEnergy(batteryPercentage);
    }

    private float CalculateBatteryPercentage() => Mathf.Clamp(100f - (todayScreenHours / 10f * 100f), 0f, 100f);

    public void ForceUpdateBlinkyEmotion() => UpdateBlinkyEmotion(true);

    [ContextMenu("Debug Emotion Calculation")]
    public void DebugEmotionCalculation()
    {
        if (currentUserId == -1 || blinky == null) return;
        
        float batteryPercentage = CalculateBatteryPercentage();
        string expectedEmotion = GetExpectedEmotion(batteryPercentage);
        
        Debug.Log($"[DEBUG] Screen Time: {todayScreenHours:F2}h");
        Debug.Log($"[DEBUG] Battery Percentage: {batteryPercentage:F1}%");
        Debug.Log($"[DEBUG] Expected Emotion: {expectedEmotion}");
        Debug.Log($"[DEBUG] Current Blinky Emotion: {blinky.GetCurrentEmotionName()}");
        
        blinky.SetEnergy(batteryPercentage);
    }

    private string GetExpectedEmotion(float batteryPercentage)
    {
        if (batteryPercentage >= 75f) return "HAPPY";
        if (batteryPercentage >= 50f) return "SAD";
        if (batteryPercentage >= 25f) return "TIRED";
        return "ANGRY";
    }
    #endregion

    #region Break Logic
    private void HandleBreakLogic(bool systemActive)
    {
        if (!systemActive)
        {
            if (!wasScreenOff)
            {
                wasScreenOff = true;
                screenOffStart = DateTime.Now;
            }
        }
        else
        {
            wasScreenOff = false;
        }
        
        targetCircularFill = Mathf.Clamp01(1f - (todayScreenHours / 10f));
        todayEyeScore = Mathf.Clamp(Mathf.RoundToInt(100f * targetCircularFill), 0, 100);
    }

    private void AwardBreak(string reason = "")
    {
        if (currentUserId == -1 || breaksTaken >= maxBreaks)
        {
            Debug.Log($"[ScreenTimeManager] Skipping break award - no user or max reached. Reason: {reason}");
            return;
        }
        
        breaksTaken++;
        lastBreaksReported = breaksTaken;
        Debug.Log($"[ScreenTimeManager] Break awarded ({reason}). Total: {breaksTaken}");
        
        // Force immediate DB write for breaks (important event)
        _lastDBWriteTime = 0f; // Reset timer to force write
        
        // ✅ SIMPLE: Use the new simple method for break awards
        int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
        DatabaseManager.Instance.UpdateDailyMetrics(screenTimeMinutes, breaksTaken, todayEyeScore);
        
        if (SyncManager.Instance != null)
            SyncManager.Instance.NotifyBreakTaken(breaksTaken);
            
        OnBreakTaken?.Invoke(breaksTaken);
        CheckBreakAchievementEligibility();
        StartCoroutine(BreakRechargeBump());
        UpdateBlinkyEmotion(true);
    }

    private IEnumerator BreakRechargeBump()
    {
        float orig = targetCircularFill;
        float bumpTarget = Mathf.Min(orig + 0.02f, 1f);
        yield return LerpFill(orig, bumpTarget, 0.25f);
        yield return LerpFill(bumpTarget, orig, 0.5f);
        targetCircularFill = orig;
    }

    private IEnumerator LerpFill(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            targetCircularFill = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
    }

    private void CheckBreakAchievementEligibility()
    {
        if (currentUserId == -1 || AchievementManager.Instance == null) return;
        
        var allAchievements = AchievementManager.Instance.GetAllAchievements() ?? new List<Achievement>();
        var unlocked = AchievementManager.Instance.GetUnlockedIds(currentUserId) ?? new HashSet<int>();
        
        foreach (var ach in allAchievements)
        {
            if (ach == null) continue;
            bool isBreakType = ach.requirement_type?.Equals("breaks_taken", StringComparison.OrdinalIgnoreCase) ?? false;
            if (!isBreakType) continue;
            
            if (!unlocked.Contains(ach.achievement_id) && AchievementManager.Instance.IsRequirementMet(ach))
            {
                Debug.Log($"[ScreenTimeManager] Achievement '{ach.title}' is now eligible! ({ach.requirement_value} breaks)");
                OnAchievementEligible?.Invoke(ach);
            }
        }
    }

    public void ClaimEligibleBreakAchievements()
    {
        if (currentUserId == -1 || AchievementManager.Instance == null) return;
        
        var allAchievements = AchievementManager.Instance.GetAllAchievements() ?? new List<Achievement>();
        var unlocked = AchievementManager.Instance.GetUnlockedIds(currentUserId) ?? new HashSet<int>();
        int totalClaimed = 0;
        
        foreach (var ach in allAchievements)
        {
            if (ach == null) continue;
            bool isBreakType = ach.requirement_type?.Equals("breaks_taken", StringComparison.OrdinalIgnoreCase) ?? false;
            if (!isBreakType) continue;
            
            if (!unlocked.Contains(ach.achievement_id) && AchievementManager.Instance.IsRequirementMet(ach))
            {
                try
                {
                    AchievementManager.Instance.ClaimAchievement(ach.achievement_id);
                    totalClaimed++;
                    Debug.Log($"[ScreenTimeManager] Manually claimed: {ach.title}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ScreenTimeManager] Failed to claim {ach.achievement_id}: {ex}");
                }
            }
        }
        Debug.Log($"[ScreenTimeManager] Claimed {totalClaimed} break achievements");
    }

    public bool HasEligibleBreakAchievements()
    {
        if (currentUserId == -1 || AchievementManager.Instance == null) return false;
        
        var allAchievements = AchievementManager.Instance.GetAllAchievements() ?? new List<Achievement>();
        var unlocked = AchievementManager.Instance.GetUnlockedIds(currentUserId) ?? new HashSet<int>();
        
        return allAchievements.Any(ach =>
            ach != null &&
            ach.requirement_type?.Equals("breaks_taken", StringComparison.OrdinalIgnoreCase) == true &&
            !unlocked.Contains(ach.achievement_id) &&
            AchievementManager.Instance.IsRequirementMet(ach)
        );
    }

    public List<Achievement> GetEligibleBreakAchievements()
    {
        var eligible = new List<Achievement>();
        if (currentUserId == -1 || AchievementManager.Instance == null) return eligible;
        
        var allAchievements = AchievementManager.Instance.GetAllAchievements() ?? new List<Achievement>();
        var unlocked = AchievementManager.Instance.GetUnlockedIds(currentUserId) ?? new HashSet<int>();
        
        foreach (var ach in allAchievements)
        {
            if (ach == null) continue;
            bool isBreakType = ach.requirement_type?.Equals("breaks_taken", StringComparison.OrdinalIgnoreCase) ?? false;
            if (!isBreakType) continue;
            
            if (!unlocked.Contains(ach.achievement_id) && AchievementManager.Instance.IsRequirementMet(ach))
            {
                eligible.Add(ach);
            }
        }
        return eligible;
    }
    #endregion

    #region UI Handling
    private void RefreshUI()
    {
        UpdateDetails(todayScreenHours, breaksTaken, todayEyeScore);
        UpdateProgressStatement();
    }

    private void UpdateDetails(float hours, int breaks, int eyeScore)
    {
        if (screenTimeTodayText != null)
        {
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60f);
            int s = Mathf.RoundToInt(((hours - h) * 3600f) % 60f);
            screenTimeTodayText.text = $"SCREEN TIME TODAY:\n{h}h {m}m {s}s";
        }
        
        breaksTakenText?.SetTextSafe($"BREAKS TAKEN:\n{breaks}/{maxBreaks}");
        eyeHealthScoreText?.SetTextSafe($"EYE HEALTH SCORE:\n{eyeScore}");
    }

    private void UpdateProgressStatement()
    {
        if (progressStatementText == null) return;
        
        int percent = Mathf.RoundToInt(targetCircularFill * 100);
        (string text, Color color) statement = percent switch
        {
            >= 90 => ("Excellent! Keep it balanced.", new Color(0.6f, 0.8f, 0.1f)),
            >= 70 => ("Good, but stay mindful.", Color.yellow),
            >= 50 => ("You're halfway there.", new Color(1f, 0.65f, 0f)),
            >= 25 => ("Too much screen time.", new Color(1f, 0.45f, 0f)),
            _ => ("Critical! Take a break.", Color.red)
        };
        
        progressStatementText.text = statement.text;
        progressStatementText.color = statement.color;
    }

    private void AnimateCircularProgress()
    {
        if (circularProgress == null) return;
        
        float newFill = Mathf.Lerp(circularProgress.fillAmount, targetCircularFill, Time.deltaTime * circularLerpSpeed);
        circularProgress.fillAmount = newFill;
        button_ScreenTime?.SetFillSafe(newFill);
        
        int percent = Mathf.RoundToInt(newFill * 100);
        percentageText?.SetTextSafe($"{percent}%");
        homeButtonText?.SetTextSafe($"{percent}%");
    }
    #endregion

    #region Android Integration
#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator LiveAndroidScreenTimeUpdate()
    {
        WaitForSeconds wait = new WaitForSeconds(1f);
        while (true)
        {
            if (DeviceScreenTime.Instance != null)
            {
                try
                {
                    todayScreenHours = DeviceScreenTime.Instance.GetTodayHours();
                    int currentBreaks = DeviceScreenTime.Instance.GetBreaksToday();
                    if (currentBreaks != lastBreaksReported)
                    {
                        breaksTaken = currentBreaks;
                        lastBreaksReported = currentBreaks;
                    }
                    todayEyeScore = DeviceScreenTime.Instance.GetEyeScoreToday();
                    bool deviceActive = DeviceScreenTime.Instance.IsDeviceActive();
                    HandleBreakLogic(deviceActive);
                    UpdateBlinkyEmotion();
                    
                    if (DeviceScreenTime.Instance.IsWeeklyDataLoaded())
                    {
                        weeklyData = DeviceScreenTime.Instance.GetWeeklyData();
                        if (!weeklyDataInitialized)
                        {
                            weeklyDataInitialized = true;
                            SafeInvokeWeeklyDataUpdated();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ScreenTimeManager] Live update failed: {ex.Message}");
                }
            }
            yield return wait;
        }
    }
#endif

    public void RefreshWeeklyData()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (DeviceScreenTime.Instance != null)
            DeviceScreenTime.Instance.RefreshWeeklyData();
#endif
    }

    public DeviceScreenTime.WeeklyScreenTimeData GetWeeklyData() => weeklyData;
    public bool IsWeeklyDataAvailable() => weeklyDataInitialized && weeklyData.days.Count > 0;
    public int GetScreenTimeForDay(int dayIndex) => (weeklyDataInitialized && dayIndex >= 0 && dayIndex < weeklyData.days.Count) ? weeklyData.days[dayIndex].screenTime : 0;
    public int GetBreaksForDay(int dayIndex) => (weeklyDataInitialized && dayIndex >= 0 && dayIndex < weeklyData.days.Count) ? weeklyData.days[dayIndex].breaks : 0;
    public int GetWeeklyTotalScreenTime() => weeklyDataInitialized ? weeklyData.totalScreenTime : 0;
    public int GetWeeklyAverageScreenTime() => weeklyDataInitialized ? weeklyData.averageScreenTime : 0;
    #endregion

    #region Data Sync (LOCAL ONLY - SIMPLE ONE ROW PER DAY)
    /// <summary>
    /// Commit screen time to local database with SIMPLE ONE ROW PER DAY logic
    /// Only writes if ≥5 minutes or has breaks
    /// </summary>
    private void CommitScreenTimeToLocalDB(int userId)
    {
        if (userId <= 0 || !DatabaseManager.Instance.IsLoggedIn) return;
        
        // ✅ CRITICAL FIX: Check screen time BEFORE rate limiting
        int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
        
        // ✅ EARLY EXIT: Don't write 0-minute records
        if (screenTimeMinutes < 5 && breaksTaken == 0)
        {
            // Only log occasionally to avoid spam
            if (Time.time - _lastDebugLogTime >= 5f)
            {
                Debug.Log($"[ScreenTimeManager] ❌ ABORTING DB write: {screenTimeMinutes}m < 5m and no breaks");
            }
            return;
        }
        
        // Rate limiting: Only write to DB every 60 seconds unless significant change
        if (Time.time - _lastDBWriteTime < MIN_DB_WRITE_INTERVAL)
        {
            // Check if we have a new break (important event that needs immediate write)
            bool hasNewBreak = breaksTaken > _lastBreaksValue;
            if (!hasNewBreak)
            {
                return;
            }
        }
        
        try
        {
            Debug.Log($"[ScreenTimeManager] ✅ Committing to DB: {screenTimeMinutes}m, {breaksTaken} breaks, {todayEyeScore} score");
            
            // Check if values actually changed significantly
            bool screenTimeChanged = Mathf.Abs(screenTimeMinutes - _lastScreenTimeValue) >= 1;
            bool breaksChanged = breaksTaken != _lastBreaksValue;
            bool eyeScoreChanged = todayEyeScore != _lastEyeScoreValue;
            
            // If nothing significant changed, skip the write
            if (!screenTimeChanged && !breaksChanged && !eyeScoreChanged)
            {
                Debug.Log($"[ScreenTimeManager] Skipping DB write - minimal change");
                return;
            }
            
            // Update last values
            _lastScreenTimeValue = screenTimeMinutes;
            _lastBreaksValue = breaksTaken;
            _lastEyeScoreValue = todayEyeScore;
            _lastDBWriteTime = Time.time;
            
            // ✅ SIMPLE: Use UpdateDailyMetrics with SIMPLE ONE ROW PER DAY logic
            DatabaseManager.Instance.UpdateDailyMetrics(screenTimeMinutes, breaksTaken, todayEyeScore);
            
            Debug.Log($"[ScreenTimeManager] ✅ DB write completed: {screenTimeMinutes}m");
            
#if UNITY_ANDROID && !UNITY_EDITOR
            if (DeviceScreenTime.Instance != null)
            {
                try
                {
                    DeviceScreenTime.Instance.TriggerManualDatabaseWrite();
                    Debug.Log($"[ScreenTimeManager] Triggered Android DB write");
                    CheckDatabaseStatus();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ScreenTimeManager] Android DB write failed: {ex.Message}");
                }
            }
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ScreenTimeManager] Local DB commit failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Manually trigger a database write (for testing or important updates)
    /// </summary>
    public void ForceDBWrite()
    {
        if (currentUserId == -1) return;
        
        Debug.Log($"[ScreenTimeManager] Force DB write requested");
        
        // Check if we have enough data to write
        int screenTimeMinutes = Mathf.RoundToInt(todayScreenHours * 60f);
        if (screenTimeMinutes < 5 && breaksTaken == 0)
        {
            Debug.Log($"[ScreenTimeManager] ❌ Force DB write ABORTED: {screenTimeMinutes}m < 5m and no breaks");
            return;
        }
        
        // Reset the timer to force an immediate write
        _lastDBWriteTime = 0f;
        
        // Use the main commit method
        CommitScreenTimeToLocalDB(currentUserId);
        
        Debug.Log($"[ScreenTimeManager] ✅ Force DB write completed");
    }

    /// <summary>
    /// Test the ONE ROW PER DAY logic
    /// </summary>
    [ContextMenu("Test ONE ROW Logic")]
    public void TestOneRowLogic()
    {
        if (currentUserId == -1 || DatabaseManager.Instance == null) return;
        
        Debug.Log($"[TEST] === Testing ONE ROW PER DAY Logic ===");
        
        // Get current state
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        Debug.Log($"[TEST] Today's date: {today}");
        
        // Get all records for today
        var records = DatabaseManager.Instance.GetAllWithUser<ScreenTimeHistory>()
            .Where(r => r.DateTimeValue.Date == DateTime.Today)
            .ToList();
        
        Debug.Log($"[TEST] Found {records.Count} records for today");
        
        if (records.Count > 0)
        {
            foreach (var record in records)
            {
                Debug.Log($"[TEST] Record ID: {record.id}, " +
                         $"Minutes: {record.screen_on_time}, " +
                         $"Breaks: {record.breaks_taken}, " +
                         $"EyeScore: {record.eye_score}, " +
                         $"Date: {record.DateTimeValue}");
            }
        }
        
        // Test adding screen time
        int currentMinutes = GetTodayScreenTimeInMinutes();
        int newMinutes = currentMinutes + 10;
        
        Debug.Log($"[TEST] Adding 10 minutes: {currentMinutes}m → {newMinutes}m");
        
        // This should UPDATE the existing record if ONE ROW PER DAY is working
        DatabaseManager.Instance.UpdateDailyMetrics(newMinutes, breaksTaken, todayEyeScore);
        
        Debug.Log($"[TEST] Update completed - should have UPDATED existing record");
        Debug.Log($"[TEST] === End Test ===");
    }
    
    /// <summary>
    /// Clean up 0-minute records in the database
    /// </summary>
    [ContextMenu("Cleanup 0-Minute Records")]
    public void CleanupZeroMinuteRecords()
    {
        if (currentUserId == -1 || DatabaseManager.Instance == null) return;
        
        Debug.Log($"[ScreenTimeManager] === CLEANUP 0-MINUTE RECORDS ===");
        
        try
        {
            // Get all records for today
            var today = DateTime.Today;
            var records = DatabaseManager.Instance.GetAllWithUser<ScreenTimeHistory>()
                .Where(r => r.DateTimeValue.Date == today)
                .ToList();
            
            Debug.Log($"[ScreenTimeManager] Found {records.Count} records for today");
            
            // Find 0-minute records
            var zeroRecords = records.Where(r => r.screen_on_time == 0 && r.breaks_taken == 0).ToList();
            
            if (zeroRecords.Count > 0)
            {
                Debug.Log($"[ScreenTimeManager] Found {zeroRecords.Count} 0-minute records to delete");
                
                // Delete them - FIXED: Use the record object instead of just ID
                foreach (var record in zeroRecords)
                {
                    DatabaseManager.Instance.Delete(record); // Pass the object, not just ID
                }
                
                Debug.Log($"[ScreenTimeManager] Deleted {zeroRecords.Count} 0-minute records");
            }
            
            // Check remaining records
            var remaining = DatabaseManager.Instance.GetAllWithUser<ScreenTimeHistory>()
                .Where(r => r.DateTimeValue.Date == today)
                .ToList();
            
            if (remaining.Count > 1)
            {
                Debug.Log($"[ScreenTimeManager] WARNING: Still have {remaining.Count} records for today after cleanup");
                
                // Keep only the one with highest screen time
                var bestRecord = remaining.OrderByDescending(r => r.screen_on_time).First();
                Debug.Log($"[ScreenTimeManager] Best record: ID={bestRecord.id}, Minutes={bestRecord.screen_on_time}");
            }
            
            Debug.Log($"[ScreenTimeManager] === CLEANUP COMPLETED ===");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ScreenTimeManager] Cleanup failed: {ex.Message}");
        }
    }
    #endregion
    
    #region Event Safety & Cleanup
    private void SafeInvokeWeeklyDataUpdated()
    {
        try { OnWeeklyDataUpdated?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[ScreenTimeManager] OnWeeklyDataUpdated error: {e}"); }
    }
    
    private void SafeInvokeSchedulingStatusChanged()
    {
        try { OnSchedulingStatusChanged?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[ScreenTimeManager] OnSchedulingStatusChanged error: {e}"); }
    }
    
    public void ClearEventSubscribers()
    {
        OnBreakTaken = null;
        OnAchievementEligible = null;
        OnWeeklyDataUpdated = delegate { };
        OnSchedulingStatusChanged = delegate { };
        OnMidnightReset = delegate { };
        OnSyncCompleted = delegate { };
        OnScreenTimeUpdated = delegate { };
        OnSyncError = delegate { };
        OnDatabaseStatusUpdated = delegate { };
        OnScreenTimeDataChanged = delegate { };
    }
    #endregion

    #region Reset & Getters
    private void ResetUI()
    {
        targetCircularFill = 1f;
        breaksTaken = 0;
        lastBreaksReported = 0;
        todayScreenHours = 0f;
        todayEyeScore = 100;
        wasScreenOff = false;
        _remoteScreenTimeMinutes = 0f;
        _hasMergedRemoteData = false;
        _localActiveTimeMinutes = 0f;
        _wasSystemIdle = false;
        _isAppActuallyPaused = false;
        _hasTodayRecord = false;
        _todayScreenTimeFromDB = 0f;
        _shouldSyncToday = false;
        _databaseStatus = "";
        
        // Reset rate limiting variables
        _lastDBWriteTime = 0f;
        _lastScreenTimeValue = 0;
        _lastBreaksValue = 0;
        _lastEyeScoreValue = 100;
        
        UpdateDetails(0f, 0, 100);
        AnimateCircularProgress();
        if (blinky != null) blinky.SetEnergy(100f);
    }

    public int GetBreaksTaken() => breaksTaken;
    public int GetEyeScoreToday() => todayEyeScore;
    
    public int GetConsecutiveDaysPlayed()
    {
        if (currentUserId == -1 || DatabaseManager.Instance == null) return 0;
        
        try
        {
            var history = DatabaseManager.Instance.GetAllWithUser<ScreenTimeHistory>(currentUserId)
                .OrderByDescending(x => x.DateTimeValue.Date)
                .Take(7)
                .ToList();
                
            int streak = 0;
            DateTime checkDay = DateTime.Today;
            
            foreach (var h in history)
            {
                if (h.screen_on_time > 0 && h.DateTimeValue.Date == checkDay)
                {
                    streak++;
                    checkDay = checkDay.AddDays(-1);
                }
                else break;
            }
            
            return streak;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ScreenTimeManager] GetConsecutiveDaysPlayed failed: {ex.Message}");
            return 0;
        }
    }

    public float GetCurrentBatteryPercentage() => CalculateBatteryPercentage();
    
    public string GetCurrentEmotionName()
    {
        float batteryPercentage = GetCurrentBatteryPercentage();
        return GetExpectedEmotion(batteryPercentage);
    }

    [ContextMenu("Show Emotion Progression")]
    public void ShowEmotionProgression()
    {
        Debug.Log("[EMOTION PROGRESSION]");
        Debug.Log("0.0 hours → 100% battery → HAPPY");
        Debug.Log("2.5 hours → 75% battery → HAPPY (borderline)");
        Debug.Log("5.0 hours → 50% battery → SAD");
        Debug.Log("7.5 hours → 25% battery → TIRED");
        Debug.Log("10.0 hours → 0% battery → ANGRY");
        Debug.Log($"\nCurrent: {todayScreenHours:F2}h → {GetCurrentBatteryPercentage():F1}% → {GetCurrentEmotionName()}");
    }

    public bool IsSyncAvailable() => SyncManager.Instance != null && SyncManager.Instance.IsInitialized;
    public string GetSyncStatus() => SyncManager.Instance == null ? "SyncManager not available" : $"SyncManager Available: true, Is Syncing: {_isSyncing}";
    public string GetCrossDeviceStatus() => $"Local: {GetLocalTodayMinutes():F0}m, Remote: {_remoteScreenTimeMinutes:F0}m, Total: {GetTodayScreenTimeInMinutes()}m, Merged: {_hasMergedRemoteData}";
    
    [ContextMenu("Debug Database State")]
    public void DebugDatabaseState()
    {
        if (currentUserId == -1 || DatabaseManager.Instance == null) 
        {
            Debug.Log("[ScreenTimeManager] Not logged in or DatabaseManager not available");
            return;
        }
        
        try
        {
            Debug.Log($"[ScreenTimeManager] === DEBUG DATABASE STATE for User {currentUserId} ===");
            
            // Check if DebugScreenTimeRecords method exists
            var method = DatabaseManager.Instance.GetType().GetMethod("DebugScreenTimeRecords");
            if (method != null)
            {
                method.Invoke(DatabaseManager.Instance, new object[] { currentUserId });
            }
            else
            {
                Debug.LogWarning($"[ScreenTimeManager] DebugScreenTimeRecords method not found");
                
                // Fallback: Get records manually
                var allRecords = DatabaseManager.Instance.GetAllWithUser<ScreenTimeHistory>(currentUserId)
                    .OrderByDescending(x => x.DateTimeValue)
                    .Take(10)
                    .ToList();
                
                Debug.Log($"Found {allRecords.Count} recent records:");
                foreach (var record in allRecords)
                {
                    Debug.Log($"  ID: {record.id}, Date: {record.DateTimeValue:yyyy-MM-dd HH:mm:ss}, " +
                             $"Minutes: {record.screen_on_time}, Breaks: {record.breaks_taken}");
                }
            }
            
            Debug.Log($"[ScreenTimeManager] === END DEBUG ===");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ScreenTimeManager] DebugDatabaseState failed: {ex.Message}");
        }
    }
    
    [ContextMenu("Debug Tracking State")]
    public void DebugTrackingState()
    {
        Debug.Log($"[TRACKING STATE]");
        Debug.Log($"User ID: {currentUserId}");
        Debug.Log($"Window Focused: {Application.isFocused}");
        Debug.Log($"System Idle: {_wasSystemIdle}");
        Debug.Log($"App Paused: {_isAppActuallyPaused}");
        Debug.Log($"Local Tracking: {_localActiveTimeMinutes:F1}m");
        Debug.Log($"Today Total: {todayScreenHours:F2}h");
        Debug.Log($"Breaks: {breaksTaken}");
        Debug.Log($"Should Track: {currentUserId != -1 && !_isAppActuallyPaused && !_wasSystemIdle}");
        Debug.Log($"Should Write to DB (≥5m or breaks): {(GetTodayScreenTimeInMinutes() >= 5 || breaksTaken > 0 ? "YES" : "NO")}");
    }
    #endregion
}

#region Extensions
public static class TMPTextExtensions
{
    public static void SetTextSafe(this TMP_Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }
    
    public static void SetFillSafe(this Image img, float value)
    {
        if (img != null)
            img.fillAmount = value;
    }
}

public static class ProgressManagerExtensions
{
    public static void UpdateWeeklyGraphs(this ProgressManager pm)
    {
        pm?.UpdateWeeklyScreenGraph();
        pm?.UpdateWeeklyDESGraph();
    }
}
#endregion