using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class EyeTriviaPopup : MonoBehaviour
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
        Debug.LogError($"🔴 EyeTriviaPopup Start - GameObject: {gameObject.name}");
        
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
        Debug.LogError("🔴 EyeTriviaPopup initialized and hidden");
    }

    public void ShowTrivia(string title, string content, string link = "")
    {
        Debug.LogError($"🎯🎯🎯 EyeTriviaPopup.ShowTrivia CALLED!");
        Debug.LogError($"🎯 Title: {title ?? "No title"}");
        Debug.LogError($"🎯 Content length: {content?.Length ?? 0} chars");
        Debug.LogError($"🎯 Link: {link ?? "No link"}");
        Debug.LogError($"🎯 Popup active before: {gameObject.activeInHierarchy}");

        // Set title
        if (titleText != null)
        {
            titleText.text = title ?? "Eye Care Trivia";
            Debug.LogError($"✅ Title text set: {titleText.text}");
        }
        else
        {
            Debug.LogError("❌ titleText is null!");
        }

        // Set content
        if (contentText != null)
        {
            contentText.text = content ?? "No content available";
            Debug.LogError($"✅ Content text set: {contentText.text}");
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
            
            // Set the button text to a placeholder like "Learn More" or "Discover More"
            var buttonText = linkButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null && hasLink)
            {
                buttonText.text = "Learn More"; // Or "Learn More", "Read More", etc.
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
        
        Debug.LogError($"🎯 ShowTrivia completed - Trivia popup should be visible now");
    }

    public void ShowTriviaFormatted(string title, string description, string funFact = "", string link = "")
    {
        Debug.LogError($"🎯🎯🎯 EyeTriviaPopup.ShowTriviaFormatted CALLED!");
        
        // Set title
        if (titleText != null)
            titleText.text = title ?? "Eye Care Trivia";

        // Format the content with description and fun fact
        System.Text.StringBuilder contentBuilder = new System.Text.StringBuilder();
        
        if (!string.IsNullOrEmpty(description))
        {
            contentBuilder.AppendLine(description);
            contentBuilder.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(funFact))
        {
            contentBuilder.AppendLine("<b>Fun Fact:</b>");
            contentBuilder.AppendLine(funFact);
        }
        
        if (contentText != null)
            contentText.text = contentBuilder.ToString().Trim();

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
                buttonText.text = "Discover More"; // Clickable placeholder
            }
        }
        
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
            
        Debug.LogError($"🎯 ShowTriviaFormatted completed");
    }

    public void ClosePopup()
    {
        Debug.LogError("🎯 EyeTriviaPopup ClosePopup called");
        
        if (UIManager.Instance != null)
        {
            Debug.LogError("✅ Calling UIManager.CloseTriviaPopup");
            UIManager.Instance.CloseTriviaPopup();
        }
        else
        {
            Debug.LogError("❌ UIManager instance not found, hiding popup directly");
            gameObject.SetActive(false);
        }
    }

    private void OpenLink()
    {
        Debug.LogError($"🎯 EyeTriviaPopup OpenLink called - URL: {currentLink}");
        
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
        currentLink = "";
        
        if (linkButton != null)
            linkButton.gameObject.SetActive(false);
    }
}