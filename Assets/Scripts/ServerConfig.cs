using UnityEngine;

public static class ServerConfig
{
    // AlwaysData live server
    private const string AlwaysDataBaseURL = "https://blinkshield.alwaysdata.net/";
    private const string ProjectFolder = "BlinkShield"; // leave blank if PHP is at root

    public static string BaseURL { get; private set; }

    static ServerConfig()
    {
        string url = AlwaysDataBaseURL.EndsWith("/") ? AlwaysDataBaseURL : AlwaysDataBaseURL + "/";

        if (!string.IsNullOrEmpty(ProjectFolder))
        {
            if (!ProjectFolder.EndsWith("/"))
                url += ProjectFolder + "/";
            else
                url += ProjectFolder;
        }

        BaseURL = url;

        Debug.Log("[ServerConfig] Using AlwaysData server: " + BaseURL);
    }
}
