using System.Collections.Generic;
using Core.Events;
using Core.Generated;
using Core.Services;
using Core.UI;
using Cysharp.Threading.Tasks;
using TMPro;
using UI.Components;
using UI.Popups;
using UI.SkinBindings;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Screens
{
    public abstract class GameScreenBase : UIScreen
    {
        private const float WordInfoIconGap = 8f;

        [Space, Header("Game Screen UI Components")]
        [SerializeField] protected TextMeshProUGUI _statusText;
        [SerializeField] protected TextMeshProUGUI _wordText;
        [SerializeField] protected RectTransform _wordInfoIconRect;
        [SerializeField] protected Image _wordInfoIcon;
        [SerializeField] protected TimerProgressBar _progressBar;

        [SerializeField] protected Button _pauseButton;
        [SerializeField] protected Button _cancelButton;
        [SerializeField] protected Button _goButton;
        [SerializeField] protected Button _repeatGame;
        [SerializeField] protected Button _passButton;

        [SerializeField] protected PlayerPanel _playerPanelOwner;
        [SerializeField] protected PlayerPanel _playerPanelOpponent;
        [SerializeField] protected WordsField _wordsField;
        [SerializeField] protected LettersField _lettersField;
        [SerializeField] protected BoosterPanelIngameScreen _boosterPanel;
        [SerializeField] protected PauseButtonAnimator _pauseButtonAnimator;
        [SerializeField] protected StatisticsPanel _statisticsPanel;
        [SerializeField] protected KeyboardPanel _keyboardPanel;
        [SerializeField] protected FocusHoleOverlay _holeOverlay;
        [SerializeField] protected FloatingBubblePopup _eraseBubblePopup;
        [SerializeField] protected FloatingPausePopup _pausePopup;
        [SerializeField] private SkinBindingGroup _skinBindings;
        
        internal TimerProgressBar TimerBar => _progressBar;
        internal PlayerPanel PlayerPanelOwner => _playerPanelOwner;
        internal PlayerPanel PlayerPanelOpponent => _playerPanelOpponent;
        internal WordsField WordsField => _wordsField;
        internal StatisticsPanel StatisticsPanel => _statisticsPanel;
        internal KeyboardPanel KeyboardPanel => _keyboardPanel;
        internal FocusHoleOverlay HoleOverlay => _holeOverlay;
        internal FloatingBubblePopup EraseBubble => _eraseBubblePopup;
        internal BoosterPanelIngameScreen BoosterPanel => _boosterPanel;
        internal Button PauseButton => _pauseButton;
        internal Button PassButton => _passButton;
        internal GameObject CancelButton => _cancelButton.gameObject;
        internal GameObject GoButton => _goButton.gameObject;
        internal GameObject RepeatGame => _repeatGame.gameObject;

        [Inject] protected LocalizationService _localization;
        [Inject] protected IUIManager _ui;
        [Inject] protected ILoadingUI _loadingUI;
        [Inject] protected ISaveService _saveService;
        [Inject] protected InterstitialPolicyService _interstitialService;
        [Inject] protected IGamePauseService _pauseService;

        protected bool _isProcessing;
        protected bool _isPaused;

        private Button _wordInfoButton;
        private string _wordInfoWord;
        private bool _isWordInfoVisible;
        private float _wordInfoIconAnchoredY;
        private bool _wordInfoButtonInitialized;

        protected virtual void Start()
        {
            EnsureWordInfoButton();
            EventBus.Subscribe<GoToHomeEvent>(OnGoToHome);
            EventBus.Subscribe<GameEndEvent>(OnGameEnd);
        }

        protected virtual void OnDestroy()
        {
            if (_wordInfoButton)
                _wordInfoButton.onClick.RemoveListener(OnWordInfoPressed);

            EventBus.Unsubscribe<GoToHomeEvent>(OnGoToHome);
            EventBus.Unsubscribe<GameEndEvent>(OnGameEnd);
        }

        public void OnPressedHome() => EventBus.Raise(new GoToHomeEvent());
        public void OnPressedOptions() => OnPressedOptionsAsync();

        public void OnPressedPause()
        {
            OnPausePressed();
            _isPaused = !_isPaused;
            _pauseButtonAnimator.SetPaused(_isPaused);
            if (_isPaused) _pausePopup.ShowAsync().Forget(); 
            else _pausePopup.HideAsync().Forget();
            _pauseService.SetUserPause(!_pauseService.IsPaused);
        }
        
        public void OnPressedGo() => EventBus.Raise(new GameGoEvent());
        public void OnPressedRepeatGame() => EventBus.Raise(new RepeatGameEvent());
        public void OnPressedCancel() => EventBus.Raise(new GameCancelEvent());
        public void OnPressedSkip()
        {
            OnSkipPressed();
            EventBus.Raise(new GameSkipEvent());
        }
        
        public void OnOpenStatistic()
        {
            OnStatisticOpened();

            OpenStatisticAsync();
        }
        
        public async UniTask OpenStatisticAsync()
        {
            SetPause(true);
            
            _statisticsPanel.ShowAsync();
            await _statisticsPanel.WaitForResultAsync();
            
            SetPause(false);
        }

        public override async UniTask ShowAsync()
        {
            _isProcessing = true;
            await base.ShowAsync();
        }

        internal virtual void Reset()
        {
            SetStatusLocalizationKey("STATUS_LABEL_NEW_GAME");
            SetTextWord(string.Empty);
            _statisticsPanel.Reset();
            _playerPanelOwner.Reset();
            _playerPanelOpponent.Reset();
            _progressBar.ResetTimer();
            _repeatGame.gameObject.SetActive(false);
            _holeOverlay.gameObject.SetActive(false);
            _eraseBubblePopup.HideAsync().Forget();
            _pausePopup.HideAsync().Forget();

            if (_isPaused)
            {
                _isPaused = false;
                _pauseButtonAnimator.SetPaused(_isPaused);
                _pauseService.SetUserPause(_isPaused);
            }
        }
        
        internal virtual List<SelectableLetter> InitWordsField() => _wordsField.InitField();
        internal virtual void InitAlphabetField() => _lettersField.InitField();
        internal virtual void SetTextWord(string value)
        {
            _isWordInfoVisible = false;
            SetWordInfoClickEnabled(false);
            _wordText.text = value;
        }

        internal virtual void SetTextWordWithInfoIcon(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                SetTextWord(string.Empty);
                return;
            }

            _wordInfoWord = word;
            _isWordInfoVisible = true;
            _wordText.text = word;
            SetWordInfoClickEnabled(true);
            UpdateWordInfoIconPosition();
        }

        internal virtual string GetTextWord() => _wordText.text;

        internal virtual void AddLetterToWord(string letter)
        {
            if (_isWordInfoVisible)
                _wordText.text = string.Empty;

            _isWordInfoVisible = false;
            SetWordInfoClickEnabled(false);
            _wordText.text += letter;
        }

        internal virtual void SetStatusLocalizationKey(string localizationKey)
        {
            _statusText.text = _localization.Get(LocalizationConst.TableUI, localizationKey);
        }

        internal virtual void RemoveLastLetter()
        {
            if (_isWordInfoVisible)
            {
                SetTextWord(string.Empty);
                return;
            }

            SetWordInfoClickEnabled(false);

            if (!string.IsNullOrEmpty(_wordText.text))
                _wordText.text = _wordText.text[..^1];
        }

        protected virtual async void OnGoToHome(GoToHomeEvent eventData)
        {
            await GoToHome();
        }

        protected virtual async void OnPressedOptionsAsync()
        {
            SetPause(true);
            var popup = await _ui.ShowPopupAsync<OptionsPopup>(AssetKey.OptionsPopup);
            await popup.WaitForResultAsync();
            SetPause(false);
        }
        
        protected virtual void OnPausePressed() { }
        protected virtual void OnSkipPressed() { }
        protected virtual void OnStatisticOpened() { }

        protected async UniTask GoToHome(bool isSaveGame = false)
        {
            await _interstitialService.TryShowAndWaitAsync(AnalyticsEvents.Placement.ExitGame);
            await _loadingUI.ShowLoadingAsync<InGameLoadingScreen>(AssetKey.InGameLoadingScreen);

            if (isSaveGame)
                await _saveService.SaveAsync();

            Reset();

            await _ui.HideAllScreensAsync();
            await _ui.ShowScreenAsync<MainMenuScreen>(AssetKey.MainMenuScreen);

            _isProcessing = false;

            await _loadingUI.HideLoadingAsync();
        }
        
        
        
        protected void SetPause(bool isPaused)
        {
            if (isPaused == _isPaused || !_isProcessing)
                return;
            
            _isPaused = isPaused;
            _pauseButtonAnimator.SetPaused(_isPaused);
            if (_isPaused) _pausePopup.ShowAsync().Forget(); 
            else _pausePopup.HideAsync().Forget();
            _pauseService.SetUserPause(!_pauseService.IsPaused);
        }

        protected virtual void OnGameEnd(GameEndEvent eventData)
        {
            TimerBar.ResetTimer();
            SetStatusLocalizationKey("STATUS_LABEL_GAME_OVER");
            _isProcessing = false;
        }

        protected UniTask UpdateSkinAsync()
        {
            return _skinBindings.PrepareAsync();
        }

        protected virtual async UniTask PrepareCommonAsync()
        {
            Reset();
            await UpdateSkinAsync();
        }

        private void EnsureWordInfoButton()
        {
            if (_wordInfoButtonInitialized || !_wordText)
                return;

            _wordInfoButton = _wordText.GetComponent<Button>();

            if (!_wordInfoButton)
                _wordInfoButton = _wordText.gameObject.AddComponent<Button>();

            _wordInfoButton.transition = Selectable.Transition.None;
            _wordInfoButton.targetGraphic = _wordText;
            _wordInfoButton.onClick.RemoveListener(OnWordInfoPressed);
            _wordInfoButton.onClick.AddListener(OnWordInfoPressed);
            _wordInfoButton.interactable = false;
            _wordText.raycastTarget = false;

            if (_wordInfoIconRect)
            {
                _wordInfoIconAnchoredY = _wordInfoIconRect.anchoredPosition.y;
                _wordInfoIconRect.gameObject.SetActive(false);
            }

            _wordInfoButtonInitialized = true;
        }

        private void SetWordInfoClickEnabled(bool value)
        {
            EnsureWordInfoButton();

            if (!value)
                _wordInfoWord = null;

            if (_wordInfoButton)
                _wordInfoButton.interactable = value;

            if (_wordText)
                _wordText.raycastTarget = value;

            if (_wordInfoIconRect)
                _wordInfoIconRect.gameObject.SetActive(value);

            if (_wordInfoIcon)
                _wordInfoIcon.raycastTarget = value;
        }

        private void OnWordInfoPressed()
        {
            if (string.IsNullOrWhiteSpace(_wordInfoWord))
                return;

            SetPause(true);
            EventBus.Raise(new ShowWordInfoEvent { word = _wordInfoWord });
        }

        private void UpdateWordInfoIconPosition()
        {
            if (!_wordText || !_wordInfoIconRect)
                return;

            _wordText.ForceMeshUpdate();

            var textBounds = _wordText.textBounds;
            var iconRect = _wordInfoIconRect.rect;
            var pivotOffset = iconRect.width * _wordInfoIconRect.pivot.x;
            var iconX = textBounds.max.x + WordInfoIconGap + pivotOffset;

            _wordInfoIconRect.anchoredPosition = new Vector2(iconX, _wordInfoIconAnchoredY);
        }
    }
}
