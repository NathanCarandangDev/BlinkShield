using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Collections;

public class EyeIssuesPage : MonoBehaviour
{
    [Header("UI References")]
    public Transform issuesContainer;
    public GameObject issuePrefab;

    private bool _isLoading = false;

    void Start()
    {
        StartCoroutine(WaitAndLoadIssues());
    }

    void OnEnable()
    {
        // Reload when page becomes active again
        if (!_isLoading && issuesContainer.childCount == 0)
        {
            StartCoroutine(WaitAndLoadIssues());
        }
    }

    private IEnumerator WaitAndLoadIssues()
    {
        if (_isLoading) yield break;
        _isLoading = true;

        // Wait for DatabaseManager to be ready
        yield return new WaitUntil(() => DatabaseManager.Instance != null && DatabaseManager.Instance.IsDatabaseReady());
        
        // Additional safety delay
        yield return new WaitForSeconds(0.5f);

        LoadIssues();
        _isLoading = false;
    }

    private void LoadIssues()
    {
        if (DatabaseManager.Instance == null)
        {
            Debug.LogError("[EyeIssuesPage] DatabaseManager instance not found.");
            return;
        }

        // Get all issues (global/static content, not user-specific)
        List<EyeIssue> allIssues = DatabaseManager.Instance.GetAll<EyeIssue>();

        if (allIssues == null || allIssues.Count == 0)
        {
            Debug.LogWarning("[EyeIssuesPage] No eye issues found in database.");
            return;
        }

        PopulateUI(allIssues);
    }

    private void PopulateUI(List<EyeIssue> issues)
    {
        foreach (Transform child in issuesContainer)
            Destroy(child.gameObject);

        foreach (var issue in issues)
        {
            GameObject issueButton = Instantiate(issuePrefab, issuesContainer);
            issueButton.SetActive(true);
            issueButton.name = "Button_" + issue.title;

            // Set button text (just the title)
            var buttonText = issueButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText != null)
            {
                buttonText.text = issue.title ?? "No Title";
                Debug.Log($"🟢 Set issue button text to: '{buttonText.text}'");
            }
            else
            {
                Debug.LogError("❌ No TextMeshProUGUI found in issue button prefab!");
            }

            // Add click listener to show full issue in popup
            var button = issueButton.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                Debug.Log($"🟢 Issue button component found: {button.gameObject.name}");
                
                // Clear any existing listeners and add ours
                button.onClick.RemoveAllListeners();
                
                // Add the actual functionality - show issue in popup
                button.onClick.AddListener(() => {
                    Debug.Log($"🎯 ISSUE BUTTON CLICKED: {issue.title}");
                    ShowIssuePopup(issue);
                });
                
                Debug.Log($"🟢 Issue button click listeners added for: {issue.title}");
            }
            else
            {
                Debug.LogError("❌ No Button component found in issue button!");
            }
        }
        
        Debug.Log($"🔴 Finished PopulateUI. Container now has {issuesContainer.childCount} issue buttons");
    }

    private void ShowIssuePopup(EyeIssue issue)
    {
        Debug.Log($"🎯🎯🎯 ShowIssuePopup CALLED for: {issue.title}");
        
        if (UIManager.Instance != null)
        {
            Debug.Log("🟢 UIManager.Instance found!");
            
            // Format the content for the popup - ONLY suggested action, no Learn More
            string popupContent = FormatIssueContent(issue);
            string popupTitle = issue.title ?? "Eye Issue";
            
            Debug.Log($"🎯 Calling UIManager to show issue popup: {popupTitle}");
            
            // Call UIManager - the link will be used for the Learn More button separately
            UIManager.Instance.ShowIssuePopup(popupTitle, popupContent, issue.link);
            
            Debug.Log("🎯 UIManager.ShowIssuePopup called");
        }
        else
        {
            Debug.LogError("❌❌❌ UIManager instance not found!");
        }
    }

    private string FormatIssueContent(EyeIssue issue)
    {
        System.Text.StringBuilder content = new System.Text.StringBuilder();
        
        // Add description
        if (!string.IsNullOrEmpty(issue.description))
        {
            content.AppendLine(issue.description);
            content.AppendLine();
        }
        
        // Add suggested action ONLY - Learn More is handled by the separate button
        if (!string.IsNullOrEmpty(issue.suggested_action))
        {
            content.AppendLine("<b>Suggested Action:</b>");
            content.AppendLine(issue.suggested_action);
            // Removed the Learn More section since it has its own button
        }
        
        return content.ToString().Trim();
    }

    // Alternative method if you want to reuse the existing tip popup
    private void ShowIssuePopupAsTip(EyeIssue issue)
    {
        Debug.Log($"🎯🎯🎯 ShowIssuePopupAsTip CALLED for: {issue.title}");
        
        if (UIManager.Instance != null)
        {
            Debug.Log("🟢 UIManager.Instance found!");
            
            // Format the content
            string popupContent = FormatIssueContent(issue);
            string popupTitle = issue.title ?? "Eye Issue";
            
            Debug.Log($"🎯 Calling UIManager.ShowTipPopup for issue: {popupTitle}");
            
            // Use the existing tip popup system
            UIManager.Instance.ShowTipPopup(popupTitle, popupContent);
            
            Debug.Log("🎯 UIManager.ShowTipPopup called for issue");
        }
        else
        {
            Debug.LogError("❌❌❌ UIManager instance not found!");
        }
    }
}