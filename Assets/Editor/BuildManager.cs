using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildManager
{
    [MenuItem("Build System/Build Selected Profile")]
    public static void BuildSelectedProfile()
    {
        BuildProfile profile = Selection.activeObject as BuildProfile;

        if (profile == null)
        {
            Debug.LogError("Please select a BuildProfile ScriptableObject to build.");
            return;
        }

        ApplyProfileSettings(profile);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = profile.GetScenePaths(),
            locationPathName = profile.outputPath,
            target = profile.targetPlatform,
            options = profile.buildOptions
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build succeeded for {profile.profileName} at {profile.outputPath}");
        }
        else
        {
            Debug.LogError($"Build failed for {profile.profileName}. Result: {report.summary.result}, Errors: {report.summary.totalErrors}");
        }
    }

    private static void ApplyProfileSettings(BuildProfile profile)
    {
        PlayerSettings.productName = profile.profileName;
        PlayerSettings.bundleVersion = profile.version;

        // Only Android and Windows (Standalone)
        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(profile.targetPlatform);

        // Suppress obsolete warning
#pragma warning disable CS0618
        PlayerSettings.SetApplicationIdentifier(targetGroup, profile.bundleIdentifier);
#pragma warning restore CS0618

        if (profile.targetPlatform == BuildTarget.Android)
        {
            PlayerSettings.Android.bundleVersionCode = profile.androidBundleVersionCode;

            if (!string.IsNullOrEmpty(profile.keystorePath))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = profile.keystorePath;
                PlayerSettings.Android.keystorePass = profile.keystorePassword;
                PlayerSettings.Android.keyaliasName = profile.keyAlias;
                PlayerSettings.Android.keyaliasPass = profile.keyAliasPassword;
            }

            PlayerSettings.Android.minSdkVersion = System.Enum.IsDefined(typeof(AndroidSdkVersions), profile.minSdkVersion)
                ? (AndroidSdkVersions)profile.minSdkVersion
                : AndroidSdkVersions.AndroidApiLevelAuto;

            PlayerSettings.Android.targetSdkVersion = System.Enum.IsDefined(typeof(AndroidSdkVersions), profile.targetSdkVersion)
                ? (AndroidSdkVersions)profile.targetSdkVersion
                : AndroidSdkVersions.AndroidApiLevelAuto;
        }
    }
}
