using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Core.DebugTools
{
    public abstract class SRDebugWordsBridgeBase<TEntry> : MonoBehaviour
    {
        protected abstract string LogPrefix { get; }

        protected abstract UniTask<IReadOnlyList<TEntry>> GetWordsAsync(string language);
        protected abstract UniTask ClearWordsAsync(string language);

        protected abstract string GetWord(TEntry entry);

        protected virtual string FormatClipboardLine(TEntry entry) => GetWord(entry);

        protected virtual bool SortClipboardLines => false;

        protected async UniTask DumpWordsAsyncInternal()
        {
            try
            {
                var language = DebugLanguageCode.GetLanguageCode();
                var words = await GetWordsAsync(language);

                if (words == null || words.Count == 0)
                {
                    Debug.Log($"{LogPrefix} pending_words_{language}: EMPTY");
                    return;
                }

                var joined = string.Join(", ",
                    words.Where(IsValidEntry)
                         .Select(GetWord));

                Debug.Log($"{LogPrefix} pending_words_{language} ({words.Count}): {joined}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} DumpWordsAsync failed: {e}");
            }
        }

        protected async UniTask DumpWordsToClipboardAsyncInternal()
        {
            try
            {
                var language = DebugLanguageCode.GetLanguageCode();
                var words = await GetWordsAsync(language);

                if (words == null || words.Count == 0)
                {
                    GUIUtility.systemCopyBuffer = string.Empty;
                    Debug.Log($"{LogPrefix} Clipboard cleared. pending_words_{language} is EMPTY.");
                    return;
                }

                IEnumerable<TEntry> filtered = words.Where(IsValidEntry);

                if (SortClipboardLines)
                    filtered = filtered.OrderBy(GetWord);

                var text = string.Join("\n", filtered.Select(FormatClipboardLine));
                GUIUtility.systemCopyBuffer = text;

                Debug.Log($"{LogPrefix} Copied {words.Count} words to clipboard for '{language}'.");
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} DumpWordsToClipboardAsync failed: {e}");
            }
        }

        protected async UniTask ClearAllWordsAsyncInternal()
        {
            try
            {
                var language = DebugLanguageCode.GetLanguageCode();
                await ClearWordsAsync(language);

                Debug.Log($"{LogPrefix} Clear all result: language={language}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} ClearAllWordsAsync failed: {e}");
            }
        }

        private bool IsValidEntry(TEntry entry)
        {
            return entry != null && !string.IsNullOrWhiteSpace(GetWord(entry));
        }
    }
}