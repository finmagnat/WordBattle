using System;
using System.IO;
using System.Linq;
using Core.Config;
using Core.Data;
using UnityEngine;
using Zenject;

namespace Core.Services
{
    public class RoundAutoSaveService
    {
        private readonly ConfigService _config;
        private readonly LocalizationService _localization;
        private readonly string _filePath;

        public bool HasRecoverySave => File.Exists(_filePath);

        [Inject]
        public RoundAutoSaveService(ConfigService config, LocalizationService localization)
            : this(config, localization, Application.persistentDataPath)
        {
        }

        public RoundAutoSaveService(ConfigService config, LocalizationService localization, string directoryPath)
        {
            _config = config;
            _localization = localization;
            // One recovery slot across all locales; the snapshot carries its own language.
            _filePath = Path.Combine(directoryPath, "round_recovery.json");
        }

        public void Save(SaveGameData data)
        {
            try
            {
                Validate(data);
                SaveGameStorage.WriteAtomic(_filePath, data);
                Debug.Log("[Recovery] Recovery save created/updated.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Recovery] Could not update recovery save; previous save retained: {exception.Message}");
            }
        }

        public SaveGameData Load()
        {
            if (!HasRecoverySave)
                return null;

            Debug.Log("[Recovery] Recovery save found on startup.");
            try
            {
                var data = SaveGameStorage.Deserialize(File.ReadAllText(_filePath));
                Validate(data);
                Debug.Log("[Recovery] Recovery save loaded.");
                return data;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Recovery] Recovery save invalid/corrupted: {exception.Message}");
                Clear("invalid/corrupted");
                return null;
            }
        }

        public void Clear(string reason)
        {
            try
            {
                bool existed = HasRecoverySave;
                File.Delete(_filePath);
                File.Delete(_filePath + ".tmp");
                if (existed)
                    Debug.Log($"[Recovery] Recovery save deleted: {reason}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Recovery] Could not delete recovery save ({reason}): {exception.Message}");
            }
        }

        private void Validate(SaveGameData data)
        {
            if (data == null || data.version != _config.Game.version || data.savedAtUtcTicks <= 0 ||
                data.savedAtUtcTicks > DateTime.MaxValue.Ticks ||
                string.IsNullOrEmpty(data.localeCode) || !_localization.GetAvailableLocaleCodes().Contains(data.localeCode) ||
                data.mode != GameOpponent.AI.ToString() ||
                data.boardRows == null || data.boardRows.Length != WordsFieldData.AMOUNT_LETTERS ||
                data.boardSize != (uint)data.boardRows.Length ||
                data.boardRows.Any(cell => cell == null || cell.Length > 1 || (cell.Length == 1 && !char.IsLetter(cell[0]))) ||
                (data.boardRows.All(string.IsNullOrEmpty) && !data.eraserUsedThisTurn) ||
                (data.boardRows.All(cell => !string.IsNullOrEmpty(cell)) && !data.hasPlacedLetter) ||
                string.IsNullOrEmpty(data.firstWord) || data.firstWord.Length != (int)_config.Game.defaultBoardSize ||
                data.lastWord == null || data.playerWords == null || data.opponentWords == null ||
                data.playerWords.Any(string.IsNullOrWhiteSpace) || data.opponentWords.Any(string.IsNullOrWhiteSpace) ||
                data.playerScore != data.playerWords.Sum(word => (long)word.Length) ||
                data.opponentScore != data.opponentWords.Sum(word => (long)word.Length) ||
                (!string.IsNullOrEmpty(data.lastWord) && !data.playerWords.Contains(data.lastWord) && !data.opponentWords.Contains(data.lastWord)) ||
                data.maxSeconds < GameConfig.MinDurationGameSeconds || data.currentSeconds < 0 || data.currentSeconds > data.maxSeconds ||
                (data.currentSeconds == data.maxSeconds && !(data.hint?.passPending == true && data.hint.timedOut)) ||
                data.winsInSeries < 0 || !Enum.IsDefined(typeof(ComplexityAI), data.levelComplexityAI))
                throw new InvalidDataException("Unsupported version, locale, mode or inconsistent round state.");

            if (float.IsNaN(data.slowdownRemainingSeconds) || float.IsInfinity(data.slowdownRemainingSeconds) || data.slowdownRemainingSeconds < 0 ||
                (data.hasPlacedLetter && (data.placedLetterIndex < 0 || data.placedLetterIndex >= data.boardRows.Length ||
                    string.IsNullOrEmpty(data.boardRows[data.placedLetterIndex]))) ||
                (data.eraserRemovalCommitted && !data.eraserUsedThisTurn) ||
                (data.eraserUsedThisTurn && !data.eraserRemovalCommitted && (data.erasedCellIndex < 0 || data.erasedCellIndex >= data.boardRows.Length ||
                    string.IsNullOrEmpty(data.erasedLetter) || data.erasedLetter.Length != 1 || !char.IsLetter(data.erasedLetter[0]))) ||
                (data.eraserSelectionPending && (data.swapSelectionPending || data.eraserUsedThisTurn)) ||
                ((data.eraserSelectionPending || data.swapSelectionPending) && data.hasPlacedLetter) ||
                (!data.playerTurn && (data.hasPlacedLetter || data.eraserUsedThisTurn || data.eraserSelectionPending ||
                    data.swapSelectionPending || data.slowdownRemainingSeconds > 0)))
                throw new InvalidDataException("Invalid pending letter or booster state.");

            if (data.hint != null && (!data.playerTurn || !data.hasPlacedLetter || data.placedLetterIndex != data.hint.cellIndex
                || !data.hint.IsValidForBoard(data.boardRows)))
                throw new InvalidDataException("Invalid active Hint state.");

            var complexity = _config.Game.GetComplexityAIItem((ComplexityAI)data.levelComplexityAI);
            if (complexity.СomplexityAiLevel != (ComplexityAI)data.levelComplexityAI || (complexity.MaxPasses > 0 &&
                (data.playerPasses >= complexity.MaxPasses || data.opponentPasses >= complexity.MaxPasses)))
                throw new InvalidDataException("Unsupported AI difficulty or round already finished.");
        }
    }
}
