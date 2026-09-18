using System.Collections.Generic;
using Core.UI.Components;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Components
{
    public class StatisticPlayerPanel : MonoBehaviour
    {
        public List<string> Words { get; private set; } = new();

        [SerializeField] private ScrollRect _scrollRect;
        [Tooltip("Ссылка на контейнер для списка слов")]
        [SerializeField] private Transform _listContent;
        [Tooltip("Ссылка на префаб с элементом текстового поля")]
        [SerializeField] private WordListItem _wordListItemPrefab;
        
        [Inject] private DiContainer _container;
        
        /// <summary>
        /// Добавить слово.
        /// </summary>
        public async void AddWord(string value)
        {
            if (_wordListItemPrefab && _listContent)
            {
                CreateWordItem(value);
                
                await UniTask.Yield();
                
                _scrollRect.verticalNormalizedPosition = 0;
            }
        }
        
        public async void AddWords(List<string> words)
        {
            if (_wordListItemPrefab && _listContent)
            {
                foreach (var value in words)
                    CreateWordItem(value);

                await UniTask.Yield();
                
                _scrollRect.verticalNormalizedPosition = 0;
            }
        }

        public void Reset()
        {
            Words.Clear();
            if (_listContent)
                foreach (Transform child in _listContent.transform)
                    Destroy(child.gameObject);
        }
        
        private void CreateWordItem(string word)
        {
            WordListItem wordListItem = _container.InstantiatePrefabForComponent<WordListItem>(_wordListItemPrefab, _listContent);
            wordListItem.Initialize(word);
            Words.Add(word);
        }
    }
}