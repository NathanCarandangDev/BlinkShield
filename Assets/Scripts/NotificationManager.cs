using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if UNITY_ANDROID
using Unity.Notifications.Android;
using UnityEngine.Android;
#endif

public class NotificationManager : MonoBehaviour
{
    private const string CHANNEL_ID = "break_reminder_channel";
    private const string FOREGROUND_CHANNEL_ID = "foreground_service_channel";

    // Screen time tracking for break reminders
    private float _lastBreakScreenTimeCheck = 0f; // Last recorded screen time in minutes
    private float _breakScreenTimeAccumulator = 0f; // Screen time accumulated since last break reminder
    private int _reminderIntervalMinutes = 5; // default 5 minutes
    
    private ScreenTimeManager _screenTimeManager;
    private Coroutine _breakReminderCoroutine;
    private bool _isPaused = false;
    private bool _isForegroundServiceRunning = false;
    private DateTime _lastNotificationTime = DateTime.MinValue;
    private const float NOTIFICATION_COOLDOWN_SECONDS = 30f; // Prevent notification spam

    // Singleton pattern for easy access
    public static NotificationManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Subscribe to session events
            SessionManager.OnSessionStarted += HandleSessionStarted;
            SessionManager.OnSessionEnded += HandleSessionEnded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
#if UNITY_ANDROID
        SetupChannels();
        RequestNotificationPermission();
#endif
        // Find ScreenTimeManager using the new method
        _screenTimeManager = UnityEngine.Object.FindFirstObjectByType<ScreenTimeManager>();
        
        // Load initial interval from DB - but only if user is logged in
        if (SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn)
        {
            _reminderIntervalMinutes = Mathf.Max(1, DatabaseManager.Instance.GetReminderIntervalMinutes());
            StartBreakReminderMonitoring();
            
            // Start service heartbeats when user is logged in
#if UNITY_ANDROID
            StartServiceHeartbeats();
#endif
        }
        else
        {
            _reminderIntervalMinutes = 5; // Default until user logs in
        }
        
        Debug.Log($"[NotificationManager] Reminder interval set to {_reminderIntervalMinutes} minutes (screen time based)");
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        SessionManager.OnSessionStarted -= HandleSessionStarted;
        SessionManager.OnSessionEnded -= HandleSessionEnded;
        
        // Stop coroutine
        if (_breakReminderCoroutine != null)
            StopCoroutine(_breakReminderCoroutine);
        
        // Stop foreground service
#if UNITY_ANDROID
        StopNotificationForegroundService();
        
        // Stop service heartbeats
        StopServiceHeartbeats();
#endif
    }

    private void OnApplicationPause(bool pause)
    {
        _isPaused = pause;
        Debug.Log($"[NotificationManager] App paused: {pause}, User logged in: {IsUserLoggedIn()}");
        
        if (pause && IsUserLoggedIn())
        {
            // App going to background - start foreground service with delay
            StartCoroutine(DelayedServiceStart());
        }
        else if (!pause && IsUserLoggedIn() && _isForegroundServiceRunning)
        {
            // App coming to foreground - stop foreground service to save battery
#if UNITY_ANDROID
            StopNotificationForegroundService();
#endif
        }
    }

    private IEnumerator DelayedServiceStart()
    {
        // Wait a moment to ensure app is fully in background
        yield return new WaitForSeconds(0.5f);
        
        if (IsUserLoggedIn() && _isPaused)
        {
#if UNITY_ANDROID
            StartNotificationForegroundService();
#endif
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && IsUserLoggedIn())
        {
            // App losing focus - start foreground service for background notifications
#if UNITY_ANDROID
            StartNotificationForegroundService();
#endif
        }
    }

    private void HandleSessionStarted(User user)
    {
        // User logged in - initialize notification system
        _reminderIntervalMinutes = Mathf.Max(1, DatabaseManager.Instance.GetReminderIntervalMinutes());
        
        // Start monitoring screen time for break reminders
        StartBreakReminderMonitoring();
        
        // Request battery optimization exemption for better background performance
#if UNITY_ANDROID
        RequestBatteryOptimizationExemption();
        
        // Start service heartbeats
        StartServiceHeartbeats();
#endif
        
        // Check if foreground service should be enabled
        bool serviceEnabled = IsForegroundServiceEnabled();
        if (serviceEnabled && _isPaused)
        {
#if UNITY_ANDROID
            StartNotificationForegroundService();
#endif
        }
        
        Debug.Log($"[NotificationManager] User logged in - break reminders activated with {_reminderIntervalMinutes} minute interval, Foreground service: {serviceEnabled}");
    }

    private void HandleSessionEnded()
    {
        // User logged out - stop notifications
        if (_breakReminderCoroutine != null)
        {
            StopCoroutine(_breakReminderCoroutine);
            _breakReminderCoroutine = null;
        }
        
        CancelAllScheduledNotifications();
#if UNITY_ANDROID
        StopNotificationForegroundService();
        StopServiceHeartbeats();
#endif
        Debug.Log("[NotificationManager] User logged out - break reminders deactivated");
    }

    private bool IsUserLoggedIn()
    {
        return SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn;
    }

    /// <summary>
    /// Start monitoring screen time for break reminders
    /// </summary>
    private void StartBreakReminderMonitoring()
    {
        if (_breakReminderCoroutine != null)
            StopCoroutine(_breakReminderCoroutine);
            
        _breakReminderCoroutine = StartCoroutine(MonitorScreenTimeForBreaks());
    }

    /// <summary>
    /// Monitors screen time and sends break reminders based on accumulated screen time
    /// </summary>
    private IEnumerator MonitorScreenTimeForBreaks()
    {
        Debug.Log("[NotificationManager] Starting screen time monitor for break reminders");
        
        // Wait for ScreenTimeManager to initialize
        yield return new WaitForSeconds(2f);
        
        // Get initial screen time
        if (_screenTimeManager != null)
        {
            _lastBreakScreenTimeCheck = _screenTimeManager.GetTodayScreenTimeInMinutes();
            Debug.Log($"[NotificationManager] Initial screen time for breaks: {_lastBreakScreenTimeCheck} minutes");
        }
        
        while (IsUserLoggedIn())
        {
            // Check more frequently when app is active, less frequently when in background
            float checkInterval = _isPaused ? 60f : 30f; // 60s in background, 30s in foreground
            yield return new WaitForSeconds(checkInterval);
            
            if (_screenTimeManager == null)
            {
                Debug.LogWarning("[NotificationManager] ScreenTimeManager not found, trying to find it...");
                _screenTimeManager = UnityEngine.Object.FindFirstObjectByType<ScreenTimeManager>();
                continue;
            }
            
            // Get current screen time from ScreenTimeManager
            float currentScreenTimeMinutes = _screenTimeManager.GetTodayScreenTimeInMinutes();
            
            // Calculate how much screen time has been added since last check
            float screenTimeDelta = currentScreenTimeMinutes - _lastBreakScreenTimeCheck;
            
            if (screenTimeDelta > 0)
            {
                // Only accumulate when screen time increases (user is actually using screen)
                _breakScreenTimeAccumulator += screenTimeDelta;
                _lastBreakScreenTimeCheck = currentScreenTimeMinutes;
                
                Debug.Log($"[NotificationManager] Screen time: {currentScreenTimeMinutes:F0}min (+{screenTimeDelta:F0}min), " +
                         $"Accumulated: {_breakScreenTimeAccumulator:F0}min, Target: {_reminderIntervalMinutes}min");
                
                // Check if enough screen time has accumulated for a break reminder
                if (_breakScreenTimeAccumulator >= _reminderIntervalMinutes)
                {
                    SendBreakReminder();
                    _breakScreenTimeAccumulator = 0f; // Reset for next interval
                }
            }
            else if (screenTimeDelta < 0)
            {
                // Screen time reset (new day) - reset tracking
                _breakScreenTimeAccumulator = 0f;
                _lastBreakScreenTimeCheck = currentScreenTimeMinutes;
            }
        }
    }

    /// <summary>
    /// Dynamically update the reminder interval.
    /// Call this from SettingsManager when the user changes the interval.
    /// </summary>
    public void UpdateReminderInterval(int minutes)
    {
        // Only update if user is logged in
        if (!IsUserLoggedIn())
        {
            Debug.LogWarning("[NotificationManager] Cannot update interval - no user logged in");
            return;
        }

        _reminderIntervalMinutes = Mathf.Max(1, minutes); // enforce minimum of 1 minute
        _breakScreenTimeAccumulator = 0f; // Reset accumulator
        
        Debug.Log($"[NotificationManager] Reminder interval updated to {_reminderIntervalMinutes} minutes (screen time)");
    }

#if UNITY_ANDROID
    private void SetupChannels()
    {
        // Setup main notification channel
        try
        {
            var existingChannel = AndroidNotificationCenter.GetNotificationChannel(CHANNEL_ID);
            
            // Check if channel is empty (not created yet)
            if (string.IsNullOrEmpty(existingChannel.Id))
            {
                var channel = new AndroidNotificationChannel()
                {
                    Id = CHANNEL_ID,
                    Name = "Break Reminders",
                    Importance = Importance.High,
                    Description = "Reminds user to take breaks",
                    CanShowBadge = true,
                    EnableLights = true,
                    EnableVibration = true,
                    LockScreenVisibility = LockScreenVisibility.Public
                };
                AndroidNotificationCenter.RegisterNotificationChannel(channel);
                Debug.Log("[NotificationManager] Main notification channel created");
            }
            else
            {
                Debug.Log("[NotificationManager] Main notification channel already exists");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NotificationManager] Error creating main channel: {e.Message}");
        }

        // Setup foreground service channel
        try
        {
            var existingForegroundChannel = AndroidNotificationCenter.GetNotificationChannel(FOREGROUND_CHANNEL_ID);
            
            if (string.IsNullOrEmpty(existingForegroundChannel.Id))
            {
                var foregroundChannel = new AndroidNotificationChannel()
                {
                    Id = FOREGROUND_CHANNEL_ID,
                    Name = "Background Service",
                    Importance = Importance.Low,
                    Description = "Keeps break reminders active",
                    LockScreenVisibility = LockScreenVisibility.Secret
                };
                AndroidNotificationCenter.RegisterNotificationChannel(foregroundChannel);
                Debug.Log("[NotificationManager] Foreground service channel created");
            }
            else
            {
                Debug.Log("[NotificationManager] Foreground service channel already exists");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NotificationManager] Error creating foreground channel: {e.Message}");
        }
    }

    private void RequestNotificationPermission()
    {
        // For Android 13+, we need to request notification permission
        try
        {
            #if UNITY_2022_1_OR_NEWER || UNITY_2021_3_OR_NEWER
            if (Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
            {
                Debug.Log("[NotificationManager] Notification permission already granted");
            }
            else
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += (permission) =>
                {
                    Debug.Log("[NotificationManager] Notification permission granted");
                };
                callbacks.PermissionDenied += (permission) =>
                {
                    Debug.LogWarning("[NotificationManager] Notification permission denied - notifications may not work");
                };
                
                Permission.RequestUserPermission("android.permission.POST_NOTIFICATIONS", callbacks);
            }
            #else
            Debug.Log("[NotificationManager] Skipping permission request (older Unity version)");
            #endif
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Error requesting notification permission: {e.Message}");
        }
    }
#endif

    private void SendBreakReminder()
    {
        // Double-check that user is still logged in before sending notification
        if (!IsUserLoggedIn()) return;

        // Prevent notification spam
        if ((DateTime.Now - _lastNotificationTime).TotalSeconds < NOTIFICATION_COOLDOWN_SECONDS)
        {
            Debug.Log($"[NotificationManager] Skipping break reminder - too soon after last notification");
            return;
        }

        string msg = $"You've been using the screen for {_reminderIntervalMinutes} minutes. Take a break and look 20ft away for 20sec!";
        ScheduleNotification("Break Reminder", msg, 0);
        _lastNotificationTime = DateTime.Now;
        Debug.Log($"[NotificationManager] Break reminder sent after {_reminderIntervalMinutes} minutes of screen time");
    }

    public void ScheduleNotification(string title, string message, int delaySeconds)
    {
        // Check if user is logged in before scheduling any notification
        if (!IsUserLoggedIn())
        {
            Debug.LogWarning($"[NotificationManager] Cannot schedule notification - no user logged in");
            return;
        }

#if UNITY_ANDROID
        var notification = new AndroidNotification()
        {
            Title = title,
            Text = message,
            FireTime = DateTime.Now.AddSeconds(delaySeconds),
            ShouldAutoCancel = true
        };
        
        // Add custom data for tracking
        notification.IntentData = "break_reminder|" + DateTime.Now.Ticks.ToString();
        
        // For immediate notifications, use exact time
        if (delaySeconds <= 0)
        {
            notification.FireTime = DateTime.Now;
            notification.Number = 1; // Set badge number
        }
        
        AndroidNotificationCenter.SendNotification(notification, CHANNEL_ID);
        
        // When scheduling future notifications, ensure foreground service is running
        if (delaySeconds > 0 && !_isForegroundServiceRunning)
        {
            StartNotificationForegroundService();
        }
#else
        Debug.Log($"[NotificationManager] Notification scheduled (Editor/Non-Android): {title} - {message}");
#endif
    }

    // ==================== NEW HELPER METHODS ====================

    /// <summary>
    /// Check if foreground service is running (calls Java method)
    /// </summary>
    public bool IsForegroundServiceRunning()
    {
#if UNITY_ANDROID
        return CheckForegroundServiceStatus();
#else
        return false;
#endif
    }

    /// <summary>
    /// Check if foreground service is enabled in preferences
    /// </summary>
    public bool IsForegroundServiceEnabled()
    {
#if UNITY_ANDROID
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var serviceRestarter = new AndroidJavaClass("com.blinkshield.screentimetracker.ServiceRestarter"))
            {
                // Use the method from ServiceRestarter
                bool isEnabled = serviceRestarter.CallStatic<bool>("isForegroundServiceEnabled", currentActivity);
                return isEnabled;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Failed to get foreground service preference: {e.Message}");
            return true; // Default to enabled
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Set foreground service preference
    /// </summary>
    public void SetForegroundServicePreference(bool enabled)
    {
#if UNITY_ANDROID
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var serviceRestarter = new AndroidJavaClass("com.blinkshield.screentimetracker.ServiceRestarter"))
            {
                serviceRestarter.CallStatic("setForegroundServiceEnabled", currentActivity, enabled);
                Debug.Log($"[NotificationManager] Foreground service preference set to: {enabled}");
                
                // Update local state
                if (enabled && _isPaused)
                {
                    StartNotificationForegroundService();
                }
                else if (!enabled && _isForegroundServiceRunning)
                {
                    StopNotificationForegroundService();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Failed to set foreground service preference: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// Toggle foreground service on/off
    /// </summary>
    public void ToggleForegroundService(bool enable)
    {
        SetForegroundServicePreference(enable);
    }

    /// <summary>
    /// Start service heartbeats (periodic service restarts)
    /// </summary>
    public void StartServiceHeartbeats()
    {
#if UNITY_ANDROID
        TriggerServiceRestart();
        Debug.Log("[NotificationManager] Service heartbeats started");
#endif
    }

    /// <summary>
    /// Stop service heartbeats
    /// </summary>
    public void StopServiceHeartbeats()
    {
#if UNITY_ANDROID
        CancelHeartbeats();
        Debug.Log("[NotificationManager] Service heartbeats stopped");
#endif
    }

    // ==================== PRIVATE HELPER METHODS ====================

#if UNITY_ANDROID
    private bool CheckForegroundServiceStatus()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                // Call the static method on the Java class
                using (var notificationService = new AndroidJavaClass("com.blinkshield.screentimetracker.NotificationForegroundService"))
                {
                    bool isRunning = notificationService.CallStatic<bool>("isServiceRunning", currentActivity);
                    return isRunning;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Error checking service status: {e.Message}");
            return false;
        }
    }

    private void TriggerServiceRestart()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var serviceRestarter = new AndroidJavaClass("com.blinkshield.screentimetracker.ServiceRestarter"))
            {
                serviceRestarter.CallStatic("triggerServiceRestart", currentActivity);
                Debug.Log("[NotificationManager] Service restart triggered");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Failed to trigger restart: {e.Message}");
        }
    }

    private void CancelHeartbeats()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var serviceRestarter = new AndroidJavaClass("com.blinkshield.screentimetracker.ServiceRestarter"))
            {
                serviceRestarter.CallStatic("cancelHeartbeats", currentActivity);
                Debug.Log("[NotificationManager] Heartbeats cancelled");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Failed to cancel heartbeats: {e.Message}");
        }
    }

    private void RequestBatteryOptimizationExemption()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var powerManager = currentActivity.Call<AndroidJavaObject>("getSystemService", "power");
                var packageName = currentActivity.Call<string>("getPackageName");
                var isIgnoringBatteryOptimizations = powerManager.Call<bool>("isIgnoringBatteryOptimizations", packageName);
                
                if (!isIgnoringBatteryOptimizations)
                {
                    Debug.Log("[NotificationManager] Requesting battery optimization exemption");
                    
                    var intent = new AndroidJavaObject("android.content.Intent", 
                        "android.settings.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS");
                        
                    var uriClass = new AndroidJavaClass("android.net.Uri");
                    var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + packageName);
                    intent.Call<AndroidJavaObject>("setData", uri);
                    
                    currentActivity.Call("startActivity", intent);
                }
                else
                {
                    Debug.Log("[NotificationManager] Already has battery optimization exemption");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Failed to request battery optimization: {e.Message}");
        }
    }

    private void StartNotificationForegroundService()
    {
        if (_isForegroundServiceRunning || !IsUserLoggedIn()) return;
        
        Debug.Log("[NotificationManager] Attempting to start foreground service...");
        
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var serviceIntent = new AndroidJavaObject("android.content.Intent", 
                    currentActivity, 
                    new AndroidJavaClass("com.blinkshield.screentimetracker.NotificationForegroundService"));
                
                // Add flags to ensure service starts properly
                serviceIntent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                
                // Start as foreground service (required for Android 8.0+)
                if (GetAndroidVersion() >= 26)
                {
                    currentActivity.Call("startForegroundService", serviceIntent);
                    Debug.Log("[NotificationManager] Started foreground service (API 26+)");
                }
                else
                {
                    currentActivity.Call("startService", serviceIntent);
                    Debug.Log("[NotificationManager] Started service (pre-API 26)");
                }
                
                _isForegroundServiceRunning = true;
                
                // Verify service started
                StartCoroutine(VerifyServiceStarted());
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Failed to start foreground service: {e.Message}");
        }
    }

    private IEnumerator VerifyServiceStarted()
    {
        yield return new WaitForSeconds(1f);
        
        bool isRunning = CheckForegroundServiceStatus();
        Debug.Log($"[NotificationManager] Service verification: Java says running={isRunning}, C# says running={_isForegroundServiceRunning}");
        
        if (!isRunning && _isForegroundServiceRunning)
        {
            Debug.LogWarning("[NotificationManager] Service flag mismatch - resetting");
            _isForegroundServiceRunning = false;
        }
    }

    private void StopNotificationForegroundService()
    {
        if (!_isForegroundServiceRunning) return;
        
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var serviceIntent = new AndroidJavaObject("android.content.Intent", 
                    currentActivity, 
                    new AndroidJavaClass("com.blinkshield.screentimetracker.NotificationForegroundService"));
                
                currentActivity.Call("stopService", serviceIntent);
                _isForegroundServiceRunning = false;
                Debug.Log("[NotificationManager] Notification foreground service stopped");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NotificationManager] Failed to stop foreground service: {e.Message}");
        }
    }

    private int GetAndroidVersion()
    {
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                return version.GetStatic<int>("SDK_INT");
            }
        }
        catch
        {
            return 21; // Default to Lollipop if can't determine
        }
    }
#endif

    /// <summary>
    /// Shows a warning notification (immediate)
    /// </summary>
    public void ShowWarning(string title, string message, float durationSeconds = 5f)
    {
        if (!IsUserLoggedIn())
        {
            Debug.LogWarning($"[NotificationManager] Cannot show warning - no user logged in");
            return;
        }

        ScheduleNotification(title, message, 0);
        Debug.Log($"[NotificationManager] Warning shown: {title} - {message}");
    }

    /// <summary>
    /// Shows an info notification (immediate)
    /// </summary>
    public void ShowInfo(string title, string message, float durationSeconds = 5f)
    {
        if (!IsUserLoggedIn())
        {
            Debug.LogWarning($"[NotificationManager] Cannot show info - no user logged in");
            return;
        }

        ScheduleNotification(title, message, 0);
        Debug.Log($"[NotificationManager] Info shown: {title} - {message}");
    }

    public void ScheduleEyeSurveyNotification(int intervalSeconds)
    {
        if (!IsUserLoggedIn()) return;
        
        string title = "Eye Survey";
        string message = "Time for your eye survey! Please answer the quick questions.";
        ScheduleNotification(title, message, intervalSeconds);
    }

    public void CancelAllScheduledNotifications()
    {
#if UNITY_ANDROID
        AndroidNotificationCenter.CancelAllScheduledNotifications();
        Debug.Log("[NotificationManager] All scheduled notifications cancelled");
#endif
    }

    public void TestNotification()
    {
        if (!IsUserLoggedIn())
        {
            Debug.Log("[TEST] Cannot send test notification - user not logged in");
            return;
        }
        
        Debug.Log("[TEST] Test notification scheduled after 5s");
        ScheduleNotification("Test Title", "This is a test notification!", 5);
    }
    
    #region Debug Methods
    [ContextMenu("Debug: Check Break Reminder Status")]
    public void DebugCheckBreakReminderStatus()
    {
        if (_screenTimeManager != null)
        {
            float currentScreenTime = _screenTimeManager.GetTodayScreenTimeInMinutes();
            Debug.Log($"[DEBUG] Current screen time: {currentScreenTime:F0}min");
            Debug.Log($"[DEBUG] Last check: {_lastBreakScreenTimeCheck:F0}min");
            Debug.Log($"[DEBUG] Accumulated: {_breakScreenTimeAccumulator:F0}min");
            Debug.Log($"[DEBUG] Target interval: {_reminderIntervalMinutes}min");
            Debug.Log($"[DEBUG] Remaining: {Mathf.Max(0, _reminderIntervalMinutes - _breakScreenTimeAccumulator):F0}min");
            Debug.Log($"[DEBUG] Foreground service running: {_isForegroundServiceRunning}");
            Debug.Log($"[DEBUG] App paused: {_isPaused}");
            Debug.Log($"[DEBUG] User logged in: {IsUserLoggedIn()}");
        }
        else
        {
            Debug.Log("[DEBUG] ScreenTimeManager not found!");
        }
    }
    
    [ContextMenu("Debug: Start Foreground Service")]
    public void DebugStartForegroundService()
    {
#if UNITY_ANDROID
        StartNotificationForegroundService();
#endif
    }
    
    [ContextMenu("Debug: Stop Foreground Service")]
    public void DebugStopForegroundService()
    {
#if UNITY_ANDROID
        StopNotificationForegroundService();
#endif
    }
    
    [ContextMenu("Debug: Request Battery Optimization")]
    public void DebugRequestBatteryOptimization()
    {
#if UNITY_ANDROID
        RequestBatteryOptimizationExemption();
#endif
    }
    
    [ContextMenu("Debug: Check Service Status")]
    public void DebugCheckServiceStatus()
    {
#if UNITY_ANDROID
        bool isRunning = CheckForegroundServiceStatus();
        Debug.Log($"[DEBUG] Foreground service running: Java={isRunning}, C#={_isForegroundServiceRunning}");
        
        if (isRunning && !_isForegroundServiceRunning)
        {
            Debug.Log("[DEBUG] Service mismatch - updating C# flag");
            _isForegroundServiceRunning = true;
        }
#endif
    }
    
    [ContextMenu("Debug: Trigger Service Restart")]
    public void DebugTriggerServiceRestart()
    {
#if UNITY_ANDROID
        TriggerServiceRestart();
#endif
    }
    
    [ContextMenu("Debug: Cancel Heartbeats")]
    public void DebugCancelHeartbeats()
    {
#if UNITY_ANDROID
        CancelHeartbeats();
#endif
    }
    
    [ContextMenu("Debug: Check Foreground Service Preference")]
    public void DebugCheckForegroundServicePreference()
    {
        bool isEnabled = IsForegroundServiceEnabled();
        Debug.Log($"[DEBUG] Foreground service preference: {isEnabled}");
    }
    #endregion
}