using System;
using System.Collections.Generic;
using System.Linq;
using Core.Config;
using Core.Services.DataDictionary;
using Cysharp.Threading.Tasks;
using Game.Logic;
using UI.Components;
using UnityEngine;
using Random = System.Random;

namespace Game.AI
{
    public sealed class SmartWordAlgorithmAsync : IAIAlgorithmAsync
    {
        private static readonly Random _rng = new();

        private bool _bCancel;

        public void Cancel() => _bCancel = true;

        public UniTask<AIWordResult> GetWordAsync(
            ComplexityAISettings settings,
            WordsFieldManager wordsFieldManager,
            DictionaryService dictionaryService)
            => GetWordAsync(settings, wordsFieldManager, dictionaryService, clueOnly: false);

        public async UniTask<AIWordResult> GetWordAsync(
            ComplexityAISettings settings,
            WordsFieldManager wordsFieldManager,
            DictionaryService dictionaryService,
            bool clueOnly)
        {
            _bCancel = false;

            var items = wordsFieldManager.WordsFieldData.Items;
            int boardMaxLen = items.Count;
            const int MinLength = 2;

            int lettersOnBoard = CountLettersOnBoard(items);
            int maxPossibleNow = Math.Min(boardMaxLen, lettersOnBoard + 1);

            int start = Math.Min(maxPossibleNow, Math.Max(MinLength, settings.WordLength.Max));

            if (settings.IsRandomWordLength)
                start = _rng.Next(settings.WordLength.Min, start + 1);

            IEnumerable<int> lengthsToTry = Enumerable.Range(MinLength, start - MinLength + 1).Reverse();

            var result = await WordSearchEngineAsync.FindWordAsync(
                fieldItems: items,
                getWordsOfLength: dictionaryService.GetWordsOfLength,
                lengthsToTryDesc: lengthsToTry,
                bCancelFunc: () => _bCancel,
                wordAlreadyUsed: wordsFieldManager.WordExist,
                yieldEveryWordChecks: 250);

            if (!result.Success)
                return AIWordResult.Fail();

            // Меняем реальное поле только после успешного поиска
            items[result.InsertIndex].SetLetter(result.InsertChar.ToString());

            if (clueOnly)
            {
                // The word is only a hint: leave its path entirely for the player to select.
                items[result.InsertIndex].SetClueHighlight(true);
                wordsFieldManager.WordsFieldData.SetSelectedIndexes(new List<int>());
            }
            else
            {
                foreach (int idx in result.PathIndexes)
                    items[idx].Highlight();

                wordsFieldManager.WordsFieldData.SetSelectedIndexes(result.PathIndexes);
            }
            wordsFieldManager.WordsFieldData.SetLetterItem(items[result.InsertIndex]);

            Debug.Log($"[SmartWordAlgorithmAsync][GetWordAsync] start = {start}, Word [{result.Word.Length}] = {result.Word}");
            return AIWordResult.Ok(result.Word);
        }

        private static int CountLettersOnBoard(IReadOnlyList<SelectableLetter> items)
        {
            int c = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].Empty())
                    c++;
            }

            return c;
        }
    }
}
