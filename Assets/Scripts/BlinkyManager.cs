using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class BlinkyManager : MonoBehaviour
{
    [Header("UI Setup")]
    public Image uiImage;                 // Assign your BlinkySprite (Image)
    public TMP_Text bubbleText;           // Optional: speech bubble text

    [Header("Emotion Sprites (open eyes)")]
    public Sprite happyOpen;
    public Sprite sadOpen;
    public Sprite angryOpen;
    public Sprite tiredOpen;

    [Header("Closed-eye Sprites")]
    public Sprite happyClosed;    // New: Closed eyes for happy
    public Sprite sadClosed;      // New: Closed eyes for sad
    public Sprite angryClosed;    // New: Closed eyes for angry
    public Sprite tiredClosed;    // New: Closed eyes for tired

    [Header("Wardrobe Overlay")]
    public Image wardrobeOverlay;         // For showing equipped items
    // REMOVED: defaultWardrobeSprite - no longer needed!

    [Header("Item Position Settings")]
    public WardrobeItemSettings ballSettings;
    public WardrobeItemSettings pinkBowSettings;
    public WardrobeItemSettings graduationHatSettings;
    public WardrobeItemSettings necktieSettings;
    public WardrobeItemSettings capSettings;

    [System.Serializable]
    public class WardrobeItemSettings
    {
        [Header("Position & Size")]
        public Vector2 positionOffset = Vector2.zero;
        public Vector2 size = new Vector2(100, 100);
        public float scale = 1.0f;
        
        [Header("Rotation (Degrees)")]
        [Tooltip("Rotation around X axis")]
        public float rotationX = 0f;
        
        [Tooltip("Rotation around Y axis")]
        public float rotationY = 0f;
        
        [Tooltip("Rotation around Z axis")]
        public float rotationZ = 0f;
    }

    [Header("Blink Settings")]
    public float blinkIntervalMin = 2f;
    public float blinkIntervalMax = 5f;
    public float blinkDuration = 0.12f;

    private int currentEmotion = -1; // -1 so it always updates at start
    private Coroutine blinkRoutine;
    private WardrobeItem currentEquippedItem;
    private Coroutine equipmentLoadRoutine;

    // Dictionary to store item settings by name
    private Dictionary<string, WardrobeItemSettings> itemSettings;

    private void Awake()
    {
        InitializeItemSettings();
    }

    private void InitializeItemSettings()
    {
        itemSettings = new Dictionary<string, WardrobeItemSettings>
        {
            { "ball", ballSettings },
            { "pink bow", pinkBowSettings },
            { "graduation hat", graduationHatSettings },
            { "necktie", necktieSettings },
            { "cap", capSettings }
        };
    }

    private void Start()
    {
        UpdateSprite();
        
        // Initialize wardrobe overlay state
        if (wardrobeOverlay != null)
        {
            // Start with it disabled (since no item is equipped yet)
            wardrobeOverlay.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        blinkRoutine = StartCoroutine(BlinkLoop());
        
        if (Application.isPlaying)
        {
            equipmentLoadRoutine = StartCoroutine(WaitForDatabaseAndLoadEquipment());
        }
    }

    private void OnDisable()
    {
        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        if (equipmentLoadRoutine != null) StopCoroutine(equipmentLoadRoutine);
    }

    // Wait for database to be ready
    private IEnumerator WaitForDatabaseAndLoadEquipment()
    {
        yield return new WaitUntil(() => DatabaseManager.Instance != null);
        yield return new WaitUntil(() => DatabaseManager.Instance.IsLoggedIn);
        yield return null;
        
        LoadCurrentEquipment();
    }

    // Load currently equipped item from database
    private void LoadCurrentEquipment()
    {
        if (DatabaseManager.Instance == null || !DatabaseManager.Instance.IsLoggedIn) 
        {
            return;
        }

        try
        {
            int userId = DatabaseManager.Instance.CurrentUserId;
            var ownedItems = ShopManager.Instance.GetUserWardrobe(userId);
            
            if (ownedItems != null)
            {
                var equippedItem = ownedItems.Find(x => x.is_equipped);
                if (equippedItem != null)
                {
                    var wardrobeItem = ShopManager.Instance.GetWardrobeItem(equippedItem.item_id);
                    if (wardrobeItem != null)
                    {
                        EquipWardrobeItem(wardrobeItem);
                    }
                    else
                    {
                        ClearWardrobeItem();
                    }
                }
                else
                {
                    ClearWardrobeItem();
                }
            }
            else
            {
                ClearWardrobeItem();
            }
        }
        catch (System.Exception)
        {
            ClearWardrobeItem();
        }
    }

    // Method to equip a wardrobe item with position/size/rotation adjustments
    public void EquipWardrobeItem(WardrobeItem item)
    {
        if (wardrobeOverlay == null)
        {
            return;
        }

        if (item?.image != null)
        {
            Sprite itemSprite = ShopManager.SpriteFromBytes(item.image);
            if (itemSprite != null)
            {
                wardrobeOverlay.sprite = itemSprite;
                wardrobeOverlay.gameObject.SetActive(true);
                currentEquippedItem = item;
                ApplyItemSettings(item.name.ToLower());
            }
            else
            {
                ClearWardrobeItem();
            }
        }
        else
        {
            ClearWardrobeItem();
        }
    }

    // Apply custom settings including rotation
    private void ApplyItemSettings(string itemName)
    {
        if (itemSettings.ContainsKey(itemName))
        {
            var settings = itemSettings[itemName];
            
            wardrobeOverlay.rectTransform.anchoredPosition = settings.positionOffset;
            wardrobeOverlay.rectTransform.sizeDelta = settings.size;
            wardrobeOverlay.rectTransform.localScale = Vector3.one * settings.scale;
            
            Vector3 rotation = new Vector3(settings.rotationX, settings.rotationY, settings.rotationZ);
            wardrobeOverlay.rectTransform.localRotation = Quaternion.Euler(rotation);
        }
        else
        {
            ResetWardrobeTransform();
        }
    }

    // Reset wardrobe transform to defaults
    private void ResetWardrobeTransform()
    {
        if (wardrobeOverlay != null)
        {
            wardrobeOverlay.rectTransform.anchoredPosition = Vector2.zero;
            wardrobeOverlay.rectTransform.sizeDelta = new Vector2(100, 100);
            wardrobeOverlay.rectTransform.localScale = Vector3.one;
            wardrobeOverlay.rectTransform.localRotation = Quaternion.identity;
        }
    }

    // Method to unequip current item with rotation reset
    public void UnequipWardrobeItem()
    {
        if (wardrobeOverlay != null)
        {
            // Simply disable the overlay - no need to set sprite
            wardrobeOverlay.gameObject.SetActive(false);
            ResetWardrobeTransform();
            currentEquippedItem = null;
        }
    }

    // Clear wardrobe item (simply hide it)
    private void ClearWardrobeItem()
    {
        if (wardrobeOverlay != null)
        {
            wardrobeOverlay.gameObject.SetActive(false);
            currentEquippedItem = null;
        }
    }

    // ---- External call (from ScreenTime manager) ----
    public void SetEnergy(float screenBatteryPercent)
    {
        // ADDED: Debug log to see what percentage we're receiving
        Debug.Log($"[BlinkyManager] SetEnergy called with: {screenBatteryPercent}%");
        
        int emotion;
        
        // Fixed emotion mapping for battery concept:
        // 100% battery (full) = HAPPY
        // 75-99% = Still HAPPY
        // 50-74% = SAD (battery getting low)
        // 25-49% = TIRED (battery quite low)
        // 0-24% = ANGRY (battery critical)
        
        // ADDED: Clamp percentage to ensure it's valid
        screenBatteryPercent = Mathf.Clamp(screenBatteryPercent, 0f, 100f);
        
        if (screenBatteryPercent >= 75f) 
        {
            emotion = 0; // Happy
        }
        else if (screenBatteryPercent >= 50f) 
        {
            emotion = 1; // Sad
        }
        else if (screenBatteryPercent >= 25f) 
        {
            emotion = 2; // Tired
        }
        else 
        {
            emotion = 3; // Angry
        }

        Debug.Log($"[BlinkyManager] Battery: {screenBatteryPercent}% → {GetEmotionName(emotion)}");

        if (emotion == currentEmotion) 
        {
            return; // no change - no debug log
        }
        
        currentEmotion = emotion;

        UpdateSprite();
        // REMOVED: UpdateWardrobeWithEmotion() - wardrobe visibility should not depend on emotion

        if (bubbleText != null)
        {
            switch (emotion)
            {
                case 0: bubbleText.text = "Yay! Full battery!"; break;
                case 1: bubbleText.text = "Battery is getting low..."; break;
                case 2: bubbleText.text = "I'm feeling tired..."; break;
                case 3: bubbleText.text = "Battery critical! Need charge!"; break;
            }
        }
    }

    // Helper method to get emotion name
    private string GetEmotionName(int emotion)
    {
        return emotion switch
        {
            0 => "HAPPY",
            1 => "SAD", 
            2 => "TIRED",
            3 => "ANGRY",
            _ => "UNKNOWN"
        };
    }

    private void UpdateSprite()
    {
        Sprite open = currentEmotion switch
        {
            0 => happyOpen,
            1 => sadOpen,
            2 => tiredOpen,
            3 => angryOpen,
            _ => happyOpen // default
        };

        if (uiImage != null) 
        {
            uiImage.sprite = open;
        }
    }

    // Get the appropriate closed-eye sprite for current emotion
    private Sprite GetClosedEyesSprite()
    {
        return currentEmotion switch
        {
            0 => happyClosed,
            1 => sadClosed,
            2 => tiredClosed,
            3 => angryClosed,
            _ => happyClosed // default
        };
    }

    // Blink loop now uses emotion-specific closed eyes
    private IEnumerator BlinkLoop()
    {
        while (true)
        {
            float wait = Random.Range(blinkIntervalMin, blinkIntervalMax);
            yield return new WaitForSeconds(wait);

            // closed eyes - use emotion-specific closed sprite
            if (uiImage != null) 
            {
                uiImage.sprite = GetClosedEyesSprite();
            }

            yield return new WaitForSeconds(blinkDuration);

            // reopen eyes
            UpdateSprite();
        }
    }

    // Manual refresh with database wait
    [ContextMenu("Refresh Equipment")]
    public void RefreshEquipment()
    {
        if (Application.isPlaying)
        {
            StartCoroutine(WaitForDatabaseAndLoadEquipment());
        }
    }

    // Test method to manually set emotions
    [ContextMenu("Test Emotion: Happy (100%)")]
    public void TestHappy() => SetEnergy(100f);

    [ContextMenu("Test Emotion: Sad (66%)")]
    public void TestSad() => SetEnergy(66f);

    [ContextMenu("Test Emotion: Tired (33%)")]
    public void TestTired() => SetEnergy(33f);

    [ContextMenu("Test Emotion: Angry (10%)")]
    public void TestAngry() => SetEnergy(10f);

    // Get currently equipped item
    public WardrobeItem GetCurrentEquippedItem()
    {
        return currentEquippedItem;
    }

    // Debug method with rotation info
    [ContextMenu("Debug Equipment Status")]
    public void DebugEquipmentStatus()
    {
        Debug.Log($"[BlinkyManager] Equipment Status:");
        Debug.Log($"- Current Equipped Item: {currentEquippedItem?.name ?? "None"}");
        Debug.Log($"- Overlay Active: {wardrobeOverlay?.gameObject.activeSelf ?? false}");
        
        if (currentEquippedItem != null && wardrobeOverlay != null)
        {
            var rect = wardrobeOverlay.rectTransform;
            var rotation = rect.localRotation.eulerAngles;
            Debug.Log($"- Position: {rect.anchoredPosition}, Size: {rect.sizeDelta}");
            Debug.Log($"- Rotation: {rotation}");
        }
    }

    // Debug current state
    [ContextMenu("Debug Current State")]
    public void DebugCurrentState()
    {
        Debug.Log($"Current Emotion: {currentEmotion} ({GetEmotionName(currentEmotion)})");
        Debug.Log($"UI Image Sprite: {uiImage?.sprite?.name ?? "NULL"}");
        Debug.Log($"Equipped Item: {currentEquippedItem?.name ?? "NONE"}");
        Debug.Log($"Wardrobe Overlay Active: {wardrobeOverlay?.gameObject.activeSelf ?? false}");
    }
    
    // ADDED: Public method to get current emotion for debugging
    public int GetCurrentEmotion()
    {
        return currentEmotion;
    }
    
    // ADDED: Public method to get current emotion name for debugging
    public string GetCurrentEmotionName()
    {
        return GetEmotionName(currentEmotion);
    }
    
    // NEW: Safely check if we should show wardrobe
    public bool ShouldShowWardrobe()
    {
        return currentEquippedItem != null && wardrobeOverlay != null;
    }
    
    // NEW: Force update wardrobe display
    public void ForceUpdateWardrobe()
    {
        if (currentEquippedItem != null)
        {
            EquipWardrobeItem(currentEquippedItem);
        }
        else
        {
            UnequipWardrobeItem();
        }
    }
}