#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core.DataDictionary.Tools;
using UnityEditor;
using UnityEngine;

namespace Core.DataDictionary.Editor
{
    public sealed class DictionaryToolsWindow : EditorWindow
    {
        private const string DefaultDictionaryPath = "Assets/Addressables/Text/Dictionaries/dict_ru.txt";

        private readonly DictionaryFileParser _parser = new DictionaryFileParser();
        private readonly List<string> _messages = new List<string>();

        private TextAsset _dictionaryAsset;
        private string _filePath = DefaultDictionaryPath;
        private string _sortCulture = "ru-RU";
        private int _maxWordLength = 9;
        private string _lastDuplicatesPath;
        private string _lastRemovedByLengthPath;
        private string _lastWordListPath;
        private Vector2 _scrollPosition;

        [MenuItem("Tools/Dictionary/Dictionary Tools")]
        public static void Open()
        {
            GetWindow<DictionaryToolsWindow>("Dictionary Tools");
        }

        public static void OpenWithAsset(TextAsset textAsset)
        {
            DictionaryToolsWindow window = GetWindow<DictionaryToolsWindow>("Dictionary Tools");
            if (textAsset != null)
            {
                Selection.activeObject = textAsset;
                window.ApplyDictionaryAsset(textAsset);
            }

            window.Focus();
        }

        private void OnEnable()
        {
            if (Selection.activeObject is TextAsset textAsset)
                ApplyDictionaryAsset(textAsset);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Dictionary Tools", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawFileSelector();
            _sortCulture = EditorGUILayout.TextField("Sort Culture", _sortCulture);
            _maxWordLength = Math.Max(1, EditorGUILayout.IntField("Max Word Length", _maxWordLength));

            EditorGUILayout.Space(8);

            bool hasFile = HasSelectedFile();
            using (new EditorGUI.DisabledScope(!hasFile))
            {
                if (GUILayout.Button("Convert Word List To Dictionary"))
                    ConvertWordListToDictionary();

                if (GUILayout.Button("Remove Empty Lines"))
                    RemoveEmptyLines();

                if (GUILayout.Button("Validate Structure"))
                    ValidateStructure();

                if (GUILayout.Button("Remove Duplicates"))
                    RemoveDuplicates();

                if (GUILayout.Button("Remove Words Longer Than Max Length"))
                    RemoveWordsLongerThanMaxLength();

                if (GUILayout.Button("Sort By Word"))
                    SortByWord();

                if (GUILayout.Button("Export Word List"))
                    ExportWordList();
            }

            if (GUILayout.Button("Use Project Selection"))
                TryUseSelection();

            if (!string.IsNullOrWhiteSpace(_lastDuplicatesPath))
                EditorGUILayout.HelpBox($"Last duplicates file: {_lastDuplicatesPath}", MessageType.Info);

            if (!string.IsNullOrWhiteSpace(_lastRemovedByLengthPath))
                EditorGUILayout.HelpBox($"Last length filter file: {_lastRemovedByLengthPath}", MessageType.Info);

            if (!string.IsNullOrWhiteSpace(_lastWordListPath))
                EditorGUILayout.HelpBox($"Last word list file: {_lastWordListPath}", MessageType.Info);

            EditorGUILayout.Space(8);
            DrawMessages();
        }

        private void DrawFileSelector()
        {
            EditorGUI.BeginChangeCheck();
            _dictionaryAsset = (TextAsset)EditorGUILayout.ObjectField("Dictionary Asset", _dictionaryAsset, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck() && _dictionaryAsset != null)
                ApplyDictionaryAsset(_dictionaryAsset);

            using (new EditorGUILayout.HorizontalScope())
            {
                _filePath = EditorGUILayout.TextField("File Path", _filePath);

                if (GUILayout.Button("Browse", GUILayout.Width(80)))
                {
                    string selectedPath = EditorUtility.OpenFilePanel("Select Dictionary File", Application.dataPath, "txt");
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        _filePath = ToProjectRelativePath(selectedPath);
                        _dictionaryAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(_filePath);
                    }
                }
            }

            if (!HasSelectedFile())
                EditorGUILayout.HelpBox("Select a dictionary .txt file.", MessageType.Warning);
        }

        private void DrawMessages()
        {
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MinHeight(140));

            if (_messages.Count == 0)
            {
                EditorGUILayout.LabelField("No messages yet.");
            }
            else
            {
                foreach (string message in _messages)
                    EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void ConvertWordListToDictionary()
        {
            var entries = DictionaryCleaner.CreateEntriesFromWordList(
                ReadLines(),
                out int emptyLineCount,
                out var issues);

            bool hasErrors = false;
            foreach (var issue in issues)
            {
                LogIssue(issue);
                if (issue.Severity == DictionaryValidationSeverity.Error)
                    hasErrors = true;
            }

            if (hasErrors)
            {
                LogError("Operation cancelled. Select a plain word list without dictionary keys.");
                return;
            }

            if (entries.Count == 0)
            {
                LogError("Operation cancelled. Word list is empty.");
                return;
            }

            WriteLines(_filePath, DictionaryFileFormatter.FormatEntries(entries));
            RefreshAsset(_filePath);

            LogInfo($"Converted word list to dictionary format. Entries: {entries.Count}. Empty lines skipped: {emptyLineCount}. Saved: {_filePath}");
        }

        private void RemoveEmptyLines()
        {
            var lines = ReadLines();
            var cleanedLines = DictionaryCleaner.RemoveEmptyLines(lines);
            int removedCount = lines.Count - cleanedLines.Count;

            WriteLines(_filePath, cleanedLines);
            RefreshAsset(_filePath);

            LogInfo($"Removed empty lines: {removedCount}. Saved: {_filePath}");
        }

        private void ValidateStructure()
        {
            var result = ParseCurrentFile();
            LogParseResult(result, "Validation finished");
        }

        private void RemoveDuplicates()
        {
            if (!TryParseValidCurrentFile(out var parseResult))
                return;

            var uniqueEntries = DictionaryCleaner.RemoveDuplicates(
                parseResult.Entries,
                out var removedEntries,
                out var warnings);

            foreach (var warning in warnings)
                LogIssue(warning);

            WriteLines(_filePath, DictionaryFileFormatter.FormatEntries(uniqueEntries));

            if (removedEntries.Count > 0)
            {
                _lastDuplicatesPath = CreateDuplicatesOutputPath(_filePath);
                WriteLines(_lastDuplicatesPath, DictionaryFileFormatter.FormatEntries(removedEntries));
            }
            else
            {
                _lastDuplicatesPath = string.Empty;
            }

            RefreshAsset(_filePath);
            if (!string.IsNullOrWhiteSpace(_lastDuplicatesPath))
                RefreshAsset(_lastDuplicatesPath);

            LogInfo($"Removed duplicates: {removedEntries.Count}. Saved: {_filePath}");
        }

        private void RemoveWordsLongerThanMaxLength()
        {
            if (!TryParseValidCurrentFile(out var parseResult))
                return;

            var filteredEntries = DictionaryCleaner.RemoveWordsLongerThan(
                parseResult.Entries,
                _maxWordLength,
                out var removedEntries);

            WriteLines(_filePath, DictionaryFileFormatter.FormatEntries(filteredEntries));

            if (removedEntries.Count > 0)
            {
                _lastRemovedByLengthPath = CreateRemovedByLengthOutputPath(_filePath, _maxWordLength);
                WriteLines(_lastRemovedByLengthPath, DictionaryFileFormatter.FormatEntries(removedEntries));
            }
            else
            {
                _lastRemovedByLengthPath = string.Empty;
            }

            RefreshAsset(_filePath);
            if (!string.IsNullOrWhiteSpace(_lastRemovedByLengthPath))
                RefreshAsset(_lastRemovedByLengthPath);

            string removedFileMessage = string.IsNullOrWhiteSpace(_lastRemovedByLengthPath)
                ? "No removed file created."
                : $"Removed file: {_lastRemovedByLengthPath}.";

            LogInfo($"Removed words longer than {_maxWordLength}: {removedEntries.Count}. Saved: {_filePath}. {removedFileMessage}");
        }

        private void SortByWord()
        {
            if (!TryParseValidCurrentFile(out var parseResult))
                return;

            var sortedEntries = DictionaryCleaner.SortByWord(parseResult.Entries, _sortCulture);
            WriteLines(_filePath, DictionaryFileFormatter.FormatEntries(sortedEntries));
            RefreshAsset(_filePath);

            LogInfo($"Sorted entries by WORD. Count: {sortedEntries.Count}. Culture: {GetCultureLabel()}.");
        }

        private void ExportWordList()
        {
            if (!TryParseValidCurrentFile(out var parseResult))
                return;

            _lastWordListPath = CreateWordListOutputPath(_filePath);
            WriteLines(_lastWordListPath, DictionaryFileFormatter.FormatWords(parseResult.Entries));
            RefreshAsset(_lastWordListPath);

            LogInfo($"Exported word list: {parseResult.Entries.Count}. Saved: {_lastWordListPath}");
        }

        private void TryUseSelection()
        {
            if (Selection.activeObject is TextAsset textAsset)
            {
                ApplyDictionaryAsset(textAsset);
                LogInfo($"Selected dictionary: {_filePath}");
                return;
            }

            LogError("Project selection is not a TextAsset dictionary file.");
        }

        private void ApplyDictionaryAsset(TextAsset textAsset)
        {
            _dictionaryAsset = textAsset;
            _filePath = AssetDatabase.GetAssetPath(textAsset);
        }

        private bool TryParseValidCurrentFile(out DictionaryParseResult result)
        {
            result = ParseCurrentFile();
            LogParseResult(result, "Validation before operation finished");

            if (!result.HasErrors)
                return true;

            LogError("Operation cancelled. Fix structure errors first.");
            return false;
        }

        private DictionaryParseResult ParseCurrentFile()
        {
            return _parser.Parse(ReadLines());
        }

        private void LogParseResult(DictionaryParseResult result, string title)
        {
            LogInfo($"{title}. Entries: {result.Entries.Count}. Issues: {result.Issues.Count}.");

            foreach (var issue in result.Issues)
                LogIssue(issue);
        }

        private List<string> ReadLines()
        {
            return new List<string>(File.ReadAllLines(GetAbsolutePath(_filePath), Encoding.UTF8));
        }

        private static void WriteLines(string path, IReadOnlyList<string> lines)
        {
            File.WriteAllLines(GetAbsolutePath(path), lines, new UTF8Encoding(false));
        }

        private bool HasSelectedFile()
        {
            return !string.IsNullOrWhiteSpace(_filePath) && File.Exists(GetAbsolutePath(_filePath));
        }

        private static string GetAbsolutePath(string path)
        {
            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(path);
        }

        private static string ToProjectRelativePath(string path)
        {
            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            string projectPath = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');

            if (fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(projectPath.Length + 1);

            return path;
        }

        private static string CreateDuplicatesOutputPath(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string extension = Path.GetExtension(sourcePath);

            return Path.Combine(directory, $"{fileName}_removed_duplicates{extension}");
        }

        private static string CreateRemovedByLengthOutputPath(string sourcePath, int maxLength)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string extension = Path.GetExtension(sourcePath);

            return Path.Combine(directory, $"{fileName}_removed_longer_than_{maxLength}{extension}");
        }

        private static string CreateWordListOutputPath(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string extension = Path.GetExtension(sourcePath);

            return Path.Combine(directory, $"{fileName}_word_list{extension}");
        }

        private static void RefreshAsset(string path)
        {
            string assetPath = ToProjectRelativePath(path).Replace('\\', '/');
            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.ImportAsset(assetPath);
            else
                AssetDatabase.Refresh();
        }

        private string GetCultureLabel()
        {
            return string.IsNullOrWhiteSpace(_sortCulture) ? "CurrentCulture" : _sortCulture;
        }

        private void LogIssue(DictionaryValidationIssue issue)
        {
            string lineInfo = issue.LineNumber > 0 ? $"Line {issue.LineNumber}: " : string.Empty;
            string message = $"{issue.Severity}: {lineInfo}{issue.Message}";
            _messages.Add(message);

            switch (issue.Severity)
            {
                case DictionaryValidationSeverity.Error:
                    Debug.LogError(message);
                    break;
                case DictionaryValidationSeverity.Warning:
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }

        private void LogInfo(string message)
        {
            _messages.Add(message);
            Debug.Log($"[DictionaryTools] {message}");
        }

        private void LogError(string message)
        {
            _messages.Add($"Error: {message}");
            Debug.LogError($"[DictionaryTools] {message}");
        }
    }
}
#endif
