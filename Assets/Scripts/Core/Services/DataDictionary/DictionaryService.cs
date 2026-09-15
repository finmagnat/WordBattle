using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Core.Services.DataDictionary
{
    public class DictionaryService : IDictionaryService
    {
        private const string WordPrefix = "WORD:";
        private const string DefinitionPrefix = "DEFINITION:";

        private readonly AddressablesLoader _loader;
        private readonly Dictionary<string, DictionaryData> _cacheByDictionaryKey = new();

        private LanguageDictionaryConfig _config;
        private HashSet<string> _words;
        private Dictionary<string, string> _definitionsByWord;
        private Dictionary<int, List<string>> _wordsByLength;
        private string _alphabet;
        private bool _isLoaded;

        public LanguageDictionaryConfig DictionaryConfig => _config;
        public string Alphabet => _alphabet; // Алфавит текущего словаря.
        public IReadOnlyCollection<string> Words => _words;
        public IReadOnlyDictionary<string, string> WordDefinitions => _definitionsByWord;
        public bool IsLoaded => _isLoaded;

        public DictionaryService(AddressablesLoader loader)
        {
            _loader = loader;
        }

        /// <summary>
        /// Инициализация словаря для конкретного языка.
        /// </summary>
        public async UniTask InitializeAsync(LanguageDictionaryConfig config)
        {
            if (config == null)
            {
                Debug.LogError("Dictionary config is null.");
                return;
            }

            string cacheKey = GetCacheKey(config);
            if (_isLoaded && _config == config)
                return;

            if (_cacheByDictionaryKey.TryGetValue(cacheKey, out var cachedData))
            {
                ApplyData(config, cachedData);
                Debug.Log($"Dictionary restored from cache. Lang: {_config.languageCode}, words: {_words.Count}, definitions: {_definitionsByWord.Count}");
                return;
            }

            _config = config;
            _isLoaded = false;
            _words = new HashSet<string>();
            _definitionsByWord = new Dictionary<string, string>();
            _wordsByLength = new Dictionary<int, List<string>>();

            // 1. Алфавит из SO
            _alphabet = _config.alphabet;

            // 2. Загружаем текстовый файл словаря по ключу Addressables
            if (string.IsNullOrWhiteSpace(_config.dictionaryAddressKey))
            {
                Debug.LogError("❌ Dictionary address key is empty in LanguageDictionaryConfig");
                return;
            }

            var textAsset = await _loader.LoadAssetAsync<TextAsset>(_config.dictionaryAddressKey);
            if (textAsset == null)
            {
                Debug.LogError($"❌ Failed to load dictionary TextAsset by key: {_config.dictionaryAddressKey}");
                return;
            }

            LoadDictionary(textAsset.text);

            BuildWordLengthIndex();
            _isLoaded = true;
            _cacheByDictionaryKey[cacheKey] = new DictionaryData(
                _config.alphabet,
                _words,
                _definitionsByWord,
                _wordsByLength);

            Debug.Log($"📘 Dictionary initialized. Lang: {_config.languageCode}, words: {_words.Count}, definitions: {_definitionsByWord.Count}");
        }

        private static string GetCacheKey(LanguageDictionaryConfig config)
        {
            return $"{config.languageCode}|{config.dictionaryAddressKey}";
        }

        private void ApplyData(LanguageDictionaryConfig config, DictionaryData data)
        {
            _config = config;
            _alphabet = data.Alphabet;
            _words = data.Words;
            _definitionsByWord = data.DefinitionsByWord;
            _wordsByLength = data.WordsByLength;
            _isLoaded = true;
        }

        private void LoadDictionary(string text)
        {
            var lines = text.Split('\n');
            int skippedMalformedLines = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                if (TryReadPrefixedValue(line, WordPrefix, out var word))
                {
                    string definition = string.Empty;
                    int definitionLineIndex = FindNextNonEmptyLine(lines, i + 1);

                    if (definitionLineIndex >= 0
                        && TryReadPrefixedValue(lines[definitionLineIndex].Trim(), DefinitionPrefix, out var parsedDefinition))
                    {
                        definition = parsedDefinition;
                        i = definitionLineIndex;
                    }
                    else
                    {
                        Debug.LogWarning($"⚠ Dictionary word '{word}' has no definition line.");
                    }

                    AddWord(word, definition);
                    continue;
                }

                skippedMalformedLines++;
            }

            if (skippedMalformedLines > 0)
                Debug.LogWarning($"⚠ Dictionary skipped {skippedMalformedLines} lines that do not match WORD/DEFINITION format.");
        }

        private static int FindNextNonEmptyLine(IReadOnlyList<string> lines, int startIndex)
        {
            for (int i = startIndex; i < lines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return i;
            }

            return -1;
        }

        private static bool TryReadPrefixedValue(string line, string prefix, out string value)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = line.Substring(prefix.Length).Trim();
                return true;
            }

            value = string.Empty;
            return false;
        }

        private void AddWord(string word, string definition)
        {
            var normalizedWord = NormalizeWord(word);
            if (string.IsNullOrEmpty(normalizedWord))
                return;

            _words.Add(normalizedWord);

            if (!string.IsNullOrWhiteSpace(definition))
                _definitionsByWord[normalizedWord] = definition.Trim();
        }

        /// <summary>
        /// Алфавит текущего словаря (как строка).
        /// </summary>
        public string GetAlphabet() => _alphabet ?? string.Empty;

        /// <summary>
        /// Индексируем слова по длине для быстрых выборок.
        /// </summary>
        private void BuildWordLengthIndex()
        {
            _wordsByLength.Clear();

            foreach (var word in _words)
            {
                int len = word.Length;
                if (!_wordsByLength.TryGetValue(len, out var list))
                {
                    list = new List<string>();
                    _wordsByLength[len] = list;
                }

                list.Add(word);
            }

            Debug.Log($"📊 Words indexed by length. Length groups: {_wordsByLength.Count}");
        }

        /// <summary>
        /// Перемешать порядок слов внутри групп по длине для новой игровой сессии.
        /// </summary>
        public void ShuffleWordsForGameSession()
        {
            if (!_isLoaded)
            {
                Debug.LogWarning("⚠ DictionaryService.ShuffleWordsForGameSession called before dictionary was loaded.");
                return;
            }

            int shuffledWords = 0;
            foreach (var words in _wordsByLength.Values)
            {
                Shuffle(words);
                shuffledWords += words.Count;
            }

            Debug.Log($"🔀 Dictionary words shuffled for game session. Groups: {_wordsByLength.Count}, words: {shuffledWords}");
        }

        private static void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>
        /// Вернуть случайное слово указанной длины.
        /// Если слов такой длины нет — возвращает null.
        /// </summary>
        public string GetRandomWord(uint wordLength)
        {
            if (!_isLoaded)
            {
                Debug.LogWarning("⚠ DictionaryService.GetRandomWord called before dictionary was loaded.");
                return null;
            }

            if (wordLength <= 0 || wordLength > int.MaxValue)
                return null;

            int len = (int)wordLength;

            if (!_wordsByLength.TryGetValue(len, out var list) || list == null || list.Count == 0)
                return null;

            int index = UnityEngine.Random.Range(0, list.Count);
            return list[index];
        }

        /// <summary>
        /// Получить все слова заданной длины (read-only).
        /// Если таких слов нет — возвращает пустой массив.
        /// </summary>
        public IReadOnlyList<string> GetWordsOfLength(int length)
        {
            if (!_isLoaded)
                return Array.Empty<string>();

            if (length <= 0)
                return Array.Empty<string>();

            if (_wordsByLength.TryGetValue(length, out var list) && list != null)
                return list;

            return Array.Empty<string>();
        }

        /// <summary>
        /// Проверка наличия слова в словаре.
        /// </summary>
        public bool Contains(string word)
        {
            if (!_isLoaded || string.IsNullOrWhiteSpace(word))
                return false;

            return _words.Contains(NormalizeWord(word));
        }

        public bool TryGetDefinition(string word, out string definition)
        {
            definition = string.Empty;

            if (!_isLoaded || string.IsNullOrWhiteSpace(word))
                return false;

            return _definitionsByWord.TryGetValue(NormalizeWord(word), out definition)
                   && !string.IsNullOrWhiteSpace(definition);
        }

        public string GetDefinition(string word)
        {
            return TryGetDefinition(word, out var definition)
                ? definition
                : string.Empty;
        }

        public DictionaryPatchApplyResult ApplyPatch(DictionaryPatchModel patch)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("Cannot apply a dictionary patch before the embedded dictionary is loaded.");

            if (!DictionaryPatchValidator.TryValidate(patch, out string error))
                throw new ArgumentException($"Cannot apply an invalid dictionary patch: {error}", nameof(patch));

            var patchedWords = new HashSet<string>(_words);
            var patchedDefinitions = new Dictionary<string, string>(_definitionsByWord);
            int removedExistingCount = 0;

            foreach (DictionaryPatchEntry entry in patch.upsert)
            {
                string word = NormalizeWord(entry.word);
                patchedWords.Add(word);

                if (string.IsNullOrWhiteSpace(entry.definition))
                    patchedDefinitions.Remove(word);
                else
                    patchedDefinitions[word] = entry.definition.Trim();
            }

            foreach (string rawWord in patch.remove)
            {
                string word = NormalizeWord(rawWord);
                if (patchedWords.Remove(word))
                    removedExistingCount++;

                patchedDefinitions.Remove(word);
            }

            _words = patchedWords;
            _definitionsByWord = patchedDefinitions;
            _wordsByLength = new Dictionary<int, List<string>>();
            BuildWordLengthIndex();

            if (_config != null)
            {
                _cacheByDictionaryKey[GetCacheKey(_config)] = new DictionaryData(
                    _alphabet,
                    _words,
                    _definitionsByWord,
                    _wordsByLength);
            }

            return new DictionaryPatchApplyResult(
                patch.upsert.Count,
                patch.remove.Count,
                removedExistingCount,
                _words.Count);
        }

        internal static string NormalizeWord(string word)
        {
            return word?.Trim().ToUpperInvariant() ?? string.Empty;
        }

        private sealed class DictionaryData
        {
            public DictionaryData(
                string alphabet,
                HashSet<string> words,
                Dictionary<string, string> definitionsByWord,
                Dictionary<int, List<string>> wordsByLength)
            {
                Alphabet = alphabet;
                Words = words;
                DefinitionsByWord = definitionsByWord;
                WordsByLength = wordsByLength;
            }

            public string Alphabet { get; }
            public HashSet<string> Words { get; }
            public Dictionary<string, string> DefinitionsByWord { get; }
            public Dictionary<int, List<string>> WordsByLength { get; }
        }
    }

    public readonly struct DictionaryPatchApplyResult
    {
        public DictionaryPatchApplyResult(
            int upsertCount,
            int removeCount,
            int removedExistingCount,
            int totalWords)
        {
            UpsertCount = upsertCount;
            RemoveCount = removeCount;
            RemovedExistingCount = removedExistingCount;
            TotalWords = totalWords;
        }

        public int UpsertCount { get; }
        public int RemoveCount { get; }
        public int RemovedExistingCount { get; }
        public int TotalWords { get; }
    }
}
