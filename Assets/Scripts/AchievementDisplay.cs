using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class AchievementDisplay : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text totalPointsText;
    public GameObject achievementPrefab;
    public Transform contentParent;
    public ScrollRect scrollRect;

    [Header("Visual Settings")]
    public Color claimedTextColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
    public Color claimedBackgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    public Color readyToClaimColor = new Color(0.2f, 0.8f, 0.2f, 1f);
    public Color lockedColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

    [Header("Performance Settings")]
    [SerializeField] private bool enableDebugLogs = true; // Enabled for debugging
    [SerializeField] private float refreshCooldown = 0.1f;

    private User currentUser;
    private HashSet<int> unlockedIds = new();
    private Dictionary<int, GameObject> achievementItems = new();
    private float lastRefreshTime = 0f;
    private bool isRefreshing = false;
    private Coroutine refreshCoroutine;

    // Cache for performance
    private List<Achievement> cachedAllAchievements = new();
    private bool cacheValid = false;

    private void OnEnable()
    {
        SessionManager.OnSessionStarted += OnSessionStarted;
        RegisterAchievementManagerEvents();

        if (SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn)
            InitializeWithCurrentUser();
    }

    private void OnDisable()
    {
        SessionManager.OnSessionStarted -= OnSessionStarted;
        UnregisterAchievementManagerEvents();
        
        if (refreshCoroutine != null)
        {
            StopCoroutine(refreshCoroutine);
            refreshCoroutine = null;
        }
        
        // Clear cache when disabled
        cacheValid = false;
        cachedAllAchievements.Clear();
    }

    private void Start()
    {
        InitializeWithCurrentUser();
    }

    #region Initialization
    private void InitializeWithCurrentUser()
    {
        if (SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn)
        {
            OnSessionStarted(SessionManager.Instance.CurrentUser);
        }
        else if (enableDebugLogs)
        {
            Debug.Log("[AchievementDisplay] No logged in user available at start");
        }
    }

    private void RegisterAchievementManagerEvents()
    {
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.OnPointsUpdated += OnPointsUpdated;
            AchievementManager.Instance.OnAchievementClaimed += OnAchievementClaimed;
            if (enableDebugLogs) Debug.Log("[AchievementDisplay] Registered AchievementManager events");
        }
        else if (enableDebugLogs)
        {
            Debug.LogWarning("[AchievementDisplay] AchievementManager not available for event registration");
        }
    }

    private void UnregisterAchievementManagerEvents()
    {
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.OnPointsUpdated -= OnPointsUpdated;
            AchievementManager.Instance.OnAchievementClaimed -= OnAchievementClaimed;
            if (enableDebugLogs) Debug.Log("[AchievementDisplay] Unregistered AchievementManager events");
        }
    }
    #endregion

    #region Event Handlers
    private void OnSessionStarted(User user)
    {
        if (user == null) 
        {
            if (enableDebugLogs) Debug.LogWarning("[AchievementDisplay] Session started with null user");
            return;
        }

        currentUser = user;
        
        if (enableDebugLogs) 
            Debug.Log($"[AchievementDisplay] Session started for user {user.id}");

        // Clear cache on new session
        cacheValid = false;
        cachedAllAchievements.Clear();

        // Force refresh all data
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        AchievementManager.Instance?.UpdateTotalPointsUI();
        
        UpdatePointsUI();
        ScheduleRefresh();
    }

    private void OnAchievementClaimed(int achievementId)
    {
        if (enableDebugLogs)
            Debug.Log($"[AchievementDisplay] Achievement {achievementId} claimed - scheduling UI update");

        // Invalidate cache and force fresh data
        cacheValid = false;
        
        // Animate the claimed achievement moving to bottom
        if (achievementItems.ContainsKey(achievementId))
        {
            StartCoroutine(AnimateAchievementClaim(achievementId));
        }
        
        // Wait a bit for database to update, then refresh
        StartCoroutine(DelayedRefreshAfterClaim());
    }

    private IEnumerator DelayedRefreshAfterClaim()
    {
        yield return new WaitForSeconds(0.3f); // Give time for database sync
        ScheduleRefresh();
    }

    private void OnPointsUpdated(int newPoints)
    {
        if (currentUser != null)
        {
            currentUser.points = newPoints;
            UpdatePointsUI();
            
            if (enableDebugLogs)
                Debug.Log($"[AchievementDisplay] Points updated to {newPoints}");
        }
    }
    #endregion

    #region UI Population & Refresh
    public void ScheduleRefresh()
    {
        if (isRefreshing || !gameObject.activeInHierarchy) 
            return;

        if (Time.time - lastRefreshTime < refreshCooldown)
        {
            // Cooldown active, schedule for later
            if (refreshCoroutine != null)
                StopCoroutine(refreshCoroutine);
            refreshCoroutine = StartCoroutine(DelayedRefresh());
            return;
        }

        RefreshAchievements();
    }

    private IEnumerator DelayedRefresh()
    {
        yield return new WaitForSeconds(refreshCooldown);
        RefreshAchievements();
        refreshCoroutine = null;
    }

    private void RefreshAchievements()
    {
        if (currentUser == null)
        {
            if (enableDebugLogs) Debug.LogWarning("[AchievementDisplay] Cannot refresh - no current user");
            return;
        }

        if (achievementPrefab == null)
        {
            Debug.LogError("[AchievementDisplay] Cannot refresh - achievementPrefab is null");
            return;
        }

        if (contentParent == null)
        {
            Debug.LogError("[AchievementDisplay] Cannot refresh - contentParent is null");
            return;
        }

        if (isRefreshing) 
        {
            if (enableDebugLogs) Debug.Log("[AchievementDisplay] Refresh already in progress, skipping");
            return;
        }

        isRefreshing = true;
        lastRefreshTime = Time.time;

        if (enableDebugLogs)
        {
            Debug.Log($"[AchievementDisplay] === REFRESHING ACHIEVEMENTS ===");
            Debug.Log($"[AchievementDisplay] User ID: {currentUser.id}");
        }

        try
        {
            // Get fresh data - don't use cache for critical operations
            var allAchievements = GetFreshAchievementsData();
            var freshUnlockedIds = GetFreshUnlockedIds();

            if (enableDebugLogs)
            {
                Debug.Log($"[AchievementDisplay] Total achievements: {allAchievements.Count}");
                Debug.Log($"[AchievementDisplay] Unlocked achievements: {freshUnlockedIds.Count}");
            }

            if (allAchievements.Count == 0)
            {
                Debug.LogError($"[AchievementDisplay] NO ACHIEVEMENTS FOUND!");
                return;
            }

            // Update our unlocked IDs cache
            unlockedIds = freshUnlockedIds;

            // Create achievement list with claimed status
            var achievementsWithClaimed = allAchievements
                .Select(a => (a, unlockedIds.Contains(a.achievement_id)))
                .ToList();

            // Validate data integrity
            ValidateAchievementData(achievementsWithClaimed);

            UpdatePointsUI();
            PopulateUI(achievementsWithClaimed);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AchievementDisplay] Error during refresh: {e.Message}");
            Debug.LogError($"[AchievementDisplay] Stack trace: {e.StackTrace}");
        }
        finally
        {
            isRefreshing = false;
            if (enableDebugLogs)
                Debug.Log($"[AchievementDisplay] === REFRESH COMPLETE ===");
        }
    }

    private List<Achievement> GetFreshAchievementsData()
    {
        // Always get fresh data from AchievementManager
        var achievements = AchievementManager.Instance?.GetAllAchievements() ?? new List<Achievement>();
        
        if (enableDebugLogs && achievements.Count == 0)
        {
            Debug.LogWarning("[AchievementDisplay] Retrieved empty achievements list from AchievementManager");
        }

        return achievements;
    }

    private HashSet<int> GetFreshUnlockedIds()
    {
        if (currentUser == null) return new HashSet<int>();
        
        var unlocked = AchievementManager.Instance?.GetUnlockedIds(currentUser.id) ?? new HashSet<int>();
        
        if (enableDebugLogs)
        {
            Debug.Log($"[AchievementDisplay] Fresh unlocked IDs: {string.Join(", ", unlocked)}");
        }
        
        return unlocked;
    }

    private void ValidateAchievementData(List<(Achievement achievement, bool claimed)> achievements)
    {
        if (!enableDebugLogs) return;

        int claimedCount = achievements.Count(a => a.claimed);
        int unclaimedCount = achievements.Count(a => !a.claimed);
        
        Debug.Log($"[AchievementDisplay] Data Validation - Total: {achievements.Count}, Claimed: {claimedCount}, Unclaimed: {unclaimedCount}");

        // Check for any achievements that are marked as claimed but shouldn't be
        foreach (var (ach, claimed) in achievements)
        {
            if (claimed)
            {
                Debug.Log($"[AchievementDisplay] ✅ {ach.achievement_id}: {ach.title} - CLAIMED");
            }
        }

        // Verify no duplicates
        var duplicateIds = achievements.GroupBy(x => x.achievement.achievement_id)
                                      .Where(g => g.Count() > 1)
                                      .Select(g => g.Key)
                                      .ToList();
        if (duplicateIds.Count > 0)
        {
            Debug.LogError($"[AchievementDisplay] ❌ DUPLICATE ACHIEVEMENT IDs: {string.Join(", ", duplicateIds)}");
        }
    }

    private void PopulateUI(List<(Achievement achievement, bool claimed)> achievements)
    {
        if (contentParent == null || achievementPrefab == null) 
        {
            Debug.LogError("[AchievementDisplay] Cannot populate UI - missing references");
            return;
        }

        // Clear old items efficiently
        foreach (Transform child in contentParent)
        {
            if (child != null)
                Destroy(child.gameObject);
        }
        achievementItems.Clear();

        if (achievements == null || achievements.Count == 0)
        {
            Debug.LogError("[AchievementDisplay] No achievements to display!");
            return;
        }

        // Sort achievements: Unclaimed (ready) -> Unclaimed (locked) -> Claimed (at bottom)
        var sortedAchievements = SortAchievementsForDisplay(achievements);

        if (enableDebugLogs)
        {
            Debug.Log($"[AchievementDisplay] Displaying {sortedAchievements.Count} achievements");
            int readyCount = sortedAchievements.Count(a => !a.claimed && IsRequirementMet(a.achievement));
            int lockedCount = sortedAchievements.Count(a => !a.claimed && !IsRequirementMet(a.achievement));
            int claimedCount = sortedAchievements.Count(a => a.claimed);
            Debug.Log($"[AchievementDisplay] Breakdown - Ready: {readyCount}, Locked: {lockedCount}, Claimed: {claimedCount}");
        }

        // Create UI items
        foreach (var (ach, isClaimed) in sortedAchievements)
        {
            CreateAchievementItem(ach, isClaimed);
        }

        // Verify final UI state
        if (enableDebugLogs)
        {
            Debug.Log($"[AchievementDisplay] Final UI items created: {achievementItems.Count}");
            DebugUIState();
        }

        // Auto-scroll to top to show available achievements
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private List<(Achievement achievement, bool claimed)> SortAchievementsForDisplay(List<(Achievement achievement, bool claimed)> achievements)
    {
        // NEW: Use AchievementManager's sorted method if available
        if (AchievementManager.Instance != null)
        {
            try
            {
                var sorted = AchievementManager.Instance.GetAchievementsForUserSorted(currentUser.id);
                if (sorted != null && sorted.Count > 0)
                {
                    if (enableDebugLogs)
                        Debug.Log($"[AchievementDisplay] Using AchievementManager sorted list with {sorted.Count} items");
                    return sorted;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AchievementDisplay] Failed to get sorted achievements from AchievementManager: {e.Message}");
            }
        }

        // Fallback: Use our own sorting logic
        var unclaimedReady = achievements
            .Where(a => !a.claimed && IsRequirementMet(a.achievement))
            .OrderByDescending(a => a.achievement.points)
            .ThenBy(a => a.achievement.achievement_id)
            .ToList();

        var unclaimedLocked = achievements
            .Where(a => !a.claimed && !IsRequirementMet(a.achievement))
            .OrderByDescending(a => a.achievement.points)
            .ThenBy(a => a.achievement.achievement_id)
            .ToList();

        var claimed = achievements
            .Where(a => a.claimed)
            .OrderByDescending(a => a.achievement.points)
            .ThenBy(a => a.achievement.achievement_id)
            .ToList();

        var result = unclaimedReady.Concat(unclaimedLocked).Concat(claimed).ToList();

        if (enableDebugLogs)
        {
            Debug.Log($"[AchievementDisplay] Using fallback sorting for {result.Count} achievements:");
            for (int i = 0; i < result.Count; i++)
            {
                var (ach, claimedStatus) = result[i];
                string status = claimedStatus ? "CLAIMED" : (IsRequirementMet(ach) ? "READY" : "LOCKED");
                Debug.Log($"[AchievementDisplay] [{i}] {ach.achievement_id}: {ach.title} - {status}");
            }
        }

        return result;
    }

    private bool IsRequirementMet(Achievement achievement)
    {
        return AchievementManager.Instance?.IsRequirementMet(achievement) ?? false;
    }
    #endregion

    #region Achievement Item Creation
    private void CreateAchievementItem(Achievement ach, bool isClaimed)
    {
        if (ach == null)
        {
            Debug.LogError("[AchievementDisplay] Cannot create item for null achievement");
            return;
        }

        GameObject item = Instantiate(achievementPrefab, contentParent);
        if (item == null)
        {
            Debug.LogError("[AchievementDisplay] Failed to instantiate achievement prefab");
            return;
        }

        item.transform.localScale = Vector3.one;
        achievementItems[ach.achievement_id] = item;

        SetupAchievementItemUI(item, ach, isClaimed);

        if (enableDebugLogs)
            Debug.Log($"[AchievementDisplay] Created UI for: {ach.title} (ID:{ach.achievement_id}) - Claimed: {isClaimed}");
    }

    private void SetupAchievementItemUI(GameObject item, Achievement ach, bool isClaimed)
    {
        TMP_Text[] texts = item.GetComponentsInChildren<TMP_Text>();
        Button claimButton = item.GetComponentInChildren<Button>();
        Image background = item.GetComponent<Image>();
        Image icon = item.transform.Find("Icon")?.GetComponent<Image>();

        // Set achievement texts
        if (texts.Length >= 3)
        {
            texts[0].text = ach.title ?? "No Title";
            texts[1].text = ach.description ?? "No Description";
            texts[2].text = $"{ach.points} pts";

            if (isClaimed)
            {
                ApplyClaimedVisuals(texts, background, icon);
            }
            else
            {
                ApplyUnclaimedVisuals(texts, background, icon);
            }
        }
        else
        {
            Debug.LogError($"[AchievementDisplay] Achievement prefab missing required Text components for: {ach.title}");
        }

        // Configure claim button
        if (claimButton != null)
        {
            SetupClaimButton(claimButton, ach, isClaimed);
        }
        else
        {
            Debug.LogWarning($"[AchievementDisplay] Achievement prefab missing Button component for: {ach.title}");
        }
    }

    private void ApplyClaimedVisuals(TMP_Text[] texts, Image background, Image icon)
    {
        foreach (var text in texts)
        {
            text.color = claimedTextColor;
        }

        if (background != null)
            background.color = claimedBackgroundColor;

        if (icon != null)
            icon.color = claimedTextColor;
    }

    private void ApplyUnclaimedVisuals(TMP_Text[] texts, Image background, Image icon)
    {
        foreach (var text in texts)
        {
            text.color = Color.white;
        }

        if (background != null)
            background.color = Color.white * 0.1f;

        if (icon != null)
            icon.color = Color.white;
    }

    private void SetupClaimButton(Button claimButton, Achievement ach, bool isClaimed)
    {
        TMP_Text btnText = claimButton.GetComponentInChildren<TMP_Text>();
        Image buttonImage = claimButton.GetComponent<Image>();
        
        claimButton.onClick.RemoveAllListeners();

        if (isClaimed)
        {
            // CLAIMED STATE - Button should be disabled and show "Claimed ✅"
            claimButton.interactable = false;
            if (btnText != null) 
            {
                btnText.text = "Claimed ✅";
                btnText.color = claimedTextColor;
            }
            
            if (buttonImage != null)
                buttonImage.color = claimedBackgroundColor;

            if (enableDebugLogs)
                Debug.Log($"[AchievementDisplay] Setting achievement {ach.achievement_id} to CLAIMED state (button disabled)");
        }
        else
        {
            bool requirementMet = IsRequirementMet(ach);
            claimButton.interactable = requirementMet;

            if (btnText != null)
            {
                if (requirementMet)
                {
                    btnText.text = $"Claim {ach.points} pts";
                    btnText.color = Color.white;
                    
                    if (buttonImage != null)
                        buttonImage.color = readyToClaimColor;
                }
                else
                {
                    btnText.text = "Locked";
                    btnText.color = claimedTextColor;
                    
                    if (buttonImage != null)
                        buttonImage.color = lockedColor;
                }
            }

            if (requirementMet)
            {
                int capturedId = ach.achievement_id;
                claimButton.onClick.AddListener(() => OnClaimButtonClicked(capturedId));
            }
            else
            {
                UpdateLockedAchievementProgress(ach, btnText);
            }

            if (enableDebugLogs)
                Debug.Log($"[AchievementDisplay] Achievement {ach.achievement_id} - Requirement met: {requirementMet}, Button interactable: {claimButton.interactable}");
        }
    }

    private void UpdateLockedAchievementProgress(Achievement ach, TMP_Text btnText)
    {
        if (btnText == null) return;
        
        var progress = AchievementManager.Instance?.GetRequirementProgress(ach) ?? (false, 0, 0);
        
        if (ach.requirement_type.ToLower() != "log_in")
        {
            btnText.text = $"{progress.current}/{progress.required}";
        }
    }
    #endregion

    #region Animation & Interaction
    private void OnClaimButtonClicked(int achievementId)
    {
        if (enableDebugLogs)
            Debug.Log($"[AchievementDisplay] Claim button clicked for achievement {achievementId}");
        
        // Visual feedback
        if (achievementItems.TryGetValue(achievementId, out GameObject item))
        {
            StartCoroutine(AnimateButtonPress(item));
        }
        
        // Call AchievementManager to claim the achievement
        AchievementManager.Instance.ClaimAchievement(achievementId, (pointsGranted) =>
        {
            if (enableDebugLogs)
                Debug.Log($"[AchievementDisplay] Claim callback - Points granted: {pointsGranted}");
        });
    }

    private IEnumerator AnimateButtonPress(GameObject item)
    {
        if (item == null) yield break;

        Button button = item.GetComponentInChildren<Button>();
        if (button != null)
        {
            button.interactable = false;
            
            // Simple scale animation
            Vector3 originalScale = item.transform.localScale;
            item.transform.localScale = originalScale * 0.95f;
            
            yield return new WaitForSeconds(0.1f);
            
            item.transform.localScale = originalScale;
        }
    }

    private IEnumerator AnimateAchievementClaim(int achievementId)
    {
        if (!achievementItems.TryGetValue(achievementId, out GameObject item) || item == null)
            yield break;

        // Highlight the achievement briefly before it moves to bottom
        Image background = item.GetComponent<Image>();
        Color originalColor = background != null ? background.color : Color.white;

        if (background != null)
        {
            background.color = readyToClaimColor;
            yield return new WaitForSeconds(0.3f);
            background.color = claimedBackgroundColor;
        }

        yield return new WaitForSeconds(0.2f);
    }
    #endregion

    #region Points Management
    private void UpdatePointsUI()
    {
        if (totalPointsText != null && currentUser != null)
        {
            int currentPoints = AchievementManager.Instance?.GetCurrentUserPoints() ?? 0;
            totalPointsText.text = $"Total Points: {currentPoints}";
        }
    }
    #endregion

    #region Public Methods
    public void ForceRefresh()
    {
        if (currentUser != null)
        {
            if (enableDebugLogs)
                Debug.Log("[AchievementDisplay] Manual force refresh triggered");
            
            // Clear cache and force fresh data
            cacheValid = false;
            RefreshAchievements();
        }
    }

    // NEW: Force refresh with proper sorting
    public void ForceRefreshWithSorting()
    {
        if (enableDebugLogs)
            Debug.Log("[AchievementDisplay] Force refresh with sorting triggered");
        
        // Clear all cached data
        cacheValid = false;
        unlockedIds.Clear();
        achievementItems.Clear();
        
        // Force refresh using AchievementManager's sorted method
        if (currentUser != null)
        {
            RefreshAchievements();
        }
    }

    public void DebugUIState()
    {
        Debug.Log($"[AchievementDisplay] === UI STATE DEBUG ===");
        Debug.Log($"[AchievementDisplay] Current user: {currentUser?.id ?? -1}");
        Debug.Log($"[AchievementDisplay] Achievement items: {achievementItems.Count}");
        Debug.Log($"[AchievementDisplay] Content child count: {contentParent.childCount}");
        Debug.Log($"[AchievementDisplay] Is Refreshing: {isRefreshing}");
        Debug.Log($"[AchievementDisplay] Cache Valid: {cacheValid}");
        
        int position = 0;
        foreach (Transform child in contentParent)
        {
            var texts = child.GetComponentsInChildren<TMP_Text>();
            string title = texts.Length > 0 ? texts[0].text : "Unknown";
            
            var button = child.GetComponentInChildren<Button>();
            string buttonState = button != null ? (button.interactable ? "Interactable" : "Disabled") : "No Button";
            string buttonText = button?.GetComponentInChildren<TMP_Text>()?.text ?? "No Text";
            
            Debug.Log($"[AchievementDisplay] Position {position}: {title} - Button: {buttonState} ('{buttonText}')");
            position++;
        }
        Debug.Log($"[AchievementDisplay] === END UI DEBUG ===");
    }

    public void EmergencyUIFix()
    {
        Debug.Log($"[AchievementDisplay] === EMERGENCY UI FIX ===");
        
        if (currentUser == null) 
        {
            Debug.LogError("[AchievementDisplay] No current user for emergency fix");
            return;
        }
        
        // Force complete refresh of ALL data
        SessionManager.Instance?.RefreshUserPointsFromDatabase();
        
        // Clear ALL cached data
        unlockedIds.Clear();
        achievementItems.Clear();
        cacheValid = false;
        cachedAllAchievements.Clear();
        
        if (refreshCoroutine != null)
        {
            StopCoroutine(refreshCoroutine);
            refreshCoroutine = null;
        }
        
        isRefreshing = false;
        
        Debug.Log($"[AchievementDisplay] Cleared all cached data, refreshing...");
        
        // Force immediate refresh
        RefreshAchievements();
    }

    public void DebugDatabaseState()
    {
        if (currentUser == null) return;

        Debug.Log($"[AchievementDisplay] === DATABASE STATE DEBUG ===");
        Debug.Log($"[AchievementDisplay] User ID: {currentUser.id}");
        
        var achievements = AchievementManager.Instance?.GetAllAchievements() ?? new List<Achievement>();
        var unlockedIds = AchievementManager.Instance?.GetUnlockedIds(currentUser.id) ?? new HashSet<int>();
        
        Debug.Log($"[AchievementDisplay] Total achievements: {achievements.Count}");
        Debug.Log($"[AchievementDisplay] Unlocked achievements: {unlockedIds.Count}");
        Debug.Log($"[AchievementDisplay] Unlocked IDs: {string.Join(", ", unlockedIds)}");
        
        foreach (var ach in achievements.Take(10)) // Limit output for performance
        {
            bool isClaimed = unlockedIds.Contains(ach.achievement_id);
            Debug.Log($"[AchievementDisplay] {ach.achievement_id}: {ach.title} - Claimed: {isClaimed}");
        }
        if (achievements.Count > 10)
            Debug.Log($"[AchievementDisplay] ... and {achievements.Count - 10} more achievements");
        Debug.Log($"[AchievementDisplay] === END DATABASE DEBUG ===");
    }

    // Test method to verify claim status for a specific achievement
    public void TestAchievementClaimStatus(int achievementId)
    {
        if (currentUser == null) return;

        bool isClaimed = AchievementManager.Instance?.GetUnlockedIds(currentUser.id).Contains(achievementId) ?? false;
        Debug.Log($"[AchievementDisplay] Achievement {achievementId} claim status: {isClaimed}");
        
        // Check if UI reflects this correctly
        if (achievementItems.TryGetValue(achievementId, out GameObject item))
        {
            Button button = item?.GetComponentInChildren<Button>();
            Debug.Log($"[AchievementDisplay] UI Button state - Interactable: {button?.interactable ?? false}, Text: {button?.GetComponentInChildren<TMP_Text>()?.text ?? "N/A"}");
        }
        else
        {
            Debug.Log($"[AchievementDisplay] Achievement {achievementId} not found in UI items");
        }
    }

    // NEW: Method to test sorting order
    public void DebugSortingOrder()
    {
        if (currentUser == null) return;

        Debug.Log($"[AchievementDisplay] === SORTING ORDER DEBUG ===");
        
        var achievements = AchievementManager.Instance?.GetAllAchievements() ?? new List<Achievement>();
        var unlockedIds = AchievementManager.Instance?.GetUnlockedIds(currentUser.id) ?? new HashSet<int>();
        var achievementsWithClaimed = achievements.Select(a => (a, unlockedIds.Contains(a.achievement_id))).ToList();
        
        var sorted = SortAchievementsForDisplay(achievementsWithClaimed);
        
        Debug.Log($"[AchievementDisplay] Final sorted order ({sorted.Count} achievements):");
        for (int i = 0; i < sorted.Count; i++)
        {
            var (ach, claimed) = sorted[i];
            string status = claimed ? "CLAIMED" : (IsRequirementMet(ach) ? "READY" : "LOCKED");
            Debug.Log($"[AchievementDisplay] [{i}] {ach.achievement_id}: {ach.title} - {status} ({ach.points} pts)");
        }
        Debug.Log($"[AchievementDisplay] === END SORTING DEBUG ===");
    }
    #endregion
}