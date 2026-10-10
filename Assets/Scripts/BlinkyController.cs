using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.InputSystem;

public class BlinkyController : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public SimpleBlinkDetector detector;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI scoreText;
    public GameObject blinkBG;
    public Button mainButton; 
    public Slider cooldownSlider; 

    [Header("Settings")]
    public float messageDuration = 2f;
    public float blinkSyncWindow = 1f;
    public float fadeDuration = 0.5f;
    public int maxGames = 3; 
    public float cooldownDuration = 1200f; 

    [Header("Game Balance")]
    public int perfectSyncScore = 10;
    public int goodSyncScore = 5;

    private bool isBlinking = false;
    private int gamesPlayed = 0;
    private bool gameStarted = false;
    private bool waitingForPlayerBlink = false;
    private float lastBlinkTime = 0f;
    private int totalScore = 0;
    private int successfulBlinks = 0;
    private int totalBlinks = 0;
    private bool isCooldownActive = false;
    private float cooldownEndTime = 0f;
    private CanvasGroup blinkBGCanvasGroup; 

    
    private PlayerInput playerInput;
    private InputAction blinkAction;

    void Start()
    {
        InitializeUI();
        SetupInputSystem();
        
        
        if (mainButton != null)
        {
            mainButton.gameObject.SetActive(true);
            mainButton.onClick.AddListener(OnMainButtonClicked);
            UpdateMainButtonText();
        }

        
        if (cooldownSlider != null)
        {
            cooldownSlider.gameObject.SetActive(false);
            cooldownSlider.minValue = 0f;
            cooldownSlider.maxValue = cooldownDuration;
        }

        
        if (blinkBG != null)
        {
            blinkBGCanvasGroup = blinkBG.GetComponent<CanvasGroup>();
            if (blinkBGCanvasGroup == null)
            {
                blinkBGCanvasGroup = blinkBG.AddComponent<CanvasGroup>();
            }
            blinkBGCanvasGroup.alpha = 0f;
        }

        
        if (detector != null)
        {
            var detectorType = detector.GetType();
            var isRunningProperty = detectorType.GetProperty("IsRunning");
            if (isRunningProperty != null)
            {
                isRunningProperty.SetValue(detector, false);
            }
        }
    }

    private void InitializeUI()
    {
        if (blinkBG != null) 
        {
            blinkBG.SetActive(false);
            if (blinkBGCanvasGroup != null)
                blinkBGCanvasGroup.alpha = 0f;
        }
        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
        if (scoreText != null) 
        {
            scoreText.text = "Ready to play?";
            scoreText.gameObject.SetActive(false);
        }
    }

    private void SetupInputSystem()
    {
        
        playerInput = GetComponent<PlayerInput>();
        if (playerInput == null)
        {
            playerInput = gameObject.AddComponent<PlayerInput>();
        }

        
        blinkAction = new InputAction("Blink", InputActionType.Button, "<Keyboard>/space");
        blinkAction.performed += OnBlinkPerformed;
        blinkAction.Enable();
    }

    private void OnBlinkPerformed(InputAction.CallbackContext context)
    {
        
        if (gameStarted && waitingForPlayerBlink && !isCooldownActive)
        {
            OnSuccessfulSync();
        }
    }

    void Update()
    {
        
        if (gameStarted && scoreText != null)
        {
            int remainingGames = maxGames - gamesPlayed;
            scoreText.text = $"Score: {totalScore} | Games: {remainingGames + 1}/{maxGames}";
        }

        
        UpdateCooldownUI();
    }

    private void UpdateCooldownUI()
    {
        if (isCooldownActive && cooldownSlider != null)
        {
            float remainingTime = cooldownEndTime - Time.time;
            
            if (remainingTime > 0)
            {
                
                cooldownSlider.value = cooldownDuration - remainingTime;
            }
            else
            {
                
                EndCooldown();
            }
        }
    }

    private void StartCooldown()
    {
        isCooldownActive = true;
        cooldownEndTime = Time.time + cooldownDuration;

        
        if (cooldownSlider != null)
        {
            cooldownSlider.gameObject.SetActive(true);
            cooldownSlider.value = 0f;
        }

        
        if (mainButton != null)
        {
            mainButton.interactable = false;
        }

        Debug.Log($"Cooldown started for {cooldownDuration} seconds. Ends at: {cooldownEndTime}");
    }

    private void EndCooldown()
    {
        isCooldownActive = false;
        
        
        if (cooldownSlider != null)
        {
            cooldownSlider.gameObject.SetActive(false);
        }

        
        if (mainButton != null)
        {
            mainButton.interactable = true;
            UpdateMainButtonText();
        }

        
        gamesPlayed = 0;
        gameStarted = false;

        
        if (feedbackText != null)
        {
            feedbackText.text = "Cooldown complete! Ready to play again!";
            feedbackText.gameObject.SetActive(true);
            StartCoroutine(FadeText(feedbackText, 0f, 1f, fadeDuration));
            StartCoroutine(HideMessageAfterDelay(3f));
        }

        Debug.Log("Cooldown finished. Games can be played again.");
    }

    
    public void StartBlink()
    {
        
        Debug.Log("Blink animation started via animation event");
        
        
    }

    
    public void BlinkRoutineEvent()
    {
        
        Debug.Log("Blink routine event triggered via animation event");
        
        
    }

    private void OnMainButtonClicked()
    {
        if (isBlinking || isCooldownActive) return;

        if (!gameStarted)
        {
            
            StartGame();
        }
        else if (gamesPlayed < maxGames)
        {
            
            RestartGame();
        }
        else
        {
            
            ExitGame();
        }
    }

    private void StartGame()
    {
        gameStarted = true;
        gamesPlayed = 1; 
        totalScore = 0;
        successfulBlinks = 0;
        totalBlinks = 0;
        
        
        if (detector != null)
        {
            var detectorType = detector.GetType();
            var isRunningProperty = detectorType.GetProperty("IsRunning");
            if (isRunningProperty != null)
            {
                isRunningProperty.SetValue(detector, true);
            }
            
            var startMethod = detectorType.GetMethod("StartDetection");
            startMethod?.Invoke(detector, null);
        }

        
        if (scoreText != null) 
        {
            scoreText.gameObject.SetActive(true);
            int remainingGames = maxGames - gamesPlayed;
            scoreText.text = $"Score: {totalScore} | Games: {remainingGames + 1}/{maxGames}";
        }

        // Hide main button during gameplay
        if (mainButton != null) mainButton.gameObject.SetActive(false);

        // Show initial instruction
        ShowTemporaryMessage("Get ready to blink when Blinky does!\nPress SPACE to blink!", 3f);

        // Start first round after delay
        StartCoroutine(StartNextRoundAfterDelay(3f));
    }

    private void RestartGame()
    {
        gamesPlayed++;
        
        // Reset score for new game
        totalScore = 0;
        successfulBlinks = 0;
        totalBlinks = 0;

        // Hide main button during gameplay
        if (mainButton != null) mainButton.gameObject.SetActive(false);

        // Update score display
        if (scoreText != null)
        {
            int remainingGames = maxGames - gamesPlayed;
            scoreText.text = $"Score: {totalScore} | Games: {remainingGames + 1}/{maxGames}";
        }

        // Show restart message
        ShowTemporaryMessage($"Starting game {gamesPlayed} of {maxGames}!\nGet ready to blink!", 2f);

        // Start first round after short delay
        StartCoroutine(StartNextRoundAfterDelay(2.5f));
    }

    private void ExitGame()
    {
        // Quit the application (or go back to main menu)
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    private IEnumerator StartNextRoundAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Add some random delay for natural pacing
        float randomDelay = Random.Range(1f, 2f);
        yield return new WaitForSeconds(randomDelay);
        
        StartBlinkRoutine();
    }

    private void StartBlinkRoutine()
    {
        if (isBlinking || isCooldownActive) return;
        
        isBlinking = true;
        waitingForPlayerBlink = true;
        lastBlinkTime = Time.time;
        totalBlinks++;
        
        StartCoroutine(BlinkRoutineCoroutine());
    }

    private IEnumerator BlinkRoutineCoroutine()
    {
        // Trigger Blinky animation
        if (animator != null)
            animator.SetTrigger("Blink");

        float syncStartTime = Time.time;
        bool success = false;
        int syncQuality = 0;

        // Wait for sync window
        float elapsed = 0f;
        while (elapsed < blinkSyncWindow && !success)
        {
            // Check for detector blink
            if (detector != null && detector.IsBlinking)
            {
                float syncTime = Time.time - syncStartTime;
                syncQuality = CalculateSyncQuality(syncTime);
                success = true;
                break;
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (success)
        {
            OnSuccessfulSync(syncQuality);
        }
        else
        {
            // Player missed the blink
            ShowResult(false, 0);
        }
    }

    private int CalculateSyncQuality(float syncTime)
    {
        float perfectSyncTime = blinkSyncWindow * 0.5f;
        float timeDifference = Mathf.Abs(syncTime - perfectSyncTime);
        float normalizedDifference = timeDifference / (blinkSyncWindow * 0.5f);

        if (normalizedDifference < 0.3f) return 2; // Perfect
        if (normalizedDifference < 0.7f) return 1; // Good
        return 0;
    }

    private void OnSuccessfulSync(int syncQuality = 1)
    {
        if (!waitingForPlayerBlink || isCooldownActive) return;

        waitingForPlayerBlink = false;
        
        // Update score
        int pointsEarned = syncQuality == 2 ? perfectSyncScore : goodSyncScore;
        totalScore += pointsEarned;
        successfulBlinks++;
        
        ShowResult(true, syncQuality);
    }

    private void ShowResult(bool success, int syncQuality)
    {
        waitingForPlayerBlink = false;
        
        // Show results
        if (blinkBG != null) 
        {
            blinkBG.SetActive(true);
            StartCoroutine(FadeBackground(0f, 1f, fadeDuration));
        }
        
        if (feedbackText != null)
        {
            string message = "";
            if (success)
            {
                if (syncQuality == 2)
                    message = "⭐ PERFECT SYNC! ⭐\nYou gained 10 points for perfect timing!";
                else if (syncQuality == 1)
                    message = "✅ Good sync!\nYou gained 5 points for blinking right!";
                else
                    message = "👍 Nice try!\nYou gained 0 points.";
            }
            else
            {
                message = "❌ Missed it!\nYou didn't blink in time. Click Play Again to try again!";
            }
                
            feedbackText.text = message;
            feedbackText.gameObject.SetActive(true);
            StartCoroutine(FadeText(feedbackText, 0f, 1f, fadeDuration));
        }

        // Show continue option after delay
        StartCoroutine(ShowContinueAfterDelay());
    }

    private IEnumerator ShowContinueAfterDelay()
    {
        yield return new WaitForSeconds(messageDuration);

        // Fade out feedback text
        if (feedbackText != null)
            yield return StartCoroutine(FadeText(feedbackText, 1f, 0f, fadeDuration));

        // Fade out background
        if (blinkBG != null)
            yield return StartCoroutine(FadeBackground(1f, 0f, fadeDuration));

        // Show main button again with updated text
        if (mainButton != null)
        {
            UpdateMainButtonText();
            mainButton.gameObject.SetActive(true);
        }

        isBlinking = false;

        // Check if we've reached max games
        if (gamesPlayed >= maxGames)
        {
            ShowGameOver();
            StartCooldown(); // Start cooldown after max games reached
        }
    }

    private void UpdateMainButtonText()
    {
        if (mainButton == null) return;

        TextMeshProUGUI buttonText = mainButton.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
        {
            if (!gameStarted)
            {
                buttonText.text = "Start Game";
            }
            else if (gamesPlayed < maxGames)
            {
                int remainingGames = maxGames - gamesPlayed;
                buttonText.text = $"Play Again ({remainingGames} left)";
            }
            else
            {
                buttonText.text = "Exit";
            }
        }
    }

    private void ShowGameOver()
    {
        if (blinkBG != null) 
        {
            blinkBG.SetActive(true);
            StartCoroutine(FadeBackground(0f, 1f, fadeDuration));
        }
        
        if (feedbackText != null)
        {
            float successRate = totalBlinks > 0 ? (float)successfulBlinks / totalBlinks * 100 : 0;
            feedbackText.text = $"🎮 Game Over!\nFinal Score: {totalScore}\nSuccess Rate: {successRate:0}%\n\nWait 20 minutes to play again";
            feedbackText.gameObject.SetActive(true);
            StartCoroutine(FadeText(feedbackText, 0f, 1f, fadeDuration));
        }

        if (mainButton != null)
        {
            UpdateMainButtonText();
            mainButton.gameObject.SetActive(true);
        }
    }

    private void ShowTemporaryMessage(string message, float duration)
    {
        if (blinkBG != null) 
        {
            blinkBG.SetActive(true);
            StartCoroutine(FadeBackground(0f, 1f, fadeDuration));
        }
        
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.gameObject.SetActive(true);
            StartCoroutine(FadeText(feedbackText, 0f, 1f, fadeDuration));
            StartCoroutine(HideMessageAfterDelay(duration));
        }
    }

    private IEnumerator HideMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (feedbackText != null && feedbackText.gameObject.activeInHierarchy)
        {
            yield return StartCoroutine(FadeText(feedbackText, 1f, 0f, fadeDuration));
        }
        
        if (blinkBG != null && blinkBG.activeInHierarchy)
        {
            yield return StartCoroutine(FadeBackground(1f, 0f, fadeDuration));
        }
    }

    private IEnumerator FadeText(TextMeshProUGUI text, float startAlpha, float endAlpha, float duration)
    {
        Color color = text.color;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            color.a = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            text.color = color;
            elapsed += Time.deltaTime;
            yield return null;
        }

        color.a = endAlpha;
        text.color = color;
    }

    private IEnumerator FadeBackground(float startAlpha, float endAlpha, float duration)
    {
        if (blinkBGCanvasGroup == null) yield break;
        
        float elapsed = 0f;

        while (elapsed < duration)
        {
            blinkBGCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        blinkBGCanvasGroup.alpha = endAlpha;
        
        // Deactivate the background when fully faded out
        if (endAlpha == 0f)
        {
            blinkBG.SetActive(false);
        }
    }

    // Public method to gracefully stop the game
    public void StopGame()
    {
        gameStarted = false;
        isBlinking = false;
        waitingForPlayerBlink = false;
        
        if (detector != null)
        {
            // Stop the detector by setting IsRunning to false
            var detectorType = detector.GetType();
            var isRunningProperty = detectorType.GetProperty("IsRunning");
            if (isRunningProperty != null)
            {
                isRunningProperty.SetValue(detector, false);
            }
            
            // Also try to call StopDetection if it exists
            var stopMethod = detectorType.GetMethod("StopDetection");
            stopMethod?.Invoke(detector, null);
        }
        
        // Reset UI
        if (mainButton != null) 
        {
            mainButton.gameObject.SetActive(true);
            UpdateMainButtonText();
        }
        
        if (blinkBG != null) 
        {
            blinkBG.SetActive(false);
            if (blinkBGCanvasGroup != null)
                blinkBGCanvasGroup.alpha = 0f;
        }
        
        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
        if (scoreText != null) 
        {
            scoreText.text = "Ready to play?";
            scoreText.gameObject.SetActive(false);
        }
        
        // Hide cooldown slider if active
        if (cooldownSlider != null)
        {
            cooldownSlider.gameObject.SetActive(false);
        }
    }

    void OnDestroy()
    {
        blinkAction?.Disable();
        blinkAction?.Dispose();
        StopGame();
    }
}