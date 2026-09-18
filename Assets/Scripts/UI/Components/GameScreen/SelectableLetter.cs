using Core.Config;
using Core.Data;
using Core.Events;
using Core.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using UnityEngine.EventSystems;

namespace UI.Components
{
    public class SelectableLetter : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler
    {
        [SerializeField] private TextMeshProUGUI _letterText;
        [SerializeField] private Image _mainBackground;
        [SerializeField] private uint _blinkCount = 3; // Количество миганий
        [SerializeField] private float _blinkDtDelay = 0.5f; // Интервал 0.5 секунды
        
        [Inject] private AudioService _audioService;

        private ISkinsService _skinsService;
        
        public int Index { get; set; } // Индекс элемента на поле [0 - n]
        
        private bool IsHighlight => _highlightState == HighlightState.Highlighted;
        
        private WordsField _wordsField;
        private float _suppressClickUntil;
        private bool _handledPointerDownSelection;
        
        private float _blinkDtTimer = 0;
        private float _blinkCounter = 0;
        private bool _bModeBlink; // Режим мигания
        private bool _isIlluminated; // Состояние подсветки в Режиме мигания
        private string _letter;
        
        private HighlightState _highlightState = HighlightState.None; // Режим подсветки (выделена буква или ячейка)
        enum HighlightState { None, SelectedCell, Highlighted };

        private void Awake()
        {
            _wordsField = GetComponentInParent<WordsField>();
        }

        [Inject]
        private void Construct(ISkinsService skinsService)
        {
            _skinsService = skinsService;
            _skinsService.OnRuntimeChanged += OnRuntimeChanged;
        }

        private void OnDestroy()
        {
            if (_skinsService != null)
                _skinsService.OnRuntimeChanged -= OnRuntimeChanged;
        }

        private void Update()
        {
            if (_bModeBlink)
            {
                _blinkDtTimer += Time.deltaTime;
                if (_blinkDtTimer >= _blinkDtDelay)
                {
                    _blinkDtTimer = 0;
                    Blink();
                    if (_blinkCounter > _blinkCount)
                    {
                        ModeBlinkClear();
                        HighlightCell();
                        EventBus.Raise(new ModeBlinkEndEvent());
                    }
                }
            }
        }
        
        public void OnPointerDown(PointerEventData eventData)
        {
            _handledPointerDownSelection = false;

            if (IsInputLocked() || Empty()) return;

            _suppressClickUntil = Time.unscaledTime + 0.05f;

            if (_wordsField != null && _wordsField.IsBoosterLetterSelectionMode)
            {
                _handledPointerDownSelection = true;
                EventBus.Raise(new LetterSelectEvent{ letter = this });
                return;
            }

            _wordsField?.BeginDragSelection(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (IsInputLocked() || Empty()) return;

            _wordsField?.ContinueDragSelection(this);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _wordsField?.EndDragSelection();
        }
        
        public void OnPressed()
        {
            bool handledPointerDownSelection = _handledPointerDownSelection;
            _handledPointerDownSelection = false;

            if (IsInputLocked())
                return;

            if (handledPointerDownSelection)
                return;

            // если PointerDown уже выбрал букву — не дублируем выбор по клику
            if (Time.unscaledTime < _suppressClickUntil)
                return;
            
            if (Empty() && !IsHighlight)
            {
                //Debug.Log("[SelectableLetter][OnPressed] [Cell Select Event] Index: " + Index + ", Empty ");
                EventBus.Raise(new CellSelectEvent{ letter = this }); // Игрок кликнул по пустой ячейке
                return;
            }

            if (!Empty() && !IsHighlight)
            {
                ModeBlinkClear();
                //Debug.Log("[SelectableLetter][OnPressed] [Letter Select Event] Index: " + Index + ", Letter: " + _letter);
                EventBus.Raise(new LetterSelectEvent{ letter = this });
            }
        }

        public void Reset()
        {
            SetLetter("");
            UnHighlight();
        }

        internal void ApplyCurrentSkin()
        {
            if (_skinsService.TryGetColor(SkinColorKey.LettersFieldColor, out Color color))
            {
                _letterText.color = color;
            }
            else
            {
                Debug.LogWarning(
                    $"[{nameof(SelectableLetter)}] Color key '{SkinColorKey.LettersFieldColor}' " +
                    "is absent in the current skin runtime.",
                    this);
            }

            SetHighlightState(HighlightState.None);
        }

        public bool ApplyBackgroundSkin(SkinSpriteKey key)
        {
            if (_skinsService != null && _skinsService.TryGetSprite(key, out Sprite sprite))
            {
                _mainBackground.sprite = sprite;
                return true;
            }

            Debug.LogWarning(
                $"[{nameof(SelectableLetter)}] Sprite key '{key}' is absent in the current skin runtime.",
                this);
            return false;
        }

        /// <summary>
        /// Включить режим "Помигать буквой"
        /// </summary>
        internal void SetModeBlink() => _bModeBlink = true;

        internal void SetLetter(string letter)
        {
            _letter = letter;
            _letterText.text = _letter;
        }

        internal string GetLetter() => _letter;
        
        internal char GetChar() => _letter != "" ? _letter[0] : ' ';
        
        internal bool Empty() => _letter == "";
        
        /// <summary>
        /// Выделить пустую ячейку
        /// </summary>
        internal void HighlightCell()
        {
            ModeBlinkClear();
            SetHighlightState(HighlightState.SelectedCell);
        }
        
        /// <summary>
        /// Выделить букву
        /// </summary>
        internal void Highlight()
        {
            ModeBlinkClear();
            SetHighlightState(HighlightState.Highlighted);
        }
        
        /// <summary>
        /// Убрать выделение с буквы
        /// </summary>
        internal void UnHighlight()
        {
            ModeBlinkClear();
            SetHighlightState(HighlightState.None);
        }

        /// <summary>
        /// Отобразить или скрыть букву на поле
        /// </summary>
        /// <param name="value">bool</param>
        internal void ShowLetter(bool value)
        {
            ModeBlinkClear();
            if (value)
            {
                _letterText.text = _letter;
                SetHighlightState(_highlightState);
            }
            else
            {
                _letterText.text = "";
                ApplyBackgroundSkin(SkinSpriteKey.CellBackgroundDefaultAlias);
            }
        }
        
        private void SetHighlightState(HighlightState state)
        {
            _highlightState = state;
            SkinSpriteKey key = state switch
            {
                HighlightState.SelectedCell => SkinSpriteKey.CellSelectedAlias,
                HighlightState.Highlighted => SkinSpriteKey.LettersSelectedAlias,
                _ => Empty()
                    ? SkinSpriteKey.CellBackgroundDefaultAlias
                    : SkinSpriteKey.CellBackgroundFilledAlias
            };

            ApplyBackgroundSkin(key);
        }

        private void Blink()
        {
            _isIlluminated = !_isIlluminated;

            if (_isIlluminated)
            {
                ApplyBackgroundSkin(SkinSpriteKey.CellBackgroundFilledAlias);
                _audioService?.PlaySfxAsync(SoundsConfig.LetterUnblinking);
            }
            else
            {
                ++_blinkCounter;
                ApplyBackgroundSkin(SkinSpriteKey.CellSelectedAlias);
                _audioService?.PlaySfxAsync(SoundsConfig.LetterBlinking);
            }
        }

        private void OnRuntimeChanged(SkinRuntime runtime)
        {
            ApplyCurrentSkin();
        }

        private bool IsInputLocked()
        {
            return _wordsField != null && _wordsField.IsInputLocked;
        }

        private void ModeBlinkClear()
        {
            if (_bModeBlink)
            {
                _bModeBlink = false;
                _isIlluminated = false;
                _blinkDtTimer = 0;
                _blinkCounter = 0;
            }
        }
    }
}
