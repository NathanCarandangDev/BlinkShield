using UnityEngine;
using System.Collections;
using System.IO;

public static class ScreenshotHelper
{
    public static IEnumerator Capture(RectTransform target, string filename, System.Action<string> onComplete)
    {
        yield return new WaitForEndOfFrame();

        Rect rect = GetScreenRect(target);
        Texture2D tex = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);
        tex.ReadPixels(rect, 0, 0);
        tex.Apply();

        string path = Path.Combine(Application.persistentDataPath, filename);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        onComplete?.Invoke(path);

        Object.Destroy(tex);
    }

    static Rect GetScreenRect(RectTransform rt)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
    }
}
