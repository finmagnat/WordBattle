#if UNITY_EDITOR
using System;
using Core.Services.DataDictionary;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace Core.Services.DataDictionary.Editor
{
    [InitializeOnLoad]
    public static class DictionaryPatchRuntimeTestCoordinator
    {
        private const string StartTimeKey = "WordBattle.DictionaryPatchRuntimeTest.StartTime";
        private const double TimeoutSeconds = 30;

        static DictionaryPatchRuntimeTestCoordinator()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += Cleanup;
        }

        public static bool IsRunning =>
            !string.IsNullOrEmpty(SessionState.GetString(DictionaryPatchRuntimeTestBridge.SessionKey, string.Empty));

        public static bool Start(DictionaryPatchRuntimeTestSnapshot snapshot, out string error)
        {
            if (EditorSettings.enterPlayModeOptionsEnabled
                && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
            {
                error = "Runtime Patch Test requires Scene Reload so the normal CoreInstaller startup pipeline runs. " +
                        "Re-enable Scene Reload in Enter Play Mode Options, then retry.";
                return false;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || IsRunning)
            {
                error = "A Play Mode session is already running or starting.";
                return false;
            }

            snapshot.originalLocale = LocalizationSettings.SelectedLocale?.Identifier.Code ?? string.Empty;
            SessionState.SetString(DictionaryPatchRuntimeTestBridge.SessionKey, JsonUtility.ToJson(snapshot));
            SessionState.SetString(DictionaryPatchRuntimeTestBridge.ResultKey, string.Empty);
            SessionState.SetString(DictionaryPatchRuntimeTestBridge.LastResultKey, string.Empty);
            SessionState.SetString(StartTimeKey, DateTime.UtcNow.Ticks.ToString());
            try
            {
                EditorApplication.isPlaying = true;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                Cleanup();
                error = $"Could not enter Play Mode: {exception.Message}";
                return false;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (!IsRunning)
                return;

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetString(StartTimeKey, DateTime.UtcNow.Ticks.ToString());
                return;
            }

            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                FinalizeResultIfAvailable();
                if (string.IsNullOrEmpty(SessionState.GetString(
                        DictionaryPatchRuntimeTestBridge.LastResultKey,
                        string.Empty)))
                {
                    StoreFailure("Play Mode was stopped manually before dictionary initialization completed.");
                }
            }

            if (change == PlayModeStateChange.EnteredEditMode)
                Cleanup();
        }

        private static void OnEditorUpdate()
        {
            if (!IsRunning)
                return;

            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (!FinalizeResultIfAvailable()
                        && string.IsNullOrEmpty(SessionState.GetString(
                            DictionaryPatchRuntimeTestBridge.LastResultKey,
                            string.Empty)))
                        StoreFailure("Play Mode did not start or ended before the test completed.");
                    Cleanup();
                }

                return;
            }

            if (FinalizeResultIfAvailable())
            {
                EditorApplication.isPlaying = false;
                return;
            }

            string ticksText = SessionState.GetString(StartTimeKey, string.Empty);
            if (long.TryParse(ticksText, out long ticks)
                && new TimeSpan(DateTime.UtcNow.Ticks - ticks).TotalSeconds >= TimeoutSeconds)
            {
                StoreFailure("Dictionary initialization timeout (30 seconds). Check internet, PlayFab login and startup logs.");
                EditorApplication.isPlaying = false;
            }
        }

        private static bool FinalizeResultIfAvailable()
        {
            string result = SessionState.GetString(DictionaryPatchRuntimeTestBridge.ResultKey, string.Empty);
            if (string.IsNullOrEmpty(result))
                return false;

            SessionState.SetString(DictionaryPatchRuntimeTestBridge.LastResultKey, result);
            return true;
        }

        private static void StoreFailure(string message)
        {
            if (!TryReadSnapshot(out DictionaryPatchRuntimeTestSnapshot snapshot))
                return;

            DictionaryPatchRuntimeTestResult result =
                DictionaryPatchRuntimeTestResult.Failed(snapshot, message);
            string json = JsonUtility.ToJson(result);
            SessionState.SetString(DictionaryPatchRuntimeTestBridge.LastResultKey, json);
            Debug.LogError(result.details);
        }

        private static bool TryReadSnapshot(out DictionaryPatchRuntimeTestSnapshot snapshot)
        {
            snapshot = null;
            string json = SessionState.GetString(DictionaryPatchRuntimeTestBridge.SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                snapshot = JsonUtility.FromJson<DictionaryPatchRuntimeTestSnapshot>(json);
                return snapshot != null;
            }
            catch
            {
                return false;
            }
        }

        private static void Cleanup()
        {
            if (TryReadSnapshot(out DictionaryPatchRuntimeTestSnapshot snapshot)
                && !string.IsNullOrWhiteSpace(snapshot.originalLocale))
            {
                try
                {
                    var original = LocalizationSettings.AvailableLocales?.GetLocale(snapshot.originalLocale);
                    if (original != null)
                        LocalizationSettings.SelectedLocale = original;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[DictionaryPatchRuntimeTest] Could not restore selected locale: {exception.Message}");
                }
            }

            SessionState.SetString(DictionaryPatchRuntimeTestBridge.SessionKey, string.Empty);
            SessionState.SetString(DictionaryPatchRuntimeTestBridge.ResultKey, string.Empty);
            SessionState.SetString(StartTimeKey, string.Empty);
        }
    }
}
#endif
