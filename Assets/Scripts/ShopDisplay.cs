using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Collections;

public class ShopDisplay : MonoBehaviour
{
    [Header("Shop UI")]
    public Transform contentParent;
    public GameObject showItemPrefab;
    public int userId;
    
    [Header("Points Display")]
    public TMP_Text pointsText; // Assign this in the Inspector to display user points

    private WardrobeItem pendingItem;
    private bool _isInitialized = false;
    private bool _isRefreshing = false;
    private Dictionary<int, GameObject> itemGameObjects = new Dictionary<int, GameObject>();

    private void OnEnable()
    {
        // Subscribe to session events like AchievementDisplay
        SessionManager.OnSessionStarted += OnSessionStarted;
        
        // Subscribe to equipment change events
        ShopManager.OnEquipmentChanged += OnEquipmentChanged;
        
        if (SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn)
            OnSessionStarted(SessionManager.Instance.CurrentUser);
    }

    private void OnDisable()
    {
        // Unsubscribe from events
        SessionManager.OnSessionStarted -= OnSessionStarted;
        ShopManager.OnEquipmentChanged -= OnEquipmentChanged;
        
        // Clear all references to prevent accessing destroyed objects
        ClearItemReferences();
    }

    private void OnDestroy()
    {
        // Ensure we unsubscribe when destroyed
        SessionManager.OnSessionStarted -= OnSessionStarted;
        ShopManager.OnEquipmentChanged -= OnEquipmentChanged;
        ClearItemReferences();
    }

    private void ClearItemReferences()
    {
        // Clear references to prevent accessing destroyed objects
        itemGameObjects.Clear();
    }

    private void OnSessionStarted(User user)
    {
        if (user == null) return;
        
        userId = user.id;
        Debug.Log($"[ShopDisplay] Session started for user: {userId}");
        
        // Refresh points immediately from database
        RefreshPointsFromDatabase();
        
        // Initialize shop display
        StartCoroutine(InitializeWhenReady());
    }

    private void Start()
    {
        Debug.Log("[ShopDisplay] Start called - Beginning initialization");
        VerifyUIComponents();
        
        // If already logged in, start immediately
        if (SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn)
        {
            OnSessionStarted(SessionManager.Instance.CurrentUser);
        }
    }

    private IEnumerator InitializeWhenReady()
    {
        Debug.Log("[ShopDisplay] Starting initialization coroutine");

        // Wait for DatabaseManager to be ready
        while (DatabaseManager.Instance == null || !DatabaseManager.Instance.IsDatabaseReady())
        {
            Debug.Log("[ShopDisplay] Waiting for DatabaseManager to be ready...");
            yield return new WaitForSeconds(0.1f);
        }

        // Wait for user to be logged in
        while (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.Log("[ShopDisplay] Waiting for user to log in...");
            yield return new WaitForSeconds(0.1f);
        }

        // Set the user ID from the current logged-in user
        userId = DatabaseManager.Instance.CurrentUserId;
        Debug.Log($"[ShopDisplay] User ID set to: {userId}");

        // Additional small delay to ensure user data is fully loaded
        yield return new WaitForSeconds(0.5f);

        _isInitialized = true;
        Debug.Log("[ShopDisplay] Initialization completed successfully");
        
        // Refresh points before displaying
        RefreshPointsFromDatabase();
        
        // Check data sources before displaying
        CheckDataSources();
        DisplayShopItems();
    }

    /// <summary>
    /// Handle equipment change events from ShopManager
    /// </summary>
    private void OnEquipmentChanged(int userId, int itemId, bool isEquipped)
    {
        Debug.Log($"[ShopDisplay] OnEquipmentChanged: userId={userId}, itemId={itemId}, isEquipped={isEquipped}");
        
        // Simple null check approach
        if (this == null) return;
        
        // Only update if it's for our current user
        if (userId != this.userId) return;
        
        // Try to find the GameObject in the scene
        GameObject itemGo = FindItemGameObject(itemId);
        if (itemGo != null && itemGo.activeInHierarchy)
        {
            // Get the button component
            Button equipButton = itemGo.GetComponent<Button>();
            if (equipButton != null)
            {
                // Find the text component in children
                TMP_Text textComponent = itemGo.GetComponentInChildren<TMP_Text>(true);
                if (textComponent != null && textComponent.gameObject.name == "ItemPrice") // Check if it's the price text
                {
                    textComponent.text = isEquipped ? "UNEQUIP" : "EQUIP";
                    
                    // Optional: Change button color based on state
                    ColorBlock colors = equipButton.colors;
                    colors.normalColor = isEquipped ? new Color(1f, 0.5f, 0.5f) : new Color(0.5f, 1f, 0.5f);
                    equipButton.colors = colors;
                    
                    Debug.Log($"[ShopDisplay] Updated button for item {itemId}: {textComponent.text}");
                }
                else
                {
                    // Try to find any TMP_Text component
                    TMP_Text[] allTexts = itemGo.GetComponentsInChildren<TMP_Text>(true);
                    foreach (TMP_Text text in allTexts)
                    {
                        if (text != null && (text.name.Contains("Price") || text.name.Contains("Item")))
                        {
                            text.text = isEquipped ? "UNEQUIP" : "EQUIP";
                            Debug.Log($"[ShopDisplay] Updated button text via fallback for item {itemId}");
                            break;
                        }
                    }
                }
            }
        }
        
        // Refresh points display
        RefreshPointsFromDatabase();
    }

    private GameObject FindItemGameObject(int itemId)
    {
        // Try to find the GameObject in our cache
        if (itemGameObjects.TryGetValue(itemId, out GameObject cachedGo))
        {
            if (cachedGo != null && cachedGo.activeInHierarchy)
            {
                return cachedGo;
            }
            else
            {
                // Remove invalid reference
                itemGameObjects.Remove(itemId);
            }
        }
        
        // If not in cache or invalid, try to find it in the scene
        // Look for item with specific itemId in the contentParent
        if (contentParent != null)
        {
            foreach (Transform child in contentParent)
            {
                // Check if this child has our item data (we'll store it in a component or check name)
                ItemDataHolder holder = child.GetComponent<ItemDataHolder>();
                if (holder != null && holder.itemId == itemId)
                {
                    itemGameObjects[itemId] = child.gameObject;
                    return child.gameObject;
                }
                
                // Alternative: Check by name pattern
                if (child.name.Contains($"Item_{itemId}") || child.name.Contains($"ShopItem_{itemId}"))
                {
                    itemGameObjects[itemId] = child.gameObject;
                    return child.gameObject;
                }
            }
        }
        
        return null;
    }

    /// <summary>
    /// Simple component to store item data on the GameObject
    /// </summary>
    private class ItemDataHolder : MonoBehaviour
    {
        public int itemId;
    }

    /// <summary>
    /// Refresh points from database like AchievementDisplay does
    /// </summary>
    private void RefreshPointsFromDatabase()
    {
        if (pointsText == null)
        {
            Debug.LogError("[ShopDisplay] PointsText is NULL! Assign it in the Inspector.");
            return;
        }

        // Make absolutely sure the text component is enabled and visible
        pointsText.enabled = true;
        pointsText.gameObject.SetActive(true);

        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("[ShopDisplay] User not logged in!");
            pointsText.text = "NOT LOGGED IN";
            return;
        }

        try
        {
            // METHOD 1: Get points from SessionManager (like AchievementDisplay)
            int sessionPoints = 0;
            if (SessionManager.Instance?.CurrentUser != null)
            {
                sessionPoints = SessionManager.Instance.CurrentUser.points;
                Debug.Log($"[ShopDisplay] SessionManager points: {sessionPoints}");
            }

            // METHOD 2: Get points directly from DatabaseManager
            int dbPoints = DatabaseManager.Instance.GetUserPoints();
            Debug.Log($"[ShopDisplay] DatabaseManager points: {dbPoints}");

            // METHOD 3: Get points from current user object
            int currentUserPoints = 0;
            var currentUser = DatabaseManager.Instance.GetCurrentUser();
            if (currentUser != null)
            {
                currentUserPoints = currentUser.points;
                Debug.Log($"[ShopDisplay] CurrentUser points: {currentUserPoints}");
            }

            // Use the most reliable points value
            int finalPoints = dbPoints; // Prefer database as source of truth
            
            // Update points text - REMOVED THE "g" AS REQUESTED
            pointsText.text = $"{finalPoints}";
            Debug.Log($"[ShopDisplay] Points updated to: {finalPoints} (Session: {sessionPoints}, DB: {dbPoints}, CurrentUser: {currentUserPoints})");
            
            // If points are 0, show a warning
            if (finalPoints == 0)
            {
                Debug.LogWarning($"[ShopDisplay] Points returned 0 for user {userId}. Expected 100.");
                
                // Try to force refresh from SessionManager
                SessionManager.Instance?.RefreshUserPointsFromDatabase();
            }
            
            // Force UI refresh
            Canvas.ForceUpdateCanvases();
        }
        catch (System.Exception ex)
        {
            pointsText.text = "ERROR";
            Debug.LogError($"[ShopDisplay] Error updating points: {ex.Message}");
        }
    }

    /// <summary>
    /// Force refresh points (can be called from context menu)
    /// </summary>
    [ContextMenu("Force Refresh Points")]
    public void ForceRefreshPoints()
    {
        Debug.Log("[ShopDisplay] Force refreshing points...");
        RefreshPointsFromDatabase();
        
        // Also refresh the shop display
        if (_isInitialized)
        {
            RefreshShopDisplay();
        }
    }

    public void DisplayShopItems()
    {
        if (_isRefreshing) 
        {
            Debug.Log("[ShopDisplay] DisplayShopItems already in progress, skipping...");
            return;
        }
        
        // Double-check initialization
        if (!_isInitialized && DatabaseManager.Instance != null && DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("[ShopDisplay] DisplayShopItems called before initialization, forcing init...");
            userId = DatabaseManager.Instance.CurrentUserId;
            _isInitialized = true;
        }
        
        StartCoroutine(DisplayShopItemsCoroutine());
    }

    private IEnumerator DisplayShopItemsCoroutine()
    {
        _isRefreshing = true;
        
        Debug.Log($"[ShopDisplay] Starting DisplayShopItemsCoroutine - UserID: {userId}, LoggedIn: {DatabaseManager.Instance?.IsLoggedIn}");

        // Wait one frame to ensure UI is ready
        yield return null;

        // STRICTER NULL CHECK - This should prevent any item creation if prefab is null
        if (contentParent == null)
        {
            Debug.LogError("[ShopDisplay] ContentParent is not assigned!");
            _isRefreshing = false;
            yield break;
        }

        if (showItemPrefab == null)
        {
            Debug.LogError("[ShopDisplay] ShowItemPrefab is NULL! Cannot create shop items.");
            _isRefreshing = false;
            yield break;
        }

        // Validate dependencies
        if (ShopManager.Instance == null)
        {
            Debug.LogError("[ShopDisplay] ShopManager instance not found");
            _isRefreshing = false;
            yield break;
        }

        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("[ShopDisplay] Cannot display shop items - user not logged in");
            _isRefreshing = false;
            yield break;
        }

        try
        {
            // Update points display FIRST - like AchievementDisplay
            RefreshPointsFromDatabase();

            // Clear existing items
            int childCount = contentParent.childCount;
            Debug.Log($"[ShopDisplay] Clearing {childCount} existing items");
            for (int i = contentParent.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(contentParent.GetChild(i).gameObject);
            }

            // Clear GameObject dictionary
            ClearItemReferences();

            // Get shop data
            List<WardrobeItem> allItems = ShopManager.Instance.GetAllShopItems();
            List<UserWardrobe> ownedItems = ShopManager.Instance.GetUserWardrobe(userId);

            Debug.Log($"[ShopDisplay] Retrieved {allItems?.Count ?? 0} shop items and {ownedItems?.Count ?? 0} owned items");

            if (allItems == null || allItems.Count == 0)
            {
                Debug.LogWarning("[ShopDisplay] No shop items found or list is empty");
                _isRefreshing = false;
                yield break;
            }

            // DOUBLE CHECK prefab is still not null before proceeding
            if (showItemPrefab == null)
            {
                Debug.LogError("[ShopDisplay] ShowItemPrefab became null during execution! Aborting.");
                _isRefreshing = false;
                yield break;
            }

            int createdItems = 0;
            int skippedItems = 0;
            
            // Create shop items
            foreach (var item in allItems)
            {
                if (item == null) 
                {
                    Debug.LogWarning("[ShopDisplay] Found null item in shop items list");
                    skippedItems++;
                    continue;
                }

                Debug.Log($"[ShopDisplay] Processing item: {item.name} (ID: {item.item_id}, Price: {item.price})");

                // FINAL CHECK before instantiating
                if (showItemPrefab == null)
                {
                    Debug.LogError("[ShopDisplay] ShowItemPrefab is null! Cannot instantiate items.");
                    break;
                }

                GameObject go = Instantiate(showItemPrefab, contentParent);
                if (go == null)
                {
                    Debug.LogError("[ShopDisplay] Failed to instantiate item prefab");
                    skippedItems++;
                    continue;
                }
                
                go.transform.localScale = Vector3.one;
                createdItems++;

                // Add ItemDataHolder to track item ID
                ItemDataHolder holder = go.AddComponent<ItemDataHolder>();
                holder.itemId = item.item_id;
                
                // Store reference to the GameObject
                itemGameObjects[item.item_id] = go;

                // Set item name
                TMP_Text nameText = go.transform.Find("ItemName")?.GetComponent<TMP_Text>();
                if (nameText != null) 
                {
                    nameText.text = !string.IsNullOrEmpty(item.name) ? item.name : "Unnamed Item";
                    Debug.Log($"[ShopDisplay] Set item name to: {nameText.text}");
                }
                else
                {
                    Debug.LogWarning("[ShopDisplay] ItemName text component not found in prefab");
                }

                // Set item image
                Image imageComponent = go.transform.Find("ItemImage")?.GetComponent<Image>();
                if (imageComponent != null)
                {
                    if (item.image != null)
                    {
                        Sprite sprite = ShopManager.SpriteFromBytes(item.image);
                        if (sprite != null) 
                        {
                            imageComponent.sprite = sprite;
                            Debug.Log($"[ShopDisplay] Set item image for: {item.name}");
                        }
                        else
                        {
                            Debug.LogWarning($"[ShopDisplay] Could not load sprite for item: {item.name}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[ShopDisplay] Item image data is null for: {item.name}");
                    }
                }
                else
                {
                    Debug.LogWarning("[ShopDisplay] ItemImage component not found in prefab");
                }

                // Set price / action text
                TMP_Text priceText = go.transform.Find("ItemPrice")?.GetComponent<TMP_Text>();
                Button button = go.GetComponent<Button>();
                
                if (priceText == null || button == null) 
                {
                    Debug.LogError($"[ShopDisplay] Missing price text or button component - PriceText: {priceText != null}, Button: {button != null}");
                    skippedItems++;
                    continue;
                }
                
                button.onClick.RemoveAllListeners();
                
                // Check if user owns this item
                UserWardrobe userWardrobe = ownedItems?.Find(x => x != null && x.item_id == item.item_id);

                if (userWardrobe != null)
                {
                    Debug.Log($"[ShopDisplay] User owns item: {item.name}, Equipped: {userWardrobe.is_equipped}");
                    
                    // Set button text based on equipped state
                    priceText.text = userWardrobe.is_equipped ? "UNEQUIP" : "EQUIP";
                    
                    // Set button color based on equipped state
                    ColorBlock colors = button.colors;
                    colors.normalColor = userWardrobe.is_equipped ? new Color(1f, 0.5f, 0.5f) : new Color(0.5f, 1f, 0.5f);
                    button.colors = colors;
                    
                    WardrobeItem capturedItem = item;
                    button.onClick.AddListener(() =>
                    {
                        // Check if the button still exists
                        if (button == null || button.gameObject == null)
                        {
                            return;
                        }

                        if (ShopManager.Instance != null && DatabaseManager.Instance.IsLoggedIn)
                        {
                            Debug.Log($"[ShopDisplay] Toggling equipment for item: {capturedItem.name}");
                            ShopManager.Instance.ToggleEquipItem(userId, capturedItem.item_id);
                            
                            // Button text will update via OnEquipmentChanged event
                            // Small delay before refresh to ensure operation completes
                            StartCoroutine(RefreshAfterDelay(0.2f));
                        }
                    });
                }
                else
                {
                    Debug.Log($"[ShopDisplay] User does not own item: {item.name}");
                    
                    // Set button to purchase mode
                    priceText.text = $"Buy ({item.price}g)";
                    
                    // Reset button color to default
                    ColorBlock colors = button.colors;
                    colors.normalColor = Color.white;
                    button.colors = colors;
                    
                    WardrobeItem capturedItem = item;
                    button.onClick.AddListener(() =>
                    {
                        // Check if the button still exists
                        if (button == null || button.gameObject == null)
                        {
                            return;
                        }

                        if (ShopManager.Instance != null && DatabaseManager.Instance.IsLoggedIn)
                        {
                            Debug.Log($"[ShopDisplay] Showing popup for item: {capturedItem.name}");
                            ShowItemPopup(capturedItem);
                        }
                    });
                }
            }

            Debug.Log($"[ShopDisplay] Successfully created {createdItems} shop items, skipped {skippedItems} items");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ShopDisplay] Error displaying shop items: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Shows the ShopPopup when an item is clicked
    /// </summary>
    private void ShowItemPopup(WardrobeItem item)
    {
        Debug.Log($"[ShopDisplay] ShowItemPopup called for: {item.name}");
        pendingItem = item;
        
        // Get current points using the same method as AchievementDisplay
        int userPoints = DatabaseManager.Instance.GetUserPoints();
        Debug.Log($"[ShopDisplay] User points: {userPoints}, Item price: {item.price}");

        if (ShopPopup.Instance != null)
        {
            // PASS USER POINTS TO THE POPUP
            ShopPopup.Instance.Show(item, userPoints,
                onPurchase: () => OnPurchaseConfirmed(item, userPoints),
                onCancel: () => OnPurchaseCancelled()
            );
        }
        else
        {
            Debug.LogError("[ShopDisplay] ShopPopup instance is null!");
        }
    }

    /// <summary>
    /// Called when purchase is confirmed in the popup
    /// </summary>
    private void OnPurchaseConfirmed(WardrobeItem item, int userPoints)
    {
        Debug.Log($"[ShopDisplay] Purchase confirmed for: {item.name}");

        // Double-check affordability (in case popup was bypassed somehow)
        if (userPoints < item.price)
        {
            Debug.LogWarning($"[ShopDisplay] Insufficient funds! User has {userPoints}g, needs {item.price}g");
            // The popup should have already prevented this, but just in case
            return;
        }

        if (ShopManager.Instance != null)
        {
            bool bought = ShopManager.Instance.PurchaseItem(userId, item.item_id);
            if (bought) 
            {
                Debug.Log($"[ShopDisplay] ✅ Successfully purchased {item.name}");
                
                // ✅ UPDATED: Trigger server sync through ShopManager
                TriggerImmediateServerSync(item);
                
                // Update points display after purchase - refresh from database like AchievementDisplay
                RefreshPointsFromDatabase();
                // Refresh after successful purchase
                StartCoroutine(RefreshAfterDelay(0.3f));
            }
            else
            {
                Debug.LogWarning($"[ShopDisplay] Failed to purchase {item.name}");
            }
        }
    }

    /// <summary>
    /// ✅ UPDATED: Enhanced server synchronization using SyncManager's event system
    /// </summary>
    private void TriggerImmediateServerSync(WardrobeItem purchasedItem)
    {
        if (SyncManager.Instance != null && SyncManager.Instance.HasValidUserForSync())
        {
            Debug.Log($"[ShopDisplay] 🚀 Triggering server sync for purchase: {purchasedItem.name}");
            
            // UPDATED: Use SyncManager's OnTableChanged event system
            SyncManager.Instance.OnTableChanged("user_wardrobe", $"item_purchased:{purchasedItem.item_id}");
            SyncManager.Instance.OnTableChanged("users", "points_updated");
            
            Debug.Log($"[ShopDisplay] Triggered OnTableChanged events for user_wardrobe and users");
            
            // Verify the purchase was recorded locally
            StartCoroutine(VerifyPurchaseAndSync(purchasedItem.item_id));
        }
        else
        {
            Debug.LogWarning($"[ShopDisplay] SyncManager not available for sync");
            
            // Additional debug info
            Debug.Log($"[ShopDisplay] SyncManager Available: {SyncManager.Instance != null}");
            Debug.Log($"[ShopDisplay] Has Valid User: {SyncManager.Instance?.HasValidUserForSync() ?? false}");
            
            // Alternative: Try manual ForceSync
            if (SyncManager.Instance != null && SyncManager.Instance.IsInitialized)
            {
                Debug.Log($"[ShopDisplay] Attempting ForceSync as alternative...");
                SyncManager.Instance.ForceSync();
            }
        }
    }

    /// <summary>
    /// ✅ NEW: Verify purchase was recorded and sync status
    /// </summary>
    private IEnumerator VerifyPurchaseAndSync(int itemId)
    {
        yield return new WaitForSeconds(0.5f); // Small delay for database to update
        
        // Verify the purchase was recorded in local wardrobe
        var ownedItems = ShopManager.Instance.GetUserWardrobe(userId);
        var purchasedItem = ownedItems?.Find(x => x.item_id == itemId);
        
        if (purchasedItem != null)
        {
            Debug.Log($"[ShopDisplay] ✅ Purchase verified in local user_wardrobe - Item ID: {itemId}, Equipped: {purchasedItem.is_equipped}");
        }
        else
        {
            Debug.LogError($"[ShopDisplay] ❌ Purchase NOT found in local user_wardrobe! Item ID: {itemId}");
        }
    }

    /// <summary>
    /// Called when purchase is cancelled in the popup
    /// </summary>
    private void OnPurchaseCancelled()
    {
        Debug.Log("[ShopDisplay] Purchase cancelled by user");
        pendingItem = null;
    }

    private IEnumerator RefreshAfterDelay(float delay)
    {
        Debug.Log($"[ShopDisplay] Refreshing after {delay} second delay");
        yield return new WaitForSeconds(delay);
        RefreshShopDisplay();
    }

    public void RefreshShopDisplay()
    {
        Debug.Log($"[ShopDisplay] RefreshShopDisplay called - Active: {gameObject.activeInHierarchy}, Initialized: {_isInitialized}");
        if (gameObject.activeInHierarchy && _isInitialized)
        {
            DisplayShopItems();
        }
    }

    // Force refresh method that can be called externally
    public void ForceRefresh()
    {
        Debug.Log("[ShopDisplay] ForceRefresh called");
        if (DatabaseManager.Instance != null && DatabaseManager.Instance.IsLoggedIn)
        {
            userId = DatabaseManager.Instance.CurrentUserId;
            RefreshShopDisplay();
        }
    }

    /// <summary>
    /// ✅ NEW: Debug method to check current wardrobe state
    /// </summary>
    [ContextMenu("Debug Current Wardrobe")]
    public void DebugCurrentWardrobe()
    {
        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("No user logged in!");
            return;
        }

        var ownedItems = ShopManager.Instance.GetUserWardrobe(userId);
        Debug.Log($"=== CURRENT WARDROBE (User: {userId}) ===");
        Debug.Log($"Total owned items: {ownedItems?.Count ?? 0}");
        
        if (ownedItems != null)
        {
            foreach (var item in ownedItems)
            {
                var wardrobeItem = ShopManager.Instance.GetWardrobeItem(item.item_id);
                string itemName = wardrobeItem?.name ?? "Unknown Item";
                Debug.Log($"- Item ID: {item.item_id}, Name: {itemName}, Equipped: {item.is_equipped}");
            }
        }
        Debug.Log($"=== END WARDROBE DEBUG ===");
    }

    /// <summary>
    /// ✅ NEW: Test equip/unequip functionality
    /// </summary>
    [ContextMenu("Test Equip/Unequip Flow")]
    public void TestEquipUnequipFlow()
    {
        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("No user logged in!");
            return;
        }

        var ownedItems = ShopManager.Instance.GetUserWardrobe(userId);
        if (ownedItems == null || ownedItems.Count == 0)
        {
            Debug.LogWarning("No owned items to test equip/unequip!");
            return;
        }

        // Find an item to test with
        var testItem = ownedItems[0];
        var wardrobeItem = ShopManager.Instance.GetWardrobeItem(testItem.item_id);
        
        Debug.Log($"=== TESTING EQUIP/UNEQUIP FLOW ===");
        Debug.Log($"Testing with: {wardrobeItem?.name ?? "Unknown"} (ID: {testItem.item_id}, Currently equipped: {testItem.is_equipped})");
        
        // Toggle the equip state
        ShopManager.Instance.ToggleEquipItem(userId, testItem.item_id);
        
        // Refresh to see the change
        StartCoroutine(RefreshAfterDelay(0.5f));
    }

    /// <summary>
    /// ✅ NEW: Test purchase with specific item
    /// </summary>
    [ContextMenu("Test Purchase Flow")]
    public void TestPurchaseFlow()
    {
        if (!DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogError("No user logged in!");
            return;
        }

        var allItems = ShopManager.Instance.GetAllShopItems();
        if (allItems == null || allItems.Count == 0)
        {
            Debug.LogError("No shop items available!");
            return;
        }

        // Find an item the user doesn't own
        var ownedItems = ShopManager.Instance.GetUserWardrobe(userId);
        WardrobeItem testItem = null;

        foreach (var item in allItems)
        {
            if (!ownedItems.Exists(owned => owned.item_id == item.item_id))
            {
                testItem = item;
                break;
            }
        }

        if (testItem != null)
        {
            Debug.Log($"=== TESTING PURCHASE FLOW ===");
            Debug.Log($"Testing purchase of: {testItem.name} (ID: {testItem.item_id}, Price: {testItem.price})");
            ShowItemPopup(testItem);
        }
        else
        {
            Debug.LogWarning("No unpurchased items available for testing");
        }
    }

    [ContextMenu("Debug Force Refresh Shop")]
    public void DebugForceRefresh()
    {
        Debug.Log("=== MANUAL SHOP REFRESH ===");
        Debug.Log($"Initialized: {_isInitialized}");
        Debug.Log($"User ID: {userId}");
        Debug.Log($"Logged in: {DatabaseManager.Instance?.IsLoggedIn}");
        Debug.Log($"ShopManager: {ShopManager.Instance != null}");
        Debug.Log($"ShowItemPrefab: {showItemPrefab != null}");
        Debug.Log($"ShopPopup: {ShopPopup.Instance != null}");
        Debug.Log($"SyncManager: {SyncManager.Instance != null}");
        Debug.Log($"Has Valid User for Sync: {SyncManager.Instance?.HasValidUserForSync() ?? false}");
        
        // Check SyncManager status
        if (SyncManager.Instance != null)
        {
            Debug.Log($"SyncManager IsInitialized: {SyncManager.Instance.IsInitialized}");
            Debug.Log($"SyncManager IsSyncing: {SyncManager.Instance.IsSyncing}");
        }
        
        if (_isInitialized && DatabaseManager.Instance != null && DatabaseManager.Instance.IsLoggedIn)
        {
            CheckDataSources();
            DisplayShopItems();
        }
        else
        {
            Debug.LogError("Cannot refresh - not properly initialized");
        }
    }

    private void CheckDataSources()
    {
        Debug.Log("=== CHECKING DATA SOURCES ===");
        
        if (ShopManager.Instance == null)
        {
            Debug.LogError("ShopManager instance is null!");
            return;
        }

        // Test data retrieval
        List<WardrobeItem> allItems = ShopManager.Instance.GetAllShopItems();
        List<UserWardrobe> ownedItems = ShopManager.Instance.GetUserWardrobe(userId);

        Debug.Log($"AllItems count: {allItems?.Count}");
        Debug.Log($"OwnedItems count: {ownedItems?.Count}");

        if (allItems != null)
        {
            foreach (var item in allItems)
            {
                if (item != null)
                {
                    Debug.Log($"Item: {item.name}, ID: {item.item_id}, Price: {item.price}, ImageData: {item.image?.Length ?? 0} bytes");
                }
            }
        }
        else
        {
            Debug.LogError("AllItems list is null!");
        }
    }

    private void VerifyUIComponents()
    {
        Debug.Log("=== VERIFYING UI COMPONENTS ===");
        
        if (contentParent == null)
        {
            Debug.LogError("Content Parent is not assigned!");
        }
        else
        {
            Debug.Log($"Content Parent assigned: {contentParent.name}");
        }

        if (showItemPrefab == null)
        {
            Debug.LogError("Show Item Prefab is not assigned!");
        }
        else
        {
            Debug.Log($"Show Item Prefab assigned: {showItemPrefab.name}");
        }

        if (pointsText == null)
        {
            Debug.LogWarning("Points Text is not assigned - points display will not work");
        }
        else
        {
            Debug.Log($"Points Text assigned: {pointsText.name}");
        }

        // Check if the prefab has the required components
        if (showItemPrefab != null)
        {
            TMP_Text nameText = showItemPrefab.transform.Find("ItemName")?.GetComponent<TMP_Text>();
            Image imageComponent = showItemPrefab.transform.Find("ItemImage")?.GetComponent<Image>();
            TMP_Text priceText = showItemPrefab.transform.Find("ItemPrice")?.GetComponent<TMP_Text>();
            Button button = showItemPrefab.GetComponent<Button>();

            Debug.Log($"Prefab components - NameText: {nameText != null}, Image: {imageComponent != null}, PriceText: {priceText != null}, Button: {button != null}");
        }
    }

    /// <summary>
    /// ✅ UPDATED: Check sync status using GetStatus() method
    /// </summary>
    [ContextMenu("Check Sync Status")]
    public void CheckSyncStatus()
    {
        if (SyncManager.Instance != null)
        {
            // UPDATED: Use GetStatus() instead of GetSmartSyncStatus()
            Debug.Log($"[ShopDisplay] Sync Status:\n{SyncManager.Instance.GetStatus()}");
            Debug.Log($"[ShopDisplay] Is Syncing: {SyncManager.Instance.IsSyncing}");
            Debug.Log($"[ShopDisplay] Last Successful Sync: {SyncManager.Instance.LastSuccessfulSync}");
        }
        else
        {
            Debug.LogWarning("[ShopDisplay] SyncManager is not available");
        }
    }

    /// <summary>
    /// ✅ UPDATED: Manually trigger a sync using ForceSync()
    /// </summary>
    [ContextMenu("Trigger Manual Sync")]
    public void TriggerManualSync()
    {
        if (SyncManager.Instance != null)
        {
            // UPDATED: Use ForceSync() instead of ForceSmartSync()
            SyncManager.Instance.ForceSync();
            Debug.Log("[ShopDisplay] Manual sync triggered");
        }
        else
        {
            Debug.LogError("[ShopDisplay] SyncManager not available for manual sync");
        }
    }
}