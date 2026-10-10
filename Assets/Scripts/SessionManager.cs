using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SessionManager : MonoBehaviour
{
    public static SessionManager Instance { get; private set; }

    [Header("Default Pages")]
    public GameObject pageHome;
    public GameObject pageLogin;
    public GameObject pageWelcome;

    [Header("Dev Options")]
    public bool DEV_MODE = false;

    public string AuthToken { get; private set; }
    private User _currentUser;
    public User CurrentUser => _currentUser;
    public bool IsLoggedIn => _currentUser != null && _currentUser.id > 0;
    public bool IsReady { get; private set; } = false;
    public bool IsSessionInitialized { get; private set; } = false;

    private float accumulatedScreenTimeMinutes;
    private float sessionStartTimeRealtime;
    private DateTime sessionStartTime;

    private Dictionary<int, User> cachedUsers = new Dictionary<int, User>();
    private readonly string[] sessionKeys = { "user_id", "nickname", "email", "password", "auth_token" };

    // FIXED: Added missing event definitions
    public static event Action<User> OnSessionStarted;
    public static event Action OnSessionEnded;
    public event Action<bool, string> OnLoginSyncCompleted;
    
    // FIXED: Added events that SyncManager tries to subscribe to
    public event Action<User> OnLoginSuccess;
    public event Action OnLogout;

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        ShowInitialUI();
        StartCoroutine(Initialize());
    }

    private void ShowInitialUI()
    {
        if (pageWelcome != null)
        {
            UIManager.Instance?.ShowMainPage(pageWelcome);
        }
        else
        {
            UIManager.Instance?.ShowMainPage(pageLogin);
        }
    }

    private IEnumerator Initialize()
    {
        yield return WaitForDependencies(10f);
        
        yield return StartCoroutine(InitializeInBackground());
    }

    private IEnumerator InitializeInBackground()
    {
        yield return StartCoroutine(LoadUsersFromPrefsAsync());
        SetupUIRefs();

        if (IsFirstAppLaunch())
        {
            ShowWelcomePage();
            IsReady = true;
            yield break;
        }

        User restoredUser = null;
        yield return StartCoroutine(LoadPersistedSessionAsync(result => restoredUser = result));
        
        if (restoredUser != null)
        {
            yield return StartCoroutine(SetupSession(restoredUser, true));
        }
        else
        {
            UIManager.Instance?.ShowMainPage(pageLogin);
        }

        IsReady = true;
    }

    private IEnumerator LoadPersistedSessionAsync(Action<User> callback)
    {
        yield return new WaitForSeconds(0.05f);
        
        if (!PlayerPrefs.HasKey("user_id")) 
        {
            callback?.Invoke(null);
            yield break;
        }

        int id = PlayerPrefs.GetInt("user_id", 0);
        if (id <= 0) 
        {
            callback?.Invoke(null);
            yield break;
        }

        AuthToken = PlayerPrefs.GetString("auth_token", null);

        if (DatabaseManager.Instance != null)
        {
            DatabaseManager.Instance.SetCurrentUser(id);
            var fullUser = DatabaseManager.Instance.GetCurrentUser();
            if (fullUser != null)
            {
                callback?.Invoke(fullUser);
                yield break;
            }
        }

        string nickname = PlayerPrefs.GetString("nickname", "");
        if (string.IsNullOrEmpty(nickname)) 
        {
            callback?.Invoke(null);
            yield break;
        }

        callback?.Invoke(new User
        {
            id = id,
            nickname = nickname,
            email = PlayerPrefs.GetString("email", ""),
            password = PlayerPrefs.GetString("password", "")
        });
    }

    private IEnumerator LoadUsersFromPrefsAsync()
    {
        yield return new WaitForSeconds(0.05f);
        LoadUsersFromPrefs();
    }

    private bool IsFirstAppLaunch()
    {
        string firstLaunchKey = "app_initialized";
        bool isFirstLaunch = !PlayerPrefs.HasKey(firstLaunchKey);
        
        if (isFirstLaunch)
        {
            PlayerPrefs.SetInt(firstLaunchKey, 1);
            PlayerPrefs.Save();
        }
        
        return isFirstLaunch;
    }

    private void ShowWelcomePage()
    {
        if (pageWelcome != null)
        {
            UIManager.Instance?.ShowMainPage(pageWelcome);
        }
        else
        {
            UIManager.Instance?.ShowMainPage(pageLogin);
        }
    }

    public void CompleteWelcomeFlow()
    {
        UIManager.Instance?.ShowMainPage(pageLogin);
    }

    private IEnumerator WaitForDependencies(float timeout)
    {
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            bool dbReady = DatabaseManager.Instance != null;
            bool uiReady = UIManager.Instance != null;

            if (dbReady && uiReady) break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void SetupUIRefs()
    {
        pageLogin = pageLogin ?? UIManager.Instance?.loginPage;
        pageHome = pageHome ?? UIManager.Instance?.allPages?[0];
    }
    #endregion

   #region Session Management - UPDATED
    public void StartSession(User user, string token = null)
    {
        if (user == null || user.id <= 0) return;

        AuthToken = token;
        SaveCurrentUserPrefs(user);
        AddOrUpdateUserInCache(user);

        // FIXED: Trigger login success event
        OnLoginSuccess?.Invoke(user);

        // NEW: Fetch all data on login
        StartCoroutine(SetupSessionWithDataFetch(user));
    }

    private IEnumerator SetupSessionWithDataFetch(User user)
    {
        if (user == null) yield break;

        // SyncManager doesn't have DisableAutoSyncTemporarily, just continue
        yield return WaitForOptionalDependencies(5f);

        // NEW: Fetch all data on login
        bool fetchSuccess = false;
        string fetchMessage = "";
        
        yield return StartCoroutine(DatabaseManager.Instance.FetchAllDataOnLogin(user.id, (success, message) => {
            fetchSuccess = success;
            fetchMessage = message;
            
            if (success)
            {
                DebugLog($"[SessionManager] Login data fetch successful: {message}");
            }
            else
            {
                DebugLog($"[SessionManager] Login data fetch failed: {message}", true);
            }
        }));

        // Set current user in database
        DatabaseManager.Instance.SetCurrentUser(user.id);
        
        // Get updated user from database
        var dbUser = DatabaseManager.Instance.GetCurrentUser();
        if (dbUser != null)
        {
            _currentUser = dbUser;
            DebugLog($"[SessionManager] User loaded from database: ID={dbUser.id}, Points={dbUser.points}");
        }
        else
        {
            _currentUser = user;
            DebugLog($"[SessionManager] Using provided user object: ID={user.id}, Points={user.points}");
            
            // Insert user into database if not found
            DatabaseManager.Instance?.InsertOrUpdateUser(user);
        }

        yield return new WaitForSeconds(0.1f);

        // Emergency points recovery if needed
        if (_currentUser != null && _currentUser.points == 0)
        {
            DebugLog($"[SessionManager] Starting emergency points recovery...");
            yield return StartCoroutine(EmergencyPointsRecovery());
        }

        // Initialize session tracking
        accumulatedScreenTimeMinutes = DatabaseManager.Instance?.GetTotalScreenTimeMinutes() ?? 0f;
        sessionStartTimeRealtime = Time.realtimeSinceStartup;
        sessionStartTime = DateTime.Now;

        IsSessionInitialized = true;
        AddOrUpdateUserInCache(_currentUser);

        // Trigger session start events
        OnSessionStarted?.Invoke(_currentUser);
        
        // Initialize managers
        if (ScreenTimeManager.Instance != null)
        {
            ScreenTimeManager.Instance.InitializeForUser(_currentUser);
        }
        
        UIManager.Instance?.ShowMainPage(pageHome);
        StartAutoSave();

        // Initialize notification system
        InitializeNotificationManager();
        
        // Update UI
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.UpdateTotalPointsUI();
        }

        // Validate data after login
        yield return new WaitForSeconds(0.2f);
        ValidateDataAfterLogin();

        // SyncManager handles its own sync logic via OnUserLogin
        DebugLog($"[SessionManager] Session setup completed for user {_currentUser?.id}");
    }

    private IEnumerator SetupSession(User user, bool isRestoredSession)
    {
        if (user == null) yield break;

        yield return WaitForOptionalDependencies(5f);

        DatabaseManager.Instance.SetCurrentUser(user.id);
        
        var dbUser = DatabaseManager.Instance.GetCurrentUser();
        if (dbUser != null)
        {
            _currentUser = dbUser;
        }
        else
        {
            _currentUser = user;
        }

        yield return new WaitForSeconds(0.05f);

        if (isRestoredSession && _currentUser != null && _currentUser.points == 0)
        {
            yield return StartCoroutine(EmergencyPointsRecovery());
        }

        accumulatedScreenTimeMinutes = DatabaseManager.Instance?.GetTotalScreenTimeMinutes() ?? 0f;
        sessionStartTimeRealtime = Time.realtimeSinceStartup;
        sessionStartTime = DateTime.Now;

        IsSessionInitialized = true;
        AddOrUpdateUserInCache(_currentUser);

        OnSessionStarted?.Invoke(_currentUser);
        
        if (ScreenTimeManager.Instance != null)
        {
            ScreenTimeManager.Instance.InitializeForUser(_currentUser);
        }
        
        UIManager.Instance?.ShowMainPage(pageHome);
        StartAutoSave();

        InitializeNotificationManager();
        
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.UpdateTotalPointsUI();
        }

        if (isRestoredSession)
        {
            yield return StartCoroutine(PushLocalChangesToServer());
        }
        else
        {
            yield return StartCoroutine(PullDataFromServer());
        }
    }

    private void ValidateDataAfterLogin()
    {
        try
        {
            if (_currentUser == null) return;
            
            var userPoints = DatabaseManager.Instance?.GetUserPoints(_currentUser.id) ?? 0;
            var userAchievements = DatabaseManager.Instance?.GetUserAchievements(_currentUser.id);
            var userWardrobe = DatabaseManager.Instance?.GetUserWardrobe();
            var screenTime = DatabaseManager.Instance?.GetTotalScreenTimeMinutes() ?? 0;
            
            DebugLog($"[SessionManager] Login data validation - " +
                    $"User ID: {_currentUser.id}, " +
                    $"Points: {userPoints}, " +
                    $"Achievements: {userAchievements?.Count ?? 0}, " +
                    $"Wardrobe: {userWardrobe?.Count ?? 0}, " +
                    $"Screen Time: {screenTime}min");
            
            // Update session user points if different
            if (_currentUser.points != userPoints)
            {
                _currentUser.points = userPoints;
                DebugLog($"[SessionManager] Updated session points to match database: {userPoints}");
            }
        }
        catch (Exception ex)
        {
            DebugLog($"[SessionManager] Login data validation error: {ex.Message}", true);
        }
    }

    private IEnumerator PullDataFromServer()
    {
        if (!IsLoggedIn || SyncManager.Instance == null) yield break;
        
        // UPDATED: Use ForceSyncCoroutine instead of PullFromServer
        yield return StartCoroutine(SyncManager.Instance.ForceSyncCoroutine((pullSuccess, pullMessage) => {
            if (pullSuccess) {
                RefreshUserPointsFromDatabase();
                DebugLog($"[SessionManager] Pull data successful: {pullMessage}");
            } else {
                DebugLog($"[SessionManager] Pull data failed: {pullMessage}", true);
            }
        }));

        yield return new WaitForSeconds(0.1f);
        ValidateDataAfterPull();
    }

    private IEnumerator PushLocalChangesToServer()
    {
        if (!IsLoggedIn || SyncManager.Instance == null) yield break;
        
        bool hasLocalChanges = CheckForLocalChanges();
        
        if (hasLocalChanges)
        {
            DebugLog($"[SessionManager] Pushing local changes to server...");
            // UPDATED: Use ForceSyncCoroutine instead of PushToServer
            yield return StartCoroutine(SyncManager.Instance.ForceSyncCoroutine((pushSuccess, pushMsg) => {
                if (pushSuccess) {
                    DebugLog($"[SessionManager] Push local changes successful: {pushMsg}");
                } else {
                    DebugLog($"[SessionManager] Push local changes failed: {pushMsg}", true);
                }
            }));
        }
        else
        {
            DebugLog($"[SessionManager] No local changes, pulling from server...");
            // UPDATED: Use ForceSyncCoroutine for both push and pull
            yield return StartCoroutine(SyncManager.Instance.ForceSyncCoroutine((pullSuccess, pullMsg) => {
                if (pullSuccess) {
                    RefreshUserPointsFromDatabase();
                    DebugLog($"[SessionManager] Pull after restore successful: {pullMsg}");
                } else {
                    DebugLog($"[SessionManager] Pull after restore failed: {pullMsg}", true);
                }
            }));
        }

        yield return new WaitForSeconds(0.05f);
        RefreshUserPointsFromDatabase();
    }

    private void ValidateDataAfterPull()
    {
        try
        {
            if (_currentUser == null) return;
            
            var userPoints = DatabaseManager.Instance?.GetUserPoints(_currentUser.id) ?? 0;
            var userAchievements = DatabaseManager.Instance?.GetUserAchievements(_currentUser.id);
            var userWardrobe = DatabaseManager.Instance?.GetUserWardrobe();
            
            DebugLog($"[SessionManager] Data validation after pull - " +
                    $"Points: {userPoints}, " +
                    $"Achievements: {userAchievements?.Count ?? 0}, " +
                    $"Wardrobe: {userWardrobe?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            DebugLog($"[SessionManager] Data validation error after pull: {ex.Message}", true);
        }
    }

    private void InitializeNotificationManager()
    {
        if (NotificationManager.Instance != null && IsLoggedIn)
        {
            try
            {
                int reminderInterval = Mathf.Max(1, DatabaseManager.Instance.GetReminderIntervalMinutes());
                NotificationManager.Instance.UpdateReminderInterval(reminderInterval);
                DebugLog($"[SessionManager] NotificationManager initialized with {reminderInterval}min interval");
            }
            catch (Exception e)
            {
                DebugLog($"[SessionManager] Failed to initialize NotificationManager: {e.Message}", true);
            }
        }
    }

    private bool CheckForLocalChanges()
    {
        if (!IsLoggedIn || DatabaseManager.Instance == null) return false;
        
        try
        {
            string localData = DatabaseManager.Instance.GetAllUserData();
            bool hasData = !string.IsNullOrEmpty(localData) && localData != "{}";
            
            // Check if data contains more than just the basic user info
            if (hasData && localData.Length > 100) // Basic user JSON is usually < 100 chars
            {
                DebugLog($"[SessionManager] Local changes found - Length: {localData.Length}");
                return true;
            }
            
            DebugLog($"[SessionManager] No significant local changes - Length: {localData?.Length ?? 0}");
            return false;
        }
        catch (Exception ex)
        {
            DebugLog($"[SessionManager] Error checking local changes: {ex.Message}", true);
            return false;
        }
    }

    private IEnumerator EmergencyPointsRecovery()
    {
        if (!IsLoggedIn || _currentUser == null) yield break;
        
        DebugLog($"[SessionManager] Starting emergency points recovery for user {_currentUser.id}");
        
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            RefreshUserPointsFromDatabase();
            
            if (_currentUser != null && _currentUser.points > 0)
            {
                DebugLog($"[SessionManager] Emergency points recovery successful on attempt {attempt}: {_currentUser.points} points");
                yield break;
            }
            
            DebugLog($"[SessionManager] Points recovery attempt {attempt} failed, waiting...");
            yield return new WaitForSeconds(0.3f);
        }
        
        // Last resort: check database directly
        try
        {
            var dbUser = DatabaseManager.Instance?.GetCurrentUser();
            if (dbUser != null && dbUser.points > 0)
            {
                _currentUser.points = dbUser.points;
                DebugLog($"[SessionManager] Direct database points recovery: {_currentUser.points} points");
            }
            else
            {
                DebugLog($"[SessionManager] Emergency points recovery failed after all attempts", true);
            }
        }
        catch (Exception ex)
        {
            DebugLog($"[SessionManager] Error in final points recovery: {ex.Message}", true);
        }
    }

    public void Logout()
    {
        if (!IsLoggedIn) 
        {
            DebugLog("[SessionManager] Logout called but no user is logged in");
            return;
        }

        DebugLog($"[SessionManager] Starting logout process for user {_currentUser.id}");
        
        // FIXED: Trigger logout event
        OnLogout?.Invoke();
        
        StartCoroutine(LogoutCoroutine());
    }

    private IEnumerator LogoutCoroutine()
    {
        int userId = _currentUser?.id ?? 0;
        if (userId <= 0)
        {
            DebugLog("[SessionManager] Logout cancelled: no valid user ID");
            yield break;
        }

        DebugLog($"[SessionManager] Starting logout process for user {userId}");
        
        // Notify SyncManager about logout
        if (SyncManager.Instance != null)
        {
            SyncManager.Instance.OnUserLogout();
        }
        
        // Save screen time WITH explicit user ID before clearing
        SaveScreenTimeWithUserId(userId);

        // Push logout tables (users, user_wardrobe, user_settings)
        if (DatabaseManager.Instance != null)
        {
            DebugLog($"[SessionManager] Pushing logout tables for user {userId}...");
            yield return StartCoroutine(DatabaseManager.Instance.PushLogoutTables(userId, 
                (success, msg) => {
                    if (success) {
                        DebugLog($"[SessionManager] Logout tables push successful: {msg}");
                    } else {
                        DebugLog($"[SessionManager] Logout tables push failed: {msg}", true);
                    }
                }));
        }

        // Commit screen time changes
        if (ScreenTimeManager.Instance != null)
        {
            ScreenTimeManager.Instance.OnUserLogoutCommit();
        }
        
        // Clear database markers
        DatabaseManager.Instance?.ClearCurrentUserMarker();

        // Clear session data
        ClearCurrentSessionPrefs();

        // Reset session variables LAST
        _currentUser = null;
        AuthToken = null;
        accumulatedScreenTimeMinutes = 0f;
        sessionStartTimeRealtime = 0f;
        IsSessionInitialized = false;

        // Trigger logout events
        OnSessionEnded?.Invoke();
        
        // Show login page
        UIManager.Instance?.ShowMainPage(pageLogin);
        
        DebugLog($"[SessionManager] Logout completed for user {userId}");
    }
    #endregion

    #region Screen Time
    public float GetCurrentScreenTimeMinutesFloat()
        => IsLoggedIn ? accumulatedScreenTimeMinutes + (Time.realtimeSinceStartup - sessionStartTimeRealtime) / 60f : 0f;

    public int GetCurrentScreenTimeMinutesInt() => Mathf.RoundToInt(GetCurrentScreenTimeMinutesFloat());

    // ✅ FIXED: Pass userId explicitly to prevent user_id = 0 writes
    public void SaveScreenTime()
    {
        if (!IsLoggedIn || _currentUser == null || _currentUser.id <= 0) return;
        accumulatedScreenTimeMinutes = GetCurrentScreenTimeMinutesInt();
        DatabaseManager.Instance?.UpdateUserScreenTimeMinutes((int)accumulatedScreenTimeMinutes, _currentUser.id);
        sessionStartTimeRealtime = Time.realtimeSinceStartup;
    }

    // ✅ Helper: Save with explicit userId (used during logout)
    private void SaveScreenTimeWithUserId(int userId)
    {
        if (userId <= 0) return;
        int screenTimeMinutes = GetCurrentScreenTimeMinutesInt();
        DatabaseManager.Instance?.UpdateUserScreenTimeMinutes(screenTimeMinutes, userId);
    }

    private IEnumerator AutoSaveScreenTime()
    {
        while (IsLoggedIn)
        {
            yield return new WaitForSeconds(120f);
            SaveScreenTime();
        }
    }
    #endregion

    #region PlayerPrefs & Cache
    private void SaveCurrentUserPrefs(User user)
    {
        if (user == null) return;

        PlayerPrefs.SetInt("user_id", user.id);
        PlayerPrefs.SetString("nickname", user.nickname ?? "");
        PlayerPrefs.SetString("email", user.email ?? "");
        PlayerPrefs.SetString("password", user.password ?? "");
        if (!string.IsNullOrEmpty(AuthToken)) PlayerPrefs.SetString("auth_token", AuthToken);
        PlayerPrefs.Save();
    }

    private void ClearCurrentSessionPrefs()
    {
        foreach (var key in sessionKeys)
            PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
    }

    private void LoadUsersFromPrefs()
    {
        if (!PlayerPrefs.HasKey("UsersTable")) return;

        try
        {
            string json = PlayerPrefs.GetString("UsersTable", "[]");
            var wrapper = JsonUtility.FromJson<UserListWrapper>(json);
            cachedUsers.Clear();
            if (wrapper?.users != null)
            {
                foreach (var user in wrapper.users)
                    if (user?.id > 0) cachedUsers[user.id] = user;
            }
        }
        catch (Exception e)
        {
            DebugLog($"[SessionManager] Error loading users cache: {e.Message}", true);
            cachedUsers.Clear();
        }
    }

    private void SaveUsersToPrefs()
    {
        try
        {
            var wrapper = new UserListWrapper { users = new List<User>(cachedUsers.Values) };
            PlayerPrefs.SetString("UsersTable", JsonUtility.ToJson(wrapper));
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            DebugLog($"[SessionManager] Error saving users cache: {e.Message}", true);
        }
    }

    public void AddOrUpdateUserInCache(User user)
    {
        if (user == null || user.id <= 0) return;
        cachedUsers[user.id] = user;
        SaveUsersToPrefs();
    }

    public User GetCachedUser(int userId)
        => cachedUsers.TryGetValue(userId, out var user) ? user : null;

    [Serializable]
    private class UserListWrapper { public List<User> users; }
    #endregion

    #region Points Synchronization
    public void RefreshUserPointsFromDatabase()
    {
        if (!IsLoggedIn) return;
        
        var dbUser = DatabaseManager.Instance?.GetCurrentUser();
        if (dbUser != null && _currentUser != null)
        {
            int oldPoints = _currentUser.points;
            _currentUser.points = dbUser.points;
            
            if (oldPoints != _currentUser.points)
            {
                DebugLog($"[SessionManager] Points updated: {oldPoints} -> {_currentUser.points}");
            }
            
            AddOrUpdateUserInCache(_currentUser);
            
            if (AchievementManager.Instance != null)
            {
                AchievementManager.Instance.UpdateTotalPointsUI();
            }
        }
        else
        {
            DebugLog("[SessionManager] Could not refresh points - user or DB not available", true);
        }
    }

    public void UpdateUserPoints(int newPoints, bool updateDatabase = true)
    {
        if (!IsLoggedIn || _currentUser == null) return;
        
        int oldPoints = _currentUser.points;
        _currentUser.points = newPoints;
        AddOrUpdateUserInCache(_currentUser);
        
        if (updateDatabase)
        {
            DatabaseManager.Instance?.UpdateUserPoints(_currentUser.id, newPoints);
            DebugLog($"[SessionManager] UpdateUserPoints: {oldPoints} -> {newPoints}");
        }
        
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.UpdateTotalPointsUI();
        }
    }

    public void AddUserPoints(int pointsToAdd)
    {
        if (!IsLoggedIn || _currentUser == null || pointsToAdd <= 0) return;
        
        int newPoints = _currentUser.points + pointsToAdd;
        UpdateUserPoints(newPoints, true);
    }
    #endregion

    #region Utilities & Diagnostics
    public bool IsSessionValidForSync()
    {
        return IsReady && IsLoggedIn && IsSessionInitialized && CurrentUser?.id > 0;
    }

    private void DebugLog(string message, bool warn = false)
    {
        if (!DEV_MODE) return;
        
        if (warn) 
        {
            UnityEngine.Debug.LogWarning(message, this);
        }
        else 
        {
            UnityEngine.Debug.Log(message, this);
        }
    }

    private IEnumerator WaitForOptionalDependencies(float timeout)
    {
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            bool syncReady = SyncManager.Instance != null;
            bool screenTimeReady = ScreenTimeManager.Instance != null;
            bool notificationReady = NotificationManager.Instance != null;
            
            if (syncReady && screenTimeReady && notificationReady) break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void StartAutoSave()
    {
        if (IsLoggedIn) StartCoroutine(AutoSaveScreenTime());
    }

    public IEnumerator CompleteLoginSetup()
    {
        if (!IsLoggedIn) yield break;

        yield return new WaitForSeconds(0.3f);

        if (SyncManager.Instance != null && SyncManager.Instance.IsInitialized)
        {
            bool hasLocalChanges = CheckForLocalChanges();
            if (hasLocalChanges)
            {
                // UPDATED: Use ForceSyncCoroutine instead of PushToServer
                yield return StartCoroutine(
                    SyncManager.Instance.ForceSyncCoroutine((success, msg) =>
                    {
                        if (success)
                        {
                            RefreshUserPointsFromDatabase();
                            
                            if (AchievementManager.Instance != null)
                            {
                                AchievementManager.Instance.UpdateTotalPointsUI();
                            }
                            
                            OnLoginSyncCompleted?.Invoke(true, msg);
                            DebugLog($"[SessionManager] Login sync successful: {msg}");
                        }
                        else
                        {
                            OnLoginSyncCompleted?.Invoke(false, msg);
                            DebugLog($"[SessionManager] Login sync failed: {msg}", true);
                        }
                    })
                );
            }
            else
            {
                OnLoginSyncCompleted?.Invoke(true, "No changes to sync");
                DebugLog("[SessionManager] Login sync: No changes to sync");
            }
        }
        else
        {
            OnLoginSyncCompleted?.Invoke(false, "SyncManager not available");
            DebugLog("[SessionManager] Login sync: SyncManager not available", true);
        }
    }

    public bool IsSessionFullyReady() => IsReady && IsLoggedIn && IsSessionInitialized;
    public bool IsReadyForLoginSync() => IsLoggedIn && IsSessionInitialized && CurrentUser != null && CurrentUser.id > 0;
    public bool IsReadyForPushDuringLogin() => IsLoggedIn && CurrentUser != null && CurrentUser.id > 0;
    public bool IsReadyForDataSync() => IsReady && IsLoggedIn && IsSessionInitialized && CurrentUser != null && CurrentUser.id > 0;

    public bool IsSessionValidForOperation(bool allowDuringLogin = false)
    {
        if (allowDuringLogin)
        {
            return IsReadyForPushDuringLogin();
        }
        else
        {
            return IsReadyForDataSync();
        }
    }

    public bool IsValidForSync(bool isLoginOperation = false)
    {
        if (!IsLoggedIn || CurrentUser == null || CurrentUser.id <= 0)
            return false;

        if (isLoginOperation) return true;
        else return IsReady && IsSessionInitialized;
    }

    public void ForceSetCurrentUser(User user)
    {
        if (user == null) return;
        _currentUser = user;
        IsSessionInitialized = true;
        
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.UpdateTotalPointsUI();
        }
        
        InitializeNotificationManager();
        DebugLog($"[SessionManager] ForceSetCurrentUser: {user.id}, Points: {user.points}");
    }

    public IEnumerator EnsureDataConsistencyOnSceneRestart()
    {
        if (!IsLoggedIn) yield break;
        
        yield return WaitForOptionalDependencies(3f);
        
        RefreshUserPointsFromDatabase();
        
        yield return null;
        
        if (_currentUser != null && _currentUser.points == 0)
        {
            yield return StartCoroutine(EmergencyPointsRecovery());
        }
    }

    public void ForceDataConsistencyCheck()
    {
        if (!IsLoggedIn) return;
        StartCoroutine(EnsureDataConsistencyOnSceneRestart());
    }
    #endregion
}