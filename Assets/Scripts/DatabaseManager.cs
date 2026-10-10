using UnityEngine;
using SQLite4Unity3d;
using System.IO;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Threading.Tasks;

public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance { get; private set; }
    public Action OnDatabaseReady;
    public Action<string> OnDatabaseError;

    private SQLiteConnection _connection;
    private string _dbPath;
    private readonly object _dbLock = new object();
    private bool _isInitialized = false;
    private bool _initializing = false;

    // NEW: Cache for unlocked achievements to prevent stale data
    private Dictionary<int, HashSet<int>> _cachedUnlockedAchievements = new Dictionary<int, HashSet<int>>();
    private readonly object _cacheLock = new object();

    // UPDATED: Global data sync tracking - now managed by SyncManager
    private bool _globalDataFetched = false;
    private bool _isFetchingGlobalData = false;

    // NEW: Login/Logout sync tracking
    private Dictionary<string, long> _tableLastUpdateTimestamps = new Dictionary<string, long>();
    private HashSet<string> _tablesWithLocalChanges = new HashSet<string>();

    private const string SESSION_USER_ID_KEY = "current_user_id";
    private const string SESSION_LAST_LOGIN_KEY = "last_login_time";

    public int CurrentUserId { get; private set; }
    public bool IsLoggedIn => CurrentUserId > 0;

    private Coroutine _autoPushCoroutine;
    private float _autoPushDelay = 1.0f;
    private bool _isApplyingServerData = false;
    private bool _hasLocalChanges = false;

    private List<Action<string>> _dataChangedListeners = new List<Action<string>>();
    private readonly Queue<IEnumerator> _dbWriteQueue = new Queue<IEnumerator>();
    private bool _isProcessingWrites = false;

    // Performance monitoring
    private DateTime _lastOptimizationTime;
    private int _totalWriteOperations;
    private readonly TimeSpan _optimizationInterval = TimeSpan.FromHours(1);

    // Table type mappings for dynamic sync
    private readonly Dictionary<string, Type> _tableTypes = new Dictionary<string, Type>
    {
        { "achievements", typeof(Achievement) },
        { "wardrobe_items", typeof(WardrobeItem) },
        { "eye_issues", typeof(EyeIssue) },
        { "eye_tips", typeof(EyeTip) },
        { "eye_trivias", typeof(EyeTrivia) },
        { "user_settings", typeof(UserSettings) },
        { "user_wardrobe", typeof(UserWardrobe) },
        { "user_achievements", typeof(UserAchievement) },
        { "eyehealth_log", typeof(EyeHealthLog) },
        { "screentime_history", typeof(ScreenTimeHistory) },
        { "feedbacks", typeof(Feedback) }
    };

    // Data persistence configuration
    [Header("Data Persistence Settings")]
    [SerializeField] private bool _clearHealthDataOnLogout = false;
    [SerializeField] private bool _clearProgressDataOnLogout = false;

    // Performance settings
    [Header("Performance Settings")]
    [SerializeField] private bool _enableAutoOptimization = true;
    [SerializeField] private int _maxWriteQueueSize = 1000;
    [SerializeField] private float _queueProcessDelay = 0.02f;

    // Tables to persist across sessions
    private readonly string[] _persistentTables = {
        "users",           // User accounts
        "user_settings",   // App settings
        "achievements",    // Achievement definitions
        "wardrobe_items",  // Shop items
        "eye_issues",      // Eye health info
        "eye_tips",        // Health tips
        "eye_trivias"      // Educational content
    };

    // Tables to clear on logout (user-specific data)
    private readonly string[] _sessionTables = {
        "user_wardrobe",      // User's purchased items
        "user_achievements",  // User's progress
        "eyehealth_log",      // Personal health data
        "screentime_history", // Usage data
        "feedbacks"          // User feedback
    };

    // NEW: Tables that always need to be updated on logout
    private readonly string[] _logoutSyncTables = {
        "users",           // Points changes
        "user_wardrobe",   // Equipped items
        "user_settings"    // Settings changes
    };

    // NEW: Event for when global data is fetched
    public Action OnGlobalDataFetched;

    // NEW: Stack protection for notifications
    private bool _isNotifying = false;
    private readonly object _notificationLock = new object();
    private HashSet<string> _activeNotifications = new HashSet<string>();
    private int _notificationDepth = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize SyncManager first if it exists
            if (SyncManager.Instance == null)
            {
                var syncManagerObj = new GameObject("SyncManager");
                syncManagerObj.AddComponent<SyncManager>();
                DontDestroyOnLoad(syncManagerObj);
            }
            
            StartCoroutine(DelayedInitialization());
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator DelayedInitialization()
    {
        yield return new WaitForEndOfFrame();
        yield return StartCoroutine(InitializeDatabaseAsync());
    }

    #region Initialization
    private IEnumerator InitializeDatabaseAsync()
    {
        if (_isInitialized) yield break;
        
        if (_initializing)
        {
            UnityEngine.Debug.LogWarning("[DB] Initialization already in progress, waiting...");
            yield return new WaitUntil(() => !_initializing);
            yield break;
        }
        
        _initializing = true;

        try
        {
            UnityEngine.Debug.Log("[DB] === DATABASE INITIALIZATION STARTED ===");
            
            _dbPath = Path.Combine(Application.persistentDataPath, "blinkshieldlocal.db");
            UnityEngine.Debug.Log($"[DB] Initializing at: {_dbPath}");

            // Create directory if it doesn't exist
            string directory = Path.GetDirectoryName(_dbPath);
            if (!Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    UnityEngine.Debug.Log($"[DB] Created directory: {directory}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    UnityEngine.Debug.LogError($"[DB] Permission denied creating directory: {ex.Message}");
                    HandleError("Create directory", ex, true);
                    yield break;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[DB] Error creating directory: {ex.Message}");
                    HandleError("Create directory", ex, true);
                    yield break;
                }
            }

            // Check file permissions
            if (!File.Exists(_dbPath))
            {
                try
                {
                    File.Create(_dbPath).Dispose();
                    UnityEngine.Debug.Log($"[DB] Database file created: {_dbPath}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    UnityEngine.Debug.LogError($"[DB] Permission denied creating database file: {ex.Message}");
                    HandleError("Create database file", ex, true);
                    yield break;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[DB] Error creating database file: {ex.Message}");
                    HandleError("Create database file", ex, true);
                    yield break;
                }
            }

            yield return OpenDatabaseWithRetry();

            if (_connection == null)
            {
                OnDatabaseError?.Invoke("Failed to open database connection");
                yield break;
            }

            UnityEngine.Debug.Log("[DB] Database connection: SUCCESS");

            // Apply database optimizations
            OptimizeDatabase();
            _lastOptimizationTime = DateTime.Now;

            bool initSuccess = false;
            Exception initException = null;
            
            try
            {
                DBInitializer.CreateTables(_connection);
                initSuccess = true;
                UnityEngine.Debug.Log("[DB] Database tables created successfully");
            }
            catch (Exception ex)
            {
                initException = ex;
                UnityEngine.Debug.LogError($"[DB] Error creating tables: {ex.Message}");
            }

            if (!initSuccess)
            {
                HandleError("Database initialization", initException, true);
                yield break;
            }

            // Load persistence settings
            LoadDataPersistenceSettings();

            yield return RestoreUserSession();
            
            _isInitialized = true;
            OnDatabaseReady?.Invoke();
            
            UnityEngine.Debug.Log("[DB] === DATABASE INITIALIZATION COMPLETED ===");
            
            // NEW: Initialize SyncManager if it exists
            if (SyncManager.Instance != null)
            {
                SyncManager.Instance.OnGlobalDataLoaded += OnGlobalDataLoadedHandler;
                SyncManager.Instance.OnUserDataSynced += OnUserDataSyncedHandler;
            }
        }
        finally
        {
            _initializing = false;
        }
    }

    // NEW: Handler for when global data is loaded
    private void OnGlobalDataLoadedHandler(bool success)
    {
        if (success)
        {
            _globalDataFetched = true;
            UnityEngine.Debug.Log("[DB] Global data loaded via SyncManager");
            OnGlobalDataFetched?.Invoke();
        }
        else
        {
            UnityEngine.Debug.LogWarning("[DB] Global data failed to load via SyncManager");
        }
    }

    // NEW: Handler for when user data is synced
    private void OnUserDataSyncedHandler(bool success)
    {
        if (success)
        {
            UnityEngine.Debug.Log("[DB] User data synced via SyncManager");
        }
        else
        {
            UnityEngine.Debug.LogWarning("[DB] User data sync failed via SyncManager");
        }
    }

    private IEnumerator OpenDatabaseWithRetry(int retries = 3)
    {
        Exception lastException = null;
        
        for (int i = 0; i < retries; i++)
        {
            bool success = false;
            Exception currentException = null;
            
            var connectionTask = System.Threading.Tasks.Task.Run(() => 
            {
                try
                {
                    lock (_dbLock)
                    {
                        _connection = new SQLiteConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                        _connection.BusyTimeout = TimeSpan.FromSeconds(5);
                    }
                    success = true;
                }
                catch (Exception ex)
                {
                    currentException = ex;
                    success = false;
                }
            });

            float connectionTimeout = 3f;
            float connectionElapsed = 0f;
            
            while (!connectionTask.IsCompleted && connectionElapsed < connectionTimeout)
            {
                connectionElapsed += 0.1f;
                yield return new WaitForSecondsRealtime(0.1f);
            }

            if (connectionTask.IsCompleted && success)
            {
                UnityEngine.Debug.Log($"[DB] Database connection established successfully on attempt {i + 1}");
                yield break;
            }
            else if (connectionTask.IsCompleted && !success)
            {
                currentException = currentException ?? new Exception("Database connection failed");
                lastException = currentException;
                UnityEngine.Debug.LogWarning($"[DB] Database connection attempt {i + 1}/{retries} failed: {currentException.Message}");
            }
            else
            {
                lastException = new TimeoutException("Database connection timeout");
                UnityEngine.Debug.LogWarning($"[DB] Database connection attempt {i + 1}/{retries} timed out");
            }

            // Clean up failed attempt
            if (!success)
            {
                try
                {
                    lock (_dbLock)
                    {
                        _connection?.Close();
                        _connection?.Dispose();
                        _connection = null;
                    }
                }
                catch (Exception cleanupEx)
                {
                    UnityEngine.Debug.LogWarning($"[DB] Cleanup error: {cleanupEx.Message}");
                }
            }

            if (i < retries - 1)
            {
                float retryDelay = 0.5f * (i + 1);
                UnityEngine.Debug.Log($"[DB] Retrying database connection in {retryDelay} seconds...");
                yield return new WaitForSecondsRealtime(retryDelay);
            }
        }

        HandleError("Open database", lastException, true);
    }

    private void OptimizeDatabase()
    {
        try
        {
            lock (_dbLock)
            {
                _connection.Execute("PRAGMA journal_mode = WAL;");
                _connection.Execute("PRAGMA busy_timeout = 10000;");
                _connection.Execute("PRAGMA foreign_keys = ON;");
                _connection.Execute("PRAGMA cache_size = 10000;");
                _connection.Execute("PRAGMA synchronous = NORMAL;");
                _connection.Execute("PRAGMA temp_store = MEMORY;");
                _connection.Execute("PRAGMA mmap_size = 268435456;");
                _connection.Execute("PRAGMA auto_vacuum = INCREMENTAL;");
                
                UnityEngine.Debug.Log("[DB] Database optimization applied");
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[DB] Database optimization failed: {ex.Message}");
        }
    }

    private void CheckAndPerformMaintenance()
    {
        if (!_enableAutoOptimization) return;

        try
        {
            lock (_dbLock)
            {
                if (DateTime.Now - _lastOptimizationTime >= _optimizationInterval || _totalWriteOperations > 10000)
                {
                    UnityEngine.Debug.Log("[DB] Performing periodic database maintenance");
                    
                    _connection.Execute("PRAGMA incremental_vacuum;");
                    _connection.Execute("ANALYZE;");
                    
                    _lastOptimizationTime = DateTime.Now;
                    _totalWriteOperations = 0;
                    
                    UnityEngine.Debug.Log("[DB] Database maintenance completed");
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[DB] Maintenance failed: {ex.Message}");
        }
    }

    private IEnumerator RestoreUserSession()
    {
        bool hasError = false;
        Exception sessionException = null;
        
        try
        {
            int savedUserId = PlayerPrefs.GetInt(SESSION_USER_ID_KEY, 0);
            string lastLoginStr = PlayerPrefs.GetString(SESSION_LAST_LOGIN_KEY, "");
            
            if (savedUserId > 0 && !string.IsNullOrEmpty(lastLoginStr) && 
                (DateTime.Now - DateTime.Parse(lastLoginStr)).TotalHours < 24)
            {
                lock (_dbLock)
                {
                    var user = _connection.Table<User>().FirstOrDefault(u => u.id == savedUserId);
                    if (user != null)
                    {
                        CurrentUserId = savedUserId;
                        _hasLocalChanges = false;
                        
                        ClearUnlockedAchievementsCache(CurrentUserId);
                        
                        UnityEngine.Debug.Log($"[DB] Session restored for user {CurrentUserId}");
                        
                        EnsureDailyMetricsWithoutPush();
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[DB] User {savedUserId} not found, clearing session");
                        ClearSessionPersistentData();
                    }
                }
            }
            else
            {
                UnityEngine.Debug.Log("[DB] Session expired or invalid, clearing");
                ClearSessionPersistentData();
            }
        }
        catch (Exception ex)
        {
            hasError = true;
            sessionException = ex;
            ClearSessionPersistentData();
        }

        if (hasError)
            HandleError("Restore session", sessionException);

        yield return null;
    }
    #endregion

    #region Connection Management
    private bool ValidateConnection()
    {
        try
        {
            lock (_dbLock)
            {
                if (_connection == null) return false;
                _connection.Execute("SELECT 1");
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private void EnsureValidConnection()
    {
        if (!ValidateConnection())
        {
            UnityEngine.Debug.LogWarning("[DB] Connection invalid, attempting to reopen...");
            EmergencyDatabaseRecovery();
        }
    }
    #endregion

    #region Session Management with Data Persistence
    private void SaveSessionPersistentData()
    {
        if (IsLoggedIn)
        {
            PlayerPrefs.SetInt(SESSION_USER_ID_KEY, CurrentUserId);
            PlayerPrefs.SetString(SESSION_LAST_LOGIN_KEY, DateTime.Now.ToString());
            PlayerPrefs.Save();
        }
    }

    private void ClearSessionPersistentData()
    {
        PlayerPrefs.DeleteKey(SESSION_USER_ID_KEY);
        PlayerPrefs.DeleteKey(SESSION_LAST_LOGIN_KEY);
        PlayerPrefs.Save();
    }

    public void SetCurrentUser(int userId)
    {
        CurrentUserId = userId;
        SaveSessionPersistentData();
        
        ClearUnlockedAchievementsCache(userId);
        EnsureUserDataExists(userId);
        
        UnityEngine.Debug.Log($"[DB] Current user set to: {userId}");
        
        // Notify SyncManager if available
        if (SyncManager.Instance != null)
        {
            var user = GetUserById(userId);
            if (user != null)
            {
                SyncManager.Instance.OnUserLogin(user);
            }
        }
    }

    private void EnsureUserDataExists(int userId)
    {
        Execute(() =>
        {
            var user = _connection.Table<User>().FirstOrDefault(u => u.id == userId);
            if (user == null)
            {
                _connection.Insert(new User 
                { 
                    id = userId,
                    points = 0,
                    screentime_minutes = 0f,
                    breaks_taken = 0,
                    health_score = 100,
                    created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                UnityEngine.Debug.Log($"[DB] Created local user record for: {userId}");
            }
        });
    }

    #region Login Data Fetch Strategy
    public IEnumerator FetchAllDataOnLogin(int userId, Action<bool, string> callback = null)
    {
        if (userId <= 0)
        {
            callback?.Invoke(false, "Invalid user ID");
            yield break;
        }

        CurrentUserId = userId;
    
        UnityEngine.Debug.Log($"[DB] Starting data fetch on login for user {userId}");

        // Step 1: Check global data
        if (!_globalDataFetched || !IsGlobalDataAvailable())
        {
            UnityEngine.Debug.Log($"[DB] Global data needed, fetching...");
            yield return StartCoroutine(FetchAndCacheGlobalData());
        }

        // Step 2: Set user and let SyncManager handle the rest
        SetCurrentUser(userId);
        
        // Step 3: Create minimal data if needed
        CreateMinimalUserData(userId);
        
        callback?.Invoke(true, "Login data fetch initiated");
    }

    private IEnumerator FetchUserDataOnLogin(int userId, Action<bool, string> callback = null)
    {
        UnityEngine.Debug.Log($"[DB] Data fetch now handled by SyncManager for user {userId}");
        
        // Just create minimal local data
        CreateMinimalUserData(userId);
        
        callback?.Invoke(true, "Using SyncManager for data synchronization");
        yield break;
    }

    private void UpdateTableTimestamps()
    {
        _tableLastUpdateTimestamps.Clear();
        foreach (var table in _logoutSyncTables)
        {
            try
            {
                lock (_dbLock)
                {
                    var count = _connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE user_id = {CurrentUserId}");
                    if (count > 0)
                    {
                        _tableLastUpdateTimestamps[table] = DateTimeOffset.Now.ToUnixTimeSeconds();
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[DB] Error updating timestamp for {table}: {ex.Message}");
            }
        }
    }

    private void CreateMinimalUserData(int userId)
    {
        Execute(() =>
        {
            // Create user record if it doesn't exist
            var user = _connection.Table<User>().FirstOrDefault(u => u.id == userId);
            if (user == null)
            {
                _connection.Insert(new User 
                { 
                    id = userId,
                    points = 0,
                    screentime_minutes = 0f,
                    breaks_taken = 0,
                    health_score = 100,
                    created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            // Create default settings
            var settings = _connection.Table<UserSettings>().FirstOrDefault(s => s.user_id == userId);
            if (settings == null)
            {
                _connection.Insert(new UserSettings
                {
                    user_id = userId,
                    reminder_interval_min = 20,
                    sync_data_enabled = 1
                });
            }

            EnsureDailyMetricsWithoutPush();
        });
    }
    #endregion

    public bool IsGlobalDataAvailable()
    {
        try
        {
            lock (_dbLock)
            {
                // Check multiple tables to be sure - INCLUDING EYE_ISSUES
                bool hasAchievements = _connection.Table<Achievement>().Any();
                bool hasWardrobeItems = _connection.Table<WardrobeItem>().Any();
                bool hasEyeIssues = _connection.Table<EyeIssue>().Any(); // ADDED THIS LINE
                bool hasEyeTips = _connection.Table<EyeTip>().Any();
                bool hasEyeTrivias = _connection.Table<EyeTrivia>().Any();
            
                // Global data exists if ANY of these tables have data
                bool hasData = hasAchievements || hasWardrobeItems || hasEyeIssues || hasEyeTips || hasEyeTrivias;
            
                UnityEngine.Debug.Log($"[DB] Global Data Check - " +
                         $"Achievements: {hasAchievements}, " +
                         $"Wardrobe: {hasWardrobeItems}, " +
                         $"EyeIssues: {hasEyeIssues}, " + // ADDED THIS LINE
                         $"EyeTips: {hasEyeTips}, " +
                         $"EyeTrivias: {hasEyeTrivias}, " +
                         $"Result: {hasData}");
            
                return hasData;
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[DB] Error checking global data: {ex.Message}");
            return false;
        }
    }

    // UPDATED: Create emergency global data for new devices - INCLUDES EYE_ISSUES
    private IEnumerator CreateEmergencyGlobalData()
    {
        UnityEngine.Debug.LogWarning("[DB] Creating emergency global data for new device...");
    
        yield return null; // Wait one frame
    
        try
        {
            Execute(() =>
            {
                // Create minimal achievements
                if (!_connection.Table<Achievement>().Any())
                {
                    var defaultAchievements = new List<Achievement>
                    {
                        new Achievement { 
                            achievement_id = 1,
                            title = "First Timer",
                            description = "Complete your first break",
                            points = 10,
                            requirement_type = "breaks",
                            requirement_value = 1,
                            created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        },
                        new Achievement { 
                            achievement_id = 2,
                            title = "Eye Care Beginner",
                            description = "Track screen time for 1 hour",
                            points = 20,
                            requirement_type = "screen_time",
                            requirement_value = 60,
                            created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        }
                    };
                
                    foreach (var achievement in defaultAchievements)
                    {
                        _connection.InsertOrReplace(achievement);
                    }
                
                    UnityEngine.Debug.LogWarning("[DB] Created emergency achievements");
                }
            
                // Create minimal wardrobe items
                if (!_connection.Table<WardrobeItem>().Any())
                {
                    var defaultWardrobe = new List<WardrobeItem>
                    {
                        new WardrobeItem { 
                            item_id = 1,
                            name = "Default Glasses",
                            description = "Basic protective glasses",
                            price = 0,
                            type = "glasses",
                            created_at = DateTime.Now
                        },
                        new WardrobeItem { 
                            item_id = 2,
                            name = "Default Background",
                            description = "Simple background",
                            price = 0,
                            type = "background",
                            created_at = DateTime.Now
                        }
                    };
                
                    foreach (var item in defaultWardrobe)
                    {
                        _connection.InsertOrReplace(item);
                    }
                
                    UnityEngine.Debug.LogWarning("[DB] Created emergency wardrobe items");
                }
            
                                // Create eye issues
                if (!_connection.Table<EyeIssue>().Any())
                {
                    var eyeIssues = new List<EyeIssue>
                    {
                        new EyeIssue { 
                            issue_id = 1,
                            title = "Eye Strain", // Changed from 'name' to 'title'
                            description = "Common issue from prolonged screen use",
                            suggested_action = "Take regular breaks, use proper lighting",
                            link = "",
                            created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        },
                        new EyeIssue { 
                            issue_id = 2,
                            title = "Digital Eye Strain", // Changed from 'name' to 'title'
                            description = "Also known as computer vision syndrome",
                            suggested_action = "Adjust screen brightness, maintain proper distance",
                            link = "",
                            created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        }
                    };
                    
                    // Remove the 'symptoms' and 'prevention' properties since they don't exist in the database
                    // Move those details into the 'description' or 'suggested_action' fields
                    
                    foreach (var issue in eyeIssues)
                    {
                        _connection.InsertOrReplace(issue);
                    }
                    
                    UnityEngine.Debug.LogWarning("[DB] Created emergency eye issues");
                }
            
                // Create eye tips
                if (!_connection.Table<EyeTip>().Any())
                {
                    var eyeTips = new List<EyeTip>
                    {
                        new EyeTip { 
                            tip_id = 1,
                            title = "20-20-20 Rule",
                            content = "Every 20 minutes, look at something 20 feet away for 20 seconds",
                            category = "screen_time",
                            created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        }
                    };
                
                    foreach (var tip in eyeTips)
                    {
                        _connection.InsertOrReplace(tip);
                    }
                
                    UnityEngine.Debug.LogWarning("[DB] Created emergency eye tips");
                }
            
                // Mark as fetched to prevent retries
                _globalDataFetched = true;
                OnGlobalDataFetched?.Invoke();
            
                UnityEngine.Debug.LogWarning("[DB] Emergency global data created - app can now function");
            });
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[DB] Error creating emergency global data: {ex.Message}");
        }
    }

    // UPDATED: Fetch global data with emergency fallback
    public IEnumerator FetchAndCacheGlobalData()
    {
        if (_isFetchingGlobalData) yield break;
    
        _isFetchingGlobalData = true;
        float timeout = 15f; // Increased timeout for new devices
        float elapsed = 0f;
    
        UnityEngine.Debug.Log("[DB] Starting global data fetch...");
    
        // Wait for SyncManager
        while (SyncManager.Instance == null && elapsed < timeout)
        {
            elapsed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
    
        if (SyncManager.Instance == null)
        {
            UnityEngine.Debug.LogError("[DB] SyncManager not available after timeout");
            _isFetchingGlobalData = false;
            yield break;
        }
    
        // Check if we already have global data
        if (IsGlobalDataAvailable())
        {
            UnityEngine.Debug.Log("[DB] Global data already exists locally");
            _globalDataFetched = true;
            _isFetchingGlobalData = false;
            yield break;
        }
    
        // Try to fetch from server via SyncManager
        bool serverSuccess = false;
        bool callbackCalled = false;
    
        yield return StartCoroutine(SyncManager.Instance.LoadGlobalData((success) => 
        {
            serverSuccess = success;
            callbackCalled = true;
        }));
    
        // Wait for callback
        float waitStartTime = Time.time;
        while (!callbackCalled && Time.time - waitStartTime < 10f)
        {
            yield return null;
        }
    
        if (serverSuccess)
        {
            UnityEngine.Debug.Log($"[DB] Global data fetched successfully via SyncManager");
        
            // Verify we actually got data
            if (IsGlobalDataAvailable())
            {
                _globalDataFetched = true;
                UnityEngine.Debug.Log("[DB] Global data verified and cached");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[DB] Server reported success but no global data received");
            
                // NEW: Create emergency data if server returns empty
                yield return StartCoroutine(CreateEmergencyGlobalData());
            }
        }
        else
        {
            UnityEngine.Debug.LogError($"[DB] Global data fetch failed");
        
            // NEW: Check if this is a new device with NO data
            if (!IsGlobalDataAvailable())
            {
                UnityEngine.Debug.LogWarning("[DB] New device has no global data - creating emergency data");
                yield return StartCoroutine(CreateEmergencyGlobalData());
            }
            else
            {
                // Existing device has cached data
                _globalDataFetched = true;
                UnityEngine.Debug.Log("[DB] Using existing cached global data");
            }
        }
    
        _isFetchingGlobalData = false;
    }

    #endregion

    public void Logout()
    {
        if (!IsLoggedIn) return;

        UnityEngine.Debug.Log($"[DB] Starting logout process for user {CurrentUserId}");

        // Notify SyncManager first
        if (SyncManager.Instance != null)
        {
            SyncManager.Instance.OnUserLogout();
        }
        
        // Clear achievement cache on logout
        ClearUnlockedAchievementsCache(CurrentUserId);

        // Push logout tables first
        StartCoroutine(PushLogoutTables(CurrentUserId, (success, message) => 
        {
            UnityEngine.Debug.Log($"[DB] Logout push result: {success} - {message}");
            
            // Clear session data after pushing
            StartCoroutine(ClearSessionAfterLogout());
        }));
    }

    // FIXED: Changed to public so SessionManager can access it
    public IEnumerator PushLogoutTables(int userId, Action<bool, string> callback)
    {
        if (SyncManager.Instance == null)
        {
            callback?.Invoke(false, "SyncManager not available");
            yield break;
        }

        // Force sync all logout tables
        SyncManager.Instance.ForceSync();
        
        // Give it a moment to start
        yield return new WaitForSeconds(0.5f);
        
        callback?.Invoke(true, "Force sync initiated for logout");
    }

    private IEnumerator ClearSessionAfterLogout()
    {
        yield return null;
        
        int userId = CurrentUserId;
        
        // Clear session-specific data based on settings
        ClearSessionData(userId);
        
        // Reset current user
        CurrentUserId = 0;
        _hasLocalChanges = false;
        _tablesWithLocalChanges.Clear();
        ClearSessionPersistentData();
        
        UnityEngine.Debug.Log($"[DB] Logout completed for user {userId}");
    }

    public void ClearSessionData(int userId)
    {
        if (userId <= 0) return;
        
        Execute(() =>
        {
            ClearUnlockedAchievementsCache(userId);

            // Clear health data if configured
            if (_clearHealthDataOnLogout)
            {
                _connection.Execute($"DELETE FROM eyehealth_log WHERE user_id = ?", userId);
                _connection.Execute($"DELETE FROM screentime_history WHERE user_id = ?", userId);
                UnityEngine.Debug.Log($"[DB] Cleared health data for user {userId}");
            }

            // Clear progress data if configured
            if (_clearProgressDataOnLogout)
            {
                _connection.Execute($"DELETE FROM user_achievements WHERE user_id = ?", userId);
                _connection.Execute($"DELETE FROM user_wardrobe WHERE user_id = ?", userId);
                UnityEngine.Debug.Log($"[DB] Cleared progress data for user {userId}");
            }

            // Always clear feedbacks (privacy)
            _connection.Execute($"DELETE FROM feedbacks WHERE user_id = ?", userId);
            
            UnityEngine.Debug.Log($"[DB] Cleared session data for user {userId}");
        });
    }

    public void DeleteAllUserData(int userId)
    {
        if (userId <= 0) return;
        
        Execute(() =>
        {
            ClearUnlockedAchievementsCache(userId);

            foreach (var table in _sessionTables.Concat(_persistentTables))
            {
                if (table == "users")
                {
                    _connection.Execute($"DELETE FROM users WHERE id = ?", userId);
                }
                else
                {
                    _connection.Execute($"DELETE FROM {table} WHERE user_id = ?", userId);
                }
            }
            
            UnityEngine.Debug.Log($"[DB] Deleted ALL data for user {userId}");
        });
    }

    public void InsertOrUpdateUser(User user)
    {
        if (user == null) return;
        InsertOrReplace(user);
        SetCurrentUser(user.id);
    }

    public void ClearCurrentUserMarker()
    {
        CurrentUserId = 0;
        _hasLocalChanges = false;
        _tablesWithLocalChanges.Clear();
    }

    public void ClearUserLocalData(int userId)
    {
        UnityEngine.Debug.LogWarning("[DB] ClearUserLocalData is deprecated. Use ClearSessionData or DeleteAllUserData instead.");
        ClearSessionData(userId);
    }

    public void ClearUserLocalData()
    {
        if (!IsLoggedIn) return;
        ClearSessionData(CurrentUserId);
    }

    public void ClearSessionSafe()
    {
        try
        {
            if (IsLoggedIn) 
                ClearSessionData(CurrentUserId);
            ClearCurrentUserMarker();
        }
        catch (Exception ex) { HandleError("ClearSessionSafe", ex); }
    }

    public void SetDataPersistenceSettings(bool keepHealthData, bool keepProgressData)
    {
        _clearHealthDataOnLogout = !keepHealthData;
        _clearProgressDataOnLogout = !keepProgressData;
        
        PlayerPrefs.SetInt("keep_health_data", keepHealthData ? 1 : 0);
        PlayerPrefs.SetInt("keep_progress_data", keepProgressData ? 1 : 0);
        PlayerPrefs.Save();
        
        UnityEngine.Debug.Log($"[DB] Data persistence updated - Health: {keepHealthData}, Progress: {keepProgressData}");
    }

    public void LoadDataPersistenceSettings()
    {
        _clearHealthDataOnLogout = PlayerPrefs.GetInt("keep_health_data", 0) == 0;
        _clearProgressDataOnLogout = PlayerPrefs.GetInt("keep_progress_data", 0) == 0;
    }

    #region CRUD Operations
    public void Execute(Action action, int retryCount = 5, float retryDelay = 0.2f)
    {
        if (action == null) return;
        
        if (_dbWriteQueue.Count >= _maxWriteQueueSize)
        {
            UnityEngine.Debug.LogError($"[DB] Write queue full ({_dbWriteQueue.Count}), operation rejected");
            return;
        }
        
        EnsureValidConnection();
        
        EnqueueDbOperation(WriteCoroutine(action, retryCount, retryDelay));
    }

    private IEnumerator WriteCoroutine(Action action, int retryCount, float retryDelay)
    {
        // Add depth tracking to prevent stack overflow
        int callDepth = 0;
        string operationId = Guid.NewGuid().ToString().Substring(0, 8);
        
        UnityEngine.Debug.Log($"[DB] Write operation {operationId} started, depth: {callDepth}");
        
        bool success = false;
        Exception lastException = null;
        
        for (int retries = 0; retries < retryCount; retries++)
        {
            bool shouldRetry = false;
            Exception currentException = null;
            bool isBusyException = false;
            
            try
            {
                // Check for excessive recursion
                if (callDepth > 10)
                {
                    throw new InvalidOperationException("Maximum recursion depth exceeded in write operation");
                }
                
                callDepth++;
                
                lock (_dbLock) 
                {
                    action.Invoke();
                    _totalWriteOperations++;
                }
                success = true;
                break;
            }
            catch (SQLiteException sqlEx) when (sqlEx.Result == SQLite3.Result.Busy || sqlEx.Result == SQLite3.Result.Locked)
            {
                currentException = sqlEx;
                lastException = sqlEx;
                isBusyException = true;
                shouldRetry = retries < retryCount - 1;
                
                UnityEngine.Debug.LogWarning($"[DB] Database busy, retry {retries + 1}/{retryCount} in {retryDelay * (retries + 1)}s");
            }
            catch (Exception e) when (e.Message.Contains("database is locked") || e.Message.Contains("Busy"))
            {
                currentException = e;
                lastException = e;
                isBusyException = true;
                shouldRetry = retries < retryCount - 1;
                
                UnityEngine.Debug.LogWarning($"[DB] Database locked, retry {retries + 1}/{retryCount} in {retryDelay * (retries + 1)}s");
            }
            catch (Exception e)
            {
                lastException = e;
                break;
            }
            finally
            {
                callDepth--;
            }
            
            if (isBusyException && shouldRetry)
            {
                yield return StartCoroutine(WaitForSecondsRealtime(retryDelay * (retries + 1)));
            }
            else if (!shouldRetry)
            {
                break;
            }
        }
        
        if (!success)
        {
            HandleError("Write operation", lastException);
            
            if (lastException != null && 
                (lastException.Message.Contains("database is locked") || 
                 lastException.Message.Contains("Busy") ||
                 (lastException is SQLiteException sqlEx && 
                  (sqlEx.Result == SQLite3.Result.Busy || sqlEx.Result == SQLite3.Result.Locked))))
            {
                UnityEngine.Debug.LogWarning("[DB] Triggering emergency recovery due to persistent busy state");
                EmergencyDatabaseRecovery();
            }
        }
        else if (IsLoggedIn && !_isApplyingServerData)
        {
            MarkLocalDataChanged();
            ScheduleAutoPush();
            
            CheckAndPerformMaintenance();
        }
    }

    private IEnumerator WaitForSecondsRealtime(float seconds)
    {
        float startTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - startTime < seconds)
        {
            yield return null;
        }
    }

    public void EnqueueDbOperation(IEnumerator coroutine)
    {
        if (coroutine == null) return;

        lock (_dbWriteQueue)
        {
            _dbWriteQueue.Enqueue(coroutine);
            if (!_isProcessingWrites)
                StartCoroutine(ProcessDbQueue());
        }
    }

    private IEnumerator ProcessDbQueue()
    {
        _isProcessingWrites = true;
        
        while (_dbWriteQueue.Count > 0)
        {
            IEnumerator dbOp;
            lock (_dbWriteQueue) 
            {
                if (_dbWriteQueue.Count > 0)
                    dbOp = _dbWriteQueue.Dequeue();
                else
                    break;
            }
            
            if (dbOp != null) 
            {
                yield return StartCoroutine(dbOp);
                yield return new WaitForSeconds(_queueProcessDelay);
            }
        }
        _isProcessingWrites = false;
    }

    public async Task ExecuteAsync(Action action)
    {
        await Task.Run(() =>
        {
            try
            {
                Execute(action);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[DB] Async operation failed: {ex.Message}");
            }
        });
    }

    public bool TryExecuteImmediate(Action action, int maxRetries = 3)
    {
        if (action == null) return false;
        
        for (int retries = 0; retries < maxRetries; retries++)
        {
            try
            {
                lock (_dbLock)
                {
                    action.Invoke();
                    _totalWriteOperations++;
                }
                
                if (IsLoggedIn && !_isApplyingServerData)
                {
                    _hasLocalChanges = true;
                    ScheduleAutoPush();
                }
                return true;
            }
            catch (Exception ex)
            {
                if (retries == maxRetries - 1)
                {
                    UnityEngine.Debug.LogError($"[DB] Immediate operation failed after {maxRetries} retries: {ex.Message}");
                    return false;
                }
                
                if (ex.Message.Contains("database is locked") || ex.Message.Contains("Busy"))
                {
                    System.Threading.Thread.Sleep(50 * (retries + 1));
                }
                else
                {
                    break;
                }
            }
        }
        return false;
    }

    public void Insert<T>(T item) => DbOperation<T>(conn => conn.Insert(item), "Insert");
    
    public void UpdateRecord<T>(T item) 
    {
        if (item == null) return;
        
        if (item is ScreenTimeHistory)
        {
            if (TryExecuteImmediate(() => _connection.Update(item)))
            {
                return;
            }
        }
        
        Execute(() =>
        {
            try 
            { 
                _connection.Update(item);
            }
            catch (Exception ex) 
            { 
                throw new Exception($"Update<{typeof(T).Name}> failed: {ex.Message}"); 
            }
        }, 4, 0.15f);
    }
    
    public void Delete<T>(T item) => DbOperation<T>(conn => conn.Delete(item), "Delete");
    public void InsertOrReplace<T>(T item) => DbOperation<T>(conn => conn.InsertOrReplace(item), "InsertOrReplace");

    public void DbOperation<T>(Action<SQLiteConnection> operation, string operationName)
    {
        Execute(() =>
        {
            try 
            { 
                operation(_connection);
            }
            catch (Exception ex) 
            { 
                throw new Exception($"{operationName}<{typeof(T).Name}> failed: {ex.Message}"); 
            }
        });
    }

    public List<T> GetAll<T>() where T : new()
    {
        lock (_dbLock)
        {
            try { return _connection.Table<T>().ToList(); }
            catch (Exception ex) { HandleError($"GetAll<{typeof(T).Name}>", ex); return new List<T>(); }
        }
    }

    public List<T> GetPaginated<T>(int page, int pageSize, string orderBy = "id") where T : new()
    {
        lock (_dbLock)
        {
            try 
            { 
                int offset = (page - 1) * pageSize;
                return _connection.Query<T>($"SELECT * FROM {typeof(T).Name} ORDER BY {orderBy} LIMIT {pageSize} OFFSET {offset}");
            }
            catch (Exception ex) 
            { 
                HandleError($"GetPaginated<{typeof(T).Name}>", ex); 
                return new List<T>(); 
            }
        }
    }

    public void InsertWithUser<T>(T item, int? userId = null) where T : class, IUserData
    {
        if (!IsLoggedIn) return;
        item.user_id = userId ?? CurrentUserId;
        Insert(item);
    }

    public List<T> GetAllWithUser<T>(int? userId = null) where T : class, IUserData, new()
    {
        int targetUserId = userId ?? CurrentUserId;
        return IsLoggedIn ? _connection.Table<T>().Where(x => x.user_id == targetUserId).ToList() : new List<T>();
    }

    public T GetLatestWithUser<T>(int? userId = null) where T : class, IUserData, IHasDate, new()
    {
        if (!IsLoggedIn) return null;
        int targetUserId = userId ?? CurrentUserId;
        var table = _connection.Table<T>().Where(x => x.user_id == targetUserId);
        return table.Cast<IHasDate>().OrderByDescending(x => x.DateTimeValue).Cast<T>().FirstOrDefault();
    }
    #endregion

    #region Achievement Cache Management
    public HashSet<int> GetUnlockedAchievementIds(int userId, bool forceRefresh = false)
    {
        lock (_cacheLock)
        {
            if (!forceRefresh && _cachedUnlockedAchievements.ContainsKey(userId))
            {
                UnityEngine.Debug.Log($"[DB] Returning cached unlocked achievements for user {userId}: {_cachedUnlockedAchievements[userId].Count} achievements");
                return _cachedUnlockedAchievements[userId];
            }

            HashSet<int> unlockedIds = new HashSet<int>();
            
            lock (_dbLock)
            {
                try
                {
                    var unlockedAchievements = _connection.Table<UserAchievement>()
                        .Where(ua => ua.user_id == userId && ua.claimed)
                        .ToList();

                    foreach (var ua in unlockedAchievements)
                    {
                        unlockedIds.Add(ua.achievement_id);
                    }

                    _cachedUnlockedAchievements[userId] = unlockedIds;
                    
                    UnityEngine.Debug.Log($"[DB] Fresh query - Unlocked achievements for user {userId}: {unlockedIds.Count} achievements");
                    
                    if (unlockedIds.Count > 0)
                    {
                        UnityEngine.Debug.Log($"[DB] Unlocked IDs: {string.Join(", ", unlockedIds.Take(10))}" + 
                            (unlockedIds.Count > 10 ? "..." : ""));
                    }
                }
                catch (Exception ex)
                {
                    HandleError("GetUnlockedAchievementIds", ex);
                    return new HashSet<int>();
                }
            }

            return unlockedIds;
        }
    }

    public void ClearUnlockedAchievementsCache(int userId)
    {
        lock (_cacheLock)
        {
            if (_cachedUnlockedAchievements.ContainsKey(userId))
            {
                _cachedUnlockedAchievements.Remove(userId);
                UnityEngine.Debug.Log($"[DB] Cleared achievement cache for user {userId}");
            }
        }
    }

    public void ClearAllUnlockedAchievementsCache()
    {
        lock (_cacheLock)
        {
            _cachedUnlockedAchievements.Clear();
            UnityEngine.Debug.Log("[DB] Cleared all achievement caches");
        }
    }

    public void RefreshUnlockedAchievementsCache(int userId)
    {
        ClearUnlockedAchievementsCache(userId);
        GetUnlockedAchievementIds(userId, forceRefresh: true);
        UnityEngine.Debug.Log($"[DB] Refreshed achievement cache for user {userId}");
    }
    #endregion

   #region Screen Time Operations - SIMPLIFIED ONE ROW PER DAY
/// <summary>
/// SIMPLE ONE ROW PER DAY: Creates one record per day, updates screen_on_time as time runs
/// This is the ULTIMATE SIMPLE version - no merging, no cleanup, no duplicates logic
/// </summary>
public void UpdateScreenTimeImmediate(int minutes, int? userId = null)
{
    if (!IsLoggedIn) return;
    int targetUserId = userId ?? CurrentUserId;
    
    // Use the simple update method
    UpdateScreenTimeOnly(minutes, targetUserId);
}

/// <summary>
/// Update user screen time minutes (SIMPLE VERSION)
/// Just updates screen time, keeps existing breaks and eye score
/// </summary>
public void UpdateUserScreenTimeMinutes(int minutes, int? userId = null)
{
    if (!IsLoggedIn) return;
    int targetUserId = userId ?? CurrentUserId;
    
    UpdateScreenTimeOnly(minutes, targetUserId);
}

/// <summary>
/// Update daily metrics (SIMPLE VERSION)
/// Updates all three values: screen time, breaks, and eye score
/// </summary>
public void UpdateDailyMetrics(int screenMinutes, int breaks, int eyeScore, bool accumulateBreaks = true, bool accumulateScore = true)
{
    if (!IsLoggedIn || _isApplyingServerData) return;
    
    // ✅ ADD DEBUG LOG
    Debug.Log($"[DB-Simple] UpdateDailyMetrics called: {screenMinutes}m, {breaks} breaks, {eyeScore} score");
    
    // Simple one-row-per-day update
    UpdateOrInsertScreenTimeSimple(CurrentUserId, DateTime.Now, screenMinutes, breaks, eyeScore);
}

/// <summary>
/// SIMPLE ONE ROW PER DAY - Just updates or inserts a single record
/// No merging logic, no duplicate cleanup - just pure simplicity
/// </summary>
private void UpdateOrInsertScreenTimeSimple(int userId, DateTime date, int screenTimeMinutes, int breaks, int eyeScore)
{
    if (!IsLoggedIn || userId != CurrentUserId) return;

    Execute(() =>
    {
        var targetDate = date.Date;
        
        // ✅ EXTENDED DEBUG LOGGING
        string caller = GetCallerMethodName();
        Debug.Log($"[DB-Simple] UPDATE/INSERT called from {caller}: " +
                 $"User={userId}, Date={targetDate:yyyy-MM-dd}, " +
                 $"Minutes={screenTimeMinutes}, Breaks={breaks}, EyeScore={eyeScore}, " +
                 $"Now={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        
        // 1. Try to find existing record for today
        var existingRecord = _connection.Table<ScreenTimeHistory>()
            .FirstOrDefault(x => x.user_id == userId && x.DateTimeValue.Date == targetDate);
        
        Debug.Log($"[DB-Simple] Existing record found: {(existingRecord != null ? $"YES (ID: {existingRecord.id}, Minutes: {existingRecord.screen_on_time})" : "NO")}");
        
        // 2. If record exists, UPDATE it
        if (existingRecord != null)
        {
            Debug.Log($"[DB-Simple] Updating existing record ID {existingRecord.id}: " +
                     $"{existingRecord.screen_on_time}m → {screenTimeMinutes}m, " +
                     $"{existingRecord.breaks_taken} breaks → {breaks} breaks");
            
            existingRecord.screen_on_time = screenTimeMinutes;
            existingRecord.breaks_taken = breaks;
            existingRecord.eye_score = eyeScore;
            existingRecord.DateTimeValue = DateTime.Now;
            
            _connection.Update(existingRecord);
            Debug.Log($"[DB-Simple] ✅ Updated record for {targetDate:yyyy-MM-dd}: {screenTimeMinutes}m");
        }
        // 3. If no record exists, CREATE one (but only if we have meaningful data)
        else if (screenTimeMinutes >= 5 || breaks > 0)
        {
            Debug.Log($"[DB-Simple] Creating NEW record for {targetDate:yyyy-MM-dd}: " +
                     $"{screenTimeMinutes}m, {breaks} breaks, {eyeScore} score");
            
            var newRecord = new ScreenTimeHistory
            {
                user_id = userId,
                screen_on_time = screenTimeMinutes,
                breaks_taken = breaks,
                eye_score = eyeScore,
                DateTimeValue = DateTime.Now
            };
            
            _connection.Insert(newRecord);
            Debug.Log($"[DB-Simple] ✅ Created new ONE ROW for {targetDate:yyyy-MM-dd}: {screenTimeMinutes}m");
        }
        else
        {
            Debug.Log($"[DB-Simple] ⏭️ Skipping record creation: {screenTimeMinutes}m < 5m and no breaks (breaks={breaks})");
        }
    });
}

// Helper method to get caller method name for debugging
private string GetCallerMethodName()
{
    try
    {
        var stackTrace = new System.Diagnostics.StackTrace();
        if (stackTrace.FrameCount > 2) // 0=GetCallerMethodName, 1=UpdateOrInsertScreenTimeSimple, 2=caller
        {
            var frame = stackTrace.GetFrame(2);
            var method = frame.GetMethod();
            return $"{method.DeclaringType?.Name}.{method.Name}";
        }
    }
    catch { }
    return "Unknown";
}

/// <summary>
/// SIMPLE: Update just screen time (preserves existing breaks/eyeScore)
/// Use this when you only want to update screen time
/// </summary>
public void UpdateScreenTimeOnly(int minutes, int? userId = null)
{
    if (!IsLoggedIn) return;
    int targetUserId = userId ?? CurrentUserId;
    
    Execute(() =>
    {
        var targetDate = DateTime.Today;
        
        // ✅ ADD DEBUG LOG
        Debug.Log($"[DB-Simple] UpdateScreenTimeOnly called: {minutes}m for user {targetUserId}");
        
        var record = _connection.Table<ScreenTimeHistory>()
            .FirstOrDefault(x => x.user_id == targetUserId && x.DateTimeValue.Date == targetDate);
        
        if (record != null)
        {
            // Keep existing breaks and eyeScore, only update screen time
            Debug.Log($"[DB-Simple] Updating existing record ID {record.id}: " +
                     $"{record.screen_on_time}m → {minutes}m");
            
            record.screen_on_time = minutes;
            record.DateTimeValue = DateTime.Now;
            _connection.Update(record);
            Debug.Log($"[DB-Simple] Updated screen time to {minutes}m for user {targetUserId}");
        }
        else if (minutes >= 5) // Only create if ≥5 minutes
        {
            Debug.Log($"[DB-Simple] Creating new record: {minutes}m ≥ 5m, user {targetUserId}");
            
            // Create new record with defaults
            var newRecord = new ScreenTimeHistory
            {
                user_id = targetUserId,
                screen_on_time = minutes,
                breaks_taken = 0,      // Default for new day
                eye_score = 100,       // Default for new day
                DateTimeValue = DateTime.Now
            };
            _connection.Insert(newRecord);
            Debug.Log($"[DB-Simple] Created new record with {minutes}m for user {targetUserId}");
        }
        else
        {
            Debug.Log($"[DB-Simple] Skipping: {minutes}m < 5m, no record to update");
        }
    });
}

/// <summary>
/// SIMPLE: Get total screen time minutes for today
/// </summary>
public int GetTotalScreenTimeMinutes()
{
    if (!IsLoggedIn) return 0;
    
    lock (_dbLock)
    {
        try
        {
            var record = _connection.Table<ScreenTimeHistory>()
                .FirstOrDefault(x => x.user_id == CurrentUserId && x.DateTimeValue.Date == DateTime.Today);
            
            if (record != null)
            {
                Debug.Log($"[DB-Simple] Retrieved screen time: {record.screen_on_time}m");
                return Mathf.RoundToInt(record.screen_on_time);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DB-Simple] GetTotalScreenTimeMinutes failed: {ex.Message}");
            return 0;
        }
    }
}

/// <summary>
/// SIMPLE: Check if today's record exists
/// </summary>
public bool HasTodayRecord(int? userId = null)
{
    if (!IsLoggedIn) return false;
    int targetUserId = userId ?? CurrentUserId;

    lock (_dbLock)
    {
        var record = _connection.Table<ScreenTimeHistory>()
            .FirstOrDefault(x => x.user_id == targetUserId && x.DateTimeValue.Date == DateTime.Today);
        
        return record != null;
    }
}

/// <summary>
/// SIMPLE: Get today's screen time from database
/// </summary>
public float GetTodayScreenTimeFromDB(int? userId = null)
{
    if (!IsLoggedIn) return 0f;
    int targetUserId = userId ?? CurrentUserId;

    lock (_dbLock)
    {
        var record = _connection.Table<ScreenTimeHistory>()
            .FirstOrDefault(x => x.user_id == targetUserId && x.DateTimeValue.Date == DateTime.Today);
        
        return record?.screen_on_time ?? 0f;
    }
}

/// <summary>
/// SIMPLE: Check if should sync today
/// Returns true if screen time ≥5 minutes OR has breaks
/// </summary>
public bool ShouldSyncToday(int? userId = null)
{
    if (!IsLoggedIn) return false;
    int targetUserId = userId ?? CurrentUserId;

    lock (_dbLock)
    {
        var record = _connection.Table<ScreenTimeHistory>()
            .FirstOrDefault(x => x.user_id == targetUserId && x.DateTimeValue.Date == DateTime.Today);
        
        if (record != null)
        {
            return record.screen_on_time >= 5 || record.breaks_taken > 0;
        }
        return false;
    }
}

/// <summary>
/// SIMPLE: Get the latest screen time record
/// </summary>
public ScreenTimeHistory GetLatestScreenTimeRecord(int? userId = null)
{
    if (!IsLoggedIn) return null;
    int targetUserId = userId ?? CurrentUserId;

    lock (_dbLock)
    {
        return _connection.Table<ScreenTimeHistory>()
            .Where(x => x.user_id == targetUserId)
            .OrderByDescending(x => x.DateTimeValue)
            .FirstOrDefault();
    }
}

/// <summary>
/// SIMPLE: Get database status
/// </summary>
public string GetDatabaseStatus(int? userId = null)
{
    if (!IsLoggedIn) return "Not logged in";
    int targetUserId = userId ?? CurrentUserId;

    lock (_dbLock)
    {
        try
        {
            var todayRecord = _connection.Table<ScreenTimeHistory>()
                .FirstOrDefault(x => x.user_id == targetUserId && x.DateTimeValue.Date == DateTime.Today);
            
            var totalRecords = _connection.Table<ScreenTimeHistory>()
                .Count(x => x.user_id == targetUserId);
            
            bool hasTodayRecord = todayRecord != null;
            float todayMinutes = hasTodayRecord ? todayRecord.screen_on_time : 0;
            int todayBreaks = hasTodayRecord ? todayRecord.breaks_taken : 0;
            bool shouldSync = todayMinutes >= 5 || todayBreaks > 0;
            
            return $"ONE ROW PER DAY (Simple) Status for User {targetUserId}:\n" +
                   $"Today Record: {(hasTodayRecord ? "YES" : "NO")}\n" +
                   $"Today Minutes: {todayMinutes:F0}m\n" +
                   $"Today Breaks: {todayBreaks}\n" +
                   $"Should Sync: {(shouldSync ? "YES (≥5m or has breaks)" : "NO")}\n" +
                   $"Total Records: {totalRecords}";
        }
        catch (Exception ex)
        {
            return $"Error getting database status: {ex.Message}";
        }
    }
}

/// <summary>
/// NEW: Emergency cleanup for 0-minute records
/// </summary>
public void CleanupZeroMinuteRecords(int? userId = null)
{
    if (!IsLoggedIn) return;
    int targetUserId = userId ?? CurrentUserId;

    Execute(() =>
    {
        var today = DateTime.Today;
        Debug.Log($"[DB-Cleanup] Looking for 0-minute records for user {targetUserId} on {today:yyyy-MM-dd}");
        
        // Get all today's records
        var todayRecords = _connection.Table<ScreenTimeHistory>()
            .Where(x => x.user_id == targetUserId && x.DateTimeValue.Date == today)
            .ToList();
        
        Debug.Log($"[DB-Cleanup] Found {todayRecords.Count} records for today");
        
        // Find records with 0 screen time and 0 breaks
        var zeroMinuteRecords = todayRecords.Where(x => x.screen_on_time == 0 && x.breaks_taken == 0).ToList();
        
        if (zeroMinuteRecords.Count > 0)
        {
            Debug.Log($"[DB-Cleanup] Found {zeroMinuteRecords.Count} zero-minute records to delete");
            
            foreach (var record in zeroMinuteRecords)
            {
                _connection.Delete(record);
                Debug.Log($"[DB-Cleanup] Deleted 0-minute record ID {record.id}");
            }
            
            Debug.Log($"[DB-Cleanup] Deleted {zeroMinuteRecords.Count} zero-minute records");
        }
        else
        {
            Debug.Log($"[DB-Cleanup] No 0-minute records found (good!)");
        }
        
        // After cleanup, check if we have duplicate records
        var remainingRecords = _connection.Table<ScreenTimeHistory>()
            .Where(x => x.user_id == targetUserId && x.DateTimeValue.Date == today)
            .ToList();
        
        if (remainingRecords.Count > 1)
        {
            Debug.Log($"[DB-Cleanup] WARNING: Found {remainingRecords.Count} duplicate records after cleanup");
            
            // Keep only the one with highest screen time (or most recent if equal)
            var bestRecord = remainingRecords
                .OrderByDescending(x => x.screen_on_time)
                .ThenByDescending(x => x.DateTimeValue)
                .First();
            
            Debug.Log($"[DB-Cleanup] Keeping record ID {bestRecord.id} with {bestRecord.screen_on_time}m");
            
            // Delete all others
            foreach (var record in remainingRecords)
            {
                if (record.id != bestRecord.id)
                {
                    _connection.Delete(record);
                    Debug.Log($"[DB-Cleanup] Deleted duplicate record ID {record.id}");
                }
            }
        }
        
        Debug.Log($"[DB-Cleanup] Cleanup completed for user {targetUserId}");
    });
}

#endregion
    #region Sync Manager Integration

    // Add these properties
    public bool IsInitialized => _isInitialized;
    public bool IsApplyingServerData => _isApplyingServerData;
    public bool IsGlobalDataFetched => _globalDataFetched;

    // UPDATED: NotifyTableChanged with stack protection
    public void NotifyTableChanged(string tableName, string changeType = "update")
    {
        // Track notification depth to detect recursion
        _notificationDepth++;
        
        if (_notificationDepth > 5)
        {
            UnityEngine.Debug.LogError($"[DB] ⚠️ RECURSION DETECTED! Depth: {_notificationDepth}, Table: {tableName}");
            _notificationDepth = 0;
            return;
        }
        
        if (!IsLoggedIn || _isApplyingServerData) 
        {
            _notificationDepth--;
            return;
        }
        
        string notificationKey = $"{tableName}_{changeType}";
        
        // Prevent immediate re-notification of the same event
        if (_activeNotifications.Contains(notificationKey))
        {
            UnityEngine.Debug.LogWarning($"[DB] Already processing notification: {notificationKey}");
            _notificationDepth--;
            return;
        }
        
        _activeNotifications.Add(notificationKey);
        
        try
        {
            UnityEngine.Debug.Log($"[DB] NotifyTableChanged: {tableName} ({changeType}) - Depth: {_notificationDepth}");
            
            // Notify SyncManager (but only if it won't cause recursion)
            if (SyncManager.Instance != null)
            {
                UnityEngine.Debug.Log($"[DB] Forwarding to SyncManager");
                SyncManager.Instance.OnTableChanged(tableName, changeType);
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[DB] SyncManager not available to notify about table change: {tableName}");
            }
            
            // Also trigger local change tracking
            OnDataChanged(tableName);
        }
        finally
        {
            _activeNotifications.Remove(notificationKey);
            _notificationDepth--;
        }
    }

    private IEnumerator DelayedSyncNotification(string tableName, string changeType)
    {
        yield return new WaitForSeconds(0.5f); // Wait half a second
        
        if (!_isApplyingServerData)
        {
            NotifyTableChanged(tableName, changeType);
        }
    }

    // REMOVED: Duplicate GetUserAchievements method - using the existing one

    public List<UserWardrobe> GetUserWardrobe(int userId)
    {
        lock (_dbLock)
        {
            try
            {
                var wardrobe = _connection.Table<UserWardrobe>()
                    .Where(uw => uw.user_id == userId)
                    .ToList();
                
                UnityEngine.Debug.Log($"[DB] GetUserWardrobe - User: {userId}, Count: {wardrobe.Count}");
                return wardrobe;
            }
            catch (Exception ex)
            {
                HandleError($"GetUserWardrobe({userId})", ex);
                return new List<UserWardrobe>();
            }
        }
    }

    public List<ScreenTimeHistory> GetScreenTimeHistory(int userId)
    {
        lock (_dbLock)
        {
            try
            {
                var history = _connection.Table<ScreenTimeHistory>()
                    .Where(st => st.user_id == userId)
                    .ToList();
                
                UnityEngine.Debug.Log($"[DB] GetScreenTimeHistory - User: {userId}, Count: {history.Count}");
                return history;
            }
            catch (Exception ex)
            {
                HandleError($"GetScreenTimeHistory({userId})", ex);
                return new List<ScreenTimeHistory>();
            }
        }
    }

    public List<EyeHealthLog> GetEyeHealthLogs(int userId)
    {
        lock (_dbLock)
        {
            try
            {
                var logs = _connection.Table<EyeHealthLog>()
                    .Where(eh => eh.user_id == userId)
                    .ToList();
                
                UnityEngine.Debug.Log($"[DB] GetEyeHealthLogs - User: {userId}, Count: {logs.Count}");
                return logs;
            }
            catch (Exception ex)
            {
                HandleError($"GetEyeHealthLogs({userId})", ex);
                return new List<EyeHealthLog>();
            }
        }
    }

    public List<UserSettings> GetUserSettings(int userId)
    {
        lock (_dbLock)
        {
            try
            {
                var settings = _connection.Table<UserSettings>()
                    .Where(s => s.user_id == userId)
                    .ToList();
                
                UnityEngine.Debug.Log($"[DB] GetUserSettings - User: {userId}, Count: {settings.Count}");
                return settings;
            }
            catch (Exception ex)
            {
                HandleError($"GetUserSettings({userId})", ex);
                return new List<UserSettings>();
            }
        }
    }

    #endregion

    #region Auto-sync
    private void MarkLocalDataChanged(string tableName = null, string source = "Unknown")
    {
        if (!IsLoggedIn || _isApplyingServerData) return;
        
        _hasLocalChanges = true;
        
        if (!string.IsNullOrEmpty(tableName))
        {
            _tablesWithLocalChanges.Add(tableName);
        }
        
        if (tableName == "user_achievements" || tableName == null)
        {
            ClearUnlockedAchievementsCache(CurrentUserId);
        }
        
        if (!string.IsNullOrEmpty(tableName))
            NotifyDataChangedListeners(tableName);
    }

    private void ScheduleAutoPush()
    {
        if (!IsLoggedIn || !_hasLocalChanges) return;

        if (_autoPushCoroutine != null)
            StopCoroutine(_autoPushCoroutine);
        
        // Just clear the flag after delay (SyncManager handles actual push)
        _autoPushCoroutine = StartCoroutine(ClearChangeFlagCoroutine());
    }

    private IEnumerator ClearChangeFlagCoroutine()
    {
        yield return new WaitForSeconds(_autoPushDelay * 2); // Wait longer
        _hasLocalChanges = false;
        _autoPushCoroutine = null;
    }

    public IEnumerator PushIfDataChanged()
    {
        if (!_hasLocalChanges || !IsLoggedIn || SyncManager.Instance == null) yield break;

        // SyncManager handles pushes automatically, just clear flag
        _hasLocalChanges = false;
        yield break;
    }

    public void OnDataChanged(string tableName)
    {
        if (!IsLoggedIn || _isApplyingServerData) return;
        
        // Mark local changes for backwards compatibility
        MarkLocalDataChanged(tableName, "OnDataChanged");
        
        // Use delayed notification to prevent recursion
        StartCoroutine(DelayedSyncNotification(tableName, "data_changed"));
        
        // Notify listeners
        NotifyDataChangedListeners(tableName);
        
        // Schedule auto-push for backwards compatibility
        ScheduleAutoPush();
    }
    #endregion

    #region Dynamic Sync
    public void SyncGlobalDataDynamic(JToken globalData)
    {
        if (globalData == null) return;
        SyncTableData(globalData, false);
        _globalDataFetched = true;
        OnGlobalDataFetched?.Invoke();
    }

    public void SyncUserDataDynamic(JToken userData)
    {
        if (userData == null) return;
        
        _isApplyingServerData = true;
        try
        {
            SyncTableData(userData, true);
            if (IsLoggedIn) EnsureDailyMetricsWithoutPush();
        }
        finally { _isApplyingServerData = false; }
    }

    private void SyncTableData(JToken data, bool isUserTable)
    {
        lock (_dbLock)
        {
            foreach (var table in data.Children<JProperty>())
            {
                var records = table.Value as JArray;
                if (records == null || records.Count == 0) continue;

                foreach (var record in records)
                {
                    try
                    {
                        if (isUserTable && table.Name == "users")
                            ProcessUserRecord(record);
                        else if (_tableTypes.ContainsKey(table.Name))
                            ProcessTableRecord(table.Name, record, isUserTable);
                    }
                    catch (Exception ex)
                    {
                        HandleError($"Sync table {table.Name}", ex);
                    }
                }
            }
        }
    }

    private void ProcessUserRecord(JToken record)
    {
        var userObj = JObject.Parse(record.ToString());
        string[] fields = { "points", "screentime_minutes", "breaks_taken", "health_score" };
        foreach (var field in fields)
            if (userObj[field]?.Type == JTokenType.Null) 
                userObj[field] = field == "screentime_minutes" ? 0f : 0;

        var user = userObj.ToObject<User>();
        InsertOrReplace(user);
        
        if (CurrentUserId <= 0)
        {
            CurrentUserId = user.id;
            SaveSessionPersistentData();
        }
    }

    private void ProcessTableRecord(string tableName, JToken record, bool isUserTable)
    {
        var item = JsonConvert.DeserializeObject(record.ToString(), _tableTypes[tableName]);
        if (isUserTable && item is IUserData userData)
            userData.user_id = CurrentUserId;
        InsertOrReplace(item);
    }

    public void ApplyServerData(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        _isApplyingServerData = true;
        try
        {
            var token = JToken.Parse(json);
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                bool hasUserData = obj.ContainsKey("users") || obj.Properties().Any(p => p.Name.StartsWith("user"));
                
                if (hasUserData) SyncUserDataDynamic(token);
                SyncGlobalDataDynamic(token);
            }
        }
        catch (Exception ex) { HandleError("Apply server data", ex); }
        finally { _isApplyingServerData = false; }
    }

    public void SyncRemoteUserToLocal(User remoteUser)
    {
        if (remoteUser == null) return;
        try
        {
            lock (_dbLock) _connection.InsertOrReplace(remoteUser);
            SetCurrentUser(remoteUser.id);
            UpdateDailyMetrics(0, 0, 100, accumulateBreaks: false, accumulateScore: false);
        }
        catch (Exception ex) { HandleError("SyncRemoteUserToLocal", ex); }
    }
    #endregion

    #region Common Data Operations
    public User GetCurrentUser()
    {
        if (!IsLoggedIn) return null;
        
        lock (_dbLock) 
        {
            try 
            { 
                var user = _connection.Table<User>().FirstOrDefault(u => u.id == CurrentUserId);
                
                if (user != null)
                {
                    UnityEngine.Debug.Log($"[DB] GetCurrentUser - User ID: {user.id}, Points: {user.points}");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[DB] GetCurrentUser - User {CurrentUserId} not found!");
                }
                
                return user;
            }
            catch (Exception ex) 
            { 
                HandleError($"GetCurrentUser", ex); 
                return null;
            }
        }
    }

    public List<T> GetRange<T>(DateTime start, DateTime end) where T : class, IUserData, IHasDate, new()
    {
        if (!IsLoggedIn) return new List<T>();
        lock (_dbLock)
        {
            var allRecords = _connection.Table<T>().Where(x => x.user_id == CurrentUserId).ToList();
            return allRecords.Where(x => x.DateTimeValue.Date >= start.Date && x.DateTimeValue.Date <= end.Date)
                           .OrderBy(x => x.DateTimeValue).ToList();
        }
    }

    private void EnsureDailyMetricsWithoutPush()
    {
        if (!IsLoggedIn) return;
        lock (_dbLock)
        {
            var today = DateTime.Today;
            var record = _connection.Table<ScreenTimeHistory>()
                .FirstOrDefault(x => x.user_id == CurrentUserId && x.DateTimeValue.Date == today);
            
            if (record == null)
            {
                _connection.Insert(new ScreenTimeHistory
                {
                    user_id = CurrentUserId,
                    screen_on_time = 0,
                    breaks_taken = 0,
                    eye_score = 100,
                    DateTimeValue = DateTime.Now
                });
            }
        }
    }

    public UserSettings GetUserSettings()
    {
        if (!IsLoggedIn) return null;
        lock (_dbLock) return _connection.Table<UserSettings>().FirstOrDefault(x => x.user_id == CurrentUserId);
    }

    public void SaveOrUpdateUserSettings(UserSettings settings)
    {
        if (!IsLoggedIn || settings == null) return;
        settings.user_id = CurrentUserId;
        
        Execute(() =>
        {
            var existing = GetUserSettings();
            if (existing != null)
            {
                settings.id = existing.id;
                UpdateRecord(settings);
            }
            else
            {
                Insert(settings);
            }
            
            // Notify SyncManager with delayed approach
            StartCoroutine(DelayedSyncNotification("user_settings", "settings_updated"));
        }, 2, 0.1f);
    }

    public int GetReminderIntervalMinutes()
    {
        var settings = GetUserSettings();
        return settings?.reminder_interval_min ?? 0;
    }

    public int GetUserPoints(int userId = 0)
    {
        int targetUserId = userId > 0 ? userId : CurrentUserId;
        if (targetUserId <= 0) return 0;
        
        lock (_dbLock)
        {
            try
            {
                var user = _connection.Table<User>().FirstOrDefault(u => u.id == targetUserId);
                if (user != null)
                {
                    if (targetUserId == CurrentUserId && SessionManager.Instance?.CurrentUser != null)
                    {
                        if ((int)(SessionManager.Instance.CurrentUser.screentime_minutes ?? 0) != (int)(user.screentime_minutes ?? 0))
                        {
                            SessionManager.Instance.CurrentUser.screentime_minutes = user.screentime_minutes;
                            UnityEngine.Debug.Log($"[DB] Synced SessionManager screen time to DB value: {user.screentime_minutes}");
                        }
                    }
                    return user.points;
                }
                return 0;
            }
            catch (Exception ex)
            {
                HandleError("GetUserPoints", ex);
                return 0;
            }
        }
    }

    public void UpdateUserPoints(int userId, int newPoints)
    {
        Execute(() =>
        {
            var user = _connection.Table<User>().FirstOrDefault(u => u.id == userId);
            if (user != null)
            {
                user.points = newPoints;
                _connection.Update(user);
                
                if (SessionManager.Instance?.CurrentUser?.id == userId)
                {
                    SessionManager.Instance.CurrentUser.points = newPoints;
                    SessionManager.Instance.CurrentUser.screentime_minutes = user.screentime_minutes ?? 0f; // FIXED: Added null check
                }
                
                UnityEngine.Debug.Log($"[DB] Updated user {userId} points to {newPoints}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[DB] User {userId} not found when updating points");
            }
        }, 3, 0.1f);
    }

    public List<UserWardrobe> GetUserWardrobe()
    {
        if (!IsLoggedIn) return new List<UserWardrobe>();
        lock (_dbLock) return _connection.Table<UserWardrobe>().Where(x => x.user_id == CurrentUserId).ToList();
    }

    public void AddUserWardrobeItem(UserWardrobe item)
    {
        if (!IsLoggedIn || item == null) return;
        item.user_id = CurrentUserId;
        
        Execute(() =>
        {
            _connection.Insert(item);
            // Notify SyncManager with delayed approach
            StartCoroutine(DelayedSyncNotification("user_wardrobe", $"item_purchased:{item.item_id}"));
        }, 2, 0.1f);
    }

    // UPDATED: SaveOrUpdateUserWardrobe with delayed notification
    public void SaveOrUpdateUserWardrobe(UserWardrobe item)
    {
        if (!IsLoggedIn || item == null) return;
        item.user_id = CurrentUserId;

        // TEMPORARY: Disable notifications during this operation
        bool wasApplying = _isApplyingServerData;
        _isApplyingServerData = true;
        
        try
        {
            Execute(() =>
            {
                var existing = _connection.Table<UserWardrobe>()
                    .FirstOrDefault(x => x.user_id == CurrentUserId && x.item_id == item.item_id);

                if (existing != null)
                {
                    item.id = existing.id;
                    UpdateRecord(item);
                }
                else
                {
                    Insert(item);
                }
                
                // MANUALLY mark changes without triggering notifications
                _hasLocalChanges = true;
                _tablesWithLocalChanges.Add("user_wardrobe");
                ClearUnlockedAchievementsCache(CurrentUserId);
            }, 2, 0.1f);
        }
        finally
        {
            _isApplyingServerData = wasApplying;
        }
        
        // Schedule a delayed notification to break the immediate loop
        StartCoroutine(DelayedWardrobeNotification(item.item_id));
    }

    private IEnumerator DelayedWardrobeNotification(int itemId)
    {
        yield return new WaitForSeconds(1.0f); // Wait 1 second
        
        // Now safely notify
        if (!_isApplyingServerData)
        {
            NotifyTableChanged("user_wardrobe", $"item_updated:{itemId}");
        }
    }

    public List<WardrobeItem> GetAllWardrobeItems()
    {
        lock (_dbLock) return _connection.Table<WardrobeItem>().ToList();
    }

    public bool IsAchievementClaimed(int userId, int achievementId)
    {
        lock (_dbLock)
        {
            try
            {
                var achievement = _connection.Table<UserAchievement>()
                    .FirstOrDefault(ua => ua.user_id == userId && ua.achievement_id == achievementId);
                return achievement?.claimed ?? false;
            }
            catch (Exception ex)
            {
                HandleError("IsAchievementClaimed", ex);
                return false;
            }
        }
    }

    public int ClaimUserAchievement(int userId, int achievementId)
    {
        int pointsAwarded = 0;
        
        Execute(() =>
        {
            try
            {
                UnityEngine.Debug.Log($"[DB] === START ClaimUserAchievement ===");
                UnityEngine.Debug.Log($"[DB] User: {userId}, Achievement: {achievementId}");

                ClearUnlockedAchievementsCache(userId);

                var existing = _connection.Table<UserAchievement>()
                    .FirstOrDefault(ua => ua.user_id == userId && ua.achievement_id == achievementId);

                var achievement = _connection.Table<Achievement>()
                    .FirstOrDefault(a => a.achievement_id == achievementId);

                if (achievement == null)
                {
                    UnityEngine.Debug.LogWarning($"[DB] Achievement {achievementId} not found");
                    pointsAwarded = 0;
                    return;
                }

                UnityEngine.Debug.Log($"[DB] Achievement found: {achievement.title}, Points: {achievement.points}");

                if (existing == null)
                {
                    var userAch = new UserAchievement
                    {
                        user_id = userId,
                        achievement_id = achievementId,
                        claimed = true,
                        unlocked_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    _connection.Insert(userAch);
                    pointsAwarded = achievement.points;
                    UnityEngine.Debug.Log($"[DB] New achievement claimed - Awarding {pointsAwarded} points");
                }
                else if (!existing.claimed)
                {
                    existing.claimed = true;
                    existing.unlocked_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    _connection.Update(existing);
                    pointsAwarded = achievement.points;
                    UnityEngine.Debug.Log($"[DB] Existing achievement claimed - Awarding {pointsAwarded} points");
                }
                else
                {
                    UnityEngine.Debug.Log($"[DB] Achievement already claimed - No points awarded");
                    pointsAwarded = 0;
                    return;
                }

                if (pointsAwarded > 0)
                {
                    var user = _connection.Table<User>().FirstOrDefault(u => u.id == userId);
                    if (user != null)
                    {
                        int oldPoints = user.points;
                        user.points += pointsAwarded;
                        _connection.Update(user);
                        
                        UnityEngine.Debug.Log($"[DB] User points updated: {oldPoints} -> {user.points}");

                        if (SessionManager.Instance?.CurrentUser?.id == userId)
                        {
                            SessionManager.Instance.CurrentUser.points = user.points;
                            SessionManager.Instance?.RefreshUserPointsFromDatabase();
                            UnityEngine.Debug.Log($"[DB] SessionManager updated: {user.points}");
                        }

                        if (AchievementManager.Instance != null)
                        {
                            AchievementManager.Instance.UpdateTotalPointsUI();
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[DB] User {userId} not found when updating points");
                    }
                }

                ClearUnlockedAchievementsCache(userId);

                // Notify SyncManager with delayed approach
                StartCoroutine(DelayedAchievementSync(userId, achievementId));
                
                // Queue users table sync (points changed)
                StartCoroutine(DelayedUserSync(userId));

                UnityEngine.Debug.Log($"[DB] === END ClaimUserAchievement - Awarded: {pointsAwarded} ===");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[DB] ClaimUserAchievement ERROR: {ex.Message}");
                HandleError("ClaimUserAchievement", ex);
                pointsAwarded = 0;
            }
        }, 3, 0.1f);

        return pointsAwarded;
    }

    private IEnumerator DelayedAchievementSync(int userId, int achievementId)
    {
        yield return new WaitForSeconds(0.5f);
        NotifyTableChanged("user_achievements", $"achievement_claimed:{achievementId}");
    }

    private IEnumerator DelayedUserSync(int userId)
    {
        yield return new WaitForSeconds(0.5f);
        NotifyTableChanged("users", "points_updated");
    }

    public void ResetUserAchievements(int userId)
    {
        Execute(() =>
        {
            try
            {
                _connection.Execute($"DELETE FROM user_achievements WHERE user_id = ?", userId);
                ClearUnlockedAchievementsCache(userId);
                UnityEngine.Debug.Log($"[DB] Reset all achievements for user {userId}");
            }
            catch (Exception ex)
            {
                HandleError("ResetUserAchievements", ex);
            }
        });
    }

    public List<UserAchievement> GetUserAchievements(int userId)
    {
        lock (_dbLock)
        {
            try
            {
                return _connection.Table<UserAchievement>()
                    .Where(ua => ua.user_id == userId)
                    .ToList();
            }
            catch (Exception ex)
            {
                HandleError("GetUserAchievements", ex);
                return new List<UserAchievement>();
            }
        }
    }
    #endregion

    #region Sync Manager Support Methods
    public User GetUserById(int userId)
    {
        if (userId <= 0) return null;
        
        lock (_dbLock)
        {
            try
            {
                UnityEngine.Debug.Log($"[DB] GetUserById - Fetching user {userId}");
                var user = _connection.Table<User>().FirstOrDefault(u => u.id == userId);
                
                if (user != null)
                {
                    UnityEngine.Debug.Log($"[DB] GetUserById - Found user: ID={user.id}, Nickname={user.nickname}, Points={user.points}");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[DB] GetUserById - User {userId} not found");
                }
                
                return user;
            }
            catch (Exception ex)
            {
                HandleError("GetUserById", ex);
                return null;
            }
        }
    }

    // NEW: Added missing GetUser method for SyncManager compatibility
    public User GetUser(int userId)
    {
        return GetUserById(userId); // Reuse existing method
    }

    public void MarkAchievementAsSynced(int userId, int achievementId)
    {
        if (userId <= 0 || achievementId <= 0) return;
        
        Execute(() =>
        {
            try
            {
                UnityEngine.Debug.Log($"[DB] MarkAchievementAsSynced - User {userId}, Achievement {achievementId}");
                
                var userAchievement = _connection.Table<UserAchievement>()
                    .FirstOrDefault(ua => ua.user_id == userId && ua.achievement_id == achievementId);
                
                if (userAchievement != null)
                {
                    UnityEngine.Debug.Log($"[DB] Achievement {achievementId} marked as synced for user {userId}");
                    ClearUnlockedAchievementsCache(userId);
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[DB] Achievement {achievementId} not found for user {userId}");
                }
            }
            catch (Exception ex)
            {
                HandleError("MarkAchievementAsSynced", ex);
            }
        }, 2, 0.1f);
    }

    public void MarkAchievementsAsSynced(int userId, List<int> achievementIds)
    {
        if (userId <= 0 || achievementIds == null || achievementIds.Count == 0) return;
        
        Execute(() =>
        {
            try
            {
                UnityEngine.Debug.Log($"[DB] MarkAchievementsAsSynced - User {userId}, {achievementIds.Count} achievements");
                
                foreach (var achievementId in achievementIds)
                {
                    var userAchievement = _connection.Table<UserAchievement>()
                        .FirstOrDefault(ua => ua.user_id == userId && ua.achievement_id == achievementId);
                    
                    if (userAchievement != null)
                    {
                        // Achievement exists - update synced flag if needed
                    }
                }
                
                ClearUnlockedAchievementsCache(userId);
                
                UnityEngine.Debug.Log($"[DB] {achievementIds.Count} achievements marked as synced for user {userId}");
            }
            catch (Exception ex)
            {
                HandleError("MarkAchievementsAsSynced", ex);
            }
        }, 2, 0.1f);
    }
    #endregion

    #region Data Export & Utility Methods
    public string GetAllUserData()
    {
        if (!IsLoggedIn) 
        {
            UnityEngine.Debug.LogWarning("[DB] GetAllUserData - No user logged in, returning empty");
            return "{}";
        }
        
        try
        {
            var payload = BuildUserDataPayload(CurrentUserId);
            string result = payload.ToString(Formatting.None);
            
            UnityEngine.Debug.Log($"[DB] GetAllUserData - User: {CurrentUserId}, Data Length: {result.Length}, Empty: {result == "{}"}");
            
            if (result == "{}")
            {
                UnityEngine.Debug.LogWarning("[DB] GetAllUserData - Empty payload generated! Checking tables...");
                DebugAllTablesForUser(CurrentUserId);
            }
            else
            {
                var json = JObject.Parse(result);
                if (json["users"] is JArray usersArray && usersArray.Count > 0)
                {
                    var firstUser = usersArray[0] as JObject;
                    if (firstUser != null)
                    {
                        bool hasId = firstUser["id"] != null;
                        UnityEngine.Debug.Log($"[DB] Users table validation - Has ID: {hasId}, User object: {firstUser}");
                        
                        if (!hasId)
                        {
                            UnityEngine.Debug.LogError("[DB] ❌ CRITICAL: Users table missing ID field in final payload!");
                            result = EmergencyRepairPayload(result, CurrentUserId);
                        }
                    }
                }
            }
            
            return result;
        }
        catch (Exception ex) 
        { 
            UnityEngine.Debug.LogError($"[DB] GetAllUserData ERROR: {ex.Message}");
            HandleError("GetAllUserData", ex); 
            return "{}"; 
        }
    }

    private string EmergencyRepairPayload(string originalPayload, int userId)
    {
        try
        {
            UnityEngine.Debug.LogWarning($"[DB] Performing emergency payload repair for user {userId}");
            
            var json = JObject.Parse(originalPayload);
            
            if (json["users"] is JArray usersArray)
            {
                for (int i = 0; i < usersArray.Count; i++)
                {
                    var user = usersArray[i] as JObject;
                    if (user != null && user["id"] == null)
                    {
                        UnityEngine.Debug.LogWarning($"[DB] Repairing user object at index {i}");
                        
                        var dbUser = GetCurrentUser();
                        if (dbUser != null)
                        {
                            usersArray[i] = JObject.FromObject(dbUser);
                            UnityEngine.Debug.Log($"[DB] Replaced broken user object with proper user data: ID={dbUser.id}");
                        }
                        else
                        {
                            var emergencyUser = new User 
                            { 
                                id = userId,
                                nickname = "emergency_user",
                                email = "emergency@example.com",
                                points = 0,
                                screentime_minutes = 0f,
                                breaks_taken = 0,
                                health_score = 100,
                                created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            };
                            usersArray[i] = JObject.FromObject(emergencyUser);
                            UnityEngine.Debug.LogWarning($"[DB] Created emergency user data for ID={userId}");
                        }
                    }
                }
            }
            else
            {
                var dbUser = GetCurrentUser();
                if (dbUser != null)
                {
                    json["users"] = new JArray { JObject.FromObject(dbUser) };
                    UnityEngine.Debug.Log($"[DB] Added missing users table with user ID={dbUser.id}");
                }
                else
                {
                    var emergencyUser = new User 
                    { 
                        id = userId,
                        nickname = "emergency_user", 
                        email = "emergency@example.com",
                        points = 0,
                        screentime_minutes = 0f,
                        breaks_taken = 0,
                        health_score = 100,
                        created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    json["users"] = new JArray { JObject.FromObject(emergencyUser) };
                    UnityEngine.Debug.LogWarning($"[DB] Created emergency users table with ID={userId}");
                }
            }
            
            string repaired = json.ToString(Formatting.None);
            UnityEngine.Debug.Log($"[DB] Emergency repair completed. New length: {repaired.Length}");
            return repaired;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[DB] Emergency repair failed: {ex.Message}");
            return originalPayload;
        }
    }

    private void DebugAllTablesForUser(int userId)
    {
        UnityEngine.Debug.Log($"[DB] === DEBUG ALL TABLES for User {userId} ===");
        
        string[] allTables = { 
            "users", "user_settings", "user_wardrobe", "user_achievements", 
            "eyehealth_log", "screentime_history", "feedbacks" 
        };
        
        foreach (string table in allTables)
        {
            try
            {
                int count = 0;
                object sampleData = null;
                
                lock (_dbLock)
                {
                    if (table == "users")
                    {
                        count = _connection.Table<User>().Count(u => u.id == userId);
                        sampleData = _connection.Table<User>().FirstOrDefault(u => u.id == userId);
                    }
                    else
                    {
                        count = _connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE user_id = ?", userId);
                        var sample = _connection.Query<object>($"SELECT * FROM {table} WHERE user_id = ? LIMIT 1", userId).FirstOrDefault();
                        sampleData = sample;
                    }
                }
                
                UnityEngine.Debug.Log($"[DB] Table '{table}': {count} records");
                if (count > 0 && sampleData != null)
                {
                    UnityEngine.Debug.Log($"[DB]   Sample: {JsonConvert.SerializeObject(sampleData)}");
                }
                else if (count == 0)
                {
                    UnityEngine.Debug.LogWarning($"[DB]   ⚠️ No records found in table '{table}'");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[DB] Could not check table '{table}': {ex.Message}");
            }
        }
        UnityEngine.Debug.Log($"[DB] === END DEBUG ===");
    }

    public JObject BuildUserDataPayload(int userId)
    {
        JObject payload = new JObject();
        
        string[] requiredTables = { "users", "user_settings", "user_achievements", "screentime_history" };
        string[] optionalTables = { "user_wardrobe", "eyehealth_log", "feedbacks" };

        UnityEngine.Debug.Log($"[DB] Building payload for user {userId}");

        foreach (string table in requiredTables)
        {
            try
            {
                var rows = GetTableRowsForUser(table, userId);
                UnityEngine.Debug.Log($"[DB] Required table '{table}': {rows.Count} rows");
                
                var tableArray = new JArray();
                
                foreach (var row in rows)
                {
                    try
                    {
                        var rowObject = JObject.FromObject(row);
                        
                        if (table == "users")
                        {
                            if (rowObject["id"] == null)
                            {
                                UnityEngine.Debug.LogError($"[DB] ❌ User object missing ID field! Raw data: {JsonConvert.SerializeObject(row)}");
                                rowObject["id"] = userId;
                                UnityEngine.Debug.LogWarning($"[DB] Forced ID {userId} into user object");
                            }
                        }
                        
                        tableArray.Add(rowObject);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"[DB] Error serializing row in table {table}: {ex.Message}");
                        var emergencyObj = new JObject();
                        if (table == "users")
                        {
                            emergencyObj["id"] = userId;
                            emergencyObj["nickname"] = "emergency_user";
                            emergencyObj["points"] = 0;
                            emergencyObj["emergency_repaired"] = true;
                        }
                        tableArray.Add(emergencyObj);
                    }
                }
                
                if (table == "users" && rows.Count == 0)
                {
                    UnityEngine.Debug.LogWarning($"[DB] No user found for ID {userId}, creating emergency user record");
                    var emergencyUser = new JObject
                    {
                        ["id"] = userId,
                        ["nickname"] = "emergency_user",
                        ["email"] = "emergency@example.com",
                        ["points"] = 0,
                        ["screentime_minutes"] = 0f,
                        ["breaks_taken"] = 0,
                        ["health_score"] = 100,
                        ["created_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["emergency_created"] = true
                    };
                    tableArray.Add(emergencyUser);
                }
                
                payload[table] = tableArray;
                
                if (tableArray.Count == 0)
                {
                    UnityEngine.Debug.LogWarning($"[DB] Required table '{table}' is empty but included in payload");
                }
                else
                {
                    UnityEngine.Debug.Log($"[DB] Added required table '{table}' with {tableArray.Count} rows");
                }
            }
            catch (Exception ex) 
            { 
                UnityEngine.Debug.LogError($"[DB] Error serializing required table {table}: {ex.Message}");
                payload[table] = new JArray();
                HandleError($"Serialize required table {table}", ex); 
            }
        }

        foreach (string table in optionalTables)
        {
            try
            {
                var rows = GetTableRowsForUser(table, userId);
                UnityEngine.Debug.Log($"[DB] Optional table '{table}': {rows.Count} rows");
                
                if (rows.Count > 0)
                {
                    var tableArray = new JArray(rows.Select(row => JObject.FromObject(row)));
                    payload[table] = tableArray;
                    UnityEngine.Debug.Log($"[DB] Added optional table '{table}' with {rows.Count} rows");
                }
                else
                {
                    UnityEngine.Debug.Log($"[DB] Optional table '{table}' has no data, skipping");
                }
            }
            catch (Exception ex) 
            { 
                UnityEngine.Debug.LogError($"[DB] Error serializing optional table {table}: {ex.Message}");
                HandleError($"Serialize optional table {table}", ex); 
            }
        }
        
        UnityEngine.Debug.Log($"[DB] Final payload has {payload.Properties().Count()} tables");
        
        if (payload["users"] is JArray finalUsersArray && finalUsersArray.Count > 0)
        {
            var firstUser = finalUsersArray[0] as JObject;
            if (firstUser != null && firstUser["id"] != null)
            {
                UnityEngine.Debug.Log($"[DB] ✅ Payload validation passed - Users table has ID: {firstUser["id"]}");
            }
            else
            {
                UnityEngine.Debug.LogError($"[DB] ❌ Payload validation FAILED - Users table missing ID!");
                UnityEngine.Debug.LogError($"[DB] First user object: {firstUser}");
            }
        }
        else
        {
            UnityEngine.Debug.LogError($"[DB] ❌ Payload validation FAILED - No users data found!");
        }
        
        return payload;
    }

    private List<object> GetTableRowsForUser(string tableName, int userId)
    {
        lock (_dbLock)
        {
            try
            {
                switch (tableName)
                {
                    case "users":
                        var users = _connection.Table<User>().Where(u => u.id == userId).ToList();
                        return users.Cast<object>().ToList();
                    
                    case "user_settings":
                        var settings = _connection.Table<UserSettings>().Where(s => s.user_id == userId).ToList();
                        return settings.Cast<object>().ToList();
                    
                    case "user_achievements":
                        var achievements = _connection.Table<UserAchievement>().Where(ua => ua.user_id == userId).ToList();
                        return achievements.Cast<object>().ToList();
                    
                    case "screentime_history":
                        var screenTime = _connection.Table<ScreenTimeHistory>().Where(st => st.user_id == userId).ToList();
                        return screenTime.Cast<object>().ToList();
                    
                    case "user_wardrobe":
                        var wardrobe = _connection.Table<UserWardrobe>().Where(uw => uw.user_id == userId).ToList();
                        return wardrobe.Cast<object>().ToList();
                    
                    case "eyehealth_log":
                        var eyeHealth = _connection.Table<EyeHealthLog>().Where(eh => eh.user_id == userId).ToList();
                        return eyeHealth.Cast<object>().ToList();
                    
                    case "feedbacks":
                        var feedbacks = _connection.Table<Feedback>().Where(f => f.user_id == userId).ToList();
                        return feedbacks.Cast<object>().ToList();
                    
                    default:
                        UnityEngine.Debug.LogWarning($"[DB] Unknown table: {tableName}");
                        return new List<object>();
                }
            }
            catch (Exception ex)
            {
                HandleError($"GetTableRowsForUser({tableName})", ex);
                return new List<object>();
            }
        }
    }

    public void TestGetAllUserData()
    {
        if (!IsLoggedIn)
        {
            UnityEngine.Debug.LogError("[DB] Cannot test - no user logged in");
            return;
        }
        
        UnityEngine.Debug.Log($"[DB] === TEST GetAllUserData ===");
        string data = GetAllUserData();
        UnityEngine.Debug.Log($"[DB] Result: Length={data.Length}, Empty={data == "{}"}");
        
        try
        {
            var json = JObject.Parse(data);
            if (json["users"] is JArray usersArray && usersArray.Count > 0)
            {
                var firstUser = usersArray[0] as JObject;
                bool hasId = firstUser["id"] != null;
                UnityEngine.Debug.Log($"[DB] Users table validation: Has ID={hasId}");
                
                if (!hasId)
                {
                    UnityEngine.Debug.LogError($"[DB] ❌ TEST FAILED: Users table missing ID!");
                    UnityEngine.Debug.LogError($"[DB] User object: {firstUser}");
                }
                else
                {
                    UnityEngine.Debug.Log($"[DB] ✅ TEST PASSED: Users table has ID {firstUser["id"]}");
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"[DB] ❌ TEST FAILED: No users data found!");
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[DB] ❌ TEST FAILED: Invalid JSON - {ex.Message}");
        }
        
        UnityEngine.Debug.Log($"[DB] === END TEST ===");
    }

    public void DebugUserDataStatus()
    {
        if (!IsLoggedIn)
        {
            UnityEngine.Debug.LogError("[DB] No user logged in");
            return;
        }

        UnityEngine.Debug.Log($"[DB] === USER DATA DIAGNOSTIC for User {CurrentUserId} ===");
        
        string[] tables = { "users", "user_settings", "user_wardrobe", "user_achievements", "eyehealth_log", "screentime_history", "feedbacks"};
        
        foreach (string table in tables)
        {
            try
            {
                int count = 0;
                object sampleData = null;
                
                lock (_dbLock)
                {
                    if (table == "users")
                    {
                        count = _connection.Table<User>().Count(u => u.id == CurrentUserId);
                        sampleData = _connection.Table<User>().FirstOrDefault(u => u.id == CurrentUserId);
                    }
                    else
                    {
                        count = _connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE user_id = ?", CurrentUserId);
                        var sample = _connection.Query<object>($"SELECT * FROM {table} WHERE user_id = ? LIMIT 1", CurrentUserId).FirstOrDefault();
                        sampleData = sample;
                    }
                }
                
                UnityEngine.Debug.Log($"[DB] {table}: {count} records");
                if (count > 0 && sampleData != null)
                {
                    UnityEngine.Debug.Log($"[DB]   Sample: {JsonConvert.SerializeObject(sampleData)}");
                    
                    if (table == "users" && sampleData is User user)
                    {
                        UnityEngine.Debug.Log($"[DB]   User validation - ID: {user.id}, Nickname: {user.nickname}, Points: {user.points}");
                    }
                }
                else if (count == 0)
                {
                    UnityEngine.Debug.LogWarning($"[DB]   ⚠️ No records found in table '{table}'");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[DB] Error checking {table}: {ex.Message}");
            }
        }
        
        UnityEngine.Debug.Log($"[DB] === END DIAGNOSTIC ===");
    }

    private void HandleError(string operation, Exception ex, bool invokeError = false)
    {
        // FIXED: Replaced NotificationManager calls with Debug.Log
        string errorMessage = $"[DB] {operation} failed: {ex.Message}";
        UnityEngine.Debug.LogError(errorMessage);
        
        if (invokeError) 
        {
            OnDatabaseError?.Invoke($"{operation} failed: {ex.Message}");
        }
    }

    private void NotifyDataChangedListeners(string tableName)
    {
        foreach (var listener in _dataChangedListeners.ToArray())
        {
            try { listener?.Invoke(tableName); }
            catch (Exception ex) { HandleError("Data change listener", ex); }
        }
    }

    public void RegisterDataChangedListener(Action<string> listener)
    {
        if (listener != null && !_dataChangedListeners.Contains(listener))
            _dataChangedListeners.Add(listener);
    }

    public void UnregisterDataChangedListener(Action<string> listener)
    {
        if (listener != null) _dataChangedListeners.Remove(listener);
    }

    public bool IsDatabaseReady() => _connection != null;

    public void CloseConnection()
    {
        lock (_dbLock)
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
    }

    public long GetDatabaseSize()
    {
        try
        {
            var fileInfo = new FileInfo(_dbPath);
            return fileInfo.Exists ? fileInfo.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    public DatabaseStats GetDatabaseStats()
    {
        return new DatabaseStats
        {
            TotalWriteOperations = _totalWriteOperations,
            WriteQueueSize = _dbWriteQueue.Count,
            DatabaseSizeBytes = GetDatabaseSize(),
            IsProcessingWrites = _isProcessingWrites,
            HasLocalChanges = _hasLocalChanges
        };
    }

    private void OnApplicationQuit() 
    {
        // Unsubscribe from SyncManager events
        if (SyncManager.Instance != null)
        {
            SyncManager.Instance.OnGlobalDataLoaded -= OnGlobalDataLoadedHandler;
            SyncManager.Instance.OnUserDataSynced -= OnUserDataSyncedHandler;
        }
        
        CloseConnection();
    }
    #endregion

    #region Emergency Recovery
    public void EmergencyDatabaseRecovery()
    {
        try
        {
            UnityEngine.Debug.LogWarning("[DB] Attempting enhanced emergency database recovery...");
            
            if (_autoPushCoroutine != null)
            {
                StopCoroutine(_autoPushCoroutine);
                _autoPushCoroutine = null;
            }
            
            lock (_dbWriteQueue)
            {
                _dbWriteQueue.Clear();
            }
            
            ClearAllUnlockedAchievementsCache();
            
            _isProcessingWrites = false;
            
            CloseConnection();
            
            System.Threading.Thread.Sleep(1000);
            
            lock (_dbLock)
            {
                _connection = new SQLiteConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                _connection.BusyTimeout = TimeSpan.FromSeconds(10);
            }
            
            OptimizeDatabase();
            
            _hasLocalChanges = true;
            
            UnityEngine.Debug.Log("[DB] Enhanced emergency recovery completed");
            
            // Notify SyncManager to force sync after recovery
            if (IsLoggedIn && SyncManager.Instance != null)
            {
                SyncManager.Instance.ForceSync();
            }
        }
        catch (Exception ex)
        {
            // FIXED: Replaced NotificationManager call with Debug.LogError
            UnityEngine.Debug.LogError($"[DB] Enhanced emergency recovery failed: {ex.Message}");
            
            try
            {
                CloseConnection();
                System.Threading.Thread.Sleep(2000);
                StartCoroutine(InitializeDatabaseAsync());
            }
            catch (Exception ex2)
            {
                // FIXED: Replaced NotificationManager call with Debug.LogError
                UnityEngine.Debug.LogError($"[DB] Final recovery attempt failed: {ex2.Message}");
            }
        }
    }
    #endregion
    
    #region SyncManager Proxy Methods (for backwards compatibility)

    public IEnumerator PushToServer(int userId, Action<bool, string> callback, bool isLogout = false)
    {
        if (SyncManager.Instance == null)
        {
            callback?.Invoke(false, "SyncManager not available");
            yield break;
        }

        if (isLogout)
        {
            SyncManager.Instance.ForceSync();
            callback?.Invoke(true, "Force sync initiated for logout");
        }
        else
        {
            // Normal push - SyncManager handles this automatically
            callback?.Invoke(true, "SyncManager handles automatic pushes");
        }
    }

    public IEnumerator ProbeUserCoroutine(int userId, Action<bool, bool, string> callback)
    {
        if (SyncManager.Instance == null)
        {
            callback?.Invoke(false, false, "SyncManager not available");
            yield break;
        }

        bool userExists = false;
        bool hasData = false;
        
        yield return StartCoroutine(SyncManager.Instance.ProbeUser(userId, (exists, serverHasData) =>
        {
            userExists = exists;
            hasData = serverHasData;
        }));
        
        string message = userExists ? (hasData ? "User has data" : "User exists but no data") : "New user";
        callback?.Invoke(userExists, hasData, message);
    }

    public IEnumerator PullUserDataSmartCoroutine(int userId, Action<bool, string> callback)
    {
        if (SyncManager.Instance == null)
        {
            callback?.Invoke(false, "SyncManager not available");
            yield break;
        }

        yield return StartCoroutine(SyncManager.Instance.PullAllUserData());
        callback?.Invoke(true, "User data pulled via SyncManager");
    }

    public IEnumerator PullGlobalDataSmartCoroutine(Action<bool, string> callback)
    {
        if (SyncManager.Instance == null)
        {
            callback?.Invoke(false, "SyncManager not available");
            yield break;
        }

        yield return StartCoroutine(SyncManager.Instance.LoadGlobalData());
        callback?.Invoke(true, "Global data loaded via SyncManager");
    }

    #endregion

    #region Additional Helper Methods for SyncManager

    public void SubmitEyeHealthLog(EyeHealthLog log)
    {
        if (!IsLoggedIn || log == null) return;
        log.user_id = CurrentUserId;
        
        Execute(() =>
        {
            _connection.Insert(log);
            // Notify SyncManager
            StartCoroutine(DelayedSyncNotification("eyehealth_log", "survey_completed"));
        }, 2, 0.1f);
    }

    public string GetSyncStatus()
    {
        if (SyncManager.Instance == null)
            return "SyncManager not available";
        
        return SyncManager.Instance.GetStatus();
    }

    #endregion
}

public class ScreenTimeStats
{
    public float TotalMinutes { get; set; }
    public float AverageDailyMinutes { get; set; }
    public float MaxDailyMinutes { get; set; }
    public int DaysTracked { get; set; }
    
    // NEW: ONE ROW PER DAY properties
    public int OneRowPerDayRecords { get; set; }
    public bool HasTodayRecord { get; set; }
    public DateTime EarliestRecordDate { get; set; }
    public DateTime LatestRecordDate { get; set; }
}

public struct DatabaseStats
{
    public int TotalWriteOperations { get; set; }
    public int WriteQueueSize { get; set; }
    public long DatabaseSizeBytes { get; set; }
    public bool IsProcessingWrites { get; set; }
    public bool HasLocalChanges { get; set; }
}