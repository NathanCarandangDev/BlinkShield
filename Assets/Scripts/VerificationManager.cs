using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro; // <-- Add this for TextMeshPro

public class VerificationManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField[] codeInputs; // <-- Updated to TMP_InputField
    public string userEmail;
    public Button confirmButton;
    public Text feedbackText;

    private const string VerifyUrl = "http://127.0.0.1/BlinkShield/signup.php";

    private void Start()
    {
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmClicked);

        for (int i = 0; i < codeInputs.Length; i++)
        {
            int index = i;

            if (codeInputs[i] != null)
                codeInputs[i].onValueChanged.AddListener(value => HandleDigitInput(index, value));
        }
    }

    private void OnEnable()
    {
        if (codeInputs?.Length > 0 && codeInputs[0] != null)
        {
            codeInputs[0].Select();
            codeInputs[0].ActivateInputField();
        }
    }

    private void HandleDigitInput(int index, string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        // Limit to 1 character
        codeInputs[index].text = value.Substring(0, 1);

        // Move to next input field if within bounds
        if (index + 1 < codeInputs.Length && codeInputs[index + 1] != null)
        {
            codeInputs[index + 1].Select();
            codeInputs[index + 1].ActivateInputField();
        }
    }

    private void OnConfirmClicked()
    {
        if (codeInputs == null || codeInputs.Length == 0)
        {
            feedbackText.text = "[!] Code input fields are not assigned.";
            return;
        }

        string code = string.Concat(codeInputs.Select(input => input?.text.Trim() ?? ""));

        if (code.Length != codeInputs.Length || !code.All(char.IsDigit))
        {
            feedbackText.text = "[!] Please enter all digits correctly.";
            return;
        }

        StartCoroutine(SendVerificationCode(userEmail, code));
    }

    private IEnumerator SendVerificationCode(string email, string code)
    {
        var form = new WWWForm();
        form.AddField("email", email);
        form.AddField("code", code);

        using (UnityWebRequest www = UnityWebRequest.Post(VerifyUrl, form))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                feedbackText.text = "[X] Network error: " + www.error;
                yield break;
            }

            string response = www.downloadHandler.text.Trim().ToLowerInvariant();

            switch (response)
            {
                case "success":
                case "verified":
                    feedbackText.text = "[/] Verified successfully.";
                    // TODO: Load next scene or unlock feature
                    break;

                case "invalid":
                    feedbackText.text = "[X] Incorrect verification code.";
                    break;

                default:
                    feedbackText.text = "[?] Unexpected response: " + response;
                    break;
            }
        }
    }
}
