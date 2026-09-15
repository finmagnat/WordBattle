#if UNITY_EDITOR
using System;
using System.Security.Cryptography;
using System.Text;
using Core.Build;
using UnityEditor;
using UnityEngine;

namespace Core.Services.DataDictionary
{
    // Editor-only diagnostic bridge. SessionState survives both Enter Play Mode domain reload settings.
    public static class DictionaryPatchRuntimeTestBridge
    {
        public const string SessionKey = "WordBattle.DictionaryPatchRuntimeTest.Session";
        public const string ResultKey = "WordBattle.DictionaryPatchRuntimeTest.Result";
        public const string LastResultKey = "WordBattle.DictionaryPatchRuntimeTest.LastResult";

        public static bool IsActive => EditorApplication.isPlaying
                                       && !string.IsNullOrEmpty(SessionState.GetString(SessionKey, string.Empty));

        public static bool TryGetSnapshot(out DictionaryPatchRuntimeTestSnapshot snapshot)
        {
            snapshot = null;
            if (!IsActive)
                return false;

            string json = SessionState.GetString(SessionKey, string.Empty);
            try
            {
                snapshot = JsonUtility.FromJson<DictionaryPatchRuntimeTestSnapshot>(json);
                return snapshot != null && snapshot.patch != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetLanguageOverride(out string language)
        {
            language = string.Empty;
            if (!TryGetSnapshot(out DictionaryPatchRuntimeTestSnapshot snapshot))
                return false;

            language = snapshot.language;
            return !string.IsNullOrWhiteSpace(language);
        }

        public static string GetPatchFingerprint(DictionaryPatchModel patch)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(patch));
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(jsonBytes)).Replace("-", string.Empty);
        }

        public static void OnDictionaryReady(DictionaryManager manager, DictionaryPatchService patchService)
        {
            if (!TryGetSnapshot(out DictionaryPatchRuntimeTestSnapshot snapshot))
                return;

            DictionaryPatchRuntimeTestResult result;
            try
            {
                result = Evaluate(snapshot, manager, patchService);
            }
            catch (Exception exception)
            {
                result = DictionaryPatchRuntimeTestResult.Failed(
                    snapshot,
                    $"Fatal dictionary test error: {exception.Message}");
            }

            SessionState.SetString(ResultKey, JsonUtility.ToJson(result));
            if (result.status == "PASSED")
                Debug.Log(result.details);
            else
                Debug.LogError(result.details);
        }

        public static DictionaryPatchRuntimeTestResult Evaluate(
            DictionaryPatchRuntimeTestSnapshot snapshot,
            DictionaryManager manager,
            DictionaryPatchService patchService)
        {
            var result = new DictionaryPatchRuntimeTestResult
            {
                language = snapshot.language,
                titleId = snapshot.titleId,
                fingerprint = snapshot.fingerprint,
                expectedRevision = snapshot.patch.revision,
                build = BuildInfo.AndroidVersionCode,
                upsertTotal = snapshot.patch.upsert.Count,
                removeTotal = snapshot.patch.remove.Count,
                runtimeRevision = patchService.GetAppliedRevision(snapshot.language)
            };

            DictionaryService dictionary = manager.Service;
            string runtimeLanguage = dictionary.DictionaryConfig?.languageCode ?? string.Empty;
            var errors = new StringBuilder();
            int errorCount = 0;

            if (!dictionary.IsLoaded)
            {
                errors.AppendLine("DictionaryService is not loaded.");
                errorCount++;
            }

            if (!patchService.WasPatchApplied(snapshot.language))
            {
                errors.AppendLine("No cache or remote Dictionary Patch was actually applied by DictionaryPatchService.");
                errorCount++;
            }

            if (!string.Equals(runtimeLanguage, snapshot.language, StringComparison.OrdinalIgnoreCase))
            {
                errors.AppendLine($"Language mismatch. Expected: {snapshot.language}; Runtime: {runtimeLanguage}.");
                errorCount++;
            }

            if (result.runtimeRevision != result.expectedRevision)
            {
                errorCount++;
                if (result.runtimeRevision < result.expectedRevision)
                    errors.AppendLine(
                        $"Runtime received an older patch revision. Expected: {result.expectedRevision}; " +
                        $"Received: {result.runtimeRevision}. PlayFab Title Data may not have propagated yet. Retry the test.");
                else
                    errors.AppendLine(
                        $"Revision mismatch. Expected: {result.expectedRevision}; Runtime: {result.runtimeRevision}.");
            }

            foreach (DictionaryPatchEntry entry in snapshot.patch.upsert)
            {
                if (!dictionary.Contains(entry.word))
                {
                    errors.AppendLine($"MISSING: {entry.word}");
                    errorCount++;
                    continue;
                }

                string expectedDefinition = entry.definition?.Trim() ?? string.Empty;
                string runtimeDefinition = dictionary.GetDefinition(entry.word);
                if (!string.Equals(expectedDefinition, runtimeDefinition, StringComparison.Ordinal))
                {
                    errors.AppendLine(
                        $"ENTRY MISMATCH: {entry.word}\n  Expected: {expectedDefinition}\n  Runtime: {runtimeDefinition}");
                    errorCount++;
                    continue;
                }

                result.upsertPassed++;
            }

            foreach (string word in snapshot.patch.remove)
            {
                if (dictionary.Contains(word))
                {
                    errors.AppendLine($"SHOULD BE REMOVED BUT EXISTS: {word}");
                    errorCount++;
                }
                else
                    result.removePassed++;
            }

            result.status = errors.Length == 0 ? "PASSED" : "FAILED";
            result.details =
                $"PATCH RUNTIME TEST {result.status}\n" +
                $"Language: {snapshot.language.ToUpperInvariant()} (runtime: {runtimeLanguage})\n" +
                $"Expected revision: {result.expectedRevision}\n" +
                $"Runtime revision: {result.runtimeRevision}\n" +
                $"Build: {result.build}\n" +
                $"Upsert: {result.upsertPassed}/{result.upsertTotal}\n" +
                $"Remove: {result.removePassed}/{result.removeTotal}\n" +
                $"Errors: {errorCount}" + (errorCount > 0 ? $"\n{errors}" : string.Empty);
            return result;
        }
    }

    [Serializable]
    public sealed class DictionaryPatchRuntimeTestSnapshot
    {
        public string language;
        public string titleId;
        public string fingerprint;
        public string originalLocale;
        public int build;
        public DictionaryPatchModel patch;
    }

    [Serializable]
    public sealed class DictionaryPatchRuntimeTestResult
    {
        public string status;
        public string language;
        public string titleId;
        public string fingerprint;
        public int expectedRevision;
        public int runtimeRevision;
        public int build;
        public int upsertPassed;
        public int upsertTotal;
        public int removePassed;
        public int removeTotal;
        public string details;

        public static DictionaryPatchRuntimeTestResult Failed(
            DictionaryPatchRuntimeTestSnapshot snapshot,
            string message)
        {
            return new DictionaryPatchRuntimeTestResult
            {
                status = "FAILED",
                language = snapshot.language,
                titleId = snapshot.titleId,
                fingerprint = snapshot.fingerprint,
                expectedRevision = snapshot.patch?.revision ?? 0,
                runtimeRevision = -1,
                build = snapshot.build,
                upsertTotal = snapshot.patch?.upsert?.Count ?? 0,
                removeTotal = snapshot.patch?.remove?.Count ?? 0,
                details = $"PATCH RUNTIME TEST FAILED\nLanguage: {snapshot.language}\n{message}"
            };
        }
    }
}
#endif
