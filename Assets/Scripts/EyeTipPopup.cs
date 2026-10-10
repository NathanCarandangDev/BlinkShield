using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class EyeTipPopup : MonoBehaviour
{
    [Header("Popup References")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI contentText;
    public Button closeButton;
    public Button linkButton;
    public ScrollRect scrollRect;
    
    private string currentLink;

    void Start()
    {
        Debug.Log($"🔴 EyeTipPopup Start - GameObject: {gameObject.name}");
        
        // Check if references are set
        if (titleText == null) Debug.LogError("❌ titleText is not assigned!");
        if (contentText == null) Debug.LogError("❌ contentText is not assigned!");
        if (closeButton == null) Debug.LogError("❌ closeButton is not assigned!");
        if (scrollRect == null) Debug.LogError("❌ scrollRect is not assigned!");
        if (linkButton == null) Debug.LogWarning("⚠️ linkButton is not assigned (optional)");

        closeButton.onClick.AddListener(ClosePopup);
        
        // Only add link button listener if it exists
        if (linkButton != null)
            linkButton.onClick.AddListener(OpenLink);
            
        gameObject.SetActive(false);
        Debug.Log($"🔴 EyeTipPopup initialized and hidden");
    }

    public void ShowTip(string title, string content, string link = "")
    {
        Debug.Log($"🎯🎯🎯 EyeTipPopup.ShowTip CALLED!");
        Debug.Log($"🎯 Title: {title ?? "No title"}");
        Debug.Log($"🎯 Content length: {content?.Length ?? 0} chars");
        Debug.Log($"🎯 Link: {link ?? "No link"}");
        Debug.Log($"🎯 Popup active before: {gameObject.activeInHierarchy}");

        // Activate the popup first
        gameObject.SetActive(true);

        // Set title
        if (titleText != null)
        {
            titleText.text = title ?? "Eye Care Tip";
            Debug.Log($"✅ Title text set: {titleText.text}");
        }
        else
        {
            Debug.LogError("❌ titleText is null!");
        }

        // Set content
        if (contentText != null)
        {
            contentText.text = content ?? "No content available";
            Debug.Log($"✅ Content text set: {contentText.text}");
        }
        else
        {
            Debug.LogError("❌ contentText is null!");
        }

        // Handle link button - show clickable placeholder text, not the actual URL
        currentLink = link;
        if (linkButton != null)
        {
            bool hasLink = !string.IsNullOrEmpty(link);
            linkButton.gameObject.SetActive(hasLink);
            
            // Set the button text to a placeholder like "Learn More"
            var buttonText = linkButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null && hasLink)
            {
                buttonText.text = "Learn More";
                Debug.Log($"✅ Link button text set to: {buttonText.text}");
            }
            
            Debug.Log($"✅ Link button active: {hasLink}");
        }
        
        // Reset scroll position
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            Debug.Log("✅ Scroll position reset to top");
        }
        
        Debug.Log($"🎯 ShowTip completed - Tip popup should be visible now");
    }

    public void ClosePopup()
    {
        Debug.Log("🎯 EyeTipPopup ClosePopup called");
        
        if (UIManager.Instance != null)
        {
            Debug.Log("✅ Calling UIManager.CloseTipPopup");
            UIManager.Instance.CloseTipPopup();
        }
        else
        {
            Debug.LogError("❌ UIManager instance not found, hiding popup directly");
            gameObject.SetActive(false);
        }
    }

    private void OpenLink()
    {
        Debug.Log($"🎯 EyeTipPopup OpenLink called - URL: {currentLink}");
        
        if (!string.IsNullOrEmpty(currentLink))
        {
            string safeUrl = currentLink.Trim();
            if (!safeUrl.StartsWith("http"))
                safeUrl = "https://" + safeUrl;
                
            Debug.Log($"🎯 Opening URL: {safeUrl}");
            Application.OpenURL(safeUrl);
        }
        else
        {
            Debug.Log("🎯 No link to open!");
        }
    }

    // Helper method to clear all fields
    public void Clear()
    {
        if (titleText != null) titleText.text = "";
        if (contentText != null) contentText.text = "";
        currentLink = "";
        
        if (linkButton != null)
            linkButton.gameObject.SetActive(false);
    }
}