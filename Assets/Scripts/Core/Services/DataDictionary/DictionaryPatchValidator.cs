using System.Collections.Generic;

namespace Core.Services.DataDictionary
{
    public static class DictionaryPatchValidator
    {
        public const int SupportedSchemaVersion = 1;

        public static bool TryValidate(DictionaryPatchModel patch, out string error)
        {
            if (patch == null)
                return Fail("Patch is null.", out error);

            if (patch.schemaVersion != SupportedSchemaVersion)
            {
                return Fail(
                    $"Unsupported schemaVersion {patch.schemaVersion}; expected {SupportedSchemaVersion}.",
                    out error);
            }

            if (patch.revision < 0)
                return Fail($"Revision must be a non-negative integer, got {patch.revision}.", out error);

            if (patch.upsert == null)
                return Fail("The 'upsert' array is missing or null.", out error);

            if (patch.remove == null)
                return Fail("The 'remove' array is missing or null.", out error);

            var upsertWords = new HashSet<string>();
            for (int i = 0; i < patch.upsert.Count; i++)
            {
                DictionaryPatchEntry entry = patch.upsert[i];
                if (entry == null)
                    return Fail($"upsert[{i}] is null.", out error);

                string word = DictionaryService.NormalizeWord(entry.word);
                if (string.IsNullOrEmpty(word))
                    return Fail($"upsert[{i}].word is empty.", out error);

                if (entry.definition == null)
                    return Fail($"upsert[{i}].definition is missing or null.", out error);

                if (!upsertWords.Add(word))
                    return Fail($"Word '{word}' occurs more than once in upsert.", out error);
            }

            var removedWords = new HashSet<string>();
            for (int i = 0; i < patch.remove.Count; i++)
            {
                string word = DictionaryService.NormalizeWord(patch.remove[i]);
                if (string.IsNullOrEmpty(word))
                    return Fail($"remove[{i}] is empty.", out error);

                if (!removedWords.Add(word))
                    return Fail($"Word '{word}' occurs more than once in remove.", out error);

                if (upsertWords.Contains(word))
                    return Fail($"Word '{word}' occurs in both upsert and remove.", out error);
            }

            error = string.Empty;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
