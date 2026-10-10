using UnityEngine;
using System.Collections.Generic;
using TMPro;
using System.Collections;

public class EyeTipsManager : MonoBehaviour
{
    [Header("UI References")]
    public Transform tipsContainer;
    public GameObject tipPrefab;

    private bool _isLoading = false;

    void Start()
    {
        StartCoroutine(WaitAndLoadTips());
    }

    void OnEnable()
    {
        // Reload when page becomes active again
        if (!_isLoading && tipsContainer.childCount == 0)
        {
            StartCoroutine(WaitAndLoadTips());
        }
    }

    private IEnumerator WaitAndLoadTips()
    {
        if (_isLoading) yield break;
        _isLoading = true;

        // Wait for DatabaseManager to be ready
        yield return new WaitUntil(() => DatabaseManager.Instance != null && DatabaseManager.Instance.IsDatabaseReady());
        
        // Additional safety delay
        yield return new WaitForSeconds(0.5f);

        LoadTips();
        _isLoading = false;
    }

    private void LoadTips()
    {
        if (DatabaseManager.Instance == null)
        {
            Debug.LogError("[EyeTipsManager] DatabaseManager instance not found!");
            return;
        }

        List<EyeTip> tips = DatabaseManager.Instance.GetAll<EyeTip>();

        if (tips == null || tips.Count == 0)
        {
            Debug.LogWarning("[EyeTipsManager] No tips found in database.");
            return;
        }

        PopulateUI(tips);
    }

    private void PopulateUI(List<EyeTip> tips)
    {
        foreach (Transform child in tipsContainer)
            Destroy(child.gameObject);

        foreach (var tip in tips)
        {
            if (tip == null) continue;

            GameObject tipButton = Instantiate(tipPrefab, tipsContainer);
            tipButton.SetActive(true);
            tipButton.name = "Button_" + tip.title;

            // Set button text (just the title)
            var buttonText = tipButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText != null)
            {
                buttonText.text = tip.title ?? "No Title";
                Debug.Log($"🟢 Set tip button text to: '{buttonText.text}'");
            }
            else
            {
                Debug.LogError("❌ No TextMeshProUGUI found in tip button prefab!");
            }

            // Add click listener to show full tip in popup
            var button = tipButton.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                Debug.Log($"🟢 Tip button component found: {button.gameObject.name}");
                
                // Clear any existing listeners and add ours
                button.onClick.RemoveAllListeners();
                
                // Add the actual functionality - show tip in popup
                button.onClick.AddListener(() => {
                    Debug.Log($"🎯 TIP BUTTON CLICKED: {tip.title}");
                    ShowTipPopup(tip);
                });
                
                Debug.Log($"🟢 Tip button click listeners added for: {tip.title}");
            }
            else
            {
                Debug.LogError("❌ No Button component found in tip button!");
            }
        }
        
        Debug.Log($"🔴 Finished PopulateUI. Container now has {tipsContainer.childCount} tip buttons");
    }

    private void ShowTipPopup(EyeTip tip)
    {
        Debug.Log($"🎯🎯🎯 ShowTipPopup CALLED for: {tip.title}");
        
        if (UIManager.Instance != null)
        {
            Debug.Log("🟢 UIManager.Instance found!");
            
            // Format the content for the popup - WITHOUT Learn More section
            string popupContent = FormatTipContent(tip);
            string popupTitle = tip.title ?? "Eye Care Tip";
            
            Debug.Log($"🎯 Calling UIManager to show tip popup: {popupTitle}");
            
            // Call UIManager with the link parameter - the link will be used for the Learn More button
            UIManager.Instance.ShowTipPopup(popupTitle, popupContent, tip.link);
            
            Debug.Log("🎯 UIManager.ShowTipPopup called with link: " + (tip.link ?? "null"));
        }
        else
        {
            Debug.LogError("❌❌❌ UIManager instance not found!");
        }
    }

    private string FormatTipContent(EyeTip tip)
    {
        System.Text.StringBuilder content = new System.Text.StringBuilder();
        
        // Add content ONLY - Learn More is handled by the separate button
        if (!string.IsNullOrEmpty(tip.content))
        {
            content.AppendLine(tip.content);
            // Removed the Learn More section since it has its own button now
        }
        
        return content.ToString().Trim();
    }
}