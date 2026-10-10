using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class Wardrobe : MonoBehaviour
{
    public Transform contentParent;
    public GameObject itemPrefab;
    public int userId;

    private void Start()
    {
        DisplayOwnedItems();
    }

    public void DisplayOwnedItems()
    {
        // Clear old items first
        foreach (Transform t in contentParent)
            Destroy(t.gameObject);

        // Get all items owned by the user
        List<UserWardrobe> owned = ShopManager.Instance.GetUserWardrobe(userId);

        foreach (var uw in owned)
        {
            // Find item info
            var item = ShopManager.Instance.GetAllShopItems().Find(x => x.item_id == uw.item_id);
            if (item == null) continue;

            GameObject go = Instantiate(itemPrefab, contentParent);

            // Item name
            go.transform.Find("ItemName").GetComponent<TMP_Text>().text = item.name;

            // Item image
            // Item image
            var imageComponent = go.transform.Find("ItemImage").GetComponent<Image>();
            Sprite itemSprite = ImageUtils.SpriteFromBytes(item.image); // <-- use byte[] version
            if (itemSprite != null) imageComponent.sprite = itemSprite;


            // Button
            Button btn = go.transform.Find("ActionButton").GetComponent<Button>();
            TMP_Text btnText = btn.transform.Find("ButtonText").GetComponent<TMP_Text>();

            if (uw.is_equipped) // <-- fixed bool check
            {
                btnText.text = "Equipped";
                btn.interactable = false;
            }
            else
            {
                btnText.text = "Equip";
                btn.interactable = true;
                btn.onClick.AddListener(() =>
                {
                    // Unequip all other items
                    foreach (var other in owned)
                        other.is_equipped = false;

                    // Equip this one
                    uw.is_equipped = true;

                    // Save changes to DB
                    DatabaseManager.Instance.SaveOrUpdateUserWardrobe(uw);

                    // Refresh UI
                    DisplayOwnedItems();
                });
            }
        }
    }
}
