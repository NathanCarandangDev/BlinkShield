using System;
using UnityEngine;

/// <summary>
/// Helper to serialize/deserialize JSON arrays with Unity's JsonUtility
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// Deserialize a JSON array into an array of objects
    /// </summary>
    public static T[] FromJson<T>(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new T[0];

        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(WrapJson(json));
        return wrapper.Items;
    }

    /// <summary>
    /// Serialize an array into a JSON string
    /// </summary>
    public static string ToJson<T>(T[] array, bool prettyPrint = false)
    {
        Wrapper<T> wrapper = new Wrapper<T> { Items = array };
        return JsonUtility.ToJson(wrapper, prettyPrint);
    }

    /// <summary>
    /// Wraps raw JSON array in an object for JsonUtility
    /// </summary>
    private static string WrapJson(string json)
    {
        json = json.Trim();
        if (json.StartsWith("["))
            return "{\"Items\":" + json + "}";
        return json;
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] Items;
    }
}
