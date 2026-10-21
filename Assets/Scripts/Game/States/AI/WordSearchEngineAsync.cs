using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UI.Components;

namespace Game.AI
{
    public static class WordSearchEngineAsync
    {
        public readonly struct SearchResult
        {
            public readonly bool Success;
            public readonly string Word;
            public readonly int InsertIndex;
            public readonly char InsertChar;
            public readonly List<int> PathIndexes;

            public SearchResult(bool success, string word, int insertIndex, char insertChar, List<int> path)
            {
                Success = success;
                Word = word;
                InsertIndex = insertIndex;
                InsertChar = insertChar;
                PathIndexes = path;
            }

            public static SearchResult Fail() => new(false, string.Empty, -1, default, new List<int>());
        }

        public static async UniTask<SearchResult> FindWordAsync(
            List<SelectableLetter> fieldItems,
            Func<int, IReadOnlyList<string>> getWordsOfLength,
            IEnumerable<int> lengthsToTryDesc,
            Func<bool> bCancelFunc,
            Func<string, bool> wordAlreadyUsed,
            int yieldEveryWordChecks = 250)
        {
            var insertCells = CollectInsertCandidates(fieldItems);
            if (insertCells.Count == 0)
                return SearchResult.Fail();

            int checkedWords = 0;

            foreach (int len in lengthsToTryDesc)
            {
                var words = getWordsOfLength(len);
                if (words == null || words.Count == 0)
                    continue;

                foreach (var raw in words)
                {
                    if (bCancelFunc())
                        return SearchResult.Fail();

                    if (++checkedWords % yieldEveryWordChecks == 0)
                        await UniTask.Yield();
                    if (bCancelFunc())
                        return SearchResult.Fail();

                    var word = raw.Trim().ToUpperInvariant();
                    if (word.Length != len)
                        continue;

                    if (wordAlreadyUsed(word))
                        continue;

                    for (int mid = 0; mid < word.Length; mid++)
                    {
                        char insertChar = word[mid];

                        foreach (int insertIndex in insertCells)
                        {
                            var used = new List<int>(word.Length) { insertIndex };

                            if (!BuildPart(fieldItems, used, insertIndex, word, mid - 1, -1,
                                    () => BuildPart(fieldItems, used, insertIndex, word, mid + 1, +1)))
                                continue;

                            if (used.Count == word.Length)
                            {
                                // DFS visits the insertion, then the prefix backwards, then the suffix.
                                // Export the actual reading order, including middle-of-word insertions.
                                var ordered = new List<int>(word.Length);
                                for (int i = mid; i >= 1; --i) ordered.Add(used[i]);
                                ordered.Add(insertIndex);
                                for (int i = mid + 1; i < used.Count; ++i) ordered.Add(used[i]);
                                return new SearchResult(true, word, insertIndex, insertChar, ordered);
                            }
                        }
                    }
                }
            }

            return SearchResult.Fail();
        }

        private static List<int> CollectInsertCandidates(IReadOnlyList<SelectableLetter> fieldItems)
        {
            var result = new List<int>(fieldItems.Count);

            for (int i = 0; i < fieldItems.Count; i++)
            {
                if (!fieldItems[i].Empty())
                    continue;

                if (HasNonEmptyNeighbor(i, fieldItems))
                    result.Add(i);
            }

            return result;
        }

        private static bool HasNonEmptyNeighbor(int index, IReadOnlyList<SelectableLetter> fieldItems)
        {
            foreach (int n in GetNeighbors(index, fieldItems.Count))
            {
                if (!IsSameRowIfHorizontal(index, n))
                    continue;

                if (!fieldItems[n].Empty())
                    return true;
            }

            return false;
        }

        private static bool BuildPart(
            IReadOnlyList<SelectableLetter> fieldItems,
            List<int> used,
            int startIndex,
            string word,
            int nextIndex,
            int direction,
            Func<bool> onComplete = null)
        {
            if (nextIndex < 0 || nextIndex >= word.Length)
                return onComplete?.Invoke() ?? true;

            return Dfs(fieldItems, used, startIndex, word, nextIndex, direction, onComplete);
        }

        private static bool Dfs(
            IReadOnlyList<SelectableLetter> fieldItems,
            List<int> used,
            int currentIndex,
            string word,
            int nextWordIndex,
            int direction,
            Func<bool> onComplete)
        {
            if (nextWordIndex < 0 || nextWordIndex >= word.Length)
                return onComplete?.Invoke() ?? true;

            char target = word[nextWordIndex];

            foreach (int n in GetNeighbors(currentIndex, fieldItems.Count))
            {
                if (!IsSameRowIfHorizontal(currentIndex, n))
                    continue;

                if (used.Contains(n))
                    continue;

                if (fieldItems[n].Empty())
                    continue;

                if (fieldItems[n].GetChar() != target)
                    continue;

                used.Add(n);

                if (Dfs(fieldItems, used, n, word, nextWordIndex + direction, direction, onComplete))
                    return true;

                used.RemoveAt(used.Count - 1);
            }

            return false;
        }

        private static IEnumerable<int> GetNeighbors(int index, int count)
        {
            int up = index - 5;
            int down = index + 5;
            int left = index - 1;
            int right = index + 1;

            if (up >= 0) yield return up;
            if (down < count) yield return down;
            if (left >= 0) yield return left;
            if (right < count) yield return right;
        }

        private static bool IsSameRowIfHorizontal(int a, int b)
        {
            int diff = a - b;
            if (diff != 1 && diff != -1)
                return true;

            return (a / 5) == (b / 5);
        }
    }
}
