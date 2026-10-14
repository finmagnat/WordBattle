using System;
using System.Collections.Generic;
using Core.Services.ReportWord;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace Core.DebugTools
{
    public sealed class SRDebugReportWordBridge : SRDebugWordsBridgeBase<ReportWordEntryDto>
    {
        [Inject] private IReportWordService _reportWordService;
        [Inject] private IReportWordLimitsService _reportWordLimitsService;

        public static SRDebugReportWordBridge Instance { get; private set; }

        public string WordToAdd { get; set; } = string.Empty;
        public ReportReason Reason { get; set; } = ReportReason.None;

        protected override string LogPrefix => "[ReportWord]";

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [ContextMenu("Dump Report Words")]
        public void DumpReportWords()
        {
            DumpWordsAsyncInternal().Forget();
        }

        [ContextMenu("Dump Report Words To Clipboard")]
        public void DumpReportWordsToClipboard()
        {
            DumpWordsToClipboardAsyncInternal().Forget();
        }

        [ContextMenu("Add Report Word")]
        public void AddReportWord()
        {
            AddReportWordAsync().Forget();
        }

        [ContextMenu("Clear All Report Words")]
        public void ClearAllReportWords()
        {
            ClearAllWordsAsyncInternal().Forget();
        }

        [ContextMenu("Reset Report Word Limits")]
        public void ResetReportWordLimits(bool disableLimits = false)
        {
            try
            {
                _reportWordLimitsService.ResetLimits(disableLimits);
                Debug.Log("[ReportWord] Limits reset.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReportWord] ResetReportWordLimits failed: {e}");
            }
        }

        protected override UniTask<IReadOnlyList<ReportWordEntryDto>> GetWordsAsync(string language)
        {
            return _reportWordService.GetPendingWordsAsync(language);
        }

        protected override async UniTask ClearWordsAsync(string language)
        {
            await _reportWordService.ClearPendingWordsAsync(language);
        }

        protected override string GetWord(ReportWordEntryDto entry)
        {
            return entry?.word;
        }

        protected override string FormatClipboardLine(ReportWordEntryDto entry)
        {
            return $"{entry.word}\t{entry.reason}";
        }

        private async UniTaskVoid AddReportWordAsync()
        {
            try
            {
                var language = DebugLanguageCode.GetLanguageCode();
                var rawWord = WordToAdd;
                var reason = Reason;

                if (string.IsNullOrWhiteSpace(rawWord))
                {
                    Debug.LogWarning("[ReportWord] WordToAdd is empty.");
                    return;
                }

                var result = await _reportWordService.SubmitWordAsync(rawWord, reason, language);

                Debug.Log($"[ReportWord] Add result: status={result.status}, word={result.normalizedWord}, language={language}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReportWord] AddReportWordAsync failed: {e}");
            }
        }
    }
}