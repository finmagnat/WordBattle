using System.Collections.Generic;
using Core.Data;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using Zenject;
using Core.Events;
using UnityEngine.UI;

namespace UI.Components
{
    public class WordsField : MonoBehaviour
    {
        [SerializeField] private GameObject _selectebleLetterPrefab;
        [SerializeField] private Image _mainBackground;
     
        //[Inject] private ISkinsService _skinsService;
        [Inject] private DiContainer _container;
        
        private const int COLS = 5;
        private const float AcceptedWordAnimationDuration = 0.8f;
        private const float AcceptedWordScale = 1.12f;
        private const int AcceptedWordPulseLoops = 2;

        private bool _isDragging;
        private bool _isInputLocked;
        private Sequence _acceptedWordSequence;
        private readonly List<SelectableLetter> _dragPath = new();
        private readonly HashSet<int> _dragVisited = new();
        
        private List<SelectableLetter> _items = new (WordsFieldData.AMOUNT_LETTERS);
        private bool _isInitialized;
        private bool _bModeEraser;
        private bool _bModeSwap;

        internal bool IsInputLocked => _isInputLocked;
        internal bool IsBoosterLetterSelectionMode => _bModeEraser || _bModeSwap;
        
        public List<SelectableLetter> InitField()
        {
            _acceptedWordSequence?.Kill();
            _acceptedWordSequence = null;
            _isDragging = false;
            _isInputLocked = false;
            _bModeEraser = false;
            _bModeSwap = false;
            _dragPath.Clear();
            _dragVisited.Clear();
            
            if (!_isInitialized)
            {
                for (int i = 0; i < WordsFieldData.AMOUNT_LETTERS; ++i)
                {
                    var letter = _container.InstantiatePrefabForComponent<SelectableLetter>(_selectebleLetterPrefab, transform);
                    letter.SetLetter("");
                    letter.Index = i;

                    _items.Add(letter);
                }
                _isInitialized = true;
            }
            else
            {
                _items.ForEach(item => item.Reset());
            }

            ApplyPreparedSkin();
            
            return _items;
        }

        public void BeginDragSelection(SelectableLetter start)
        {
            if (_isInputLocked || _bModeEraser || _bModeSwap || start == null || start.Empty())
                return;

            _isDragging = true;
            _dragPath.Clear();
            _dragVisited.Clear();

            // первая буква — всегда попытка выбрать
            AddToPathAndNotifySelect(start);
        }

        public void ContinueDragSelection(SelectableLetter current)
        {
            if (_isInputLocked || !_isDragging || current == null || current.Empty())
                return;

            // ШАГ НАЗАД: зашли на предпоследнюю букву => снимаем последнюю
            if (_dragPath.Count >= 2 && current == _dragPath[^2])
            {
                RemoveLastFromPathAndNotifyBacktrack();
                return;
            }

            // Нельзя "кусать себя" (любое другое повторное посещение запрещено)
            if (_dragVisited.Contains(current.Index))
                return;

            // Если это не первая — проверяем ортогонального соседа (без диагоналей)
            if (_dragPath.Count > 0)
            {
                var last = _dragPath[^1];
                if (!AreOrthogonalNeighbours(last.Index, current.Index))
                    return;
            }

            AddToPathAndNotifySelect(current);
        }

        public void EndDragSelection()
        {
            _isDragging = false;
        }

        public async UniTask PlayAcceptedWordScaleAnimationAsync(IReadOnlyList<int> selectedIndexes)
        {
            if (selectedIndexes == null || selectedIndexes.Count == 0)
                return;

            _acceptedWordSequence?.Kill();

            var targets = new List<Transform>(selectedIndexes.Count);
            foreach (int index in selectedIndexes)
            {
                if (index < 0 || index >= _items.Count || _items[index] == null)
                    continue;

                Transform target = _items[index].transform;
                if (!targets.Contains(target))
                    targets.Add(target);
            }

            if (targets.Count == 0)
                return;

            var baseScales = new Vector3[targets.Count];
            float pulseDuration = AcceptedWordAnimationDuration / AcceptedWordPulseLoops;
            Sequence sequence = DOTween.Sequence();
            _acceptedWordSequence = sequence;

            for (int i = 0; i < targets.Count; ++i)
            {
                Transform target = targets[i];
                target.DOKill();

                baseScales[i] = target.localScale;
                target.localScale = baseScales[i];

                sequence.Join(
                    target
                        .DOScale(baseScales[i] * AcceptedWordScale, pulseDuration)
                        .SetEase(Ease.InOutSine)
                        .SetLoops(AcceptedWordPulseLoops, LoopType.Yoyo));
            }

            try
            {
                await sequence.AsyncWaitForCompletion();
            }
            finally
            {
                for (int i = 0; i < targets.Count; ++i)
                {
                    if (targets[i])
                        targets[i].localScale = baseScales[i];
                }

                if (_acceptedWordSequence == sequence)
                    _acceptedWordSequence = null;
            }
        }

        internal void SetInputLocked(bool value)
        {
            _isInputLocked = value;

            if (value)
                EndDragSelection();
        }
        
        private void ApplyPreparedSkin()
        {
            //_mainBackground.sprite = _skinsService.GetSprite(SkinSpriteKey.FrameBackgroundAlias);
            _items.ForEach(item => item.ApplyCurrentSkin());
        }
        
        internal void SetModeEraser(bool value)
        {
            _bModeEraser = value;
        }
        
        internal void SetModeSwap(bool value)
        {
            _bModeSwap = value;
        }

        private void AddToPathAndNotifySelect(SelectableLetter letter)
        {
            if (!_dragVisited.Add(letter.Index))
                return;

            _dragPath.Add(letter);

            // всё остальное (валидность по игровым правилам, Highlight, добавление буквы в слово)
            // делает WordsFieldManager через существующий LetterSelectEvent
            //Debug.Log("[WordsField][OnPressed] [Letter Select Event] Index: " + letter.Index + ", Letter: " + letter.GetLetter());
            EventBus.Raise(new LetterSelectEvent { letter = letter });
        }

        private void RemoveLastFromPathAndNotifyBacktrack()
        {
            if (_dragPath.Count == 0)
                return;

            // локально откатываем маршрут
            var last = _dragPath[^1];
            _dragPath.RemoveAt(_dragPath.Count - 1);
            _dragVisited.Remove(last.Index);

            // дальше пусть менеджер снимет Highlight и скажет UI удалить последний символ
            EventBus.Raise(new LetterBacktrackEvent());
        }

        private bool AreOrthogonalNeighbours(int aIndex, int bIndex)
        {
            int ax = aIndex % COLS;
            int ay = aIndex / COLS;
            int bx = bIndex % COLS;
            int by = bIndex / COLS;

            int dx = Mathf.Abs(ax - bx);
            int dy = Mathf.Abs(ay - by);

            return dx + dy == 1;
        }

        private void OnDisable()
        {
            _acceptedWordSequence?.Kill();
            _acceptedWordSequence = null;
            _isInputLocked = false;
        }
    }

}
