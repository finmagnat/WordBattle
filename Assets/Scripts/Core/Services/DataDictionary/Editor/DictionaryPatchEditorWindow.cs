#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                EditorGUILayout.Space(12);
                DrawRemoveSection();
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
            ValidateCurrentPatch();
        }

        private void MarkModified()
        {
            _modified = true;
            hasUnsavedChanges = true;
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
