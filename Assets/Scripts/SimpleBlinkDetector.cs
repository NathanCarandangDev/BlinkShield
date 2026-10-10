using UnityEngine;
using OpenCvSharp;
using System.Collections.Generic;
using System.Linq;

public class SimpleBlinkDetector : MonoBehaviour
{
    [Header("Preview")]
    public Renderer displayRenderer;
    
    [Header("Detection Settings")]
    public float earThreshold = 0.25f;
    public int consecutiveFrames = 3;
    public float blinkCooldown = 0.4f;

    [Header("Cascades")]
    public string faceCascadeFile = "haarcascade_frontalface_default.xml";
    public string eyeCascadeFile = "haarcascade_eye.xml";

    private WebCamTexture webcamTexture;
    private CascadeClassifier faceCascade;
    private CascadeClassifier eyeCascade;
    
    private bool isBlinking = false;
    private float blinkTimer = 0f;
    private int blinkCount = 0;
    private int framesWithLowConfidence = 0;
    private float currentConfidence = 1f;

    public System.Action OnBlinkDetected;
    public bool IsBlinking { get; private set; }

    void Start()
    {
        InitializeWebcam();
        LoadCascades();
    }

    private void InitializeWebcam()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No webcam devices found!");
            return;
        }

        webcamTexture = new WebCamTexture(devices[0].name, 640, 480, 30);
        if (displayRenderer != null)
            displayRenderer.material.mainTexture = webcamTexture;
    }

    private void LoadCascades()
    {
        try
        {
            string facePath = System.IO.Path.Combine(Application.streamingAssetsPath, faceCascadeFile);
            string eyePath = System.IO.Path.Combine(Application.streamingAssetsPath, eyeCascadeFile);

            faceCascade = new CascadeClassifier(facePath);
            eyeCascade = new CascadeClassifier(eyePath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load cascades: {e.Message}");
        }
    }

    void Update()
    {
        if (!webcamTexture.isPlaying) return;

        DetectBlinkHybrid();
        
        if (isBlinking)
        {
            blinkTimer -= Time.deltaTime;
            if (blinkTimer <= 0f)
            {
                isBlinking = false;
                IsBlinking = false;
            }
        }
    }

    private void DetectBlinkHybrid()
    {
        using (Mat frame = GetWebcamMat())
        using (Mat gray = new Mat())
        {
            if (frame.Empty()) return;

            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);

            // Detect faces
            var faces = faceCascade.DetectMultiScale(
                gray, 1.1, 3, HaarDetectionType.ScaleImage, new OpenCvSharp.Size(100, 100));

            if (faces.Length > 0)
            {
                var face = faces[0]; // Use largest face
                
                // Calculate eye detection confidence instead of just "eyes found"
                float confidence = CalculateEyeConfidence(gray, face, frame);
                currentConfidence = confidence;

                // Use confidence-based blink detection
                if (confidence < 0.3f) // Low confidence = possible blink
                {
                    framesWithLowConfidence++;
                    if (framesWithLowConfidence >= consecutiveFrames && !isBlinking)
                    {
                        TriggerBlink();
                    }
                }
                else
                {
                    framesWithLowConfidence = 0;
                }
            }
            else
            {
                // No face detected
                framesWithLowConfidence = 0;
                currentConfidence = 0f;
            }
        }
    }

    private float CalculateEyeConfidence(Mat gray, OpenCvSharp.Rect face, Mat debugFrame = null)
    {
        // Focus on upper face region
        var eyeRegion = new OpenCvSharp.Rect(
            face.X,
            face.Y + face.Height / 4,
            face.Width,
            face.Height / 2
        );

        using (Mat eyeROI = new Mat(gray, eyeRegion))
        {
            // Detect eyes with confidence scoring
            var eyes = eyeCascade.DetectMultiScale(
                eyeROI, 1.1, 2, HaarDetectionType.ScaleImage, 
                new OpenCvSharp.Size(25, 25), new OpenCvSharp.Size(80, 80));

            // Calculate confidence based on number and quality of eye detections
            float confidence = 0f;
            
            if (eyes.Length >= 2)
            {
                // Two eyes detected - high confidence
                confidence = 1.0f;
                
                // Additional check: verify eye positions are reasonable
                if (AreEyesProperlySpaced(eyes))
                    confidence = 1.0f;
                else
                    confidence = 0.6f;
            }
            else if (eyes.Length == 1)
            {
                // One eye detected - medium confidence
                confidence = 0.5f;
            }
            else
            {
                // No eyes detected - low confidence
                confidence = 0.1f;
            }

            // Debug visualization
            if (debugFrame != null)
            {
                DrawConfidenceInfo(debugFrame, eyeRegion, eyes, confidence);
            }

            return confidence;
        }
    }

    private bool AreEyesProperlySpaced(OpenCvSharp.Rect[] eyes)
    {
        if (eyes.Length < 2) return false;
        
        // Check if eyes are horizontally aligned and properly spaced
        float eye1CenterX = eyes[0].X + eyes[0].Width / 2;
        float eye2CenterX = eyes[1].X + eyes[1].Width / 2;
        float eye1CenterY = eyes[0].Y + eyes[0].Height / 2;
        float eye2CenterY = eyes[1].Y + eyes[1].Height / 2;
        
        float horizontalDistance = Mathf.Abs(eye1CenterX - eye2CenterX);
        float verticalDifference = Mathf.Abs(eye1CenterY - eye2CenterY);
        
        // Eyes should be reasonably spaced and at similar heights
        return horizontalDistance > eyes[0].Width && verticalDifference < eyes[0].Height / 2;
    }

    private void DrawConfidenceInfo(Mat frame, OpenCvSharp.Rect eyeRegion, OpenCvSharp.Rect[] eyes, float confidence)
    {
        // Draw eye region
        Cv2.Rectangle(frame, eyeRegion, new Scalar(255, 255, 0), 1);
        
        // Draw detected eyes
        foreach (var eye in eyes)
        {
            var absoluteEye = new OpenCvSharp.Rect(
                eyeRegion.X + eye.X,
                eyeRegion.Y + eye.Y,
                eye.Width,
                eye.Height
            );
            Cv2.Rectangle(frame, absoluteEye, new Scalar(0, 0, 255), 2);
        }
        
        // Display confidence info
        Cv2.PutText(frame, $"Confidence: {confidence:F2}", new Point(10, 30), 
                   HersheyFonts.HersheySimplex, 0.7, new Scalar(255, 255, 255), 2);
        Cv2.PutText(frame, $"Blinks: {blinkCount}", new Point(10, 60), 
                   HersheyFonts.HersheySimplex, 0.7, new Scalar(255, 255, 255), 2);
        Cv2.PutText(frame, isBlinking ? "BLINKING!" : "Active", new Point(10, 90), 
                   HersheyFonts.HersheySimplex, 0.7, isBlinking ? new Scalar(0, 0, 255) : new Scalar(0, 255, 0), 2);
    }

    private void TriggerBlink()
    {
        isBlinking = true;
        IsBlinking = true;
        blinkTimer = blinkCooldown;
        blinkCount++;
        
        Debug.Log($"BLINK DETECTED! Confidence: {currentConfidence:F2}, Total: {blinkCount}");
        OnBlinkDetected?.Invoke();
    }

    private Mat GetWebcamMat()
    {
        // Your existing implementation is fine
        if (webcamTexture == null || !webcamTexture.isPlaying) 
            return new Mat();

        Color32[] pixels = webcamTexture.GetPixels32();
        if (pixels == null || pixels.Length == 0) 
            return new Mat();

        byte[] bytes = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 4 + 0] = pixels[i].b;
            bytes[i * 4 + 1] = pixels[i].g;
            bytes[i * 4 + 2] = pixels[i].r;
            bytes[i * 4 + 3] = pixels[i].a;
        }

        return new Mat(webcamTexture.height, webcamTexture.width, MatType.CV_8UC4, bytes);
    }

    void OnDestroy()
    {
        webcamTexture?.Stop();
        faceCascade?.Dispose();
        eyeCascade?.Dispose();
    }
}