using System;
using System.Collections.Generic;

namespace Core.Data
{
    // Null in legacy saves means no active Hint. Reveal animation itself is never serialized.
    [Serializable]
    public sealed class HintState
    {
        public bool active;
        public string targetWord;
        public int[] orderedPath;
        public string letter;
        public int cellIndex;
        public int targetLength;
        // A pass is committed exactly once after reveal (or immediately when recovering it).
        public bool passPending;
        public bool timedOut;

        public HintState Copy() => new()
        {
            active = active, targetWord = targetWord, orderedPath = orderedPath == null ? null : (int[])orderedPath.Clone(),
            letter = letter, cellIndex = cellIndex, targetLength = targetLength,
            passPending = passPending, timedOut = timedOut
        };

        public bool IsValidForBoard(string[] board)
        {
            if (!active || board == null || string.IsNullOrEmpty(targetWord) || targetLength != targetWord.Length || targetLength < 2
                || orderedPath == null || orderedPath.Length != targetLength || cellIndex < 0 || cellIndex >= board.Length
                || string.IsNullOrEmpty(letter) || letter.Length != 1 || board[cellIndex] != letter || (timedOut && !passPending))
                return false;
            var visited = new HashSet<int>();
            for (int i = 0; i < orderedPath.Length; i++)
            {
                int index = orderedPath[i];
                if (index < 0 || index >= board.Length || !visited.Add(index) || board[index] != targetWord[i].ToString())
                    return false;
                if (i > 0)
                {
                    int previous = orderedPath[i - 1];
                    if (Math.Abs(previous / 5 - index / 5) + Math.Abs(previous % 5 - index % 5) != 1)
                        return false;
                }
            }
            return visited.Contains(cellIndex);
        }
    }
}
