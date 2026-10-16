#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tests.AutoRecovery.Editor
{
    [InitializeOnLoad]
    public static class AutoRecoveryPlayModeCoordinator
    {
        private const string OriginalSceneKey = "Wordlet.RecoveryTests.OriginalPlayScene";
        private const string StartedKey = "Wordlet.RecoveryTests.Started";
        private const string FingerprintKey = "Wordlet.RecoveryTests.ProductionFingerprint";
        private const string RestartKey = "Wordlet.RecoveryTests.RestartPending";
        static AutoRecoveryPlayModeCoordinator()
        {
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.update += Update;
        }

        [MenuItem("Tools/Tests/Run isolated Auto Recovery Play Mode suite")]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop the current Play Mode first.");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("Enable Scene Reload before running isolated recovery tests.");
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AutoRecoveryPlayModeSuite.ScenePath);
            if (scene == null) throw new InvalidOperationException("Isolated test scene is missing.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/AutoRecoveryTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            SessionState.SetString(AutoRecoveryPlayModeSuite.RootKey, root);
            SessionState.SetString(OriginalSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(FingerprintKey, Fingerprint());
            SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetInt(AutoRecoveryPlayModeSuite.StageKey, 1);
            SessionState.SetBool(AutoRecoveryPlayModeSuite.FailedKey, false);
            SessionState.SetBool(AutoRecoveryPlayModeSuite.DoneKey, false);
            SessionState.SetBool(RestartKey, false);
            SessionState.SetBool(AutoRecoveryPlayModeSuite.ActiveKey, true);
            File.WriteAllText(Path.Combine(root, "report.md"), "# Isolated Auto Recovery Play Mode integration run\n\nProduction: " + Application.persistentDataPath + "\n\n");
            EditorSceneManager.playModeStartScene = scene;
            EditorApplication.isPlaying = true;
        }

        private static string Fingerprint()
        {
            string root = Application.persistentDataPath;
            if (!Directory.Exists(root)) return "No production directory";
            using var sha = SHA256.Create();
            return string.Join("\n", Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) == "round_recovery.json" || Path.GetFileName(path) == "saved_game.json")
                .OrderBy(path => path)
                .Select(path => path + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)))));
        }

        private static void Update()
        {
            if (!SessionState.GetBool(AutoRecoveryPlayModeSuite.ActiveKey, false)) return;
            if (SessionState.GetBool(RestartKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                EditorApplication.isPlaying = true;
                if (EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(RestartKey, false);
                return;
            }
            if (long.TryParse(SessionState.GetString(StartedKey, ""), out long started) && new TimeSpan(DateTime.UtcNow.Ticks - started).TotalMinutes > 5)
            {
                SessionState.SetBool(AutoRecoveryPlayModeSuite.FailedKey, true);
                SessionState.SetBool(AutoRecoveryPlayModeSuite.DoneKey, true);
                Debug.LogError("[RecoveryTests] Test timeout.");
            }
            if (EditorApplication.isPlaying && SessionState.GetBool(AutoRecoveryPlayModeSuite.DoneKey, false)) EditorApplication.isPlaying = false;
        }

        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(AutoRecoveryPlayModeSuite.ActiveKey, false)) return;
            if (state != PlayModeStateChange.EnteredEditMode) return;
            if (SessionState.GetBool(AutoRecoveryPlayModeSuite.DoneKey, false)
                && !SessionState.GetBool(AutoRecoveryPlayModeSuite.FailedKey, false)
                && SessionState.GetInt(AutoRecoveryPlayModeSuite.StageKey, 1) == 1)
            {
                SessionState.SetInt(AutoRecoveryPlayModeSuite.StageKey, 2);
                SessionState.SetBool(AutoRecoveryPlayModeSuite.DoneKey, false);
                SessionState.SetBool(RestartKey, true);
                return;
            }
            bool unchanged = Fingerprint() == SessionState.GetString(FingerprintKey, "");
            string root = SessionState.GetString(AutoRecoveryPlayModeSuite.RootKey, "");
            File.AppendAllText(Path.Combine(root, "report.md"), "\nProduction saves unchanged: " + unchanged + "\n");
            bool passed = unchanged && SessionState.GetBool(AutoRecoveryPlayModeSuite.DoneKey, false) && !SessionState.GetBool(AutoRecoveryPlayModeSuite.FailedKey, false);
            File.AppendAllText(Path.Combine(root, "report.md"), "Result: " + (passed ? "PASSED" : "FAILED / interrupted") + "\n");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(OriginalSceneKey, ""));
            SessionState.SetBool(AutoRecoveryPlayModeSuite.ActiveKey, false);
            Debug.Log("[RecoveryTests] " + (passed ? "PASSED" : "FAILED") + "; report: " + Path.Combine(root, "report.md"));
        }
    }
}
#endif
