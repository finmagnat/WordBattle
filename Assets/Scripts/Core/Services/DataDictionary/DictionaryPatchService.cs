using System;
using System.Collections.Generic;
using System.IO;
using Core.Build;
using Cysharp.Threading.Tasks;
using PlayFab.ClientModels;
using UnityEngine;

namespace Core.Services.DataDictionary
{
    public sealed class DictionaryPatchService
    {
        private const string TitleDataKeyPrefix = "DictionaryPatch_";
        private const string CacheDirectoryName = "DictionaryPatches";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly HashSet<string> _processedLocales = new();

        public async UniTask ApplyLatestPatchAsync(string languageCode, DictionaryService dictionaryService)
        {
            string locale = NormalizeLocale(languageCode);
            if (string.IsNullOrEmpty(locale))
            {
                Debug.LogWarning("[DictionaryPatch] Locale is empty. Using embedded dictionary.");
                return;
            }

            if (!_processedLocales.Add(locale))
            {
                Debug.Log($"[DictionaryPatch] Locale {locale} was already synchronized in this session.");
                return;
            }

            try
            {
                await ApplyLatestPatchInternalAsync(locale, dictionaryService);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[DictionaryPatch] Unexpected error for locale {locale}: {exception.Message}. " +
                    "Startup will continue with the available dictionary.");
            }
        }

        private static async UniTask ApplyLatestPatchInternalAsync(
            string locale,
            DictionaryService dictionaryService)
        {
            int buildNumber = BuildInfo.AndroidVersionCode;
            string cachePath = GetCachePath(locale);
            int cachedRevision = 0;
            bool cacheApplied = false;

            DictionaryPatchCache cache = await TryLoadCacheAsync(cachePath, locale);
            if (cache != null)
            {
                if (cache.buildNumber != buildNumber)
                {
                    Debug.LogWarning(
                        $"[DictionaryPatch] Ignoring stale {locale} cache: build {cache.buildNumber}, " +
                        $"current build {buildNumber}. Embedded fallback is active.");
                    TryDeleteStaleCache(cachePath, locale);
                }
                else
                {
                    DictionaryPatchModel cachedPatch = cache.ToPatch();
                    if (DictionaryPatchValidator.TryValidate(cachedPatch, out string cacheError))
                    {
                        DictionaryPatchApplyResult result = dictionaryService.ApplyPatch(cachedPatch);
                        cachedRevision = cachedPatch.revision;
                        cacheApplied = true;
                        LogApplied(locale, "cached", cachedPatch.revision, result);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[DictionaryPatch] Invalid {locale} cache ignored: {cacheError} " +
                            "Using embedded fallback.");
                    }
                }
            }
            else
            {
                Debug.Log($"[DictionaryPatch] No cached patch for {locale}. Using embedded dictionary as fallback.");
            }

            string titleDataKey = TitleDataKeyPrefix + locale;
            DictionaryPatchModel remotePatch = await TryLoadRemotePatchAsync(titleDataKey, locale);
            if (remotePatch == null)
            {
                Debug.LogWarning(
                    $"[DictionaryPatch] Remote patch unavailable for {locale}. " +
                    $"Continuing with {(cacheApplied ? $"cached revision {cachedRevision}" : "embedded dictionary")}.");
                return;
            }

            Debug.Log(
                $"[DictionaryPatch] Locale {locale}: cached revision {cachedRevision}, " +
                $"remote revision {remotePatch.revision}.");

            if (remotePatch.revision <= cachedRevision)
            {
                Debug.Log(
                    $"[DictionaryPatch] Remote revision {remotePatch.revision} is not newer. " +
                    "Runtime dictionary and cache remain unchanged.");
                return;
            }

            var newCache = DictionaryPatchCache.FromPatch(buildNumber, remotePatch);
            if (!await TrySaveCacheAsync(cachePath, newCache, locale))
            {
                Debug.LogWarning(
                    $"[DictionaryPatch] Remote revision {remotePatch.revision} was not applied because " +
                    "the cache could not be saved. Continuing with the previous local state.");
                return;
            }

            DictionaryPatchApplyResult remoteResult = dictionaryService.ApplyPatch(remotePatch);
            LogApplied(locale, "remote", remotePatch.revision, remoteResult);
        }

        private static async UniTask<DictionaryPatchModel> TryLoadRemotePatchAsync(
            string titleDataKey,
            string locale)
        {
            GetTitleDataResult result;
            try
            {
                result = await PlayFabAsync.GetTitleDataAsync(new GetTitleDataRequest
                {
                    Keys = new List<string> { titleDataKey }
                }).Timeout(RequestTimeout);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[DictionaryPatch] PlayFab request failed for {locale}: {exception.Message}");
                return null;
            }

            if (result?.Data == null || !result.Data.TryGetValue(titleDataKey, out string json))
            {
                Debug.LogWarning($"[DictionaryPatch] Title Data key '{titleDataKey}' is missing.");
                return null;
            }

            if (!TryDeserializePatch(json, out DictionaryPatchModel patch, out string error))
            {
                Debug.LogWarning($"[DictionaryPatch] Rejected remote patch for {locale}: {error}");
                return null;
            }

            return patch;
        }

        private static async UniTask<DictionaryPatchCache> TryLoadCacheAsync(string path, string locale)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                string json = await File.ReadAllTextAsync(path);
                DictionaryPatchCache cache = JsonUtility.FromJson<DictionaryPatchCache>(json);
                if (cache == null)
                    throw new InvalidDataException("JSON produced a null cache object.");

                Debug.Log(
                    $"[DictionaryPatch] Found {locale} cache: build {cache.buildNumber}, " +
                    $"revision {cache.revision}.");
                return cache;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[DictionaryPatch] Failed to read {locale} cache: {exception.Message}. " +
                    "Using embedded fallback.");
                return null;
            }
        }

        private static async UniTask<bool> TrySaveCacheAsync(
            string path,
            DictionaryPatchCache cache,
            string locale)
        {
            string tempPath = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonUtility.ToJson(cache, true);
                await File.WriteAllTextAsync(tempPath, json);
                File.Copy(tempPath, path, true);
                File.Delete(tempPath);
                Debug.Log(
                    $"[DictionaryPatch] Saved {locale} cache for build {cache.buildNumber}, " +
                    $"revision {cache.revision}.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DictionaryPatch] Failed to save {locale} cache: {exception.Message}");
                TryDeleteFile(tempPath);
                return false;
            }
        }

        public static bool TryDeserializePatch(
            string json,
            out DictionaryPatchModel patch,
            out string error)
        {
            patch = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON is empty.";
                return false;
            }

            try
            {
                patch = JsonUtility.FromJson<DictionaryPatchModel>(json);
            }
            catch (Exception exception)
            {
                error = $"Malformed JSON: {exception.Message}";
                return false;
            }

            return DictionaryPatchValidator.TryValidate(patch, out error);
        }

        private static string GetCachePath(string locale)
        {
            return Path.Combine(
                Application.persistentDataPath,
                CacheDirectoryName,
                $"dictionary_patch_{locale.ToLowerInvariant()}.json");
        }

        private static string NormalizeLocale(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return string.Empty;

            string normalized = languageCode.Trim();
            int separatorIndex = normalized.IndexOf('-');
            if (separatorIndex > 0)
                normalized = normalized.Substring(0, separatorIndex);

            return normalized.ToUpperInvariant();
        }

        private static void TryDeleteStaleCache(string path, string locale)
        {
            try
            {
                TryDeleteFile(path);
                Debug.Log($"[DictionaryPatch] Deleted stale cache for {locale}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DictionaryPatch] Could not delete stale {locale} cache: {exception.Message}");
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void LogApplied(
            string locale,
            string source,
            int revision,
            DictionaryPatchApplyResult result)
        {
            Debug.Log(
                $"[DictionaryPatch] Applied {source} patch for {locale}, revision {revision}: " +
                $"upsert {result.UpsertCount}, remove {result.RemoveCount} " +
                $"({result.RemovedExistingCount} existed). Runtime words: {result.TotalWords}.");
        }
    }
}
