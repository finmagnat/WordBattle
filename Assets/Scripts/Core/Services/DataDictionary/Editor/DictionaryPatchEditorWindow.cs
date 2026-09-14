#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Core.DataDictionary.Editor;
using Core.DataDictionary.Tools;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Core.Services.DataDictionary.Editor
{
    public sealed class DictionaryPatchEditorWindow : EditorWindow
    {
        private const string DefaultTitleId = "1F1BB3";
        private const string TitleIdEditorPrefsKey = "WordBattle.DictionaryPatchEditor.TitleId";

        private readonly DictionaryPatchAdminClient _adminClient = new();

        private PatchLanguage _language = PatchLanguage.RU;
        private SearchScope _searchScope = SearchScope.All;
        private DictionaryPatchModel _patch;
        private string _titleId;
        private string _search = string.Empty;
        private string _status = "Not Loaded";
        private string _statusDetails = string.Empty;
        private string _validationError = string.Empty;
        private string _loadedTitleId = string.Empty;
        private string _loadedTitleDataKey = string.Empty;
        private int _loadedRevision;
        private bool _isLoaded;
        private bool _modified;
        private bool _busy;
        private Vector2 _scrollPosition;
        private TextAsset _bulkUpsertAsset;
        private string _bulkUpsertPath = string.Empty;
        private string _bulkUpsertResult = string.Empty;
        private TextAsset _bulkRemoveAsset;
        private string _bulkRemovePath = string.Empty;
        private string _bulkRemoveResult = string.Empty;
        private TextAsset _dictionaryAsset;
        private string _releaseStatus = string.Empty;
        private DictionaryPatchDiff _previewDiff;

        [MenuItem("Tools/Dictionary/Patch Editor")]
        public static void Open()
        {
            GetWindow<DictionaryPatchEditorWindow>("Dictionary Patch");
        }

        private void OnEnable()
        {
            _titleId = EditorPrefs.GetString(TitleIdEditorPrefsKey, DefaultTitleId);
            _patch ??= CreateEmptyPatch();
            saveChangesMessage =
                "The Dictionary Patch Editor contains changes that have not been saved to PlayFab.";
            ValidateCurrentPatch();
        }

        public override void SaveChanges()
        {
            ShowNotification(new GUIContent("Use 'Save to PlayFab' before closing the window."));
        }

        public override void DiscardChanges()
        {
            _modified = false;
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }

        private void OnGUI()
        {
            DrawConnectionSection();
            EditorGUILayout.Space(8);
            DrawSummarySection();
            EditorGUILayout.Space(8);
            DrawActionButtons();
            EditorGUILayout.Space(8);
            DrawSearchSection();
            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(_busy))
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                DrawUpsertSection();
                EditorGUILayout.Space(8);
                DrawBulkUpsertSection();
                EditorGUILayout.Space(12);
                DrawRemoveSection();
                EditorGUILayout.Space(8);
                DrawBulkRemoveSection();
                EditorGUILayout.Space(16);
                DrawReleaseSection();
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(8);
            DrawValidationStatus();
        }

        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("PlayFab Dictionary Patch", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(_busy))
            {
                string candidateTitleId = EditorGUILayout.DelayedTextField("Title ID", _titleId);
                if (!string.Equals(candidateTitleId, _titleId, StringComparison.Ordinal))
                    TryChangeTitleId(candidateTitleId);

                PatchLanguage candidateLanguage =
                    (PatchLanguage)EditorGUILayout.EnumPopup("Language", _language);
                if (candidateLanguage != _language)
                    TryChangeLanguage(candidateLanguage);
            }

            EditorGUILayout.LabelField("Title Data Key", CurrentTitleDataKey);
            EditorGUILayout.LabelField(
                "Secret Key status",
                DictionaryPatchAdminClient.HasSecretKey ? "Found" : "Missing");

            if (!DictionaryPatchAdminClient.HasSecretKey)
            {
                EditorGUILayout.HelpBox(
                    $"Set the Windows user environment variable " +
                    $"'{DictionaryPatchAdminClient.SecretEnvironmentVariable}', then restart Unity.",
                    MessageType.Error);
            }
        }

        private void DrawSummarySection()
        {
            EditorGUILayout.LabelField("Status", _modified ? "Modified *" : _status);
            EditorGUILayout.LabelField("Schema version", _patch.schemaVersion.ToString());
            EditorGUILayout.LabelField("Loaded revision", _isLoaded ? _loadedRevision.ToString() : "-");
            EditorGUILayout.LabelField("Next revision", _isLoaded ? (_loadedRevision + 1).ToString() : "-");
            EditorGUILayout.LabelField("Upsert count", _patch.upsert.Count.ToString());
            EditorGUILayout.LabelField("Remove count", _patch.remove.Count.ToString());
            EditorGUILayout.LabelField("Approx JSON size", $"{GetApproximateJsonSize()} bytes");

            if (!string.IsNullOrWhiteSpace(_statusDetails))
                EditorGUILayout.HelpBox(_statusDetails, GetStatusMessageType());
        }

        private void DrawActionButtons()
        {
            bool hasConnectionSettings = HasConnectionSettings();
            bool isValid = DictionaryPatchValidator.TryValidate(_patch, out _);
            bool canSave = hasConnectionSettings
                           && _isLoaded
                           && _modified
                           && isValid
                           && LoadedContextMatchesCurrent()
                           && !_busy;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasConnectionSettings || _busy))
                {
                    if (GUILayout.Button(_isLoaded ? "Reload from PlayFab" : "Load from PlayFab"))
                        ConfirmAndLoad();
                }

                using (new EditorGUI.DisabledScope(_busy))
                {
                    if (GUILayout.Button("Validate"))
                        ValidateWithStatus();
                }

                using (new EditorGUI.DisabledScope(!canSave))
                {
                    if (GUILayout.Button("Save to PlayFab"))
                        SaveCurrentPatchAsync().Forget();
                }

                using (new EditorGUI.DisabledScope(
                           !hasConnectionSettings || !_isLoaded || _busy || !LoadedContextMatchesCurrent()))
                {
                    if (GUILayout.Button("Clear Patch..."))
                        ConfirmAndClear();
                }
            }
        }

        private void DrawSearchSection()
        {
            EditorGUILayout.LabelField("Search / Filter", EditorStyles.boldLabel);
            _search = EditorGUILayout.TextField("Search", _search);
            _searchScope = (SearchScope)EditorGUILayout.EnumPopup("Scope", _searchScope);
        }

        private void DrawUpsertSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Upsert ({_patch.upsert.Count})", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Sort by Word", GUILayout.Width(110)))
                {
                    _patch.upsert.Sort((left, right) =>
                        StringComparer.CurrentCultureIgnoreCase.Compare(left?.word, right?.word));
                    MarkModified();
                }
            }

            int removeIndex = -1;
            for (int i = 0; i < _patch.upsert.Count; i++)
            {
                DictionaryPatchEntry entry = _patch.upsert[i];
                if (!MatchesUpsertSearch(entry))
                    continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.BeginChangeCheck();
                    entry.word = EditorGUILayout.TextField("Word", entry.word ?? string.Empty);
                    EditorGUILayout.LabelField("Definition");
                    entry.definition = EditorGUILayout.TextArea(
                        entry.definition ?? string.Empty,
                        GUILayout.MinHeight(42));
                    if (EditorGUI.EndChangeCheck())
                        MarkModified();

                    if (GUILayout.Button("Remove Upsert"))
                        removeIndex = i;
                }
            }

            if (removeIndex >= 0)
            {
                _patch.upsert.RemoveAt(removeIndex);
                MarkModified();
            }

            if (GUILayout.Button("+ Add Upsert"))
            {
                _search = string.Empty;
                _patch.upsert.Add(new DictionaryPatchEntry
                {
                    word = string.Empty,
                    definition = string.Empty
                });
                MarkModified();
            }
        }

        private void DrawRemoveSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Remove ({_patch.remove.Count})", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Sort by Word", GUILayout.Width(110)))
                {
                    _patch.remove.Sort(StringComparer.CurrentCultureIgnoreCase);
                    MarkModified();
                }
            }

            int removeIndex = -1;
            for (int i = 0; i < _patch.remove.Count; i++)
            {
                if (!MatchesSearch(_patch.remove[i]))
                    continue;

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUI.BeginChangeCheck();
                    _patch.remove[i] = EditorGUILayout.TextField("Word", _patch.remove[i] ?? string.Empty);
                    if (EditorGUI.EndChangeCheck())
                        MarkModified();

                    if (GUILayout.Button("Delete", GUILayout.Width(70)))
                        removeIndex = i;
                }
            }

            if (removeIndex >= 0)
            {
                _patch.remove.RemoveAt(removeIndex);
                MarkModified();
            }

            if (GUILayout.Button("+ Add Remove"))
            {
                _search = string.Empty;
                _patch.remove.Add(string.Empty);
                MarkModified();
            }
        }

        private void DrawBulkUpsertSection()
        {
            EditorGUILayout.LabelField("Bulk Upsert", EditorStyles.boldLabel);
            DrawTextSourceSelector(
                ref _bulkUpsertAsset,
                ref _bulkUpsertPath,
                "Select Upsert Dictionary File");

            using (new EditorGUI.DisabledScope(!_isLoaded || _busy || !HasSource(_bulkUpsertAsset, _bulkUpsertPath)))
            {
                if (GUILayout.Button("Import Upsert"))
                    ImportBulkUpsert();
            }

            if (!string.IsNullOrWhiteSpace(_bulkUpsertResult))
                EditorGUILayout.HelpBox(_bulkUpsertResult, MessageType.Info);
        }

        private void DrawBulkRemoveSection()
        {
            EditorGUILayout.LabelField("Bulk Remove", EditorStyles.boldLabel);
            DrawTextSourceSelector(
                ref _bulkRemoveAsset,
                ref _bulkRemovePath,
                "Select Remove Word List");

            using (new EditorGUI.DisabledScope(!_isLoaded || _busy || !HasSource(_bulkRemoveAsset, _bulkRemovePath)))
            {
                if (GUILayout.Button("Import Remove"))
                    ImportBulkRemove();
            }

            if (!string.IsNullOrWhiteSpace(_bulkRemoveResult))
                EditorGUILayout.HelpBox(_bulkRemoveResult, MessageType.Info);
        }

        private void DrawReleaseSection()
        {
            EditorGUILayout.LabelField("RELEASE / BASELINE", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _dictionaryAsset = (TextAsset)EditorGUILayout.ObjectField(
                "Dictionary",
                _dictionaryAsset,
                typeof(TextAsset),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                _previewDiff = null;
                _releaseStatus = string.Empty;
            }

            if (GUILayout.Button("Use Selection", GUILayout.Width(110)))
            {
                if (Selection.activeObject is TextAsset selectedAsset)
                {
                    _dictionaryAsset = selectedAsset;
                    _previewDiff = null;
                    _releaseStatus = string.Empty;
                }
                else
                {
                    _releaseStatus = "Project selection is not a TextAsset.";
                }
            }

            DrawDictionaryLanguageStatus();

            bool patchValid = DictionaryPatchValidator.TryValidate(_patch, out _);
            bool canPreview = _isLoaded && !_busy && patchValid && _dictionaryAsset != null;
            bool canApply = canPreview && !_modified && LoadedContextMatchesCurrent() && IsDictionaryLanguageValid();

            if (_modified)
            {
                EditorGUILayout.HelpBox(
                    "Save patch to PlayFab before applying it to the dictionary.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canPreview))
                {
                    if (GUILayout.Button("Preview Changes"))
                        PreviewDictionaryChanges();
                }

                using (new EditorGUI.DisabledScope(!canApply))
                {
                    if (GUILayout.Button("Apply Patch to Dictionary..."))
                        ApplyPatchToDictionary();
                }
            }

            if (!string.IsNullOrWhiteSpace(_releaseStatus))
                EditorGUILayout.HelpBox(_releaseStatus, MessageType.Info);

            DrawPreview();
        }

        private void DrawTextSourceSelector(
            ref TextAsset sourceAsset,
            ref string sourcePath,
            string browseTitle)
        {
            EditorGUI.BeginChangeCheck();
            sourceAsset = (TextAsset)EditorGUILayout.ObjectField(
                "Source",
                sourceAsset,
                typeof(TextAsset),
                false);
            if (EditorGUI.EndChangeCheck())
                sourcePath = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : string.Empty;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                {
                    if (Selection.activeObject is TextAsset selectedAsset)
                    {
                        sourceAsset = selectedAsset;
                        sourcePath = AssetDatabase.GetAssetPath(selectedAsset);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Invalid Selection",
                            "Project selection is not a TextAsset.",
                            "OK");
                    }
                }

                if (GUILayout.Button("Browse"))
                {
                    string selectedPath = EditorUtility.OpenFilePanel(browseTitle, Application.dataPath, "txt");
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        sourcePath = selectedPath;
                        sourceAsset = TryLoadProjectTextAsset(selectedPath);
                    }
                }
            }

            if (sourceAsset == null && !string.IsNullOrWhiteSpace(sourcePath))
                EditorGUILayout.LabelField("File", sourcePath, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawValidationStatus()
        {
            if (string.IsNullOrEmpty(_validationError))
            {
                EditorGUILayout.HelpBox("Patch validation passed.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox($"Validation failed: {_validationError}", MessageType.Error);

            if (TryGetConflictingWord(out string conflictingWord))
            {
                if (GUILayout.Button($"Remove '{conflictingWord}' from Upsert"))
                {
                    _patch.upsert.RemoveAll(entry => WordsEqual(entry?.word, conflictingWord));
                    MarkModified();
                }
            }
        }

        private void ImportBulkUpsert()
        {
            _bulkUpsertResult = string.Empty;
            if (!DictionaryPatchFileOperations.TryParseUpsertSource(
                    _bulkUpsertAsset,
                    _bulkUpsertPath,
                    out List<DictionaryEntry> importedEntries,
                    out string error))
            {
                ShowUpsertImportFailure(error);
                return;
            }

            if (!DictionaryPatchFileOperations.TryMergeUpsert(
                    _patch,
                    importedEntries,
                    out DictionaryPatchModel candidate,
                    out DictionaryPatchBulkImportResult importResult,
                    out string candidateError))
            {
                ShowUpsertImportFailure(candidateError);
                return;
            }

            _patch = candidate;
            _bulkUpsertResult =
                $"Imported: {importResult.Imported}\n" +
                $"Added: {importResult.Added}\n" +
                $"Replaced: {importResult.Existing}\n" +
                $"Moved from Remove: {importResult.Moved}";
            MarkModified();
        }

        private void ImportBulkRemove()
        {
            _bulkRemoveResult = string.Empty;
            if (!DictionaryPatchFileOperations.TryParseRemoveSource(
                    _bulkRemoveAsset,
                    _bulkRemovePath,
                    out List<string> importedWords,
                    out string error))
            {
                EditorUtility.DisplayDialog(
                    "Import failed",
                    $"Remove list validation errors found.\n\n{error}",
                    "OK");
                return;
            }

            if (!DictionaryPatchFileOperations.TryMergeRemove(
                    _patch,
                    importedWords,
                    out DictionaryPatchModel candidate,
                    out DictionaryPatchBulkImportResult importResult,
                    out string candidateError))
            {
                EditorUtility.DisplayDialog(
                    "Import failed",
                    $"Patch validation failed.\n\n{candidateError}",
                    "OK");
                return;
            }

            _patch = candidate;
            _bulkRemoveResult =
                $"Imported: {importResult.Imported}\n" +
                $"Added: {importResult.Added}\n" +
                $"Already present: {importResult.Existing}\n" +
                $"Moved from Upsert: {importResult.Moved}";
            MarkModified();
        }

        private void ShowUpsertImportFailure(string error)
        {
            bool openTools = EditorUtility.DisplayDialog(
                "Import failed",
                $"Dictionary validation errors found.\n\n{error}",
                "Open in Dictionary Tools",
                "Cancel");
            if (!openTools)
                return;

            if (_bulkUpsertAsset != null)
                DictionaryToolsWindow.OpenWithAsset(_bulkUpsertAsset);
            else
                DictionaryToolsWindow.Open();
        }

        private void PreviewDictionaryChanges()
        {
            if (!DictionaryPatchFileOperations.TryCreateApplyPlan(
                    _dictionaryAsset,
                    CurrentLanguageCode,
                    _patch,
                    out DictionaryPatchApplyPlan plan,
                    out string error))
            {
                _previewDiff = null;
                _releaseStatus = error;
                return;
            }

            _previewDiff = plan.Diff;
            _releaseStatus =
                $"Preview ready. Add: {plan.Diff.Added.Count}, " +
                $"Replace: {plan.Diff.Replaced.Count}, Remove: {plan.Diff.Removed.Count}.";
        }

        private void ApplyPatchToDictionary()
        {
            if (_modified)
            {
                _releaseStatus = "Save patch to PlayFab before applying it to the dictionary.";
                return;
            }

            if (!DictionaryPatchFileOperations.TryCreateApplyPlan(
                    _dictionaryAsset,
                    CurrentLanguageCode,
                    _patch,
                    out DictionaryPatchApplyPlan plan,
                    out string error))
            {
                _previewDiff = null;
                _releaseStatus = error;
                return;
            }

            _previewDiff = plan.Diff;
            string dictionaryName = Path.GetFileNameWithoutExtension(plan.AssetPath);
            string confirmation =
                $"Apply {CurrentTitleDataKey} revision {plan.Revision} to {dictionaryName}?\n\n" +
                $"Add: {plan.Diff.Added.Count}\n" +
                $"Replace: {plan.Diff.Replaced.Count}\n" +
                $"Remove: {plan.Diff.Removed.Count}\n\n" +
                "Dictionary will be modified on disk.";

            if (!EditorUtility.DisplayDialog(
                    "Apply Patch to Dictionary",
                    confirmation,
                    "Apply",
                    "Cancel"))
            {
                return;
            }

            if (!DictionaryPatchFileOperations.TryApplyPlan(plan, out error))
            {
                _releaseStatus = error;
                return;
            }

            _releaseStatus =
                $"Patch applied successfully.\n" +
                $"Revision: {plan.Revision}\n" +
                $"Added: {plan.Diff.Added.Count}\n" +
                $"Replaced: {plan.Diff.Replaced.Count}\n" +
                $"Removed: {plan.Diff.Removed.Count}";
        }

        private void DrawDictionaryLanguageStatus()
        {
            if (_dictionaryAsset == null)
                return;

            if (DictionaryPatchFileOperations.TryGetDictionaryLanguage(
                    _dictionaryAsset,
                    out string language,
                    out string error))
            {
                bool matches = string.Equals(
                    language,
                    CurrentLanguageCode,
                    StringComparison.OrdinalIgnoreCase);
                EditorGUILayout.HelpBox(
                    matches
                        ? $"Dictionary language: {language.ToUpperInvariant()} (matches patch)."
                        : $"Dictionary language: {language.ToUpperInvariant()} (current patch: {_language}).",
                    matches ? MessageType.Info : MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }

        private void DrawPreview()
        {
            if (_previewDiff == null)
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                $"Preview — Add: {_previewDiff.Added.Count}, " +
                $"Replace: {_previewDiff.Replaced.Count}, Remove: {_previewDiff.Removed.Count}",
                EditorStyles.boldLabel);
            DrawPreviewGroup("ADD", "+", _previewDiff.Added);
            DrawPreviewGroup("UPDATE", "~", _previewDiff.Replaced);
            DrawPreviewGroup("REMOVE", "-", _previewDiff.Removed);
        }

        private static void DrawPreviewGroup(
            string title,
            string marker,
            IReadOnlyList<string> words)
        {
            if (words.Count == 0)
                return;

            EditorGUILayout.LabelField($"{title}:", EditorStyles.miniBoldLabel);
            foreach (string word in words)
                EditorGUILayout.LabelField($"{marker} {word}");
        }

        private void ConfirmAndLoad()
        {
            if (_modified && !EditorUtility.DisplayDialog(
                    "Discard unsaved changes?",
                    "Loading from PlayFab will discard the current unsaved changes.",
                    "Load and Discard",
                    "Cancel"))
            {
                return;
            }

            LoadFromPlayFabAsync().Forget();
        }

        private async UniTaskVoid LoadFromPlayFabAsync()
        {
            BeginOperation("Loading...");
            try
            {
                DictionaryPatchAdminClient.TitleDataReadResult remote =
                    await _adminClient.GetTitleDataAsync(_titleId, CurrentTitleDataKey);

                if (!remote.Exists)
                {
                    _patch = CreateEmptyPatch();
                    _loadedRevision = 0;
                    _isLoaded = true;
                    _modified = true;
                    hasUnsavedChanges = true;
                    _previewDiff = null;
                    _releaseStatus = string.Empty;
                    _bulkUpsertResult = string.Empty;
                    _bulkRemoveResult = string.Empty;
                    CaptureLoadedContext();
                    SetStatus(
                        "Modified",
                        $"Key '{CurrentTitleDataKey}' does not exist. An empty revision 0 patch was created " +
                        "locally; press Save to create it in PlayFab.");
                    ValidateCurrentPatch();
                    return;
                }

                if (!DictionaryPatchService.TryDeserializePatch(
                        remote.Value,
                        out DictionaryPatchModel loadedPatch,
                        out string error))
                {
                    throw new InvalidOperationException($"Remote patch is invalid: {error}");
                }

                _patch = loadedPatch;
                _loadedRevision = loadedPatch.revision;
                _isLoaded = true;
                _modified = false;
                hasUnsavedChanges = false;
                _previewDiff = null;
                _releaseStatus = string.Empty;
                _bulkUpsertResult = string.Empty;
                _bulkRemoveResult = string.Empty;
                CaptureLoadedContext();
                SetStatus(
                    "Loaded",
                    $"Loaded '{CurrentTitleDataKey}', schema {loadedPatch.schemaVersion}, " +
                    $"revision {loadedPatch.revision}, upsert {loadedPatch.upsert.Count}, " +
                    $"remove {loadedPatch.remove.Count}.");
                ValidateCurrentPatch();
            }
            catch (Exception exception)
            {
                SetStatus("Error", exception.Message);
            }
            finally
            {
                EndOperation();
            }
        }

        private async UniTaskVoid SaveCurrentPatchAsync()
        {
            await SavePatchAsync(CloneWithRevision(_patch, _loadedRevision + 1), "Patch saved successfully.");
        }

        private async UniTask SavePatchAsync(DictionaryPatchModel patchToSave, string successMessage)
        {
            if (!DictionaryPatchValidator.TryValidate(patchToSave, out string candidateError))
            {
                SetStatus("Error", $"Save blocked: {candidateError}");
                return;
            }

            BeginOperation("Checking remote revision...");
            try
            {
                DictionaryPatchAdminClient.TitleDataReadResult remote =
                    await _adminClient.GetTitleDataAsync(_titleId, CurrentTitleDataKey);

                int remoteRevision;
                if (!remote.Exists)
                {
                    remoteRevision = 0;
                }
                else if (!DictionaryPatchService.TryDeserializePatch(
                             remote.Value,
                             out DictionaryPatchModel remotePatch,
                             out string remoteError))
                {
                    throw new InvalidOperationException(
                        $"Remote patch cannot be validated: {remoteError}. Reload before saving.");
                }
                else
                {
                    remoteRevision = remotePatch.revision;
                }

                if (remoteRevision != _loadedRevision)
                {
                    SetStatus(
                        "Error",
                        $"Remote patch has changed (loaded revision {_loadedRevision}, " +
                        $"remote revision {remoteRevision}). Reload before saving.");
                    return;
                }

                if (!DictionaryPatchValidator.TryValidate(patchToSave, out string saveError))
                    throw new InvalidOperationException($"Save blocked: {saveError}");

                SetStatus("Saving...", string.Empty);
                string json = JsonUtility.ToJson(patchToSave, true);
                await _adminClient.SetTitleDataAsync(_titleId, CurrentTitleDataKey, json);

                _patch = patchToSave;
                _loadedRevision = patchToSave.revision;
                _modified = false;
                hasUnsavedChanges = false;
                _previewDiff = null;
                _releaseStatus = string.Empty;
                CaptureLoadedContext();
                ValidateCurrentPatch();
                SetStatus(
                    "Loaded",
                    $"{successMessage} Key '{CurrentTitleDataKey}', revision {_loadedRevision}.");
            }
            catch (Exception exception)
            {
                SetStatus("Error", exception.Message);
            }
            finally
            {
                EndOperation();
            }
        }

        private void ConfirmAndClear()
        {
            string message =
                $"Language: {_language}\n" +
                $"Current revision: {_loadedRevision}\n" +
                $"Upsert: {_patch.upsert.Count}\n" +
                $"Remove: {_patch.remove.Count}\n\n" +
                "Patch should only be cleared after all its changes are included in a new release build " +
                "and that build has become MinimumSupportedBuild.\n\n" +
                $"An empty patch with revision {_loadedRevision + 1} will be saved. The key will not be deleted.";

            if (!EditorUtility.DisplayDialog("Clear Dictionary Patch?", message, "Clear and Save", "Cancel"))
                return;

            DictionaryPatchModel emptyPatch = CreateEmptyPatch();
            emptyPatch.revision = _loadedRevision + 1;
            SavePatchAsync(emptyPatch, "Patch cleared safely.").Forget();
        }

        private void TryChangeLanguage(PatchLanguage language)
        {
            if (!ConfirmDiscardForContextChange("switch language"))
                return;

            _language = language;
            ResetLoadedState();
        }

        private void TryChangeTitleId(string titleId)
        {
            if (!ConfirmDiscardForContextChange("change Title ID"))
                return;

            _titleId = titleId.Trim();
            EditorPrefs.SetString(TitleIdEditorPrefsKey, _titleId);
            ResetLoadedState();
        }

        private bool ConfirmDiscardForContextChange(string action)
        {
            return !_modified || EditorUtility.DisplayDialog(
                "Discard unsaved changes?",
                $"Unsaved patch changes will be discarded when you {action}.",
                "Discard",
                "Cancel");
        }

        private void ResetLoadedState()
        {
            _patch = CreateEmptyPatch();
            _loadedRevision = 0;
            _isLoaded = false;
            _modified = false;
            hasUnsavedChanges = false;
            _loadedTitleId = string.Empty;
            _loadedTitleDataKey = string.Empty;
            _status = "Not Loaded";
            _statusDetails = string.Empty;
            _previewDiff = null;
            _releaseStatus = string.Empty;
            _bulkUpsertResult = string.Empty;
            _bulkRemoveResult = string.Empty;
            ValidateCurrentPatch();
        }

        private void MarkModified()
        {
            _modified = true;
            hasUnsavedChanges = true;
            _previewDiff = null;
            _releaseStatus = string.Empty;
            ValidateCurrentPatch();
            Repaint();
        }

        private void ValidateWithStatus()
        {
            ValidateCurrentPatch();
            if (string.IsNullOrEmpty(_validationError))
                SetStatus(_modified ? "Modified" : _status, "Validation passed.");
            else
                SetStatus("Error", $"Validation failed: {_validationError}");
        }

        private void ValidateCurrentPatch()
        {
            DictionaryPatchValidator.TryValidate(_patch, out _validationError);
        }

        private bool TryGetConflictingWord(out string word)
        {
            foreach (DictionaryPatchEntry entry in _patch.upsert)
            {
                string conflict = _patch.remove.FirstOrDefault(removeWord => WordsEqual(entry?.word, removeWord));
                if (!string.IsNullOrWhiteSpace(conflict))
                {
                    word = conflict.Trim();
                    return true;
                }
            }

            word = string.Empty;
            return false;
        }

        private bool MatchesUpsertSearch(DictionaryPatchEntry entry)
        {
            if (string.IsNullOrEmpty(_search))
                return true;

            return _searchScope switch
            {
                SearchScope.Word => MatchesSearch(entry?.word),
                SearchScope.Definition => MatchesSearch(entry?.definition),
                _ => MatchesSearch(entry?.word) || MatchesSearch(entry?.definition)
            };
        }

        private bool MatchesSearch(string value)
        {
            return string.IsNullOrEmpty(_search)
                   || (!string.IsNullOrEmpty(value)
                       && value.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool WordsEqual(string left, string right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private bool HasConnectionSettings()
        {
            return DictionaryPatchAdminClient.HasSecretKey && !string.IsNullOrWhiteSpace(_titleId);
        }

        private bool LoadedContextMatchesCurrent()
        {
            return string.Equals(_loadedTitleId, _titleId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(_loadedTitleDataKey, CurrentTitleDataKey, StringComparison.Ordinal);
        }

        private bool IsDictionaryLanguageValid()
        {
            return _dictionaryAsset != null
                   && DictionaryPatchFileOperations.TryGetDictionaryLanguage(
                       _dictionaryAsset,
                       out string language,
                       out _)
                   && string.Equals(
                       language,
                       CurrentLanguageCode,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSource(TextAsset asset, string path)
        {
            if (asset != null)
                return true;

            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private static TextAsset TryLoadProjectTextAsset(string absolutePath)
        {
            string normalizedPath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string assetsPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            if (!normalizedPath.StartsWith(assetsPath + "/", StringComparison.OrdinalIgnoreCase))
                return null;

            string assetPath = "Assets" + normalizedPath.Substring(assetsPath.Length);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
        }

        private void CaptureLoadedContext()
        {
            _loadedTitleId = _titleId;
            _loadedTitleDataKey = CurrentTitleDataKey;
        }

        private void BeginOperation(string status)
        {
            _busy = true;
            SetStatus(status, string.Empty);
        }

        private void EndOperation()
        {
            _busy = false;
            Repaint();
        }

        private void SetStatus(string status, string details)
        {
            _status = status;
            _statusDetails = details;
            Repaint();
        }

        private MessageType GetStatusMessageType()
        {
            return _status == "Error" ? MessageType.Error : MessageType.Info;
        }

        private int GetApproximateJsonSize()
        {
            return Encoding.UTF8.GetByteCount(JsonUtility.ToJson(_patch));
        }

        private string CurrentTitleDataKey => $"DictionaryPatch_{_language}";
        private string CurrentLanguageCode => _language.ToString().ToLowerInvariant();

        private static DictionaryPatchModel CreateEmptyPatch()
        {
            return new DictionaryPatchModel
            {
                schemaVersion = DictionaryPatchValidator.SupportedSchemaVersion,
                revision = 0,
                upsert = new List<DictionaryPatchEntry>(),
                remove = new List<string>()
            };
        }

        private static DictionaryPatchModel CloneWithRevision(DictionaryPatchModel source, int revision)
        {
            return new DictionaryPatchModel
            {
                schemaVersion = source.schemaVersion,
                revision = revision,
                upsert = source.upsert.Select(entry => new DictionaryPatchEntry
                {
                    word = entry?.word,
                    definition = entry?.definition
                }).ToList(),
                remove = new List<string>(source.remove)
            };
        }

        private enum PatchLanguage
        {
            RU,
            UK,
            EN
        }

        private enum SearchScope
        {
            All,
            Word,
            Definition
        }
    }
}
#endif
