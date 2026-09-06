#if UNITY_EDITOR
using System.Linq;
using Core.Services;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Core.Config.EditorTools
{
    public static class AndroidBuildTools
    {
        [MenuItem("Tools/Build/Android Closed Test AAB")]
        public static void BuildClosedTestAab()
        {
            BuildAndroidAab(
                requiredEnvironment: AdsEnvironment.Test,
                defaultFileName: "Wordlet-closed-test.aab",
                buildLabel: "Closed Test");
        }

        [MenuItem("Tools/Build/Android Production AAB")]
        public static void BuildProductionAab()
        {
            BuildAndroidAab(
                requiredEnvironment: AdsEnvironment.Production,
                defaultFileName: "Wordlet-production.aab",
                buildLabel: "Production");
        }

        private static void BuildAndroidAab(
            AdsEnvironment requiredEnvironment,
            string defaultFileName,
            string buildLabel)
        {
            var adsConfig = FindAdsConfig();

            if (adsConfig.Environment != requiredEnvironment)
            {
                EditorUtility.DisplayDialog(
                    $"{buildLabel} build blocked",
                    $"AdsConfig.Environment должен быть {requiredEnvironment}.\n\n" +
                    $"Сейчас выбран {adsConfig.Environment}, поэтому {buildLabel} AAB не будет собран.",
                    "OK");

                Selection.activeObject = adsConfig;
                EditorGUIUtility.PingObject(adsConfig);

                throw new BuildFailedException(
                    $"{buildLabel} build blocked: " +
                    $"AdsConfig.Environment == {adsConfig.Environment}, " +
                    $"expected {requiredEnvironment}.");
            }

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new BuildFailedException("В Build Settings нет включённых сцен.");

            var outputPath = EditorUtility.SaveFilePanel(
                $"Save {buildLabel} AAB",
                "",
                defaultFileName,
                "aab");

            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            EditorUserBuildSettings.buildAppBundle = true;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            Debug.Log(
                $"[Build] Starting Android {buildLabel} AAB. " +
                $"AdsEnvironment: {adsConfig.Environment}");

            var report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"{buildLabel} build failed: {report.summary.result}");
            }

            Debug.Log(
                $"[Build] Android {buildLabel} AAB created successfully: {outputPath}");

            // Open Explorer/Finder and select the generated AAB.
            EditorUtility.RevealInFinder(outputPath);
        }

        private static AdsConfig FindAdsConfig()
        {
            var guids = AssetDatabase.FindAssets("t:AdsConfig");

            if (guids.Length == 0)
                throw new BuildFailedException("AdsConfig asset не найден.");

            if (guids.Length > 1)
            {
                throw new BuildFailedException(
                    $"Найдено несколько AdsConfig ({guids.Length}). " +
                    "Нужно оставить один глобальный AdsConfig либо указать его явно.");
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var config = AssetDatabase.LoadAssetAtPath<AdsConfig>(path);

            if (config == null)
            {
                throw new BuildFailedException(
                    $"Не удалось загрузить AdsConfig: {path}");
            }

            return config;
        }
    }
}
#endif
