using System;
using System.Collections.Generic;

namespace Core.Data
{
    [Serializable]
    public class SaveGameData
    {
        public int version = 1;            // версия игровой механики
        public long savedAtUtcTicks;       // дата-время сохранения
        public string localeCode;          // "ru", "uk", "en"
        
        public string mode;                // "AI" (пока только AI) / "PVP"
        public uint boardSize;             // например 5
        public string[] boardRows;         // буквы на кнопках
        public int levelComplexityAI;      // уровень сложности
        public bool playerTurn;            // ход игрока
        public int maxSeconds;             // время хода
        public int currentSeconds;         // текущее время хода
        
        public uint playerScore;            // счет игрока
        public uint playerPasses;           // пропуски игрока
        public uint opponentScore;          // счет оппонента
        public uint opponentPasses;         // пропуски оппонента

        public string firstWord;                   // исходное слово
        public string lastWord;                    // последнее добавленное слово
        public List<string> playerWords = new();   // слова игрока
        public List<string> opponentWords = new(); // слова оппонента
    }
}