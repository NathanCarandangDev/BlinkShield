using UnityEngine;
using UnityEditor;

[CreateAssetMenu(fileName = "BuildProfile", menuName = "Build System/Build Profile")]
public class BuildProfile : ScriptableObject
{
    [Header("General")]
    public string profileName;
    public BuildTarget targetPlatform;
    public string outputPath;
    public BuildOptions buildOptions = BuildOptions.None;

    [Header("Scenes")]
    public SceneAsset[] scenes;

    [Header("Versioning")]
    public string bundleIdentifier;  // e.g., com.company.game
    public string version = "1.0.0";
    public int androidBundleVersionCode = 1;

    [Header("Android Specific")]
    public string keystorePath;
    public string keystorePassword;
    public string keyAlias;
    public string keyAliasPassword;
    public int minSdkVersion = 21;
    public int targetSdkVersion = 36;

    // Helper to get scene paths
    public string[] GetScenePaths()
    {
        string[] paths = new string[scenes.Length];
        for (int i = 0; i < scenes.Length; i++)
        {
            paths[i] = AssetDatabase.GetAssetPath(scenes[i]);
        }
        return paths;
    }
}
