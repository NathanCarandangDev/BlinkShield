using UnityEngine;

[RequireComponent(typeof(Camera))]
public class UniversalPortraitScale : MonoBehaviour
{
    [Header("Target Portrait Design Resolution")]
    public float targetWidth = 1080f;
    public float targetHeight = 1920f;
    public float baseOrthographicSize = 5f;

    private Camera cam;
    private float lastWidth;
    private float lastHeight;

    void Start()
    {
        cam = GetComponent<Camera>();
        AdjustCamera();
        LockToPortrait();
    }

    void Update()
    {
        // Detects window resize or resolution change
        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            AdjustCamera();
            LockToPortrait();
        }
    }

    void AdjustCamera()
    {
        lastWidth = Screen.width;
        lastHeight = Screen.height;

        float targetAspect = targetWidth / targetHeight;
        float currentAspect = (float)Screen.width / Screen.height;

        // Keep consistent vertical scaling, allowing vertical resizing
        cam.orthographicSize = baseOrthographicSize * Mathf.Max(1f, targetAspect / currentAspect);
    }

    void LockToPortrait()
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        // Prevent landscape ratio but still allow vertical resizing
        if (Screen.width > Screen.height)
        {
            int newWidth = Mathf.RoundToInt(Screen.height * (targetWidth / targetHeight));
            Screen.SetResolution(newWidth, Screen.height, false);
        }
#endif

#if UNITY_ANDROID || UNITY_IOS
        Screen.orientation = ScreenOrientation.Portrait;
#endif
    }
}
