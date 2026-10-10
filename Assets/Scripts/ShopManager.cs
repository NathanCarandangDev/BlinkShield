using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System; // ✅ ADD THIS LINE

/// <summary>
/// Handles shop logic: purchasing, equipping, and retrieving items.
/// FIXED VERSION - Properly updates local database when equipping/unequipping
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }
    
    // Event for UI to listen to equipment changes
    public delegate void EquipmentChangedHandler(int userId, int itemId, bool isEquipped);
    public static event EquipmentChangedHandler OnEquipmentChanged;
    
    // ✅ ADDED: Event for UI to listen to points changes
    public delegate void PointsChangedHandler(int userId, int newPoints);
    public static event PointsChangedHandler OnPointsChanged;

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
        }
    }

    /// <summary>
    /// Get the full catalog of wardrobe items.
    /// </summary>
    public List<WardrobeItem> GetAllShopItems()
    {
        return DatabaseManager.Instance.GetAllWardrobeItems();
    }

    /// <summary>
    /// Get all items owned by a specific user.
    /// </summary>
    public List<UserWardrobe> GetUserWardrobe(int userId)
    {
        DatabaseManager.Instance.SetCurrentUser(userId);
        return DatabaseManager.Instance.GetUserWardrobe();
    }

    /// <summary>
    /// Get a specific wardrobe item by ID
    /// </summary>
    public WardrobeItem GetWardrobeItem(int itemId)
    {
        var allItems = DatabaseManager.Instance.GetAllWardrobeItems();
        return allItems?.Find(item => item.item_id == itemId);
    }

    /// <summary>
    /// ✅ FIXED: Attempt to purchase an item for a user with proper points deduction
    /// </summary>
    public bool PurchaseItem(int userId, int itemId)
    {
        DatabaseManager.Instance.SetCurrentUser(userId);

        // Check if user already owns the item
        var owned = DatabaseManager.Instance.GetUserWardrobe()
            .Find(x => x.item_id == itemId);
        if (owned != null)
        {
            Debug.Log($"[ShopManager] User {userId} already owns item {itemId}");
            return false;
        }

        // Get item price and user points
        var item = GetWardrobeItem(itemId);
        if (item == null)
        {
            Debug.LogError($"[ShopManager] Item {itemId} not found!");
            return false;
        }

        int userPoints = DatabaseManager.Instance.GetUserPoints();
        int itemPrice = (int)item.price;

        // Check if user has enough points
        if (userPoints < itemPrice)
        {
            Debug.LogWarning($"[ShopManager] Insufficient points! User has {userPoints}, needs {itemPrice}");
            return false;
        }

        // ✅ FIXED: Deduct points - Make sure we're updating the correct user
        int newPoints = userPoints - itemPrice;
        
        // Log before update
        Debug.Log($"[ShopManager] Before purchase: User {userId} has {userPoints}g. Buying item {itemId} for {itemPrice}g");
        Debug.Log($"[ShopManager] Expected new points: {newPoints}g");
        
        // ✅ FIXED ERROR #1: UpdateUserPoints returns void, not bool
        DatabaseManager.Instance.UpdateUserPoints(userId, newPoints);
        
        // ✅ IMPORTANT: Refresh the current user after points update
        DatabaseManager.Instance.SetCurrentUser(userId);

        // Verify the points were updated
        int updatedPoints = DatabaseManager.Instance.GetUserPoints();
        if (updatedPoints != newPoints)
        {
            Debug.LogError($"[ShopManager] Points mismatch! Expected: {newPoints}, Got: {updatedPoints}");
            
            // Try direct query
            var user = DatabaseManager.Instance.GetUserById(userId);
            if (user != null)
            {
                Debug.LogError($"[ShopManager] Direct user query shows points: {user.points}");
            }
            
            // Try to fix it by updating again
            DatabaseManager.Instance.UpdateUserPoints(userId, newPoints);
            
            // Check one more time
            DatabaseManager.Instance.SetCurrentUser(userId);
            int finalPoints = DatabaseManager.Instance.GetUserPoints();
            if (finalPoints != newPoints)
            {
                Debug.LogError($"[ShopManager] CRITICAL: Could not update points after retry! Expected: {newPoints}, Got: {finalPoints}");
                return false;
            }
        }

        // Add item to user's wardrobe
        var userWardrobeItem = new UserWardrobe
        {
            user_id = userId, // ✅ IMPORTANT: Set user_id
            item_id = itemId,
            is_equipped = false,
            updated_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") // ✅ ADD: Set timestamp
        };
        DatabaseManager.Instance.AddUserWardrobeItem(userWardrobeItem);

        Debug.Log($"[ShopManager] ✅ SUCCESS! User {userId} purchased item {itemId} for {itemPrice}g. New balance: {newPoints}g");
        
        // Update SessionManager if available
        if (SessionManager.Instance?.CurrentUser != null)
        {
            SessionManager.Instance.CurrentUser.points = newPoints;
            Debug.Log($"[ShopManager] Updated SessionManager points to: {SessionManager.Instance.CurrentUser.points}");
        }

        // ✅ FIXED: Trigger points changed event
        OnPointsChanged?.Invoke(userId, newPoints);

        // Sync to server
        SyncPurchaseToServer(userId, itemId, newPoints);

        // ✅ ADDED: Refresh UI immediately - FIXED: Removed tag error
        RefreshUI(userId, itemId, newPoints);

        return true;
    }

    /// <summary>
    /// ✅ UPDATED: Sync the purchase to the server with points
    /// </summary>
    private void SyncPurchaseToServer(int userId, int itemId, int newPoints)
    {
        if (SyncManager.Instance != null && SyncManager.Instance.HasValidUserForSync())
        {
            Debug.Log($"[ShopManager] Syncing purchase to server for user {userId}, item {itemId}, new points: {newPoints}");
            
            // Use OnTableChanged event system
            SyncManager.Instance.OnTableChanged("user_wardrobe", $"item_purchased:{itemId}");
            SyncManager.Instance.OnTableChanged("users", $"points_updated:{newPoints}");
            
            // Optionally force a sync
            SyncManager.Instance.ForceSync();
        }
    }

    /// <summary>
    /// ✅ FIXED: Equip/Unequip an item (unequips others when equipping)
    /// Now properly updates local database with timestamps
    /// </summary>
    public void ToggleEquipItem(int userId, int itemId)
    {
        Debug.Log($"[ShopManager] ToggleEquipItem called for user {userId}, item {itemId}");
        
        DatabaseManager.Instance.SetCurrentUser(userId);

        var owned = DatabaseManager.Instance.GetUserWardrobe();
        var targetItem = owned?.Find(x => x.item_id == itemId);
        
        if (targetItem == null)
        {
            Debug.LogError($"[ShopManager] User {userId} doesn't own item {itemId}");
            return;
        }

        Debug.Log($"[ShopManager] Item {itemId} is currently equipped: {targetItem.is_equipped}");

        // Store the new state BEFORE changing
        bool newEquippedState = !targetItem.is_equipped;
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (targetItem.is_equipped)
        {
            // Unequip the item
            Debug.Log($"[ShopManager] Unequipping item {itemId}");
            targetItem.is_equipped = false;
            targetItem.updated_at = timestamp; // ✅ ADD: Set timestamp
            
            // ✅ FIXED: Ensure user_id is set before saving
            if (targetItem.user_id <= 0)
            {
                targetItem.user_id = userId;
                Debug.Log($"[ShopManager] Set user_id to: {targetItem.user_id}");
            }
            
            DatabaseManager.Instance.SaveOrUpdateUserWardrobe(targetItem);
            Debug.Log($"[ShopManager] Database save called for unequip, timestamp: {timestamp}");
            
            // Notify BlinkyManager to remove the item
            NotifyBlinkyUnequip(itemId);
            
            // ✅ FIXED: Trigger equipment change event
            OnEquipmentChanged?.Invoke(userId, itemId, false);
        }
        else
        {
            // Equip the item and unequip all others
            Debug.Log($"[ShopManager] Equipping item {itemId}, unequipping others");
            
            int unequippedCount = 0;
            foreach (var uw in owned)
            {
                bool shouldBeEquipped = (uw.item_id == itemId);
                
                if (uw.is_equipped != shouldBeEquipped)
                {
                    uw.is_equipped = shouldBeEquipped;
                    uw.updated_at = timestamp; // ✅ ADD: Set timestamp for all changed items
                    
                    // ✅ FIXED: Ensure user_id is set before saving
                    if (uw.user_id <= 0)
                    {
                        uw.user_id = userId;
                    }
                    
                    DatabaseManager.Instance.SaveOrUpdateUserWardrobe(uw);
                    Debug.Log($"[ShopManager] Saved item {uw.item_id}, equipped: {uw.is_equipped}, timestamp: {timestamp}");
                    
                    if (!shouldBeEquipped)
                    {
                        unequippedCount++;
                    }
                }
            }
            
            Debug.Log($"[ShopManager] Unequipped {unequippedCount} other items");
            
            // Notify BlinkyManager to show the new item
            NotifyBlinkyEquip(itemId);
            
            // ✅ FIXED: Trigger equipment change event
            OnEquipmentChanged?.Invoke(userId, itemId, true);
        }

        // ✅ FIXED: Verify the change was saved to local database
        VerifyLocalDatabaseUpdate(userId, itemId, newEquippedState, timestamp);

        // Sync to server using OnTableChanged event system
        if (SyncManager.Instance != null && SyncManager.Instance.HasValidUserForSync())
        {
            Debug.Log($"[ShopManager] Syncing equipment change to server with timestamp: {timestamp}");
            
            string eventName = newEquippedState ? $"item_equipped:{itemId}:{timestamp}" : $"item_unequipped:{itemId}:{timestamp}";
            SyncManager.Instance.OnTableChanged("user_wardrobe", eventName);
            
            // Optionally force a sync
            SyncManager.Instance.ForceSync();
        }
    }

    /// <summary>
    /// ✅ FIXED: Verify the local database was updated with timestamp check
    /// </summary>
    private void VerifyLocalDatabaseUpdate(int userId, int itemId, bool expectedEquipped, string expectedTimestamp = null)
    {
        // Wait a moment for database operations
        StartCoroutine(DelayedVerification(userId, itemId, expectedEquipped, expectedTimestamp));
    }

    private IEnumerator DelayedVerification(int userId, int itemId, bool expectedEquipped, string expectedTimestamp = null)
    {
        yield return new WaitForSeconds(0.1f); // Small delay
        
        // Refresh data from database
        DatabaseManager.Instance.SetCurrentUser(userId);
        var freshItems = DatabaseManager.Instance.GetUserWardrobe();
        var freshItem = freshItems?.Find(x => x.item_id == itemId);
        
        if (freshItem != null)
        {
            if (freshItem.is_equipped == expectedEquipped)
            {
                Debug.Log($"[ShopManager] ✅ Local database verification PASSED: item {itemId} is_equipped = {freshItem.is_equipped}");
                
                // Check timestamp if provided
                if (!string.IsNullOrEmpty(expectedTimestamp) && !string.IsNullOrEmpty(freshItem.updated_at))
                {
                    Debug.Log($"[ShopManager] Timestamp: DB={freshItem.updated_at}, Expected={expectedTimestamp}");
                }
            }
            else
            {
                Debug.LogError($"[ShopManager] ❌ Local database verification FAILED!");
                Debug.Log($"[ShopManager] Expected: {expectedEquipped}, Got: {freshItem.is_equipped}");
                
                // Try to fix it
                freshItem.is_equipped = expectedEquipped;
                freshItem.updated_at = string.IsNullOrEmpty(expectedTimestamp) ? 
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : expectedTimestamp;
                if (freshItem.user_id <= 0) freshItem.user_id = userId;
                DatabaseManager.Instance.SaveOrUpdateUserWardrobe(freshItem);
                Debug.Log($"[ShopManager] Attempted to fix database state");
            }
        }
        else
        {
            Debug.LogError($"[ShopManager] ❌ Could not find item {itemId} in database!");
        }
    }

    /// <summary>
    /// Notify BlinkyManager about equipment changes
    /// </summary>
    private void NotifyBlinkyEquip(int itemId)
    {
        var item = GetWardrobeItem(itemId);
        if (item != null)
        {
            BlinkyManager blinky = FindFirstObjectByType<BlinkyManager>();
            if (blinky != null)
            {
                blinky.EquipWardrobeItem(item);
                Debug.Log($"[ShopManager] Notified BlinkyManager to equip: {item.name}");
            }
            else
            {
                Debug.LogWarning("[ShopManager] BlinkyManager not found in scene");
            }
        }
    }

    /// <summary>
    /// Notify BlinkyManager about unequip changes
    /// </summary>
    private void NotifyBlinkyUnequip(int itemId)
    {
        BlinkyManager blinky = FindFirstObjectByType<BlinkyManager>();
        if (blinky != null)
        {
            blinky.UnequipWardrobeItem();
            Debug.Log($"[ShopManager] Notified BlinkyManager to unequip item");
        }
        else
        {
            Debug.LogWarning("[ShopManager] BlinkyManager not found in scene");
        }
    }

    /// <summary>
    /// Check if an item is equipped for a specific user
    /// </summary>
    public bool IsItemEquipped(int userId, int itemId)
    {
        DatabaseManager.Instance.SetCurrentUser(userId);
        var owned = DatabaseManager.Instance.GetUserWardrobe()
            .Find(x => x.item_id == itemId);
        
        return owned?.is_equipped ?? false;
    }

    /// <summary>
    /// Get button text based on equipment status
    /// </summary>
    public string GetEquipButtonText(int userId, int itemId)
    {
        return IsItemEquipped(userId, itemId) ? "UNEQUIP" : "EQUIP";
    }

    /// <summary>
    /// ✅ FIXED: Refresh UI after purchase - NO MORE TAG ERRORS
    /// </summary>
    private void RefreshUI(int userId, int purchasedItemId, int newPoints)
    {
        Debug.Log($"[ShopManager] RefreshUI called - User: {userId}, Item: {purchasedItemId}, New Points: {newPoints}");
        
        // Option 1: Broadcast a message to all objects in scene
        // This is safer than using tags
        GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            // Skip inactive objects
            if (!obj.activeInHierarchy) continue;
            
            // Check if object might have UI components
            if (obj.GetComponent<TMPro.TMP_Text>() != null || 
                obj.GetComponent<UnityEngine.UI.Button>() != null ||
                obj.GetComponent<UnityEngine.UI.Image>() != null)
            {
                // Send message - this won't cause errors even if method doesn't exist
                obj.SendMessage("OnShopUpdate", SendMessageOptions.DontRequireReceiver);
            }
        }
        
        // Option 2: Specifically update points displays
        UpdatePointsDisplay(newPoints);
        
        // Option 3: Force refresh the ShopDisplay if it exists
        RefreshShopDisplay();
        
        Debug.Log($"[ShopManager] UI refresh completed");
    }

    /// <summary>
    /// ✅ NEW: Update points display specifically
    /// </summary>
    private void UpdatePointsDisplay(int points)
    {
        // Find all TMP_Text objects that might be displaying points
        TMPro.TMP_Text[] allTexts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
        foreach (TMPro.TMP_Text text in allTexts)
        {
            if (text == null) continue;
            
            // Check if this text might be displaying points
            string currentText = text.text;
            if (currentText.Contains("Points") || 
                currentText.Contains("points") || 
                text.gameObject.name.Contains("Point") ||
                text.gameObject.name.Contains("Gold") ||
                text.gameObject.name.Contains("G"))
            {
                // Update points display
                text.text = $"{points}";
                Debug.Log($"[ShopManager] Updated points display: {text.gameObject.name} -> {points}");
            }
        }
    }

    /// <summary>
    /// ✅ NEW: Refresh shop display component
    /// </summary>
    private void RefreshShopDisplay()
    {
        // Find ShopDisplay component in scene
        ShopDisplay shopDisplay = UnityEngine.Object.FindObjectOfType<ShopDisplay>();
        if (shopDisplay != null)
        {
            Debug.Log($"[ShopManager] Found ShopDisplay, forcing refresh...");
            shopDisplay.ForceRefresh();
        }
        else
        {
            Debug.LogWarning($"[ShopManager] ShopDisplay not found in scene");
            
            // Try alternative name
            MonoBehaviour[] allBehaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in allBehaviours)
            {
                if (behaviour.GetType().Name.Contains("Shop") || 
                    behaviour.GetType().Name.Contains("Display"))
                {
                    behaviour.SendMessage("RefreshUI", SendMessageOptions.DontRequireReceiver);
                    behaviour.SendMessage("DisplayShopItems", SendMessageOptions.DontRequireReceiver);
                    behaviour.SendMessage("ForceRefresh", SendMessageOptions.DontRequireReceiver);
                }
            }
        }
    }

    /// <summary>
    /// ✅ ADDED: Debug method to check points before/after purchase
    /// </summary>
    [ContextMenu("Debug Points System")]
    public void DebugPointsSystem()
    {
        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("No user logged in!");
            return;
        }

        int userId = DatabaseManager.Instance.CurrentUserId;
        
        Debug.Log($"=== POINTS SYSTEM DEBUG ===");
        Debug.Log($"Current user ID: {userId}");
        
        // Check points in database
        int points = DatabaseManager.Instance.GetUserPoints();
        Debug.Log($"Database.GetUserPoints(): {points}g");
        
        // Check direct user query
        var user = DatabaseManager.Instance.GetUserById(userId);
        if (user != null)
        {
            Debug.Log($"Direct user query points: {user.points}g");
        }
        
        // Check SessionManager
        if (SessionManager.Instance?.CurrentUser != null)
        {
            Debug.Log($"SessionManager points: {SessionManager.Instance.CurrentUser.points}g");
        }
        
        // Check all shop items and prices
        var shopItems = GetAllShopItems();
        Debug.Log($"\nShop Items ({shopItems?.Count ?? 0}):");
        if (shopItems != null)
        {
            // FIXED ERROR #3: Remove LINQ Take method, use simple loop
            int itemsToShow = Mathf.Min(5, shopItems.Count);
            for (int i = 0; i < itemsToShow; i++)
            {
                var item = shopItems[i];
                Debug.Log($"- ID: {item.item_id}, Name: {item.name}, Price: {item.price}g");
            }
            if (shopItems.Count > 5) Debug.Log($"... and {shopItems.Count - 5} more");
        }
        
        Debug.Log($"=== END DEBUG ===");
    }

    /// <summary>
    /// ✅ ADDED: Test purchase system with a specific item
    /// </summary>
    [ContextMenu("Test Purchase System")]
    public void TestPurchaseSystem()
    {
        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("No user logged in!");
            return;
        }

        int userId = DatabaseManager.Instance.CurrentUserId;
        
        Debug.Log($"=== TESTING PURCHASE SYSTEM ===");
        
        // Get user's current points
        int initialPoints = DatabaseManager.Instance.GetUserPoints();
        Debug.Log($"Initial points: {initialPoints}g");
        
        // Find a test item (warxdrobe with ID 1, for example)
        int testItemId = 1; // Change this to your warxdrobe item ID
        
        // Try to purchase
        bool success = PurchaseItem(userId, testItemId);
        
        if (success)
        {
            Debug.Log($"✅ Purchase successful!");
        }
        else
        {
            Debug.Log($"❌ Purchase failed!");
        }
        
        // Check final points
        int finalPoints = DatabaseManager.Instance.GetUserPoints();
        Debug.Log($"Final points: {finalPoints}g");
        Debug.Log($"Points difference: {initialPoints - finalPoints}g");
        
        Debug.Log($"=== END TEST ===");
    }

    /// <summary>
    /// Debug method to check user wardrobe contents
    /// </summary>
    [ContextMenu("Debug User Wardrobe")]
    public void DebugUserWardrobe()
    {
        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("No user logged in!");
            return;
        }

        int userId = DatabaseManager.Instance.CurrentUserId;
        var ownedItems = GetUserWardrobe(userId);
        
        Debug.Log($"=== USER WARDROBE DEBUG (User: {userId}) ===");
        Debug.Log($"Total owned items: {ownedItems?.Count ?? 0}");
        Debug.Log($"User points: {DatabaseManager.Instance.GetUserPoints()}g");
        
        if (ownedItems != null)
        {
            foreach (var item in ownedItems)
            {
                var wardrobeItem = GetWardrobeItem(item.item_id);
                string itemName = wardrobeItem?.name ?? "Unknown Item";
                Debug.Log($"- Item ID: {item.item_id}, Name: {itemName}, Equipped: {item.is_equipped}, UserId: {item.user_id}, Updated: {item.updated_at}");
            }
        }
        Debug.Log($"=== END WARDROBE DEBUG ===");
    }

    /// <summary>
    /// Test equip/unequip system
    /// </summary>
    [ContextMenu("Test Equip/Unequip System")]
    public void TestEquipUnequipSystem()
    {
        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("No user logged in!");
            return;
        }

        int userId = DatabaseManager.Instance.CurrentUserId;
        var ownedItems = GetUserWardrobe(userId);
        
        if (ownedItems == null || ownedItems.Count == 0)
        {
            Debug.LogError("No owned items to test equip/unequip!");
            return;
        }

        // Test with the first owned item
        var testItem = ownedItems[0];
        Debug.Log($"=== TESTING EQUIP/UNEQUIP ===");
        Debug.Log($"Testing with item ID: {testItem.item_id}, Currently equipped: {testItem.is_equipped}, Last updated: {testItem.updated_at}");
        
        // Equip the item
        ToggleEquipItem(userId, testItem.item_id);
        
        // Wait and check
        StartCoroutine(TestAfterDelay(userId, testItem.item_id));
    }

    private IEnumerator TestAfterDelay(int userId, int itemId)
    {
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log($"\nChecking after toggle...");
        bool equipped = IsItemEquipped(userId, itemId);
        Debug.Log($"Item {itemId} is now equipped: {equipped}");
        Debug.Log($"Button text: {GetEquipButtonText(userId, itemId)}");
        
        // Check database directly
        DatabaseManager.Instance.SetCurrentUser(userId);
        var items = DatabaseManager.Instance.GetUserWardrobe();
        var item = items?.Find(x => x.item_id == itemId);
        if (item != null)
        {
            Debug.Log($"Database shows equipped: {item.is_equipped}, Updated: {item.updated_at}");
        }
        
        Debug.Log($"=== END TEST ===");
    }

    /// <summary>
    /// Get the current points of a user.
    /// </summary>
    public int GetUserPoints(int userId)
    {
        DatabaseManager.Instance.SetCurrentUser(userId);
        return DatabaseManager.Instance.GetUserPoints();
    }

    /// <summary>
    /// Convert decimal price to int for display and calculation
    /// </summary>
    public static int ConvertPriceToInt(decimal price)
    {
        return (int)price;
    }

    /// <summary>
    /// Get all equipped items for a user
    /// </summary>
    public List<UserWardrobe> GetEquippedItems(int userId)
    {
        DatabaseManager.Instance.SetCurrentUser(userId);
        var allItems = DatabaseManager.Instance.GetUserWardrobe();
        
        return allItems?.FindAll(item => item.is_equipped) ?? new List<UserWardrobe>();
    }

    /// <summary>
    /// Check if user can afford an item
    /// </summary>
    public bool CanAffordItem(int userId, int itemId)
    {
        var item = GetWardrobeItem(itemId);
        if (item == null)
            return false;

        int userPoints = GetUserPoints(userId);
        int itemPrice = ConvertPriceToInt(item.price);

        return userPoints >= itemPrice;
    }
    
    /// <summary>
    /// ✅ ADDED: Convert a byte[] from the database into a Unity Sprite.
    /// This method is needed by BlinkyManager
    /// </summary>
    public static Sprite SpriteFromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;

        Texture2D tex = new Texture2D(2, 2);
        if (tex.LoadImage(bytes))
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        return null;
    }
    
    /// <summary>
    /// ✅ ADDED: Keep the old EquipItem method for backward compatibility
    /// </summary>
    public void EquipItem(int userId, int itemId)
    {
        ToggleEquipItem(userId, itemId);
    }
    
    /// <summary>
    /// ✅ ADDED: Unequip a specific item
    /// </summary>
    public void UnequipItem(int userId, int itemId)
    {
        Debug.Log($"[ShopManager] UnequipItem called for user {userId}, item {itemId}");
        
        DatabaseManager.Instance.SetCurrentUser(userId);
        var owned = DatabaseManager.Instance.GetUserWardrobe();
        var targetItem = owned?.Find(x => x.item_id == itemId);
        
        if (targetItem == null || !targetItem.is_equipped)
        {
            Debug.LogWarning($"[ShopManager] Item {itemId} is not equipped for user {userId}");
            return;
        }
        
        targetItem.is_equipped = false;
        targetItem.updated_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        // Ensure user_id is set before saving
        if (targetItem.user_id <= 0)
        {
            targetItem.user_id = userId;
        }
        
        DatabaseManager.Instance.SaveOrUpdateUserWardrobe(targetItem);
        
        Debug.Log($"[ShopManager] User {userId} unequipped item {itemId}");
        
        // Notify BlinkyManager
        NotifyBlinkyUnequip(itemId);
        
        // Trigger event
        OnEquipmentChanged?.Invoke(userId, itemId, false);
        
        // Sync to server
        if (SyncManager.Instance != null && SyncManager.Instance.HasValidUserForSync())
        {
            SyncManager.Instance.OnTableChanged("user_wardrobe", $"item_unequipped:{itemId}:{targetItem.updated_at}");
            SyncManager.Instance.ForceSync();
        }
    }
    
    /// <summary>
    /// ✅ ADDED: Unequip all items for a user
    /// </summary>
    public void UnequipAllItems(int userId)
    {
        Debug.Log($"[ShopManager] UnequipAllItems called for user {userId}");
        
        DatabaseManager.Instance.SetCurrentUser(userId);
        var owned = DatabaseManager.Instance.GetUserWardrobe();
        
        int unequippedCount = 0;
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var item in owned)
        {
            if (item.is_equipped)
            {
                item.is_equipped = false;
                item.updated_at = timestamp;
                
                // Ensure user_id is set before saving
                if (item.user_id <= 0)
                {
                    item.user_id = userId;
                }
                
                DatabaseManager.Instance.SaveOrUpdateUserWardrobe(item);
                unequippedCount++;
                
                // Trigger event for each unequipped item
                OnEquipmentChanged?.Invoke(userId, item.item_id, false);
            }
        }
        
        Debug.Log($"[ShopManager] Unequipped {unequippedCount} items for user {userId}");
        
        // Notify BlinkyManager
        BlinkyManager blinky = FindFirstObjectByType<BlinkyManager>();
        if (blinky != null)
        {
            blinky.UnequipWardrobeItem();
        }
        
        // Sync to server
        if (SyncManager.Instance != null && SyncManager.Instance.HasValidUserForSync())
        {
            SyncManager.Instance.OnTableChanged("user_wardrobe", $"all_items_unequipped:{timestamp}");
            SyncManager.Instance.ForceSync();
        }
    }
}