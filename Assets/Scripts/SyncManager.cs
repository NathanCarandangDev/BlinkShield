using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text;

public class SyncManager : MonoBehaviour
{
    public static SyncManager Instance { get; private set; }
    private const string SyncHubURL = "https://blinkshield.alwaysdata.net/BlinkShield/sync_hub.php";
    
    [Header("Sync Configuration")]
    [SerializeField] private float _pushCooldown = 2f;
    
    [Header("Pull Settings")]
    [SerializeField] private float _pullOnLoginDelay = 1f;
    
    [Header("Performance Settings")]
    [SerializeField] private float _pushDebounceTime = 0.5f;
    [SerializeField] private int _batchSize = 3;
    
    [Header("Screen Time Sync")]
    [SerializeField] private float _screenTimeSyncInterval = 300f; // Sync every 5 minutes
    [SerializeField] private float _screenTimeChangeThreshold = 5f; // Sync when change ≥5 min

    // Private fields
    private DatabaseManager _dbManager => DatabaseManager.Instance;
    private SessionManager _sessionManager => SessionManager.Instance;
    private bool DevMode => _sessionManager?.DEV_MODE ?? false;

    // State tracking
    private bool _isInitialized = false;
    private bool _isSyncing = false;
    private long _cachedGlobalVersion = 0;
    private bool _globalDataLoaded = false;
    private bool _isPullingGlobalData = false;
    private bool _userDataPulled = false;
    private int _currentUserId = 0;
    private bool _isLoggedIn = false;

    // Simple tracking
    private Dictionary<string, DateTime> _lastPushTime = new Dictionary<string, DateTime>();
    private Dictionary<string, DateTime> _lastChangeTime = new Dictionary<string, DateTime>();
    private HashSet<string> _pendingTables = new HashSet<string>();

    // Queue system
    private Queue<string> _pushQueue = new Queue<string>();
    private Coroutine _pushProcessor = null;

    // Status tracking
    private DateTime _lastGlobalDataCheck = DateTime.MinValue;
    private DateTime _lastSuccessfulSync = DateTime.MinValue;

    // NEW: Screen time sync tracking
    private float _lastScreenTimeSync = 0f;
    private float _lastSyncedScreenTimeMinutes = 0f;
    private Dictionary<DateTime, float> _cachedScreenTimeData = new Dictionary<DateTime, float>();

    // Events
    public event Action<bool> OnGlobalDataLoaded;
    public event Action<bool> OnUserDataSynced;
    public event Action<float, DateTime> OnScreenTimeSynced = delegate { };
    public event Action<float, DateTime> OnScreenTimeReceived = delegate { };

    // Properties
    public bool IsPullingGlobalData => _isPullingGlobalData;
    public bool IsSyncing => _isSyncing;
    public DateTime LastSuccessfulSync => _lastSuccessfulSync;
    public bool IsInitialized => _isInitialized;
    public bool UserDataPulled => _userDataPulled;
    public bool GlobalDataLoaded => _globalDataLoaded;
    public long GlobalDataVersion => _cachedGlobalVersion;
    public TimeSpan TimeSinceLastGlobalCheck => DateTime.Now - _lastGlobalDataCheck;

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
        StartCoroutine(Initialize());
    }

    private void OnEnable() { }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        if (!_isInitialized || _isPullingGlobalData)
            return;

        // Daily global data check
        if (_globalDataLoaded && TimeSinceLastGlobalCheck.TotalHours >= 24)
        {
            _lastGlobalDataCheck = DateTime.Now;
            StartCoroutine(CheckForGlobalDataUpdates());
        }

        // Check screen time sync
        CheckScreenTimeSync();
    }
    #endregion

    #region Initialization & Global Data
    private IEnumerator Initialize()
    {
        UnityEngine.Debug.Log("[SyncManager] Initializing...");
        yield return new WaitForSeconds(1f);
        LoadCachedGlobalVersion();
        LoadCachedScreenTimeData();

        if (!_globalDataLoaded)
        {
            UnityEngine.Debug.Log("[SyncManager] No cached global data, loading fresh...");
            yield return StartCoroutine(PullGlobalData());
        }
        else
        {
            UnityEngine.Debug.Log($"[SyncManager] Global data cached (version: {_cachedGlobalVersion})");
            if (TimeSinceLastGlobalCheck.TotalHours >= 24)
            {
                UnityEngine.Debug.Log("[SyncManager] Daily global data check due");
                yield return StartCoroutine(CheckForGlobalDataUpdates());
            }
            else
            {
                TimeSpan timeRemaining = TimeSpan.FromHours(24) - TimeSinceLastGlobalCheck;
                UnityEngine.Debug.Log($"[SyncManager] Next global check in: {timeRemaining.Hours}h {timeRemaining.Minutes}m");
            }
        }

        _isInitialized = true;
        UnityEngine.Debug.Log("[SyncManager] Initialized");
    }

    private void LoadCachedGlobalVersion()
    {
        _cachedGlobalVersion = PlayerPrefs.GetInt("GlobalDataVersion", 0);
        _globalDataLoaded = (_cachedGlobalVersion > 0);
        string lastCheckString = PlayerPrefs.GetString("LastGlobalCheck", "");
        if (!string.IsNullOrEmpty(lastCheckString) && DateTime.TryParse(lastCheckString, out DateTime lastCheck))
        {
            _lastGlobalDataCheck = lastCheck;
        }
        else
        {
            _lastGlobalDataCheck = DateTime.MinValue;
        }
    }

    private void LoadCachedScreenTimeData()
    {
        string cachedData = PlayerPrefs.GetString("CachedScreenTimeData", "");
        if (!string.IsNullOrEmpty(cachedData))
        {
            try
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, float>>(cachedData);
                foreach (var kvp in data)
                {
                    if (DateTime.TryParse(kvp.Key, out DateTime date))
                    {
                        _cachedScreenTimeData[date.Date] = kvp.Value;
                    }
                }
                UnityEngine.Debug.Log($"[SyncManager] Loaded {_cachedScreenTimeData.Count} cached screen time entries");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SyncManager] Error loading cached screen time  {ex.Message}");
            }
        }
    }

    private void SaveCachedScreenTimeData()
    {
        try
        {
            var dataToSave = new Dictionary<string, float>();
            foreach (var kvp in _cachedScreenTimeData)
            {
                dataToSave[kvp.Key.ToString("yyyy-MM-dd")] = kvp.Value;
            }
            string jsonData = JsonConvert.SerializeObject(dataToSave);
            PlayerPrefs.SetString("CachedScreenTimeData", jsonData);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[SyncManager] Error saving cached screen time  {ex.Message}");
        }
    }

    private void SaveCachedGlobalVersion()
    {
        PlayerPrefs.SetInt("GlobalDataVersion", (int)_cachedGlobalVersion);
        PlayerPrefs.SetString("LastGlobalCheck", DateTime.Now.ToString());
        PlayerPrefs.Save();
        if (DevMode) UnityEngine.Debug.Log($"[SyncManager] Saved global version {_cachedGlobalVersion} at {DateTime.Now}");
    }

    public IEnumerator LoadGlobalData(Action<bool> callback = null)
    {
        _isPullingGlobalData = true;
        string url = $"{SyncHubURL}?global_data=1";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.timeout = 15;
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "success" && json["global_data"] is JObject globalData)
                    {
                        _dbManager.SyncGlobalDataDynamic(globalData);
                        _globalDataLoaded = true;
                        _cachedGlobalVersion = json["global_version"]?.Value<long>() ?? DateTimeOffset.Now.ToUnixTimeSeconds();
                        SaveCachedGlobalVersion();
                        UnityEngine.Debug.Log($"[SyncManager] Global data loaded (version: {_cachedGlobalVersion})");
                        _lastSuccessfulSync = DateTime.Now;
                        OnGlobalDataLoaded?.Invoke(true);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Error parsing global  {ex.Message}");
                    OnGlobalDataLoaded?.Invoke(false);
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"[SyncManager] Failed to load global  {www.error}");
                OnGlobalDataLoaded?.Invoke(false);
            }
        }
        _isPullingGlobalData = false;
        callback?.Invoke(_globalDataLoaded);
    }

    private IEnumerator CheckForGlobalDataUpdates()
    {
        UnityEngine.Debug.Log("[SyncManager] Checking for global data updates...");
        string url = $"{SyncHubURL}?check_global_version=1&current_version={_cachedGlobalVersion}";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.timeout = 10;
            yield return www.SendWebRequest();
            bool updateNeeded = false;
            long serverVersion = 0;

            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "update_available")
                    {
                        serverVersion = json["global_version"]?.Value<long>() ?? 0;
                        if (serverVersion > _cachedGlobalVersion)
                        {
                            updateNeeded = true;
                        }
                        else
                        {
                            SaveCachedGlobalVersion();
                            UnityEngine.Debug.Log($"[SyncManager] Global data is up to date (version: {_cachedGlobalVersion})");
                        }
                    }
                    else if (json["status"]?.ToString() == "up_to_date")
                    {
                        SaveCachedGlobalVersion();
                        UnityEngine.Debug.Log($"[SyncManager] Global data is up to date");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Error checking global updates: {ex.Message}");
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"[SyncManager] Network error checking global updates: {www.error}");
            }

            if (updateNeeded)
            {
                UnityEngine.Debug.Log($"[SyncManager] Global data update available (local: {_cachedGlobalVersion}, server: {serverVersion})");
                yield return StartCoroutine(PullGlobalData());
            }
        }
    }

    private IEnumerator PullGlobalData()
    {
        yield return StartCoroutine(LoadGlobalData());
    }
    #endregion

    #region User Login/Logout
    public void OnUserLogin(User user)
    {
        if (user == null || user.id <= 0) return;

        _currentUserId = user.id;
        _isLoggedIn = true;
        _userDataPulled = false;
        UnityEngine.Debug.Log($"[SyncManager] User logged in: {user.id}");

        _pendingTables.Clear();
        _lastPushTime.Clear();
        _lastChangeTime.Clear();
        _pushQueue.Clear();

        _lastSyncedScreenTimeMinutes = 0f;
        _lastScreenTimeSync = Time.time;

        if (!_globalDataLoaded || _isPullingGlobalData)
        {
            UnityEngine.Debug.Log("[SyncManager] Waiting for global data to load before pulling user data...");
            StartCoroutine(WaitForGlobalDataThenPullUserData());
        }
        else
        {
            StartCoroutine(PullUserDataOnLogin());
        }
    }

    private IEnumerator WaitForGlobalDataThenPullUserData()
    {
        while (_isPullingGlobalData) yield return null;
        if (_globalDataLoaded)
        {
            StartCoroutine(PullUserDataOnLogin());
        }
        else
        {
            UnityEngine.Debug.LogError("[SyncManager] Global data failed to load, cannot pull user data");
        }
    }

    private IEnumerator PullUserDataOnLogin()
    {
        yield return new WaitForSeconds(_pullOnLoginDelay);
        if (!_isLoggedIn) yield break;

        UnityEngine.Debug.Log($"[SyncManager] Checking server for user {_currentUserId}...");
        bool userExists = false;
        bool hasData = false;

        yield return StartCoroutine(ProbeUserCoroutine(_currentUserId, (exists, hasServerData) =>
        {
            userExists = exists;
            hasData = hasServerData;
        }));

        if (userExists)
        {
            yield return StartCoroutine(FetchTodayScreenTime());
        }

        if (!userExists)
        {
            UnityEngine.Debug.Log($"[SyncManager] New user {_currentUserId} - pushing initial data");
            yield return StartCoroutine(PushAllUserData());
            _userDataPulled = true;
            OnUserDataSynced?.Invoke(true);
        }
        else if (hasData)
        {
            UnityEngine.Debug.Log($"[SyncManager] Existing user {_currentUserId} - pulling from server");
            yield return StartCoroutine(PullUserData());
            _userDataPulled = true;
            OnUserDataSynced?.Invoke(true);
        }
        else
        {
            UnityEngine.Debug.Log($"[SyncManager] User exists but has no data - pushing local data");
            yield return StartCoroutine(PushAllUserData());
            _userDataPulled = true;
            OnUserDataSynced?.Invoke(true);
        }

        UnityEngine.Debug.Log($"[SyncManager] User data sync complete for {_currentUserId}");
    }

    public IEnumerator ProbeUser(int userId, Action<bool, bool> callback)
    {
        yield return StartCoroutine(ProbeUserCoroutine(userId, callback));
    }

    private IEnumerator ProbeUserCoroutine(int userId, Action<bool, bool> callback)
    {
        string url = $"{SyncHubURL}?probe_user=1&user_id={userId}";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "success")
                    {
                        bool exists = json["user_exists"]?.Value<bool>() ?? false;
                        bool hasData = json["has_data"]?.Value<bool>() ?? false;
                        callback?.Invoke(exists, hasData);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Error parsing probe response: {ex.Message}");
                    callback?.Invoke(false, false);
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"[SyncManager] Network error probing user: {www.error}");
                callback?.Invoke(false, false);
            }
        }
    }

    public IEnumerator PullAllUserData(Action<bool> callback = null)
    {
        if (!_isLoggedIn)
        {
            callback?.Invoke(false);
            yield break;
        }
        string url = $"{SyncHubURL}?user_data=1&user_id={_currentUserId}";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "success" && json["user_data"] is JObject userData)
                    {
                        _dbManager.SyncUserDataDynamic(userData);
                        UnityEngine.Debug.Log($"[SyncManager] User data pulled successfully");
                        _lastSuccessfulSync = DateTime.Now;
                        _userDataPulled = true;
                        OnUserDataSynced?.Invoke(true);
                        callback?.Invoke(true);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Error parsing user  {ex.Message}");
                    OnUserDataSynced?.Invoke(false);
                    callback?.Invoke(false);
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"[SyncManager] Failed to pull user  {www.error}");
                OnUserDataSynced?.Invoke(false);
                callback?.Invoke(false);
            }
        }
    }

    private IEnumerator PullUserData()
    {
        yield return StartCoroutine(PullAllUserData());
    }

    public void OnUserLogout()
    {
        if (!_isLoggedIn) return;
        UnityEngine.Debug.Log("[SyncManager] User logging out");

        // Sync final screen time (without is_final)
        if (ScreenTimeManager.Instance != null)
        {
            float finalMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, finalMinutes, false)); // ← isFinal = false
        }

        if (_pushQueue.Count > 0 || _pendingTables.Count > 0)
        {
            StartCoroutine(PushPendingChangesOnLogout());
        }
        else
        {
            CleanupAfterLogout();
        }
    }

    private void CleanupAfterLogout()
    {
        _currentUserId = 0;
        _isLoggedIn = false;
        _userDataPulled = false;
        _pendingTables.Clear();
        _pushQueue.Clear();
        _lastSyncedScreenTimeMinutes = 0f;
        if (_pushProcessor != null)
        {
            StopCoroutine(_pushProcessor);
            _pushProcessor = null;
        }
        UnityEngine.Debug.Log("[SyncManager] User logged out");
    }

    private IEnumerator PushPendingChangesOnLogout()
    {
        UnityEngine.Debug.Log("[SyncManager] Pushing pending changes before logout...");
        foreach (var table in _pendingTables)
        {
            if (!_pushQueue.Contains(table))
            {
                _pushQueue.Enqueue(table);
            }
        }

        while (_pushQueue.Count > 0 && _isLoggedIn)
        {
            string tableName = _pushQueue.Dequeue();
            yield return StartCoroutine(PushTableToServer(tableName));
            if (_pushQueue.Count > 0)
                yield return new WaitForSeconds(0.1f);
        }

        CleanupAfterLogout();
        UnityEngine.Debug.Log("[SyncManager] Logout sync complete");
    }
    #endregion

    #region Screen Time Sync - FIXED: Removed is_final
    private void CheckScreenTimeSync()
    {
        if (!_isLoggedIn || _isSyncing || ScreenTimeManager.Instance == null)
            return;

        if (Time.time - _lastScreenTimeSync >= _screenTimeSyncInterval)
        {
            float currentMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, currentMinutes, false));
        }
        else if (Mathf.Abs(ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes() - _lastSyncedScreenTimeMinutes) >= _screenTimeChangeThreshold)
        {
            float currentMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, currentMinutes, false));
        }
    }

    public IEnumerator SyncScreenTimeData(int userId, DateTime date, float minutes)
    {
        yield return StartCoroutine(SyncScreenTimeDataCoroutine(date, minutes, false));
    }

    public IEnumerator SyncScreenTimeData(int userId, DateTime date, float minutes, bool isFinal)
    {
        // ⚠️ isFinal is IGNORED — not sent to server
        yield return StartCoroutine(SyncScreenTimeDataCoroutine(date, minutes, false));
    }

    private IEnumerator SyncScreenTimeDataCoroutine(DateTime date, float minutes, bool isFinal = false)
    {
        if (!_isLoggedIn || _isSyncing)
            yield break;

        _isSyncing = true;
        try
        {
            string dateKey = date.ToString("yyyy-MM-dd");
            UnityEngine.Debug.Log($"[SyncManager] Syncing screen time for {dateKey}: {minutes:F0}m");

            // ✅ FIXED: REMOVED is_final from formData
            var formData = new Dictionary<string, string>
            {
                { "user_id", _currentUserId.ToString() },
                { "action", "update_screen_time" },
                { "date", dateKey },
                { "screen_on_time", minutes.ToString("F0") }
                // ❌ DO NOT include "is_final"
            };

            using (UnityWebRequest www = UnityWebRequest.Post(SyncHubURL, formData))
            {
                www.timeout = 10;
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var json = JObject.Parse(www.downloadHandler.text);
                        if (json["status"]?.ToString() == "success")
                        {
                            _lastSyncedScreenTimeMinutes = minutes;
                            _lastScreenTimeSync = Time.time;
                            _lastSuccessfulSync = DateTime.Now;
                            _cachedScreenTimeData[date.Date] = minutes;
                            SaveCachedScreenTimeData();
                            UnityEngine.Debug.Log($"[SyncManager] Screen time synced: {minutes:F0}m for {dateKey}");
                            OnScreenTimeSynced?.Invoke(minutes, date);
                        }
                        else
                        {
                            string error = json["message"]?.ToString() ?? "Unknown error";
                            UnityEngine.Debug.LogWarning($"[SyncManager] Screen time sync failed: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"[SyncManager] Error parsing screen time response: {ex.Message}");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Network error syncing screen time: {www.error}");
                }
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private IEnumerator FetchTodayScreenTime()
    {
        if (!_isLoggedIn) yield break;

        DateTime today = DateTime.Today;
        string dateKey = today.ToString("yyyy-MM-dd");

        if (_cachedScreenTimeData.ContainsKey(today.Date))
        {
            float cachedMinutes = _cachedScreenTimeData[today.Date];
            if (cachedMinutes > 0)
            {
                _lastSyncedScreenTimeMinutes = cachedMinutes;
                OnScreenTimeReceived?.Invoke(cachedMinutes, today);
                UnityEngine.Debug.Log($"[SyncManager] Using cached screen time: {cachedMinutes:F0}m");
                yield break;
            }
        }

        string url = $"{SyncHubURL}?action=get_screen_time&user_id={_currentUserId}&date={dateKey}";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.timeout = 10;
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "success")
                    {
                        float remoteMinutes = json["minutes"]?.Value<float>() ?? 0f;
                        if (remoteMinutes > 0)
                        {
                            _lastSyncedScreenTimeMinutes = remoteMinutes;
                            _cachedScreenTimeData[today.Date] = remoteMinutes;
                            SaveCachedScreenTimeData();
                            OnScreenTimeReceived?.Invoke(remoteMinutes, today);
                            UnityEngine.Debug.Log($"[SyncManager] Fetched remote screen time: {remoteMinutes:F0}m");
                        }
                        else
                        {
                            UnityEngine.Debug.Log($"[SyncManager] No remote screen time data for today");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[SyncManager] Error parsing screen time response: {ex.Message}");
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[SyncManager] Network error fetching screen time: {www.error}");
            }
        }
    }

    public async System.Threading.Tasks.Task<float> GetRemoteScreenTime(int userId, DateTime date)
    {
        return await GetRemoteScreenTimeInternal(userId, date);
    }

    public async System.Threading.Tasks.Task<float> GetRemoteScreenTime(int userId, DateTime date, bool forceRefresh)
    {
        if (!forceRefresh && _cachedScreenTimeData.ContainsKey(date.Date))
        {
            return _cachedScreenTimeData[date.Date];
        }
        return await GetRemoteScreenTimeInternal(userId, date);
    }

    private async System.Threading.Tasks.Task<float> GetRemoteScreenTimeInternal(int userId, DateTime date)
    {
        if (!_isLoggedIn || userId != _currentUserId)
            return 0f;

        string dateKey = date.ToString("yyyy-MM-dd");
        string url = $"{SyncHubURL}?action=get_screen_time&user_id={userId}&date={dateKey}";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.timeout = 10;
            var operation = www.SendWebRequest();
            while (!operation.isDone)
                await System.Threading.Tasks.Task.Yield();

            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "success")
                    {
                        float minutes = json["minutes"]?.Value<float>() ?? 0f;
                        _cachedScreenTimeData[date.Date] = minutes;
                        SaveCachedScreenTimeData();
                        return minutes;
                    }
                }
                catch
                {
                    return 0f;
                }
            }
        }
        return 0f;
    }

    public float GetCachedScreenTime(int userId, DateTime date)
    {
        if (!_isLoggedIn || userId != _currentUserId)
            return 0f;
        if (_cachedScreenTimeData.ContainsKey(date.Date))
        {
            return _cachedScreenTimeData[date.Date];
        }
        return 0f;
    }
    #endregion

    #region ScreenTimeManager Integration Methods
    public void OnAppResume()
    {
        if (!_isLoggedIn || !_isInitialized) return;
        UnityEngine.Debug.Log("[SyncManager] App resumed - checking screen time sync");

        if (ScreenTimeManager.Instance != null)
        {
            float currentMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            if (Mathf.Abs(currentMinutes - _lastSyncedScreenTimeMinutes) >= _screenTimeChangeThreshold)
            {
                StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, currentMinutes, false));
            }
        }

        if (_pendingTables.Count > 0)
        {
            UnityEngine.Debug.Log($"[SyncManager] App resumed - {_pendingTables.Count} pending tables to sync");
            foreach (var table in _pendingTables.ToList())
            {
                if (!_pushQueue.Contains(table))
                {
                    _pushQueue.Enqueue(table);
                }
            }
            StartPushProcessor();
        }
    }

    public void NotifyScreenTimeChanged(float minutes)
    {
        if (!_isLoggedIn || !_isInitialized) return;
        if (Mathf.Abs(minutes - _lastSyncedScreenTimeMinutes) >= _screenTimeChangeThreshold)
        {
            StartCoroutine(ScheduleScreenTimeSync(minutes));
        }
    }

    private IEnumerator ScheduleScreenTimeSync(float minutes)
    {
        yield return new WaitForSeconds(_pushDebounceTime);
        if (_isLoggedIn && ScreenTimeManager.Instance != null)
        {
            float currentMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            if (Mathf.Abs(currentMinutes - _lastSyncedScreenTimeMinutes) >= _screenTimeChangeThreshold)
            {
                StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, currentMinutes, false));
            }
        }
    }

    public void RequestSync()
    {
        if (!_isLoggedIn || !_isInitialized) return;
        UnityEngine.Debug.Log("[SyncManager] Manual sync requested");

        if (ScreenTimeManager.Instance != null)
        {
            float currentMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, currentMinutes, false));
        }

        if (_pendingTables.Count > 0)
        {
            foreach (var table in _pendingTables.ToList())
            {
                if (!_pushQueue.Contains(table))
                {
                    _pushQueue.Enqueue(table);
                }
            }
            StartPushProcessor();
        }
    }

    public void NotifyBreakTaken(int breakDuration)
    {
        if (!_isLoggedIn || !_isInitialized) return;
        UnityEngine.Debug.Log($"[SyncManager] Break taken: {breakDuration} seconds");

        if (ScreenTimeManager.Instance != null)
        {
            float currentMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            if (Mathf.Abs(currentMinutes - _lastSyncedScreenTimeMinutes) >= 1f)
            {
                StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, currentMinutes, false));
            }
        }
    }
    #endregion

    #region Simple Push System
    public void OnTableChanged(string tableName, string changeType = "update")
    {
        if (!_isInitialized || !_isLoggedIn || string.IsNullOrEmpty(tableName))
            return;

        if (!_userDataPulled)
        {
            if (DevMode) UnityEngine.Debug.LogWarning($"[SyncManager] User data not pulled yet, skipping push for {tableName}");
            return;
        }

        if (IsGlobalTable(tableName))
        {
            if (DevMode) UnityEngine.Debug.Log($"[SyncManager] Skipping push for global table: {tableName}");
            return;
        }

        if (_lastChangeTime.ContainsKey(tableName))
        {
            float secondsSinceLastChange = (float)(DateTime.Now - _lastChangeTime[tableName]).TotalSeconds;
            if (secondsSinceLastChange < _pushDebounceTime)
            {
                if (DevMode) UnityEngine.Debug.Log($"[SyncManager] Debouncing {tableName} change");
                _pendingTables.Add(tableName);
                return;
            }
        }

        _lastChangeTime[tableName] = DateTime.Now;
        _pendingTables.Add(tableName);

        if (_lastPushTime.ContainsKey(tableName))
        {
            float secondsSinceLastPush = (float)(DateTime.Now - _lastPushTime[tableName]).TotalSeconds;
            if (secondsSinceLastPush < _pushCooldown)
            {
                if (DevMode) UnityEngine.Debug.Log($"[SyncManager] Cooldown active for {tableName}");
                return;
            }
        }

        if (!_pushQueue.Contains(tableName))
        {
            _pushQueue.Enqueue(tableName);
        }

        StartPushProcessor();
        if (DevMode) UnityEngine.Debug.Log($"[SyncManager] Table changed: {tableName} ({changeType}) - Added to queue");
    }

    private bool IsGlobalTable(string tableName)
    {
        string[] globalTables = {
            "achievements",
            "wardrobe_items", 
            "eye_tips",
            "eye_trivias",
            "eye_issues",
            "feedbacks"
        };
        return globalTables.Contains(tableName);
    }

    private void StartPushProcessor()
    {
        if (_pushProcessor == null && _pushQueue.Count > 0)
        {
            _pushProcessor = StartCoroutine(ProcessPushQueue());
        }
    }

    private IEnumerator ProcessPushQueue()
    {
        while (_pushQueue.Count > 0 && _isLoggedIn)
        {
            string tableName = _pushQueue.Dequeue();
            yield return StartCoroutine(PushTableToServer(tableName));
            _lastPushTime[tableName] = DateTime.Now;
            _pendingTables.Remove(tableName);
            if (_pushQueue.Count > 0)
                yield return new WaitForSeconds(0.1f);
        }
        _pushProcessor = null;

        if (_pendingTables.Count > 0)
        {
            foreach (var table in _pendingTables.ToList())
            {
                if (!_pushQueue.Contains(table))
                {
                    _pushQueue.Enqueue(table);
                }
            }
            if (_pushQueue.Count > 0)
            {
                StartPushProcessor();
            }
        }
    }

    private IEnumerator PushTableToServer(string tableName)
    {
        if (_isSyncing) yield break;
        _isSyncing = true;

        string tableData = GetTableData(tableName);
        if (string.IsNullOrEmpty(tableData) || tableData == "[]")
        {
            if (DevMode) UnityEngine.Debug.Log($"[SyncManager] No data to push for {tableName}");
            _isSyncing = false;
            yield break;
        }

        JObject payload;
        try
        {
            payload = new JObject
            {
                [tableName] = JArray.Parse(tableData)
            };
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[SyncManager] Error creating payload for {tableName}: {ex.Message}");
            _isSyncing = false;
            yield break;
        }

        var formData = new Dictionary<string, string>
        {
            { "user_id", _currentUserId.ToString() },
            { "payload", payload.ToString() }
        };

        using (UnityWebRequest www = UnityWebRequest.Post(SyncHubURL, formData))
        {
            www.timeout = 10;
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "success")
                    {
                        int rowsAffected = json["rows_affected"]?.Value<int>() ?? 0;
                        if (DevMode) UnityEngine.Debug.Log($"[SyncManager] Successfully pushed {tableName}: {rowsAffected} rows");
                        _lastSuccessfulSync = DateTime.Now;
                    }
                    else
                    {
                        string error = json["message"]?.ToString() ?? "Unknown error";
                        UnityEngine.Debug.LogError($"[SyncManager] Failed to push {tableName}: {error}");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Error parsing response for {tableName}: {ex.Message}");
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"[SyncManager] Network error pushing {tableName}: {www.error}");
            }
        }
        _isSyncing = false;
    }

    private IEnumerator PushBatchToServer(List<string> tableNames)
    {
        if (tableNames.Count == 0 || _isSyncing)
            yield break;

        _isSyncing = true;
        var combinedData = new JObject();
        foreach (var tableName in tableNames.Take(_batchSize))
        {
            string data = GetTableData(tableName);
            if (!string.IsNullOrEmpty(data) && data != "[]")
            {
                try
                {
                    combinedData[tableName] = JArray.Parse(data);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Error parsing {tableName} for batch: {ex.Message}");
                }
            }
        }

        if (combinedData.Count == 0)
        {
            _isSyncing = false;
            yield break;
        }

        var formData = new Dictionary<string, string>
        {
            { "user_id", _currentUserId.ToString() },
            { "payload", combinedData.ToString() },
            { "batch_push", "1" }
        };

        using (UnityWebRequest www = UnityWebRequest.Post(SyncHubURL, formData))
        {
            www.timeout = 15;
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(www.downloadHandler.text);
                    if (json["status"]?.ToString() == "success")
                    {
                        int totalRows = json["total_rows_affected"]?.Value<int>() ?? 0;
                        UnityEngine.Debug.Log($"[SyncManager] Batch push successful: {totalRows} rows");
                        _lastSuccessfulSync = DateTime.Now;
                        foreach (var tableName in combinedData.Properties().Select(p => p.Name))
                        {
                            _lastPushTime[tableName] = DateTime.Now;
                            _pendingTables.Remove(tableName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SyncManager] Error parsing batch response: {ex.Message}");
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"[SyncManager] Network error in batch push: {www.error}");
            }
        }
        _isSyncing = false;
    }

    private IEnumerator PushAllUserData()
    {
        if (!_isLoggedIn) yield break;

        var userTables = new List<string> 
        { 
            "users",
            "user_achievements",
            "user_wardrobe",
            "screentime_history",
            "eyehealth_log",
            "user_settings"
        };

        UnityEngine.Debug.Log($"[SyncManager] Pushing all user data ({userTables.Count} tables)...");
        for (int i = 0; i < userTables.Count; i += _batchSize)
        {
            var batch = userTables.Skip(i).Take(_batchSize).ToList();
            yield return StartCoroutine(PushBatchToServer(batch));
            if (i + _batchSize < userTables.Count)
                yield return new WaitForSeconds(0.5f);
        }

        UnityEngine.Debug.Log("[SyncManager] All user data pushed");
        _userDataPulled = true;
        OnUserDataSynced?.Invoke(true);
    }
    #endregion

    #region Helper Methods
    private string GetTableData(string tableName)
    {
        try
        {
            switch (tableName)
            {
                case "users":
                    var user = _dbManager?.GetUserById(_currentUserId);
                    return user != null ? JsonConvert.SerializeObject(new[] { user }) : "[]";
                case "user_achievements":
                    var achievements = _dbManager?.GetUserAchievements(_currentUserId);
                    return achievements != null ? JsonConvert.SerializeObject(achievements) : "[]";
                case "user_wardrobe":
                    var wardrobe = _dbManager?.GetUserWardrobe(_currentUserId);
                    return wardrobe != null ? JsonConvert.SerializeObject(wardrobe) : "[]";
                case "screentime_history":
                    var screentime = _dbManager?.GetScreenTimeHistory(_currentUserId);
                    return screentime != null ? JsonConvert.SerializeObject(screentime) : "[]";
                case "eyehealth_log":
                    var eyeHealth = _dbManager?.GetEyeHealthLogs(_currentUserId);
                    return eyeHealth != null ? JsonConvert.SerializeObject(eyeHealth) : "[]";
                case "user_settings":
                    var settings = _dbManager?.GetUserSettings(_currentUserId);
                    return settings != null ? JsonConvert.SerializeObject(settings) : "[]";
                default:
                    return "[]";
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[SyncManager] Error getting data for {tableName}: {ex.Message}");
            return "[]";
        }
    }
    #endregion

    #region Public API
    public IEnumerator ForceSyncCoroutine(Action<bool, string> callback)
    {
        if (!_isLoggedIn)
        {
            callback?.Invoke(false, "User not logged in");
            yield break;
        }

        UnityEngine.Debug.Log("[SyncManager] Force sync requested");

        if (ScreenTimeManager.Instance != null)
        {
            float currentMinutes = ScreenTimeManager.Instance.GetTodayScreenTimeInMinutes();
            yield return StartCoroutine(SyncScreenTimeDataCoroutine(DateTime.Today, currentMinutes, false));
        }

        yield return StartCoroutine(PushAllUserData());
        callback?.Invoke(true, "Force sync completed");
    }

    public IEnumerator PullGlobalDataCoroutine(Action<bool, string> callback)
    {
        yield return StartCoroutine(PullGlobalData());
        callback?.Invoke(_globalDataLoaded, "Global data sync completed");
    }

    public void ForceSync()
    {
        if (!_isLoggedIn) return;
        StartCoroutine(ForceSyncCoroutine((success, message) =>
        {
            UnityEngine.Debug.Log($"[SyncManager] Force sync completed: {success} - {message}");
        }));
    }

    public bool HasValidUserForSync()
    {
        return _isLoggedIn && _currentUserId > 0 && _isInitialized && _userDataPulled;
    }

    public void CheckGlobalUpdatesManually()
    {
        if (!_isInitialized) return;
        StartCoroutine(CheckForGlobalDataUpdates());
    }
    #endregion

    #region Event Handlers
    public void OnAchievementClaimed(int achievementId)
    {
        OnTableChanged("user_achievements", $"achievement_claimed:{achievementId}");
        OnTableChanged("users", "points_updated");
    }

    public void OnItemPurchased(int itemId)
    {
        OnTableChanged("user_wardrobe", $"item_purchased:{itemId}");
    }

    public void OnScreenTimeUpdated()
    {
        OnTableChanged("screentime_history", "screen_time_updated");
    }

    public void OnSurveyCompleted()
    {
        OnTableChanged("eyehealth_log", "survey_completed");
    }

    public void OnSettingsChanged()
    {
        OnTableChanged("user_settings", "settings_updated");
    }
    #endregion

    #region Status
    public string GetStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Sync Manager Status ===");
        sb.AppendLine($"Initialized: {_isInitialized}");
        sb.AppendLine($"Logged In: {_isLoggedIn} (User: {_currentUserId})");
        sb.AppendLine($"Global Data Loaded: {_globalDataLoaded} (Version: {_cachedGlobalVersion})");
        sb.AppendLine($"User Data Pulled: {_userDataPulled}");
        sb.AppendLine($"Currently Syncing: {_isSyncing}");
        sb.AppendLine($"Push Queue: {_pushQueue.Count} operations");
        sb.AppendLine($"Pending Tables: {_pendingTables.Count}");
        sb.AppendLine($"Last Successful Sync: {_lastSuccessfulSync:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Last Global Check: {_lastGlobalDataCheck:yyyy-MM-dd HH:mm:ss}");

        sb.AppendLine("\n=== Screen Time Sync ===");
        sb.AppendLine($"Last Synced Minutes: {_lastSyncedScreenTimeMinutes:F0}m");
        sb.AppendLine($"Time Since Last Sync: {Time.time - _lastScreenTimeSync:F0}s");
        sb.AppendLine($"Cached Entries: {_cachedScreenTimeData.Count}");

        if (_cachedScreenTimeData.Count > 0)
        {
            sb.AppendLine("\nRecent Screen Time Cache:");
            int count = 0;
            foreach (var kvp in _cachedScreenTimeData.OrderByDescending(x => x.Key).Take(3))
            {
                sb.AppendLine($"  {kvp.Key:yyyy-MM-dd}: {kvp.Value:F0}m");
                count++;
                if (count >= 3) break;
            }
        }

        if (_globalDataLoaded)
        {
            var nextCheck = _lastGlobalDataCheck.AddHours(24);
            var timeUntilNext = nextCheck - DateTime.Now;
            if (timeUntilNext.TotalSeconds > 0)
            {
                sb.AppendLine($"\nNext Global Check: {nextCheck:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Time Until Next: {timeUntilNext.Hours}h {timeUntilNext.Minutes}m");
            }
            else
            {
                sb.AppendLine("\nNext Global Check: DUE NOW");
            }
        }

        if (_pushQueue.Count > 0)
        {
            sb.AppendLine("\nPush Queue:");
            foreach (var table in _pushQueue)
            {
                sb.AppendLine($"  - {table}");
            }
        }

        if (_pendingTables.Count > 0)
        {
            sb.AppendLine("\nPending Tables:");
            foreach (var table in _pendingTables)
            {
                sb.AppendLine($"  - {table}");
            }
        }

        return sb.ToString();
    }
    #endregion
}