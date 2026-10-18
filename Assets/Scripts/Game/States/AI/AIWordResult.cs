namespace Game.AI
{
    public readonly struct AIWordResult
    {
        public readonly bool Success;
        public readonly string Word;
        public readonly int InsertIndex;
        public readonly string InsertLetter;
        public readonly int[] OrderedPath;

        public AIWordResult(bool success, string word, int insertIndex = -1, string insertLetter = null, int[] orderedPath = null)
        {
            Success = success;
            Word = word;
            InsertIndex = insertIndex;
            InsertLetter = insertLetter;
            OrderedPath = orderedPath;
        }

        public static AIWordResult Fail() => new(false, string.Empty);
        public static AIWordResult Ok(string word) => new(true, word);
    }

}
