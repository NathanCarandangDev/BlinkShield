using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public class ShopPopup : MonoBehaviour
{
    public static ShopPopup Instance { get; private set; }

    [Header("Popup References")]
    public GameObject popupPanel;
    public Image itemImage;
    public TMP_Text itemNameText;
    public TMP_Text itemPriceText;
    public TMP_Text descriptionText;
    public Button purchaseButton;
    public Button cancelButton;

    [Header("Item Preview Images - Assign in Inspector")]
    public List<ItemImageMapping> itemPreviews = new List<ItemImageMapping>();

    [System.Serializable]
    public class ItemImageMapping
    {
        public string itemName;
        public Sprite previewSprite;
    }

    private Action onPurchaseCallback;
    private Action onCancelCallback;
    private WardrobeItem currentItem;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        // Hide popup initially
        if (popupPanel != null)
            popupPanel.SetActive(false);

        // Setup button listeners
        if (purchaseButton != null)
            purchaseButton.onClick.AddListener(OnPurchaseClicked);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);
    }

    /// <summary>
    /// Show the purchase popup for an item with points check
    /// </summary>
    public void Show(WardrobeItem item, int userPoints, Action onPurchase = null, Action onCancel = null)
    {
        if (popupPanel == null)
        {
            Debug.LogError("[ShopPopup] Popup panel is not assigned!");
            return;
        }

        // Store current item and callbacks
        currentItem = item;
        onPurchaseCallback = onPurchase;
        onCancelCallback = onCancel;

        // Populate UI with item data - NAME COMES FROM CLICKED ITEM
        if (itemNameText != null)
            itemNameText.text = !string.IsNullOrEmpty(item.name) ? item.name : "Unnamed Item";

        if (itemPriceText != null)
            itemPriceText.text = $"{item.price}g";

        // Check if user has enough points and set appropriate message
        bool canAfford = userPoints >= item.price;
        
        if (descriptionText != null)
        {
            if (canAfford)
            {
                descriptionText.text = $"Do you want to buy this item for {item.price}g?\n\nYou have: {userPoints}g\nAfter purchase: {userPoints - item.price}g";
            }
            else
            {
                descriptionText.text = $"You don't have enough points!\n\nYou need: {item.price}g\nYou have: {userPoints}g\nMissing: {item.price - userPoints}g";
            }
        }

        // Set item image from PRE-ASSIGNED PREVIEW SPRITES
        SetPreviewImage(item.name);

        // Set purchase button state based on affordability
        if (purchaseButton != null)
        {
            purchaseButton.interactable = canAfford;
            TMP_Text purchaseText = purchaseButton.GetComponentInChildren<TMP_Text>();
            if (purchaseText != null)
            {
                purchaseText.text = canAfford ? $"Purchase ({item.price}g)" : "Not Enough Points";
            }
        }

        // Set button texts
        if (cancelButton != null)
        {
            TMP_Text cancelText = cancelButton.GetComponentInChildren<TMP_Text>();
            if (cancelText != null)
                cancelText.text = "Cancel";
        }

        // Show the popup
        popupPanel.SetActive(true);
        Debug.Log($"[ShopPopup] Showing popup for: {item.name}. User points: {userPoints}, Item price: {item.price}, Can afford: {canAfford}");
    }

    /// <summary>
    /// Set the preview image from pre-assigned sprites based on item name
    /// </summary>
    private void SetPreviewImage(string itemName)
    {
        if (itemImage == null)
        {
            Debug.LogWarning("[ShopPopup] ItemImage is not assigned!");
            return;
        }

        // Find the preview sprite for this item
        Sprite foundSprite = FindPreviewSprite(itemName);

        if (foundSprite != null)
        {
            itemImage.sprite = foundSprite;
            itemImage.gameObject.SetActive(true);
            Debug.Log($"[ShopPopup] Set preview image for: {itemName}");
        }
        else
        {
            itemImage.gameObject.SetActive(false);
            Debug.LogWarning($"[ShopPopup] No preview image found for: {itemName}");
        }
    }

    /// <summary>
    /// Find the pre-assigned preview sprite for an item by name
    /// </summary>
    private Sprite FindPreviewSprite(string itemName)
    {
        if (itemPreviews == null || itemPreviews.Count == 0)
        {
            Debug.LogWarning("[ShopPopup] No preview images assigned in Inspector!");
            return null;
        }

        // Try exact match first
        foreach (var mapping in itemPreviews)
        {
            if (mapping.itemName == itemName && mapping.previewSprite != null)
            {
                return mapping.previewSprite;
            }
        }

        // Try case-insensitive match
        foreach (var mapping in itemPreviews)
        {
            if (string.Equals(mapping.itemName, itemName, StringComparison.OrdinalIgnoreCase) && mapping.previewSprite != null)
            {
                return mapping.previewSprite;
            }
        }

        // Try partial match
        foreach (var mapping in itemPreviews)
        {
            if (mapping.itemName != null && itemName != null && 
                mapping.itemName.ToLower().Contains(itemName.ToLower()) && mapping.previewSprite != null)
            {
                return mapping.previewSprite;
            }
        }

        return null;
    }

    /// <summary>
    /// Hide the popup
    /// </summary>
    public void Hide()
    {
        if (popupPanel != null)
            popupPanel.SetActive(false);

        // Clear callbacks and current item
        onPurchaseCallback = null;
        onCancelCallback = null;
        currentItem = null;

        Debug.Log("[ShopPopup] Popup hidden");
    }

    /// <summary>
    /// Method to disable purchase button (for insufficient funds)
    /// </summary>
    public void DisablePurchaseButton(string disabledText = "Not Enough Points")
    {
        if (purchaseButton != null)
        {
            purchaseButton.interactable = false;
            TMP_Text purchaseText = purchaseButton.GetComponentInChildren<TMP_Text>();
            if (purchaseText != null)
                purchaseText.text = disabledText;
        }
    }

    private void OnPurchaseClicked()
    {
        Debug.Log($"[ShopPopup] Purchase button clicked for: {currentItem?.name}");
        onPurchaseCallback?.Invoke();
        Hide();
    }

    private void OnCancelClicked()
    {
        Debug.Log("[ShopPopup] Cancel button clicked");
        onCancelCallback?.Invoke();
        Hide();
    }

    /// <summary>
    /// Check if popup is currently visible
    /// </summary>
    public bool IsVisible()
    {
        return popupPanel != null && popupPanel.activeInHierarchy;
    }

    /// <summary>
    /// Get the current item being displayed
    /// </summary>
    public WardrobeItem GetCurrentItem()
    {
        return currentItem;
    }
}