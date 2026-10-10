using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class EyeIssuePopup : MonoBehaviour
{
    [Header("Popup References")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI contentText;
    public TextMeshProUGUI suggestedActionText;
    public Button closeButton;
    public Button linkButton;
    public ScrollRect scrollRect;
    
    private string currentLink;

    void Start()
    {
        Debug.LogError($"🔴 EyeIssuePopup Start - GameObject: {gameObject.name}");
        
        // Check if references are set
        if (titleText == null) Debug.LogError("❌ titleText is not assigned!");
        if (contentText == null) Debug.LogError("❌ contentText is not assigned!");
        if (suggestedActionText == null) Debug.LogError("❌ suggestedActionText is not assigned!");
        if (closeButton == null) Debug.LogError("❌ closeButton is not assigned!");
        if (scrollRect == null) Debug.LogError("❌ scrollRect is not assigned!");
        if (linkButton == null) Debug.LogWarning("⚠️ linkButton is not assigned (optional)");

        closeButton.onClick.AddListener(ClosePopup);
        
        // Only add link button listener if it exists
        if (linkButton != null)
            linkButton.onClick.AddListener(OpenLink);
            
        gameObject.SetActive(false);
        Debug.LogError("🔴 EyeIssuePopup initialized and hidden");
    }

    public void ShowIssue(string title, string content, string link = "")
    {
        Debug.LogError($"🎯🎯🎯 EyeIssuePopup.ShowIssue CALLED!");
        Debug.LogError($"🎯 Title: {title ?? "No title"}");
        Debug.LogError($"🎯 Content length: {content?.Length ?? 0} chars");
        Debug.LogError($"🎯 Link: {link ?? "No link"}");
        Debug.LogError($"🎯 Popup active before: {gameObject.activeInHierarchy}");

        // Set title
        if (titleText != null)
        {
            titleText.text = title ?? "Eye Issue";
            Debug.LogError($"✅ Title text set: {titleText.text}");
        }
        else
        {
            Debug.LogError("❌ titleText is null!");
        }

        // Set main content
        if (contentText != null)
        {
            contentText.text = content ?? "No content available";
            Debug.LogError($"✅ Content text set: {contentText.text}");
        }
        else
        {
            Debug.LogError("❌ contentText is null!");
        }

        // Set suggested action (extract from content or leave empty)
        if (suggestedActionText != null)
        {
            // You can parse the content to extract suggested action or leave it empty
            // For now, we'll leave it empty or set a default
            suggestedActionText.text = ""; // Or extract from content if structured
            Debug.LogError("✅ Suggested action text cleared");
        }

        // Handle link button - show clickable placeholder text, not the actual URL
        currentLink = link;
        if (linkButton != null)
        {
            bool hasLink = !string.IsNullOrEmpty(link);
            linkButton.gameObject.SetActive(hasLink);
            
            // Set the button text to a placeholder like "Learn More" or "Click for more info"
            var buttonText = linkButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null && hasLink)
            {
                buttonText.text = "Learn More"; // Or "Click for more info", "View Resource", etc.
                Debug.LogError($"✅ Link button text set to: {buttonText.text}");
            }
            
            Debug.LogError($"✅ Link button active: {hasLink}");
        }
        
        // Reset scroll position
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            Debug.LogError("✅ Scroll position reset to top");
        }
        
        Debug.LogError($"🎯 ShowIssue completed - Issue popup should be visible now");
    }

    public void ShowIssueFormatted(string title, string description, string suggestedAction, string link = "")
    {
        Debug.LogError($"🎯🎯🎯 EyeIssuePopup.ShowIssueFormatted CALLED!");
        
        // Set title
        if (titleText != null)
            titleText.text = title ?? "Eye Issue";

        // Format the content with description and suggested action
        System.Text.StringBuilder contentBuilder = new System.Text.StringBuilder();
        
        if (!string.IsNullOrEmpty(description))
        {
            contentBuilder.AppendLine(description);
            contentBuilder.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(suggestedAction))
        {
            contentBuilder.AppendLine("<b>Suggested Action:</b>");
            contentBuilder.AppendLine(suggestedAction);
        }
        
        if (contentText != null)
            contentText.text = contentBuilder.ToString().Trim();

        // Set suggested action separately if you have a dedicated field
        if (suggestedActionText != null && !string.IsNullOrEmpty(suggestedAction))
        {
            suggestedActionText.text = suggestedAction;
        }

        // Handle link button with placeholder text
        currentLink = link;
        if (linkButton != null)
        {
            bool hasLink = !string.IsNullOrEmpty(link);
            linkButton.gameObject.SetActive(hasLink);
            
            // Set placeholder text for the link button
            var buttonText = linkButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null && hasLink)
            {
                buttonText.text = "Learn More"; // Clickable placeholder
            }
        }
        
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
            
        Debug.LogError($"🎯 ShowIssueFormatted completed");
    }

    public void ClosePopup()
    {
        Debug.LogError("🎯 EyeIssuePopup ClosePopup called");
        
        if (UIManager.Instance != null)
        {
            Debug.LogError("✅ Calling UIManager.CloseIssuePopup");
            UIManager.Instance.CloseIssuePopup();
        }
        else
        {
            Debug.LogError("❌ UIManager instance not found, hiding popup directly");
            gameObject.SetActive(false);
        }
    }

    private void OpenLink()
    {
        Debug.LogError($"🎯 EyeIssuePopup OpenLink called - URL: {currentLink}");
        
        if (!string.IsNullOrEmpty(currentLink))
        {
            string safeUrl = currentLink.Trim();
            if (!safeUrl.StartsWith("http"))
                safeUrl = "https://" + safeUrl;
                
            Debug.LogError($"🎯 Opening URL: {safeUrl}");
            Application.OpenURL(safeUrl);
        }
        else
        {
            Debug.LogError("🎯 No link to open!");
        }
    }

    // Helper method to clear all fields
    public void Clear()
    {
        if (titleText != null) titleText.text = "";
        if (contentText != null) contentText.text = "";
        if (suggestedActionText != null) suggestedActionText.text = "";
        currentLink = "";
        
        if (linkButton != null)
            linkButton.gameObject.SetActive(false);
    }
}