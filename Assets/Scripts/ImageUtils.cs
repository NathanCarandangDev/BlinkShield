using UnityEngine;
using System;

public static class ImageUtils
{
    /// <summary>
    /// Convert a Base64 string into a Unity Sprite.
    /// </summary>
    public static Sprite SpriteFromBase64(string base64, float pixelsPerUnit = 100f)
    {
        if (string.IsNullOrEmpty(base64)) return null;

        try
        {
            byte[] imageBytes = Convert.FromBase64String(base64);
            return SpriteFromBytes(imageBytes, pixelsPerUnit);
        }
        catch (Exception e)
        {
            Debug.LogError("[ImageUtils] Failed to convert base64 to sprite: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Convert a raw byte[] (from DB or file) into a Unity Sprite.
    /// </summary>
    public static Sprite SpriteFromBytes(byte[] imageBytes, float pixelsPerUnit = 100f)
    {
        if (imageBytes == null || imageBytes.Length == 0) return null;

        try
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(imageBytes))
            {
                return Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit
                );
            }
            else
            {
                Debug.LogWarning("[ImageUtils] Texture.LoadImage failed (invalid image data).");
                UnityEngine.Object.Destroy(tex);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[ImageUtils] Failed to convert byte[] to sprite: " + e.Message);
        }

        return null;
    }

    /// <summary>
    /// Legacy alias (keeps older scripts compiling).
    /// </summary>
    public static Sprite GetSpriteFromBytes(byte[] imageBytes, float pixelsPerUnit = 100f)
    {
        return SpriteFromBytes(imageBytes, pixelsPerUnit);
    }
}
