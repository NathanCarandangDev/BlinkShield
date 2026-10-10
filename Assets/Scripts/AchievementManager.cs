using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;

/// <summary>
/// Manages achievements: checking requirements, claiming, and updating points.
/// Fully delegates storage and point updates to DatabaseManager and syncs SessionManager.
/// Compatible with AchievementDisplay and UserAchievementsManager.
/// </summary>
public class AchievementManager : MonoBehaviour
{
    private static AchievementManager _instance;
    public static AchievementManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            var go = new GameObject(nameof(AchievementManager));
            _instance = go.AddComponent<AchievementManager>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    [Header("UI References")]
    public TMP_Text totalPointsText;

    public event Action<int> OnPointsUpdated;
    public event Action<int> OnAchievementClaimed;

    // FIX: Use method instead of static property to avoid caching null values
    private User GetCurrentUser() => SessionManager.Instance?.CurrentUser;

    private bool _isInitialized = false;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        // FIX: Subscribe to session events instead of using coroutine
        SessionManager.OnSessionStarted += OnSessionStarted;
        SessionManager.OnSessionEnded += OnSessionEnded;

        Debug.Log($"[AchievementManager] Awake() - SessionManager: {SessionManager.Instance != null}");
    }

    #region Session Event Handlers
    private void OnSessionStarted(User user)
    {
        Debug.Log($"[AchievementManager] Session started for user {user?.id ?? -1}");
        
        if (user != null)
        {
            StartCoroutine(InitializeForUser(user));
        }
        else
        {
            Debug.LogWarning("[AchievementManager] Session started with null user");
        }
    }

    private void OnSessionEnded()
    {
        Debug.Log("[AchievementManager] Session ended - cleaning up");
        _isInitialized = false;
        
        // Clear UI if needed
        if (totalPointsText != null)
            totalPointsText.text = "Total Points: 0";
    }

    private IEnumerator InitializeForUser(User user)
    {
        Debug.Log($"[AchievementManager] Initializing for user {user.id}");
        
        // Wait for DatabaseManager to be ready
        while (DatabaseManager.Instance == null)
        {
            Debug.Log("[AchievementManager] Waiting for DatabaseManager...");
            yield return new WaitForSeconds(0.1f);
        }

        // Wait a frame for everything to settle
        yield return null;

        // Use SessionManager's refresh method instead of our own
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        
        // Update UI
        UpdateTotalPointsUI();
        
        _isInitialized = true;
        Debug.Log($"[AchievementManager] Initialization complete for user {user.id}");
    }

    private void OnDestroy()
    {
        // FIX: Clean up event subscriptions
        SessionManager.OnSessionStarted -= OnSessionStarted;
        SessionManager.OnSessionEnded -= OnSessionEnded;
    }
    #endregion

    #region Achievements Data Access

    public List<Achievement> GetAllAchievements() =>
        DatabaseManager.Instance?.GetAll<Achievement>() ?? new List<Achievement>();

    // FIX: Added forceRefresh parameter to handle cache issues
    public HashSet<int> GetUnlockedIds(int userId, bool forceRefresh = false)
    {
        var unlockedIds = DatabaseManager.Instance?.GetUnlockedAchievementIds(userId, forceRefresh) ?? new HashSet<int>();
        Debug.Log($"[AchievementManager] GetUnlockedIds for user {userId}: {unlockedIds.Count} achievements (forceRefresh: {forceRefresh})");
        
        // Debug specific achievements to verify data
        if (unlockedIds.Count > 0)
        {
            Debug.Log($"[AchievementManager] Unlocked IDs sample (first 5): {string.Join(", ", unlockedIds.Take(5))}");
        }
        
        return unlockedIds;
    }

    public bool IsRequirementMet(Achievement ach)
    {
        var user = GetCurrentUser();
        if (ach == null || string.IsNullOrEmpty(ach.requirement_type) || user == null)
            return false;

        bool requirementMet = ach.requirement_type.ToLower() switch
        {
            "log_in" => true,
            "breaks_taken" => ScreenTimeManager.Instance?.GetBreaksTaken() >= ach.requirement_value,
            "total_points" => GetCurrentUserPoints() >= ach.requirement_value,
            "achievements_unlocked" => GetUnlockedIds(user.id).Count >= ach.requirement_value,
            "consecutive_days" => ScreenTimeManager.Instance?.GetConsecutiveDaysPlayed() >= ach.requirement_value,
            _ => WarnRequirement(ach.requirement_type)
        };

        Debug.Log($"[AchievementManager] IsRequirementMet for '{ach.title}': {requirementMet} (Type: {ach.requirement_type}, Required: {ach.requirement_value})");
        return requirementMet;
    }

    public (bool met, int current, int required) GetRequirementProgress(Achievement ach)
    {
        var user = GetCurrentUser();
        if (ach == null || user == null) return (false, 0, 0);

        return ach.requirement_type.ToLower() switch
        {
            "log_in" => (true, 1, 1),
            "breaks_taken" => (
                ScreenTimeManager.Instance?.GetBreaksTaken() >= ach.requirement_value,
                ScreenTimeManager.Instance?.GetBreaksTaken() ?? 0,
                ach.requirement_value
            ),
            "total_points" => (
                GetCurrentUserPoints() >= ach.requirement_value,
                GetCurrentUserPoints(),
                ach.requirement_value
            ),
            "achievements_unlocked" => (
                GetUnlockedIds(user.id).Count >= ach.requirement_value,
                GetUnlockedIds(user.id).Count,
                ach.requirement_value
            ),
            "consecutive_days" => (
                ScreenTimeManager.Instance?.GetConsecutiveDaysPlayed() >= ach.requirement_value,
                ScreenTimeManager.Instance?.GetConsecutiveDaysPlayed() ?? 0,
                ach.requirement_value
            ),
            _ => (false, 0, ach.requirement_value)
        };
    }

    private static bool WarnRequirement(string type)
    {
        Debug.LogWarning($"[AchievementManager] Unknown requirement type: {type}");
        return false;
    }

    #endregion

    #region Claiming Achievements - IMPROVED WITH PROPER SORTING

    public void ClaimAchievement(int achievementId, Action<int> onPointsGranted = null)
    {
        var user = GetCurrentUser();
        if (user == null)
        {
            Debug.LogWarning("[AchievementManager] No current user session");
            onPointsGranted?.Invoke(0);
            return;
        }

        Debug.Log($"[AchievementManager] === START CLAIM PROCESS ===");
        Debug.Log($"[AchievementManager] Claiming achievement {achievementId} for user {user.id}");
        
        // Get initial state
        int pointsBefore = GetCurrentUserPoints();
        Debug.Log($"[AchievementManager] Initial points: {pointsBefore}");

        var ach = GetAllAchievements().FirstOrDefault(a => a.achievement_id == achievementId);
        if (ach == null)
        {
            Debug.LogWarning($"[AchievementManager] Achievement {achievementId} not found");
            onPointsGranted?.Invoke(0);
            return;
        }

        Debug.Log($"[AchievementManager] Found achievement: {ach.title}, Points: {ach.points}");

        // Check if already claimed - with more detailed logging
        bool alreadyClaimed = GetUnlockedIds(user.id).Contains(achievementId);
        if (alreadyClaimed)
        {
            Debug.LogWarning($"[AchievementManager] Achievement {achievementId} already claimed in database!");
            onPointsGranted?.Invoke(0);
            return;
        }

        if (!IsRequirementMet(ach))
        {
            Debug.LogWarning($"[AchievementManager] Requirement not met for achievement {achievementId}");
            onPointsGranted?.Invoke(0);
            return;
        }

        // FIX: Clear cache BEFORE claiming to ensure fresh data
        DatabaseManager.Instance.ClearUnlockedAchievementsCache(user.id);

        // FIX: Single database call - let DatabaseManager handle everything
        Debug.Log($"[AchievementManager] Calling DatabaseManager.ClaimUserAchievement...");
        int pointsAwarded = DatabaseManager.Instance.ClaimUserAchievement(user.id, achievementId);
        Debug.Log($"[AchievementManager] Database returned pointsAwarded: {pointsAwarded}");

        // FIX: Force immediate refresh of user data
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        
        // Get points after claiming
        int pointsAfter = GetCurrentUserPoints();
        int actualPointsAwarded = pointsAfter - pointsBefore;

        Debug.Log($"[AchievementManager] Points after claim: {pointsAfter}");
        Debug.Log($"[AchievementManager] Expected award: {pointsAwarded}, Actual award: {actualPointsAwarded}");

        // FIX: Use force refresh to get fresh data from database
        bool isNowClaimed = GetUnlockedIds(user.id, forceRefresh: true).Contains(achievementId);
        Debug.Log($"[AchievementManager] Achievement now claimed in database: {isNowClaimed}");

        if (!isNowClaimed)
        {
            Debug.LogError($"[AchievementManager] CRITICAL: Achievement {achievementId} was not marked as claimed after database operation!");
            
            // FIX: Emergency fallback - check database directly
            bool dbCheck = DatabaseManager.Instance.IsAchievementClaimed(user.id, achievementId);
            Debug.LogError($"[AchievementManager] DIRECT DB CHECK: {dbCheck}");
            
            if (dbCheck)
            {
                Debug.LogError($"[AchievementManager] CACHE INCONSISTENCY: Database says claimed but cache doesn't reflect it!");
                // Force cache refresh and try again
                DatabaseManager.Instance.ClearUnlockedAchievementsCache(user.id);
                isNowClaimed = GetUnlockedIds(user.id, forceRefresh: true).Contains(achievementId);
                Debug.LogError($"[AchievementManager] After cache refresh: {isNowClaimed}");
            }
        }

        // FIX: Simplified mismatch handling - just recalculate if needed
        if (actualPointsAwarded != pointsAwarded && pointsAwarded > 0)
        {
            Debug.LogWarning($"[AchievementManager] Points mismatch! Recalculating all points...");
            RecalculateUserPoints();
            pointsAfter = GetCurrentUserPoints();
            actualPointsAwarded = pointsAfter - pointsBefore;
            Debug.Log($"[AchievementManager] After recalculation - Points: {pointsAfter}, Actual award: {actualPointsAwarded}");
        }

        // FIX: Update UI and notify - IMPORTANT: Fire events after database is updated
        OnPointsUpdated?.Invoke(pointsAfter);
        OnAchievementClaimed?.Invoke(achievementId);
        UpdateTotalPointsUI();

        // FIX: Trigger display refresh with proper sorting
        StartCoroutine(RefreshAchievementsDisplayWithSorting());

        Debug.Log($"[AchievementManager] Achievement '{ach.title}' claimed! +{pointsAwarded} points (Total: {pointsAfter})");
        Debug.Log($"[AchievementManager] === END CLAIM PROCESS ===");
        
        onPointsGranted?.Invoke(pointsAwarded);
    }

    // NEW: Method to refresh achievements display with proper sorting
    private IEnumerator RefreshAchievementsDisplayWithSorting()
    {
        yield return new WaitForSeconds(0.1f); // Small delay to ensure database is updated
        
        // Force refresh the achievements data
        ForceRefreshAchievementsData();
        
        // FIX: Updated to use FindObjectsByType instead of deprecated FindObjectsOfType
        var achievementDisplays = FindObjectsByType<AchievementDisplay>(FindObjectsSortMode.None);
        foreach (var display in achievementDisplays)
        {
            if (display.isActiveAndEnabled)
            {
                display.ForceRefreshWithSorting();
            }
        }
        
        Debug.Log($"[AchievementManager] Refreshed {achievementDisplays.Length} achievement displays with proper sorting");
    }

    #endregion

    #region Points Management - SIMPLIFIED

    public void RecalculateUserPoints()
    {
        var user = GetCurrentUser();
        if (user == null) return;

        Debug.Log($"[AchievementManager] === RECALCULATING POINTS ===");
        
        var unlockedIds = GetUnlockedIds(user.id, forceRefresh: true); // FIX: Force refresh
        int totalPoints = GetAllAchievements()
            .Where(a => unlockedIds.Contains(a.achievement_id))
            .Sum(a => a.points);

        Debug.Log($"[AchievementManager] Unlocked achievements: {unlockedIds.Count}, Total points: {totalPoints}");

        DatabaseManager.Instance.UpdateUserPoints(user.id, totalPoints);

        // FIX: Use SessionManager's method instead of direct assignment
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        
        UpdateTotalPointsUI();
        OnPointsUpdated?.Invoke(totalPoints);

        Debug.Log($"[AchievementManager] Recalculated total points: {totalPoints}");
        Debug.Log($"[AchievementManager] === END RECALCULATION ===");
    }

    public void UpdateTotalPointsUI()
    {
        if (totalPointsText != null)
        {
            int points = GetCurrentUserPoints();
            totalPointsText.text = $"Total Points: {points}";
            Debug.Log($"[AchievementManager] UI Updated: {points} points");
        }
    }

    public int GetCurrentUserPoints()
    {
        var user = GetCurrentUser();
        if (user == null) return 0;

        // FIX: Always get points from database as source of truth
        int pointsFromDb = DatabaseManager.Instance?.GetUserPoints(user.id) ?? user.points;
        
        // FIX: Use SessionManager's sync method instead of direct assignment
        if (SessionManager.Instance?.CurrentUser != null && SessionManager.Instance.CurrentUser.points != pointsFromDb)
        {
            SessionManager.Instance.RefreshUserPointsFromDatabase();
        }
        
        return pointsFromDb;
    }

    #endregion

    #region User Achievement Info - WITH SORTING SUPPORT

    // NEW: Method to get achievements sorted by status (unclaimed first, then claimed)
    public List<(Achievement achievement, bool claimed)> GetAchievementsForUserSorted(int userId)
    {
        var achievements = GetAchievementsForUser(userId);
        
        // Sort: unclaimed achievements first, then claimed achievements
        // Within each group, sort by achievement ID or points
        return achievements
            .OrderBy(a => a.claimed) // false (0) first, then true (1)
            .ThenByDescending(a => a.achievement.points) // Higher points first within each group
            .ThenBy(a => a.achievement.achievement_id) // Then by achievement ID
            .ToList();
    }

    public List<(Achievement achievement, bool claimed)> GetAchievementsForUser(int userId)
    {
        Debug.Log($"[AchievementManager] GetAchievementsForUser for user {userId}");
        
        // SIMPLIFIED: Always get fresh data directly
        var all = GetAllAchievements();
        var unlockedIds = GetUnlockedIds(userId, forceRefresh: true); // FIX: Force refresh
        
        Debug.Log($"[AchievementManager] Returning {all.Count} achievements, {unlockedIds.Count} unlocked");
        
        var result = all.Select(a => (a, unlockedIds.Contains(a.achievement_id))).ToList();
        
        // Quick verification
        if (result.Count != all.Count)
        {
            Debug.LogError($"[AchievementManager] ACHIEVEMENT COUNT MISMATCH! Input: {all.Count}, Output: {result.Count}");
        }
        
        return result;
    }

    public string GetAchievementStatus(int achievementId)
    {
        var user = GetCurrentUser();
        bool isClaimed = GetUnlockedIds(user?.id ?? 0, forceRefresh: false).Contains(achievementId); // FIX: Optional refresh
        Debug.Log($"[AchievementManager] GetAchievementStatus {achievementId}: {isClaimed}");
        return isClaimed ? "Claimed ✅" : "Claim";
    }

    public void CheckForNewUnlocks()
    {
        var user = GetCurrentUser();
        if (user == null) return;

        foreach (var (achievement, claimed) in GetAchievementsForUser(user.id))
        {
            if (!claimed && IsRequirementMet(achievement))
                Debug.Log($"[AchievementManager] Achievement '{achievement.title}' is now available to claim!");
        }
    }

    public (int totalPoints, int unlockedCount, int totalCount) GetUserProgressStats()
    {
        var user = GetCurrentUser();
        if (user == null) return (0, 0, 0);

        int points = GetCurrentUserPoints();
        var unlockedIds = GetUnlockedIds(user.id, forceRefresh: false); // FIX: Optional refresh
        return (points, unlockedIds.Count, GetAllAchievements().Count);
    }

    public void ResetUserAchievements(int userId)
    {
        DatabaseManager.Instance?.ResetUserAchievements(userId);
        DatabaseManager.Instance?.ClearUnlockedAchievementsCache(userId); // FIX: Clear cache
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        UpdateTotalPointsUI();
        OnPointsUpdated?.Invoke(GetCurrentUserPoints());

        Debug.Log($"[AchievementManager] Reset achievements and points for user {userId}");
    }

    #endregion

    #region Debug Methods

    public void DebugPointsFlow(int achievementId)
    {
        var user = GetCurrentUser();
        if (user == null) return;
        
        int pointsBefore = GetCurrentUserPoints();
        Debug.Log($"[Points Flow] Before claim: {pointsBefore}");
        
        ClaimAchievement(achievementId, (pointsAwarded) => {
            int pointsAfter = GetCurrentUserPoints();
            Debug.Log($"[Points Flow] After claim: Awarded={pointsAwarded}, Total={pointsAfter}, Difference={pointsAfter - pointsBefore}");
        });
    }

    public void ForcePointsSync()
    {
        var user = GetCurrentUser();
        if (user == null) return;
        
        DatabaseManager.Instance?.ClearUnlockedAchievementsCache(user.id); // FIX: Clear cache
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        UpdateTotalPointsUI();
        
        Debug.Log($"[AchievementManager] Force sync completed");
    }

    public void DebugAchievementState(int achievementId)
    {
        var user = GetCurrentUser();
        if (user == null) return;
        
        Debug.Log($"[AchievementManager] === DEBUG ACHIEVEMENT {achievementId} ===");
        Debug.Log($"[AchievementManager] User ID: {user.id}, Current Points: {GetCurrentUserPoints()}");
        
        var ach = GetAllAchievements().FirstOrDefault(a => a.achievement_id == achievementId);
        if (ach != null)
        {
            Debug.Log($"[AchievementManager] Achievement {achievementId}: {ach.title}, Worth: {ach.points} points");
            
            bool isClaimed = GetUnlockedIds(user.id, forceRefresh: true).Contains(achievementId); // FIX: Force refresh
            Debug.Log($"[AchievementManager] Is Claimed: {isClaimed}");
            
            bool requirementMet = IsRequirementMet(ach);
            Debug.Log($"[AchievementManager] Requirement Met: {requirementMet}");
            
            var progress = GetRequirementProgress(ach);
            Debug.Log($"[AchievementManager] Progress: {progress.current}/{progress.required}");
            
            // Check database directly
            bool dbClaimed = DatabaseManager.Instance.IsAchievementClaimed(user.id, achievementId);
            Debug.Log($"[AchievementManager] DB Check - Claimed: {dbClaimed}");
            
            // Check if there's a discrepancy
            if (isClaimed != dbClaimed)
            {
                Debug.LogError($"[AchievementManager] CLAIM STATUS MISMATCH! Cached: {isClaimed}, Database: {dbClaimed}");
                
                // FIX: Auto-clear cache if mismatch detected
                DatabaseManager.Instance.ClearUnlockedAchievementsCache(user.id);
                bool afterRefresh = GetUnlockedIds(user.id, forceRefresh: true).Contains(achievementId);
                Debug.LogError($"[AchievementManager] After cache clear: {afterRefresh}");
            }
        }
        else
        {
            Debug.LogWarning($"[AchievementManager] Achievement {achievementId} not found in database");
        }
        
        Debug.Log($"[AchievementManager] === END DEBUG ACHIEVEMENT {achievementId} ===");
    }

    public void DebugAllAchievements()
    {
        var user = GetCurrentUser();
        if (user == null) return;
        
        Debug.Log($"[AchievementManager] === DEBUG ALL ACHIEVEMENTS ===");
        Debug.Log($"[AchievementManager] User ID: {user.id}, Current Points: {GetCurrentUserPoints()}");
        
        var allAchievements = GetAllAchievements();
        var unlockedIds = GetUnlockedIds(user.id, forceRefresh: true); // FIX: Force refresh
        
        Debug.Log($"[AchievementManager] Total achievements: {allAchievements.Count}");
        Debug.Log($"[AchievementManager] Unlocked achievements: {unlockedIds.Count}");
        
        foreach (var ach in allAchievements)
        {
            bool isClaimed = unlockedIds.Contains(ach.achievement_id);
            bool requirementMet = IsRequirementMet(ach);
            
            Debug.Log($"[AchievementManager] {ach.achievement_id}: {ach.title} - " +
                     $"Points: {ach.points}, Claimed: {isClaimed}, Requirement Met: {requirementMet}");
        }
        
        Debug.Log($"[AchievementManager] === END DEBUG ALL ACHIEVEMENTS ===");
    }

    public void EmergencyPointsFix()
    {
        var user = GetCurrentUser();
        if (user == null) return;
        
        Debug.Log($"[AchievementManager] === EMERGENCY POINTS FIX ===");
        
        // Clear all caches first
        DatabaseManager.Instance?.ClearUnlockedAchievementsCache(user.id);
        
        // Let SessionManager handle the emergency fix
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        
        // Fallback: recalculate if still issues
        if (GetCurrentUserPoints() == 0)
        {
            RecalculateUserPoints();
        }
        
        UpdateTotalPointsUI();
        
        Debug.Log($"[AchievementManager] Emergency fix completed. Points: {GetCurrentUserPoints()}");
        Debug.Log($"[AchievementManager] === END EMERGENCY FIX ===");
    }

    private IEnumerator DelayedDebug(int achievementId)
    {
        yield return new WaitForSeconds(0.5f);
        DebugAchievementState(achievementId);
        
        // Final state check
        Debug.Log($"[AchievementManager] Final points: {GetCurrentUserPoints()}");
        Debug.Log($"[AchievementManager] === END TEST CLAIM ACHIEVEMENT {achievementId} ===");
    }

    public void DebugInitializationState()
    {
        Debug.Log($"[AchievementManager] === INITIALIZATION STATE ===");
        Debug.Log($"[AchievementManager] Instance: {_instance != null}");
        Debug.Log($"[AchievementManager] IsInitialized: {_isInitialized}");
        Debug.Log($"[AchievementManager] SessionManager: {SessionManager.Instance != null}");
        Debug.Log($"[AchievementManager] CurrentUser: {GetCurrentUser() != null}");
        Debug.Log($"[AchievementManager] User ID: {GetCurrentUser()?.id ?? -1}");
        Debug.Log($"[AchievementManager] User Points: {GetCurrentUserPoints()}");
        Debug.Log($"[AchievementManager] DatabaseManager: {DatabaseManager.Instance != null}");
        Debug.Log($"[AchievementManager] === END INITIALIZATION STATE ===");
    }

    // NEW: Force refresh achievements data
    public void ForceRefreshAchievementsData()
    {
        var user = GetCurrentUser();
        if (user == null) return;
        
        Debug.Log($"[AchievementManager] Force refreshing achievements data for user {user.id}");
        
        // Clear any cached data and force fresh database queries
        DatabaseManager.Instance?.ClearUnlockedAchievementsCache(user.id);
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        UpdateTotalPointsUI();
        
        // Trigger UI update
        OnPointsUpdated?.Invoke(GetCurrentUserPoints());
        
        Debug.Log($"[AchievementManager] Force refresh completed");
    }

    public void TestClaimAchievement(int achievementId)
    {
        var user = GetCurrentUser();
        if (user == null) return;
        
        Debug.Log($"[AchievementManager] === TEST CLAIM ACHIEVEMENT {achievementId} ===");
        
        // Debug before
        DebugAchievementState(achievementId);
        
        // Try to claim
        ClaimAchievement(achievementId, (pointsAwarded) => {
            Debug.Log($"[AchievementManager] Claim callback - Points awarded: {pointsAwarded}");
            
            // Wait a moment then debug after
            StartCoroutine(DelayedDebug(achievementId));
        });
    }

    #endregion
}