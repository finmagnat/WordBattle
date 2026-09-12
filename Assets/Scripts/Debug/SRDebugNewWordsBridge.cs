using System;
using System.Collections.Generic;
using Core.Services;
using Core.Services.NewWords;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace Core.DebugTools
{
    public sealed class SRDebugNewWordsBridge : SRDebugWordsBridgeBase<NewWordEntryDto>
    {
        [Inject] private INewWordsService _newWordsService;
        [Inject] private INewWordsLimitsService _newWordsLimitsService;
        [Inject] private LocalizationService _localization;

        public static SRDebugNewWordsBridge Instance { get; private set; }

        public string WordToAdd { get; set; } = string.Empty;

        protected override string LogPrefix => "[NewWords]";
        protected override bool SortClipboardLines => true;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [ContextMenu("Dump Pending Words")]
        public void DumpPendingWords()
        {
            DumpWordsAsyncInternal().Forget();
        }

        [ContextMenu("Dump Pending Words To Clipboard")]
        public void DumpPendingWordsToClipboard()
        {
            DumpWordsToClipboardAsyncInternal().Forget();
        }

        [ContextMenu("Add Pending Word")]
        public void AddPendingWord()
        {
            AddPendingWordAsync().Forget();
        }

        [ContextMenu("Clear All Pending Words")]
        public void ClearAllPendingWords()
        {
            ClearAllWordsAsyncInternal().Forget();
        }

        [ContextMenu("Reset New Words Limits")]
        public void ResetNewWordsLimits(bool disableLimits = false)
        {
            try
            {
                _newWordsLimitsService.ResetLimits(disableLimits);
                Debug.Log("[NewWords] Limits reset.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NewWords] ResetNewWordsLimits failed: {e}");
            }
        }
        
        public void SetLocalization()
        {
            var localeCode = DebugLanguageCode.Get();
            _localization.SetLocale(localeCode);
        }


        protected override UniTask<IReadOnlyList<NewWordEntryDto>> GetWordsAsync(string language)
        {
            return _newWordsService.GetPendingWordsAsync(language);
        }

        protected override async UniTask ClearWordsAsync(string language)
        {
            await _newWordsService.ClearPendingWordsAsync(language);
        }

        protected override string GetWord(NewWordEntryDto entry)
        {
            return entry?.word;
        }

        private async UniTaskVoid AddPendingWordAsync()
        {
            try
            {
                var language = DebugLanguageCode.Get();
                var rawWord = WordToAdd;

                if (string.IsNullOrWhiteSpace(rawWord))
                {
                    Debug.LogWarning("[NewWords] WordToAdd is empty.");
                    return;
                }

                var result = await _newWordsService.SubmitWordAsync(rawWord, language);

                Debug.Log($"[NewWords] Add result: status={result.status}, word={result.normalizedWord}, language={language}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NewWords] AddPendingWordAsync failed: {e}");
            }
        }
    }
}