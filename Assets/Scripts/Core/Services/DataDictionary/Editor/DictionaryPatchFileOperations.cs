#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Core.DataDictionary.Tools;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Core.Services.DataDictionary.Editor
{
    public static class DictionaryPatchFileOperations
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new(false);

        public static bool TryReadSource(
            TextAsset asset,
            string filePath,
            out List<string> lines,
            out string resolvedPath,
            out string error)
        {
            resolvedPath = asset != null ? AssetDatabase.GetAssetPath(asset) : filePath;
            if (string.IsNullOrWhiteSpace(resolvedPath))
                return Fail("Select a source text file.", out lines, out error);

            string absolutePath = GetAbsolutePath(resolvedPath);
            if (!File.Exists(absolutePath))
                return Fail($"Source file does not exist: {resolvedPath}", out lines, out error);

            try
            {
                lines = new List<string>(File.ReadAllLines(absolutePath, Encoding.UTF8));
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                return Fail($"Could not read '{resolvedPath}': {exception.Message}", out lines, out error);
            }
        }

        public static bool TryParseUpsertSource(
            TextAsset asset,
            string filePath,
            out List<DictionaryEntry> entries,
            out string error)
        {
            if (!TryReadSource(asset, filePath, out List<string> lines, out _, out error))
            {
                entries = null;
                return false;
            }

            return TryParseValidDictionary(lines, out entries, out error);
        }

        public static bool TryParseRemoveSource(
            TextAsset asset,
            string filePath,
            out List<string> words,
            out string error)
        {
            words = null;
            if (!TryReadSource(asset, filePath, out List<string> lines, out _, out error))
                return false;

            List<DictionaryEntry> entries = DictionaryCleaner.CreateEntriesFromWordList(
                lines,
                out int emptyLineCount,
                out List<DictionaryValidationIssue> issues);

            DictionaryValidationIssue firstError = issues.FirstOrDefault(
                issue => issue.Severity == DictionaryValidationSeverity.Error);
            if (firstError != null)
            {
                error = firstError.Message;
                return false;
            }

            if (emptyLineCount > 0)
            {
                error = $"The remove list contains {emptyLineCount} empty line(s).";
                return false;
            }

            if (entries.Count == 0)
            {
                error = "The remove list is empty.";
                return false;
            }

            var uniqueWords = new HashSet<string>(StringComparer.Ordinal);
            words = new List<string>(entries.Count);
            foreach (DictionaryEntry entry in entries)
            {
                string word = DictionaryCleaner.NormalizeWord(entry.Word);
                if (string.IsNullOrEmpty(word))
                {
                    error = $"Line {entry.WordLineNumber} contains an empty word.";
                    words = null;
                    return false;
                }

                if (!uniqueWords.Add(word))
                {
                    error = $"Word '{word}' occurs more than once in the remove source.";
                    words = null;
                    return false;
                }

                words.Add(word);
            }

            error = string.Empty;
            return true;
        }

        public static bool TryMergeUpsert(
            DictionaryPatchModel sourcePatch,
            IReadOnlyList<DictionaryEntry> importedEntries,
            out DictionaryPatchModel mergedPatch,
            out DictionaryPatchBulkImportResult result,
            out string error)
        {
            mergedPatch = ClonePatch(sourcePatch);
            int added = 0;
            int replaced = 0;
            int moved = 0;

            foreach (DictionaryEntry importedEntry in importedEntries)
            {
                string word = DictionaryCleaner.NormalizeWord(importedEntry.Word);
                int upsertIndex = mergedPatch.upsert.FindIndex(entry => WordsEqual(entry?.word, word));
                bool removedFromRemove = mergedPatch.remove.RemoveAll(removeWord =>
                    WordsEqual(removeWord, word)) > 0;
                var patchEntry = new DictionaryPatchEntry
                {
                    word = word,
                    definition = importedEntry.Definition?.Trim() ?? string.Empty
                };

                if (upsertIndex >= 0)
                    mergedPatch.upsert[upsertIndex] = patchEntry;
                else
                    mergedPatch.upsert.Add(patchEntry);

                if (removedFromRemove)
                    moved++;
                else if (upsertIndex >= 0)
                    replaced++;
                else
                    added++;
            }

            if (!DictionaryPatchValidator.TryValidate(mergedPatch, out error))
            {
                mergedPatch = null;
                result = default;
                return false;
            }

            result = new DictionaryPatchBulkImportResult(importedEntries.Count, added, replaced, moved);
            error = string.Empty;
            return true;
        }

        public static bool TryMergeRemove(
            DictionaryPatchModel sourcePatch,
            IReadOnlyList<string> importedWords,
            out DictionaryPatchModel mergedPatch,
            out DictionaryPatchBulkImportResult result,
            out string error)
        {
            mergedPatch = ClonePatch(sourcePatch);
            int added = 0;
            int alreadyPresent = 0;
            int moved = 0;

            foreach (string importedWord in importedWords)
            {
                string word = DictionaryCleaner.NormalizeWord(importedWord);
                bool removedFromUpsert = mergedPatch.upsert.RemoveAll(entry =>
                    WordsEqual(entry?.word, word)) > 0;
                bool existsInRemove = mergedPatch.remove.Any(removeWord => WordsEqual(removeWord, word));

                if (!existsInRemove)
                    mergedPatch.remove.Add(word);

                if (removedFromUpsert)
                    moved++;
                else if (existsInRemove)
                    alreadyPresent++;
                else
                    added++;
            }

            if (!DictionaryPatchValidator.TryValidate(mergedPatch, out error))
            {
                mergedPatch = null;
                result = default;
                return false;
            }

            result = new DictionaryPatchBulkImportResult(
                importedWords.Count,
                added,
                alreadyPresent,
                moved);
            error = string.Empty;
            return true;
        }

        public static bool TryCreateApplyPlan(
            TextAsset dictionaryAsset,
            string expectedLanguage,
            DictionaryPatchModel patch,
            out DictionaryPatchApplyPlan plan,
            out string error)
        {
            plan = null;
            if (dictionaryAsset == null)
            {
                error = "Select a source dictionary TextAsset.";
                return false;
            }

            if (!DictionaryPatchValidator.TryValidate(patch, out string patchError))
            {
                error = $"Patch validation failed: {patchError}";
                return false;
            }

            if (!TryGetDictionaryLanguage(dictionaryAsset, out string actualLanguage, out error))
                return false;

            if (!string.Equals(actualLanguage, expectedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Selected dictionary is {actualLanguage.ToUpperInvariant()}, but the current patch is " +
                        $"{expectedLanguage.ToUpperInvariant()}.";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(dictionaryAsset);
            string absolutePath = GetAbsolutePath(assetPath);
            byte[] originalBytes;
            List<string> sourceLines;
            try
            {
                originalBytes = File.ReadAllBytes(absolutePath);
                sourceLines = new List<string>(File.ReadAllLines(absolutePath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                error = $"Could not read dictionary '{assetPath}': {exception.Message}";
                return false;
            }

            if (!TryParseValidDictionary(sourceLines, out List<DictionaryEntry> sourceEntries, out error))
                return false;

            DictionaryPatchDiff diff = CalculateDiff(sourceEntries, patch);
            List<DictionaryEntry> updatedEntries = ApplyPatch(sourceEntries, patch);
            string cultureName = GetCultureName(expectedLanguage);
            List<DictionaryEntry> sortedEntries = DictionaryCleaner.SortByWord(updatedEntries, cultureName);
            List<string> outputLines = DictionaryFileFormatter.FormatEntries(sortedEntries);

            if (!TryParseValidDictionary(outputLines, out _, out string outputError))
            {
                error = $"Prepared dictionary failed validation: {outputError}";
                return false;
            }

            plan = new DictionaryPatchApplyPlan(
                assetPath,
                absolutePath,
                originalBytes,
                outputLines,
                patch.revision,
                diff);
            error = string.Empty;
            return true;
        }

        public static bool TryApplyPlan(DictionaryPatchApplyPlan plan, out string error)
        {
            if (plan == null)
            {
                error = "Apply plan is missing.";
                return false;
            }

            byte[] currentBytes;
            try
            {
                currentBytes = File.ReadAllBytes(plan.AbsolutePath);
            }
            catch (Exception exception)
            {
                error = $"Could not re-read dictionary before applying: {exception.Message}";
                return false;
            }

            if (!currentBytes.SequenceEqual(plan.OriginalBytes))
            {
                error = "Dictionary changed on disk after preview. Preview again before applying.";
                return false;
            }

            string temporaryPath = plan.AbsolutePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backupPath = plan.AbsolutePath + ".dictionary_patch_backup";
            bool replacementCompleted = false;
            bool backupCreated = false;
            bool keepBackup = false;

            try
            {
                File.WriteAllLines(temporaryPath, plan.OutputLines, Utf8WithoutBom);

                if (File.Exists(backupPath))
                {
                    throw new IOException(
                        $"A previous recovery backup already exists at '{backupPath}'. " +
                        "Inspect or remove it before applying again.");
                }

                File.Replace(temporaryPath, plan.AbsolutePath, backupPath, true);
                backupCreated = true;
                replacementCompleted = true;

                List<string> savedLines = new(File.ReadAllLines(plan.AbsolutePath, Encoding.UTF8));
                if (!TryParseValidDictionary(savedLines, out _, out string validationError))
                    throw new InvalidDataException($"Saved dictionary failed validation: {validationError}");

                AssetDatabase.ImportAsset(plan.AssetPath, ImportAssetOptions.ForceUpdate);
                File.Delete(backupPath);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                if (replacementCompleted && File.Exists(backupPath))
                {
                    try
                    {
                        File.Copy(backupPath, plan.AbsolutePath, true);
                        AssetDatabase.ImportAsset(plan.AssetPath, ImportAssetOptions.ForceUpdate);
                    }
                    catch (Exception restoreException)
                    {
                        keepBackup = true;
                        error = $"Apply failed: {exception.Message}. Automatic restore also failed: " +
                                $"{restoreException.Message}. Backup kept at '{backupPath}'.";
                        return false;
                    }
                }

                error = $"Dictionary was not applied: {exception.Message}";
                return false;
            }
            finally
            {
                TryDelete(temporaryPath);
                if (backupCreated && !keepBackup)
                    TryDelete(backupPath);
            }
        }

        public static bool TryGetDictionaryLanguage(
            TextAsset dictionaryAsset,
            out string language,
            out string error)
        {
            language = string.Empty;
            string assetPath = AssetDatabase.GetAssetPath(dictionaryAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                error = "The selected TextAsset is not a project asset.";
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var entry = settings?.FindAssetEntry(guid);
            string address = entry?.address;

            switch (address)
            {
                case "dict_ru":
                    language = "ru";
                    break;
                case "dict_uk":
                    language = "uk";
                    break;
                case "dict_en":
                    language = "en";
                    break;
                default:
                    error = $"'{assetPath}' is not one of the configured Addressables dictionaries " +
                            "(dict_ru, dict_uk, dict_en).";
                    return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool TryParseValidDictionary(
            IReadOnlyList<string> lines,
            out List<DictionaryEntry> entries,
            out string error)
        {
            var parser = new DictionaryFileParser();
            DictionaryParseResult parseResult = parser.Parse(lines);
            DictionaryValidationIssue firstError = parseResult.Issues.FirstOrDefault(
                issue => issue.Severity == DictionaryValidationSeverity.Error);
            if (firstError != null)
            {
                entries = null;
                error = firstError.Message;
                return false;
            }

            if (parseResult.Entries.Count == 0)
            {
                entries = null;
                error = "Dictionary contains no entries.";
                return false;
            }

            DictionaryCleaner.RemoveDuplicates(
                parseResult.Entries,
                out List<DictionaryEntry> duplicates,
                out _);
            if (duplicates.Count > 0)
            {
                entries = null;
                DictionaryEntry duplicate = duplicates[0];
                error = $"Word '{DictionaryCleaner.NormalizeWord(duplicate.Word)}' occurs more than once " +
                        $"(line {duplicate.WordLineNumber}).";
                return false;
            }

            entries = parseResult.Entries;
            error = string.Empty;
            return true;
        }

        private static DictionaryPatchDiff CalculateDiff(
            IReadOnlyList<DictionaryEntry> sourceEntries,
            DictionaryPatchModel patch)
        {
            var sourceWords = new HashSet<string>(
                sourceEntries.Select(entry => DictionaryCleaner.NormalizeWord(entry.Word)),
                StringComparer.Ordinal);
            var added = new List<string>();
            var replaced = new List<string>();
            var removed = new List<string>();

            foreach (DictionaryPatchEntry entry in patch.upsert)
            {
                string word = DictionaryCleaner.NormalizeWord(entry.word);
                if (sourceWords.Contains(word))
                    replaced.Add(word);
                else
                    added.Add(word);
            }

            foreach (string rawWord in patch.remove)
            {
                string word = DictionaryCleaner.NormalizeWord(rawWord);
                if (sourceWords.Contains(word))
                    removed.Add(word);
            }

            return new DictionaryPatchDiff(added, replaced, removed);
        }

        private static List<DictionaryEntry> ApplyPatch(
            IReadOnlyList<DictionaryEntry> sourceEntries,
            DictionaryPatchModel patch)
        {
            var entriesByWord = sourceEntries.ToDictionary(
                entry => DictionaryCleaner.NormalizeWord(entry.Word),
                entry => new DictionaryEntry(
                    DictionaryCleaner.NormalizeWord(entry.Word),
                    entry.Definition?.Trim() ?? string.Empty,
                    0,
                    0),
                StringComparer.Ordinal);

            foreach (DictionaryPatchEntry entry in patch.upsert)
            {
                string word = DictionaryCleaner.NormalizeWord(entry.word);
                entriesByWord[word] = new DictionaryEntry(word, entry.definition.Trim(), 0, 0);
            }

            foreach (string rawWord in patch.remove)
                entriesByWord.Remove(DictionaryCleaner.NormalizeWord(rawWord));

            return entriesByWord.Values.ToList();
        }

        private static DictionaryPatchModel ClonePatch(DictionaryPatchModel source)
        {
            return new DictionaryPatchModel
            {
                schemaVersion = source.schemaVersion,
                revision = source.revision,
                upsert = source.upsert.Select(entry => new DictionaryPatchEntry
                {
                    word = entry?.word,
                    definition = entry?.definition
                }).ToList(),
                remove = new List<string>(source.remove)
            };
        }

        private static bool WordsEqual(string left, string right)
        {
            return string.Equals(
                DictionaryCleaner.NormalizeWord(left),
                DictionaryCleaner.NormalizeWord(right),
                StringComparison.Ordinal);
        }

        private static string GetCultureName(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "ru" => "ru-RU",
                "uk" => "uk-UA",
                "en" => "en-US",
                _ => string.Empty
            };
        }

        private static string GetAbsolutePath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
        }

        private static bool Fail(
            string message,
            out List<string> lines,
            out string error)
        {
            lines = null;
            error = message;
            return false;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup. The source dictionary has already been restored or saved.
            }
        }
    }

    public sealed class DictionaryPatchApplyPlan
    {
        public DictionaryPatchApplyPlan(
            string assetPath,
            string absolutePath,
            byte[] originalBytes,
            IReadOnlyList<string> outputLines,
            int revision,
            DictionaryPatchDiff diff)
        {
            AssetPath = assetPath;
            AbsolutePath = absolutePath;
            OriginalBytes = originalBytes;
            OutputLines = outputLines;
            Revision = revision;
            Diff = diff;
        }

        public string AssetPath { get; }
        public string AbsolutePath { get; }
        public byte[] OriginalBytes { get; }
        public IReadOnlyList<string> OutputLines { get; }
        public int Revision { get; }
        public DictionaryPatchDiff Diff { get; }
    }

    public sealed class DictionaryPatchDiff
    {
        public DictionaryPatchDiff(
            IReadOnlyList<string> added,
            IReadOnlyList<string> replaced,
            IReadOnlyList<string> removed)
        {
            Added = added;
            Replaced = replaced;
            Removed = removed;
        }

        public IReadOnlyList<string> Added { get; }
        public IReadOnlyList<string> Replaced { get; }
        public IReadOnlyList<string> Removed { get; }
    }

    public readonly struct DictionaryPatchBulkImportResult
    {
        public DictionaryPatchBulkImportResult(int imported, int added, int existing, int moved)
        {
            Imported = imported;
            Added = added;
            Existing = existing;
            Moved = moved;
        }

        public int Imported { get; }
        public int Added { get; }
        public int Existing { get; }
        public int Moved { get; }
    }
}
#endif
