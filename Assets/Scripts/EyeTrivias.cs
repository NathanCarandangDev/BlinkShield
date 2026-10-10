using UnityEngine;
using System.Collections.Generic;
using TMPro;
using System.Collections;

public class EyeCareTriviasPage : MonoBehaviour
{
    [Header("UI References")]
    public Transform triviasContainer;
    public GameObject triviaPrefab;

    private bool _isLoading = false;

    void Start()
    {
        StartCoroutine(WaitAndLoadTrivias());
    }

    void OnEnable()
    {
        // Reload when page becomes active again
        if (!_isLoading && triviasContainer.childCount == 0)
        {
            StartCoroutine(WaitAndLoadTrivias());
        }
    }

    private IEnumerator WaitAndLoadTrivias()
    {
        if (_isLoading) yield break;
        _isLoading = true;

        // Wait for DatabaseManager to be ready
        yield return new WaitUntil(() => DatabaseManager.Instance != null && DatabaseManager.Instance.IsDatabaseReady());
        
        // Additional safety delay
        yield return new WaitForSeconds(0.5f);

        LoadTrivias();
        _isLoading = false;
    }

    private void LoadTrivias()
    {
        if (DatabaseManager.Instance == null)
        {
            Debug.LogError("[EyeCareTriviasPage] DatabaseManager instance not found!");
            return;
        }

        List<EyeTrivia> trivias = DatabaseManager.Instance.GetAll<EyeTrivia>();

        if (trivias == null || trivias.Count == 0)
        {
            Debug.LogWarning("[EyeCareTriviasPage] No trivias found in database.");
            return;
        }

        PopulateUI(trivias);
    }

    private void PopulateUI(List<EyeTrivia> trivias)
    {
        foreach (Transform child in triviasContainer)
            Destroy(child.gameObject);

        foreach (var trivia in trivias)
        {
            if (trivia == null) continue;

            GameObject triviaButton = Instantiate(triviaPrefab, triviasContainer);
            triviaButton.SetActive(true);
            triviaButton.name = "Button_" + trivia.title;

            // Set button text (just the title)
            var buttonText = triviaButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText != null)
            {
                buttonText.text = trivia.title ?? "No Title";
                Debug.Log($"🟢 Set trivia button text to: '{buttonText.text}'");
            }
            else
            {
                Debug.LogError("❌ No TextMeshProUGUI found in trivia button prefab!");
            }

            // Add click listener to show full trivia in popup
            var button = triviaButton.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                Debug.Log($"🟢 Trivia button component found: {button.gameObject.name}");
                
                // Clear any existing listeners and add ours
                button.onClick.RemoveAllListeners();
                
                // Add the actual functionality - show trivia in popup
                button.onClick.AddListener(() => {
                    Debug.Log($"🎯 TRIVIA BUTTON CLICKED: {trivia.title}");
                    ShowTriviaPopup(trivia);
                });
                
                Debug.Log($"🟢 Trivia button click listeners added for: {trivia.title}");
            }
            else
            {
                Debug.LogError("❌ No Button component found in trivia button!");
            }
        }
        
        Debug.Log($"🔴 Finished PopulateUI. Container now has {triviasContainer.childCount} trivia buttons");
    }

    private void ShowTriviaPopup(EyeTrivia trivia)
    {
        Debug.Log($"🎯🎯🎯 ShowTriviaPopup CALLED for: {trivia.title}");
        
        if (UIManager.Instance != null)
        {
            Debug.Log("🟢 UIManager.Instance found!");
            
            // Format the content for the popup - WITHOUT Learn More section
            string popupContent = FormatTriviaContent(trivia);
            string popupTitle = trivia.title ?? "Eye Care Trivia";
            
            Debug.Log($"🎯 Calling UIManager to show trivia popup: {popupTitle}");
            
            // Call UIManager - the link will be used for the Learn More button separately
            UIManager.Instance.ShowTriviaPopup(popupTitle, popupContent, trivia.link);
            
            Debug.Log("🎯 UIManager.ShowTriviaPopup called");
        }
        else
        {
            Debug.LogError("❌❌❌ UIManager instance not found!");
        }
    }

    private string FormatTriviaContent(EyeTrivia trivia)
    {
        System.Text.StringBuilder content = new System.Text.StringBuilder();
        
        // Add description ONLY - Learn More is handled by the separate button
        if (!string.IsNullOrEmpty(trivia.description))
        {
            content.AppendLine(trivia.description);
            // Removed the Learn More section since it has its own button
        }
        
        return content.ToString().Trim();
    }

    // Alternative method if you want to reuse the existing tip popup
    private void ShowTriviaPopupAsTip(EyeTrivia trivia)
    {
        Debug.Log($"🎯🎯🎯 ShowTriviaPopupAsTip CALLED for: {trivia.title}");
        
        if (UIManager.Instance != null)
        {
            Debug.Log("🟢 UIManager.Instance found!");
            
            // Format the content
            string popupContent = FormatTriviaContent(trivia);
            string popupTitle = trivia.title ?? "Eye Care Trivia";
            
            Debug.Log($"🎯 Calling UIManager.ShowTipPopup for trivia: {popupTitle}");
            
            // Use the existing tip popup system
            UIManager.Instance.ShowTipPopup(popupTitle, popupContent);
            
            Debug.Log("🎯 UIManager.ShowTipPopup called for trivia");
        }
        else
        {
            Debug.LogError("❌❌❌ UIManager instance not found!");
        }
    }
}