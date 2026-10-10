using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections;

public class ZoomTapGame : MonoBehaviour
{
    [Header("UI References")]
    public Button startButton;
    public TMP_Text startButtonText;
    public GameObject feedbackPanel;
    public TMP_Text feedbackText;
    public Slider cooldownSlider; // assign in Inspector

    [Header("Video")]
    public VideoPlayer videoPlayer;

    [Header("Game Settings")]
    public float winMinTime = 16f;
    public float winMaxTime = 16.99f;
    public float totalTime = 20f;

    [Header("Cooldown Settings")]
    public int maxRestarts = 3;
    public float cooldownDuration = 1200f; // 20 min

    [Header("Game Rewards")]
    public int winPoints = 5; // Points to award when winning

    private float currentTime;
    private bool gameRunning = false;
    private bool canTap = false;
    private int restartCount = 0;

    private bool cooldownActive = false;
    private float cooldownTimeRemaining = 0f;

    void OnEnable()
    {
        feedbackPanel.SetActive(false);
        startButton.onClick.AddListener(StartGame);

        // Show or hide slider correctly based on cooldown state
        if (cooldownSlider != null)
        {
            if (cooldownActive)
            {
                cooldownSlider.gameObject.SetActive(true);
                cooldownSlider.maxValue = cooldownDuration;
                cooldownSlider.value = cooldownTimeRemaining;
            }
            else
            {
                cooldownSlider.gameObject.SetActive(false);
            }
        }
    }

    void OnDisable()
    {
        startButton.onClick.RemoveListener(StartGame);
        if (videoPlayer != null) videoPlayer.Stop();
        gameRunning = false;
        canTap = false;
        // don't hide cooldownSlider here
    }

    void StartGame()
    {
        // block if cooldown
        if (cooldownActive)
        {
            feedbackText.text = "Cooldown...";
            feedbackPanel.SetActive(true);

            // Start a coroutine to go back after 3 seconds
            StartCoroutine(GoBackAfterDelay(1f));
            return;
        }
        
        // Exit button behavior
        if (startButtonText.text == "Exit")
        {
            // start cooldown
            cooldownActive = true;
            cooldownTimeRemaining = cooldownDuration;

            if (cooldownSlider != null)
            {
                cooldownSlider.maxValue = cooldownDuration;
                cooldownSlider.value = cooldownDuration;
                cooldownSlider.gameObject.SetActive(true);
            }
            UIManager.Instance.GoBack(); // go back to previous UI page

            ResetGameState();
            return;
        }

        if (videoPlayer != null)
        {
            videoPlayer.time = 0;
            videoPlayer.Play();
        }

        currentTime = 0f;
        gameRunning = true;
        canTap = true;

        startButton.interactable = false;
        startButtonText.text = "Running...";
        feedbackPanel.SetActive(false);
    }

    void Update()
    {
        // update timer + check tap when running
        if (gameRunning)
        {
            currentTime += Time.deltaTime;

            bool tapped =
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);

            if (canTap && tapped)
            {
                CheckWin();
            }

            if (currentTime >= totalTime)
            {
                EndGame(false, "Time up!");
            }
        }

        // update cooldown
        if (cooldownActive)
        {
            cooldownTimeRemaining -= Time.deltaTime;
            if (cooldownTimeRemaining < 0) cooldownTimeRemaining = 0;

            if (cooldownSlider != null)
                cooldownSlider.value = cooldownTimeRemaining;

            if (cooldownTimeRemaining <= 0)
            {
                cooldownActive = false;
                if (cooldownSlider != null)
                    cooldownSlider.gameObject.SetActive(false);

                ResetGameState();
                Debug.Log("Cooldown finished. Game can be played again.");
            }
        }
    }

    void CheckWin()
    {
        if (currentTime < winMinTime)
            EndGame(false, "Too Early");
        else if (currentTime > winMaxTime)
            EndGame(false, "Too Late");
        else
            HandleWin();
    }

    void EndGame(bool win, string message)
    {
        gameRunning = false;
        canTap = false;

        if (videoPlayer != null) videoPlayer.Pause();

        feedbackText.text = message;
        feedbackPanel.SetActive(true);

        // increment restart count only after game ends
        restartCount++;

        // Decide button text based on restarts
        if (restartCount < maxRestarts)
        {
            startButtonText.text = "Restart";
        }
        else
        {
            startButtonText.text = "Exit";
        }

        startButton.interactable = true;
    }

    private void ResetGameState()
    {
        // stop video
        if (videoPlayer != null) videoPlayer.Stop();

        // reset internal state
        gameRunning = false;
        canTap = false;
        currentTime = 0f;

        // reset UI
        feedbackPanel.SetActive(false);

        if (!cooldownActive)
        {
            // reset restart counter and button if not in cooldown
            restartCount = 0;
            startButtonText.text = "Start";
        }

        startButton.interactable = true;

        Debug.Log("[ZoomTapGame] State reset");
    }

    // Called when player wins
    private void HandleWin()
    {
        if (DatabaseManager.Instance == null || !DatabaseManager.Instance.IsLoggedIn)
        {
            Debug.LogWarning("Cannot award points: No database connection or user not logged in");
            feedbackText.text = "You won! (Log in to earn points)";
            feedbackPanel.SetActive(true);
            return;
        }

        // Add points to database
        AwardPointsToUser(DatabaseManager.Instance.CurrentUserId, winPoints);
    }

    private void AwardPointsToUser(int userId, int points)
    {
        if (userId <= 0) return;

        // Get current points
        int currentPoints = DatabaseManager.Instance.GetUserPoints(userId);
        int newPoints = currentPoints + points;

        // Update points in database
        DatabaseManager.Instance.UpdateUserPoints(userId, newPoints);

        // Show feedback
        feedbackText.text = $"You won! +{points} points";
        feedbackPanel.SetActive(true);

        Debug.Log($"[ZoomTapGame] Awarded {points} points to user {userId}. Total: {newPoints}");
    }

    private IEnumerator GoBackAfterDelay(float delay)
    {
        // wait for X seconds
        yield return new WaitForSeconds(delay);

        // then go back
        UIManager.Instance.GoBack();
    }
}