using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SignInManager : MonoBehaviour
{
    [Header("Input Fields")]
    public TMP_InputField nicknameInput;
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    public TMP_Text feedbackText;

    [Header("Terms & Conditions")]
    public Button termsButton;
    public GameObject termsPanel;
    public TMP_Text termsText;
    public Button closeTermsButton;

    [Header("Server Configuration")]
    public string signupFile = "signup.php";

    [Header("Security")]
    public bool allowInsecureConnections = true; // Only enable for development

    [Header("Navigation Settings")]
    public bool usePanelsInsteadOfScenes = true;
    public GameObject signupPanel;
    public GameObject loginPanel;
    public string loginSceneName = "pageLogin";

    private string SignupURL => $"{ServerConfig.BaseURL}{signupFile}";

    void Start()
    {
        // Initialize terms panel
        if (termsPanel != null)
            termsPanel.SetActive(false);

        // Set up terms button click event
        if (termsButton != null)
            termsButton.onClick.AddListener(ShowTermsPanel);

        // Set up close button for terms panel
        if (closeTermsButton != null)
            closeTermsButton.onClick.AddListener(CloseTermsPanel);
    }

    public void OnSignUpButtonClicked()
    {
        if (!ValidateInputs()) return;
        StartCoroutine(SignUpCoroutine());
    }

    private bool ValidateInputs()
    {
        string nickname = nicknameInput.text.Trim();
        string email = emailInput.text.Trim();
        string password = passwordInput.text.Trim();

        if (string.IsNullOrEmpty(nickname) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            feedbackText.text = "Please fill in all fields.";
            return false;
        }

        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            feedbackText.text = "Please enter a valid email address.";
            return false;
        }

        if (password.Length < 8)
        {
            feedbackText.text = "Password must be at least 8 characters.";
            return false;
        }

        return true;
    }

    private void ShowTermsPanel()
    {
        if (termsPanel != null)
        {
            termsPanel.SetActive(true);
            if (termsText != null)
            {
                termsText.text = GetTermsAndConditionsText();
            }
        }
    }

    private void CloseTermsPanel()
    {
        if (termsPanel != null)
        {
            termsPanel.SetActive(false);
        }
    }

    private string GetTermsAndConditionsText()
    {
        // Replace this with your actual terms and conditions text
        return @"TERMS AND CONDITIONS

1. ACCEPTANCE OF TERMS
By using our service, you agree to be bound by these Terms and Conditions.

2. USER ACCOUNT
You are responsible for maintaining the confidentiality of your account and password.

3. PRIVACY
Your personal information will be handled in accordance with our Privacy Policy.

4. PROHIBITED ACTIVITIES
You may not use the service for any illegal purpose or to transmit malicious code.

5. TERMINATION
We reserve the right to terminate accounts for violations of these terms.

Please read these terms carefully before proceeding with sign up.";
    }

    private IEnumerator SignUpCoroutine()
    {
        string nickname = nicknameInput.text.Trim();
        string email = emailInput.text.Trim();
        string password = passwordInput.text.Trim();

        feedbackText.text = "Signing up...";
        Debug.Log($"Attempting signup: Nickname={nickname}, Email={email} | URL={SignupURL}");

        WWWForm form = new WWWForm();
        form.AddField("nickname", nickname);
        form.AddField("email", email);
        form.AddField("password", password);

        using (UnityWebRequest www = UnityWebRequest.Post(SignupURL, form))
        {
            if (allowInsecureConnections)
                www.certificateHandler = new BypassCertificateHandler();

            www.timeout = 10;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                feedbackText.text = GetErrorMessage(www);
                Debug.LogError($"Signup failed: {feedbackText.text}");
            }
            else
            {
                ProcessServerResponse(www.downloadHandler.text);
            }
        }
    }

    private string GetErrorMessage(UnityWebRequest request)
    {
        return request.result switch
        {
            UnityWebRequest.Result.ConnectionError => request.error?.ToLower().Contains("certificate") == true
                ? "Security error: Invalid certificate. Please use HTTPS."
                : $"Connection error: {request.error}",
            UnityWebRequest.Result.ProtocolError => $"Server error: {request.error}",
            _ => "Unknown error occurred"
        };
    }

    private void ProcessServerResponse(string response)
    {
        string cleanResponse = response.Trim();
        Debug.Log($"Server response: {cleanResponse}");

        // Standardize JSON-friendly keyword matching
        string lower = cleanResponse.ToLower();

        if (lower.Contains("\"status\":\"success\"") || lower.Contains("success"))
        {
            feedbackText.text = "Signup successful! Redirecting to login...";
            ClearInputFields();
            StartCoroutine(RedirectToLogin());
        }
        else if (lower.Contains("nickname already taken"))
        {
            feedbackText.text = "Nickname already taken.";
        }
        else if (lower.Contains("email already registered"))
        {
            feedbackText.text = "Email already registered.";
        }
        else if (lower.Contains("\"status\":\"duplicate\""))
        {
            feedbackText.text = "Duplicate account detected.";
        }
        else
        {
            feedbackText.text = "Signup failed. Please try again.";
            Debug.LogWarning($"Unexpected signup response: {cleanResponse}");
        }
    }

    private IEnumerator RedirectToLogin()
    {
        yield return new WaitForSeconds(2f);

        if (usePanelsInsteadOfScenes)
        {
            signupPanel?.SetActive(false);
            loginPanel?.SetActive(true);
        }
        else
        {
            SceneManager.LoadScene(loginSceneName);
        }
    }

    private void ClearInputFields()
    {
        nicknameInput.text = "";
        emailInput.text = "";
        passwordInput.text = "";
    }

    private class BypassCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }
}