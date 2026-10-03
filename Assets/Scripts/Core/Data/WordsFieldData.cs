using System.Collections.Generic;
using Core.Config;
using UI.Components;
using UnityEngine;

namespace Core.Data
{
    public class WordsFieldData
    {
        public const int AMOUNT_LETTERS = 25;
        private const int BOARD_WIDTH = 5;

        public SelectableLetter SelectedItem => _selectedItem;
        
        public List<SelectableLetter> Items => _items;

        private List<SelectableLetter> _items; // Список всех элементов на поле.
        private List<int> _selectedIndex = new(); // Индексы выбранных букв составляющих слово.
        private SelectableLetter _selectedItem; // Выделенная пустая ячейка (для будущей установки в нее буквы).
        private SelectableLetter _setItem; // Установленная буква.
        private List<string> _words = new(); // Список слов собранных в текущей игровой сессии.

        internal void SetItems(List<SelectableLetter> items)
        {
            _items = items;
        }

        /// <summary>
        /// Установить первое слово в среднюю строку поля.
        /// </summary>
        /// <param name="word">Слово</param>
        internal void SetFirstWord(string word)
        {
            _words.Add(word);
            for (int i = 10, n = 0; i < 15; ++i, ++n)
            {
                _items[i].SetLetter(word[n].ToString());
                _items[i].UnHighlight();
            }
        }

        /// <summary>
        /// Проверить слово на соответствие правилам игры
        /// </summary>
        /// <param name="word">Слово</param>
        /// <returns>Успех</returns>
        internal GameError CheckWord(string word)
        {
            if (_selectedIndex.Count == 0)
                return GameError.WordNoSelected;

            if (_setItem == null || !_selectedIndex.Exists(x => x == _setItem.Index))
                return GameError.SetLetterNoSelected;

            if (Exist(word))
                return GameError.WordAlreadyBeen;

            return GameError.None;
        }

        /// <summary>
        /// Добавить слово в список слов собранных в текущей игровой сессии.
        /// </summary>
        /// <param name="word">Слово</param>
        internal void SaveWord(string word)
        {
            _words.Add(word);
        }

        internal List<int> GetSelectedIndexesSnapshot()
        {
            return new List<int>(_selectedIndex);
        }

        internal void BlinkNoSelectedLetter()
        {
            if (_setItem)
                _setItem.SetModeBlink();
        }

        internal void ShowLetters(bool value)
        {
            _items.ForEach(item => item.ShowLetter(value));
        }

        internal bool Exist(string word)
        {
            return _words.Exists(x => x == word);
        }

        /// <summary>
        /// Проверить, можно ли игроку выбрать данную букву на поле
        /// </summary>
        /// <param name="item">Выбранный игроком элемент поля</param>
        /// <returns>Успех</returns>
        internal bool CheckSelectLetter(SelectableLetter item)
        {
            int index = item.Index;

            // Повторно нажал одну и ту же кнопку - не брать данные с кнопки.
            if (_selectedIndex.Exists(x => x == index))
                return false;

            // На кнопке нет буквы
            if (_items[index].Empty())
                return false;

            // Это первая выбранная буква
            if (_selectedIndex.Count == 0)
            {
                _selectedIndex.Add(index);
                return true; // Дальше сверяться не с чем
            }

            int indexLast = _selectedIndex[_selectedIndex.Count - 1];

            if (AreOrthogonalNeighbors(indexLast, index))
            {
                _selectedIndex.Add(index);
                return true;
            }

            return false; // Нажата по диагонали или дальше чем на одну позицию
        }

        /// <summary>
        /// Полностью ли заполнено поле.
        /// </summary>
        /// <returns>Успех</returns>
        internal bool Filled()
        {
            foreach (var item in _items)
            {
                if (item.Empty())
                    return false;
            }

            return true;
        }
        
        /// <summary>
        /// Поле заполнено или пустое (стерто ластиком).
        /// </summary>
        /// <returns>Успех</returns>
        internal bool FilledOrEmpty()
        {
            int countEmpty = 0, countNotEmpty = 0;
            foreach (var item in _items)
            {
                if (item.Empty())
                    ++countEmpty;
                else
                    ++countNotEmpty;
            }

            return countEmpty == AMOUNT_LETTERS || countNotEmpty == AMOUNT_LETTERS;
        }
        

        /// <summary>
        /// Проверка попытки игрока установить новую букву в желаемую позицию.
        /// </summary>
        /// <param name="index">Позиция элемента на поле</param>
        /// <returns>Успех</returns>
        internal bool TrySetLetter(int index)
        {
            if ((index < 0 || index >= _items.Count) || !_items[index].Empty())
                return false; // Целевая позиция должна быть свободна

            foreach (int neighborIndex in GetOrthogonalNeighbors(index))
            {
                if (!_items[neighborIndex].Empty())
                    return true;
            }

            return false;
        }

        internal void SetSelectedIndexes(List<int> selectedIndex)
        {
            _selectedIndex = selectedIndex ?? new List<int>();
        }

        internal void SetLetterItem(SelectableLetter item)
        {
            _setItem = item;
        }

        internal void SetSelectedCell(SelectableLetter item)
        {
            _selectedItem = item;
        }
        
        internal void CellSelectCancel()
        {
            if (_selectedItem != null)
            {
                _selectedItem.UnHighlight();
                _selectedItem = null;
            }
        }

        internal bool SetLetterToSelectedCell(string letter)
        {
            if (_selectedItem != null)
            {
                _selectedItem.SetLetter(letter);
                _selectedItem.HighlightCell();
                _setItem = _selectedItem;
                _selectedItem = null;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Смыть временное выделение.
        /// </summary>
        internal void Clear()
        {
            _items?.ForEach(item => item.UnHighlight());
            _selectedIndex.Clear();
            _selectedItem = null;
            _setItem = null;
        }

        /// <summary>
        /// Отменить.
        /// Убирается выделение и установленная, но еще не зафиксированная буква.
        /// </summary>
        internal void Cancel()
        {
            _setItem?.SetLetter("");
            Clear();
        }

        internal void Reset()
        {
            _items?.ForEach(item => item.SetLetter(""));
            Clear();
            _words.Clear();
            _selectedItem = null;
            _setItem = null;
        }

        internal bool BacktrackOneStep(out SelectableLetter removedItem)
        {
            removedItem = null;

            if (_selectedIndex == null || _selectedIndex.Count == 0)
                return false;

            int lastIndex = _selectedIndex[_selectedIndex.Count - 1];
            _selectedIndex.RemoveAt(_selectedIndex.Count - 1);

            removedItem = _items[lastIndex];
            return true;
        }

        public string[] GetBoardData()
        {
            string[] list = new string[AMOUNT_LETTERS];

            for (int i = 0; i < AMOUNT_LETTERS; ++i)
                list[i] = _items[i].GetLetter();

            return list;
        }

        public void SetSaveGameData(SaveGameData saveGameData)
        {
            _words.Add(saveGameData.firstWord);

            for (int i = 0; i < saveGameData.boardRows.Length; ++i)
            {
                _items[i].SetLetter(saveGameData.boardRows[i]);
                _items[i].UnHighlight();
            }

            _words.AddRange(saveGameData.playerWords);
            _words.AddRange(saveGameData.opponentWords);

            _selectedIndex.Clear();
            _selectedItem = null;
            _setItem = null;
        }

        private static bool AreOrthogonalNeighbors(int a, int b)
        {
            if (a == b)
                return false;

            // Вертикальные соседи
            if (a - BOARD_WIDTH == b || a + BOARD_WIDTH == b)
                return true;

            // Горизонтальные соседи — только в пределах одной строки
            if ((a / BOARD_WIDTH) == (b / BOARD_WIDTH))
            {
                if (a - 1 == b || a + 1 == b)
                    return true;
            }

            return false;
        }

        private static IEnumerable<int> GetOrthogonalNeighbors(int index)
        {
            int up = index - BOARD_WIDTH;
            int down = index + BOARD_WIDTH;
            int left = index - 1;
            int right = index + 1;

            if (up >= 0)
                yield return up;

            if (down < AMOUNT_LETTERS)
                yield return down;

            if (left >= 0 && (left / BOARD_WIDTH) == (index / BOARD_WIDTH))
                yield return left;

            if (right < AMOUNT_LETTERS && (right / BOARD_WIDTH) == (index / BOARD_WIDTH))
                yield return right;
        }
        
    }
}
