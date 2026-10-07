using Core.Config;
using Core.Services.DataDictionary;
using Cysharp.Threading.Tasks;
using Game.Logic;

namespace Game.AI
{
    public class AIGameController : IState
    {
        private WordsFieldManager _wordsFieldManager;
        private DictionaryService _dictionaryService;

        private readonly SmartWordAlgorithmAsync _algorithm = new();

        public void Destroy() { }

        internal void Init(WordsFieldManager wordsFieldManager, DictionaryService dictionaryService)
        {
            _wordsFieldManager = wordsFieldManager;
            _dictionaryService = dictionaryService;
        }

        /// <summary>
        /// Универсальный поиск слова (для хода ИИ, подсказок, бустеров).
        /// Применяет найденный ход к полю; clueOnly ставит только новую букву без выбора слова.
        /// </summary>
        internal UniTask<AIWordResult> FindWordAsync(ComplexityAISettings settings, bool clueOnly = false)
        {
            return _algorithm.GetWordAsync(settings, _wordsFieldManager, _dictionaryService, clueOnly);
        }

        /// <summary>
        /// Прервать поиск слова (не важно почему: таймер, смена сцены, отмена игроком и т.п.).
        /// </summary>
        internal void AbortSearch()
        {
            _algorithm.Cancel(); // можно переименовать в алгоритме в Cancel()
        }
    }
}
