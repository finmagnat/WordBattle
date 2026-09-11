using System;
using System.Collections.Generic;
using System.Threading;
using Core.Config;
using Core.Data;
using Core.DebugTools;
using Core.Services.DataDictionary;
using Core.Events;
using Core.Generated;
using Core.Services;
using Core.UI;
using Cysharp.Threading.Tasks;
using Game.AI;
using Core.Services.Inventory;
using UI.Popups;
using UI.Screens;
using UnityEngine;
using Zenject;

namespace Game.Logic
{
    /*
     * Controls overall game flow: board setup, player turn, opponent turn, and game over.
     */
    public class GameController : IDisposable, IGameBoosterHost
    {
        [Inject] private DictionaryService _dictionaryService;
        [Inject] private LocalizationService _localization;
        [Inject] private ConfigService _configService;
        [Inject] private IProfileService _profile;
        [Inject] private AudioService _audioService;
        [Inject] private IVibrationService _vibrationService;
        [Inject] private StarterBonusService _starterBonusService;
        [Inject] private IUIManager _ui;
        [Inject] private MissingWordPopupPresenter _missingWordPopupPresenter;
        [Inject] private ShowWordInfoPresenter _wordInfoPresenter;
        [Inject] private InterstitialPolicyService _interstitialService;
        [Inject] private ISaveService _saveService;
        [Inject] private DictionaryManager _dictionaryManager;
        [Inject] private GameBoosterController _boosterController;
        [Inject] private GameAnalyticsPayloadFactory _analyticsPayloadFactory;
        [Inject] private GameAnalyticsReporter _analyticsReporter;
        [Inject] private IInventoryService _inventory;
        [Inject] private InventorySyncService _inventorySync;

        private readonly SemaphoreSlim _blockUiLock = new(1, 1);

        private WordsFieldManager _wordsFieldManager = new();
        private AIGameController _ai = new();
        private GameScreenBase _gameScreen;
        private GameOpponent _gameOpponent;

        private bool _bStart; // Game has started.
        private bool _bPause; // Game is paused.
        private bool _bLetterPut; // The local player has placed a letter.
        private bool _bModePlayOwner = true; // It is the local player's turn.
        private bool _isSavingWord;
        private bool _ownerPassedLastTurn;

        private uint _maxPasses;
        private ComplexityAI _complexityAI;
        private ComplexityAISettings _complexityAISettings;
        private int _durationGame;
        private string _firstWord;
        private string _lastAcceptedWord;
        private SaveGameData _saveGameData;
        private bool _isSavedGame;
        private bool _timeExpiredStatus;
        private int _winsInSeries;

        public int RoundDurationSeconds => _durationGame;
        public string LocaleCode => _localization.CurrentLocale.Identifier.Code;

        public async UniTask InitializeAsync()
        {
            EventBus.Subscribe<GameScreenStartEvent>(OnGameScreenStart);
            EventBus.Subscribe<GameScreenReadyEvent>(OnGameScreenReady);

            EventBus.Subscribe<GamePauseChangedEvent>(OnGamePause);
            EventBus.Subscribe<GameGoEvent>(OnGameGo);
            EventBus.Subscribe<GameCancelEvent>(OnGameCancel);
            EventBus.Subscribe<GameSkipEvent>(OnGameSkip);
            EventBus.Subscribe<RepeatGameEvent>(OnRepeatGame);

            EventBus.Subscribe<CellSelectEvent>(OnCellSelect);
            EventBus.Subscribe<CellSelectCancelEvent>(OnCellSelectCancel);
            EventBus.Subscribe<CellSelectSuccessEvent>(OnCellSelectSuccess);
            EventBus.Subscribe<KeyboardLetterSelectEvent>(OnKeyboardLetterSelect);
            EventBus.Subscribe<LetterPutSuccessEvent>(OnLetterPutSuccess);
            EventBus.Subscribe<LetterPutToWordEvent>(OnLetterPutToWord);
            EventBus.Subscribe<LetterRemoveLastFromWordEvent>(OnLetterRemoveLastFromWord);
            EventBus.Subscribe<PlayerErrorEvent>(OnErrorPlayer);
            EventBus.Subscribe<ModeBlinkEndEvent>(OnModeBlinkEnd);

            EventBus.Subscribe<TimeExpiredEvent>(OnTimeExpired);

            EventBus.Subscribe<OpponentFindWordEvent>(OnOpponentFindWordSuccess);
            EventBus.Subscribe<OpponentFindWordFailEvent>(OnOpponentFindWordFail);

            EventBus.Subscribe<UseBoosterEvent>(OnActivateBooster);
            EventBus.Subscribe<PurchaseSuccessEvent>(OnPurchaseSuccessEvent);
            EventBus.Subscribe<ShowWordInfoEvent>(OnShowWordInfoEvent);

            _wordsFieldManager.Initialize();
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<GameScreenStartEvent>(OnGameScreenStart);
            EventBus.Unsubscribe<GameScreenReadyEvent>(OnGameScreenReady);

            EventBus.Unsubscribe<GamePauseChangedEvent>(OnGamePause);
            EventBus.Unsubscribe<GameGoEvent>(OnGameGo);
            EventBus.Unsubscribe<GameCancelEvent>(OnGameCancel);
            EventBus.Unsubscribe<GameSkipEvent>(OnGameSkip);
            EventBus.Unsubscribe<RepeatGameEvent>(OnRepeatGame);

            EventBus.Unsubscribe<CellSelectEvent>(OnCellSelect);
            EventBus.Unsubscribe<CellSelectCancelEvent>(OnCellSelectCancel);
            EventBus.Unsubscribe<CellSelectSuccessEvent>(OnCellSelectSuccess);
            EventBus.Unsubscribe<KeyboardLetterSelectEvent>(OnKeyboardLetterSelect);
            EventBus.Unsubscribe<LetterPutSuccessEvent>(OnLetterPutSuccess);
            EventBus.Unsubscribe<LetterPutToWordEvent>(OnLetterPutToWord);
            EventBus.Unsubscribe<LetterRemoveLastFromWordEvent>(OnLetterRemoveLastFromWord);
            EventBus.Unsubscribe<PlayerErrorEvent>(OnErrorPlayer);
            EventBus.Unsubscribe<ModeBlinkEndEvent>(OnModeBlinkEnd);

            EventBus.Unsubscribe<TimeExpiredEvent>(OnTimeExpired);

            EventBus.Unsubscribe<OpponentFindWordEvent>(OnOpponentFindWordSuccess);
            EventBus.Unsubscribe<OpponentFindWordFailEvent>(OnOpponentFindWordFail);

            EventBus.Unsubscribe<UseBoosterEvent>(OnActivateBooster);
            EventBus.Unsubscribe<PurchaseSuccessEvent>(OnPurchaseSuccessEvent);
            EventBus.Unsubscribe<ShowWordInfoEvent>(OnShowWordInfoEvent);

            _wordsFieldManager.Destroy();
        }

        public SaveGameData GetGameData()
        {
            if (_bLetterPut)
                Cancel();

            var boardRows = _wordsFieldManager.WordsFieldData.GetBoardData();

            var data = new SaveGameData
            {
                version = _configService.Game.version,
                localeCode = _localization.CurrentLocale.Identifier.Code,
                savedAtUtcTicks = DateTime.UtcNow.Ticks,

                mode = _gameOpponent.ToString(),
                boardSize = (uint)(boardRows?.Length ?? 0),
                boardRows = boardRows,
                levelComplexityAI = (int)_complexityAI,
                playerTurn = _bModePlayOwner,
                maxSeconds = _durationGame,
                currentSeconds = (int)_gameScreen.TimerBar.GetCurrentValue(),

                playerScore = _gameScreen.PlayerPanelOwner.Score,
                opponentScore = _gameScreen.PlayerPanelOpponent.Score,
                playerPasses = _gameScreen.PlayerPanelOwner.Pass,
                opponentPasses = _gameScreen.PlayerPanelOpponent.Pass,

                firstWord = _firstWord,
                playerWords = _gameScreen.StatisticsPanel.StatisticPlayerPlayerPanelOwner.Words,
                opponentWords = _gameScreen.StatisticsPanel.StatisticPlayerPlayerPanelOpponent.Words
            };

            return data;
        }

        public void SetGameData(SaveGameData data)
        {
            _saveGameData = data;
        }

        private void OnGameScreenStart(GameScreenStartEvent eventData)
        {
            bool isSavedGame = _saveGameData != null;

            _gameScreen = eventData.Screen;
            _gameOpponent = eventData.Opponent;
            _boosterController.Attach(_gameScreen, _wordsFieldManager, _ai);
            _boosterController.ResetForNewGame();
            _gameScreen.BoosterPanel.Refresh();
            _wordsFieldManager.Reset();

            _gameScreen.GoButton.SetActive(false);
            _gameScreen.CancelButton.SetActive(false);
            _gameScreen.PauseButton.interactable = true;
            _gameScreen.PassButton.interactable = true;

            _bPause = false;
            _ownerPassedLastTurn = false;
            _lastAcceptedWord = null;

            _gameScreen.PlayerPanelOwner.SetPlayerName(_localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyNamePlayerOwner));

            switch (_gameOpponent)
            {
                case GameOpponent.AI:
                    _gameScreen.PlayerPanelOpponent.SetPlayerName(_localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyNamePlayerAI));

                    _complexityAI = _saveGameData != null ?
                        (ComplexityAI)_saveGameData.levelComplexityAI :
                        (ComplexityAI)PlayerPrefs.GetInt(PlayerPrefsKey.ComplexityAI);

                    _complexityAISettings = _configService.Game.GetComplexityAIItem(_complexityAI);
                    _maxPasses = _complexityAISettings.MaxPasses;

                    if (!isSavedGame)
                        _dictionaryService.ShuffleWordsForGameSession();

                    _ai.Init(_wordsFieldManager, _dictionaryService);

                    _bModePlayOwner = true;
                    break;
                case GameOpponent.Friend:
                    _gameScreen.PlayerPanelOpponent.SetPlayerName(_localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyNamePlayerOpponent));
                    _maxPasses = _configService.Game.maxPassesByDefault;
                    _bModePlayOwner = true;
                    break;
            }

            var wordsFieldItems = _gameScreen.InitWordsField();
            _wordsFieldManager.SetWordsFieldData(wordsFieldItems);

            _gameScreen.InitAlphabetField();
            if (_gameScreen.KeyboardPanel.IsVisible)
                _gameScreen.KeyboardPanel.HideAsync().Forget();

            SaveGameData savedGameData = _saveGameData;

            if (savedGameData != null)
            {
                _firstWord = savedGameData.firstWord;
                _wordsFieldManager.WordsFieldData.SetSaveGameData(savedGameData);

                _gameScreen.StatisticsPanel.SetStartWord(_firstWord);
                _gameScreen.StatisticsPanel.StatisticPlayerPlayerPanelOwner.AddWords(savedGameData.playerWords);
                _gameScreen.PlayerPanelOwner.SetData(savedGameData.playerScore, savedGameData.playerPasses, _maxPasses);

                _gameScreen.StatisticsPanel.StatisticPlayerPlayerPanelOpponent.AddWords(savedGameData.opponentWords);
                _gameScreen.PlayerPanelOpponent.SetData(savedGameData.opponentScore, savedGameData.opponentPasses, _maxPasses);

                _durationGame = GameDurationSettings.ClampDurationGameSeconds(savedGameData.maxSeconds);
                _gameScreen.TimerBar.SetTargetValue(_durationGame);
                _gameScreen.TimerBar.SetCurrentValue(savedGameData.currentSeconds);
                _isSavedGame = true;
            }
            else
            {
                _firstWord = _dictionaryService.GetRandomWord(_configService.Game.defaultBoardSize);
                _wordsFieldManager.SetFirstWord(_firstWord);

                _gameScreen.StatisticsPanel.SetStartWord(_firstWord);
                _gameScreen.PlayerPanelOwner.SetPass(0, _maxPasses);
                _gameScreen.PlayerPanelOpponent.SetPass(0, _maxPasses);

                _durationGame = GameDurationSettings.GetDurationGameSeconds(_configService.Game);
                _gameScreen.TimerBar.SetTargetValue(_durationGame);
            }

            if (_bModePlayOwner)
                _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_GO_OWNER");
            else
                _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_GO_OPPONENT");

            _saveGameData = null;
            _bStart = true;

            if (_gameOpponent == GameOpponent.AI)
            {
                if (isSavedGame)
                    TrackAiSavedGameStarted(savedGameData);
                else
                    TrackAiGameStarted();
            }

            if (eventData.AutoStart)
                OnGameScreenReady(null);
        }

        private void OnGameScreenReady(GameScreenReadyEvent eventData)
        {
            if (_bStart)
            {
                _gameScreen.TimerBar.StartTimer();
                _audioService?.PlaySfxAsync(SoundsConfig.StartNewGame);
            }
        }

        private void OnGamePause(GamePauseChangedEvent eventData)
        {
            if (!_bStart || !_bModePlayOwner)
                return;

            PauseCooldownAsync().Forget();

            _bPause = eventData.IsPaused;

            if (_bPause)
            {
                _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_PAUSE");
                _gameScreen.TimerBar.StopTimer();
                if (_gameScreen.KeyboardPanel.IsVisible)
                    _gameScreen.KeyboardPanel.HideAsync().Forget();
            }
            else
            {
                if (!_boosterController.IsSlowdownActive)
                    _gameScreen.TimerBar.StartTimer();
                _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_GO_OWNER");
                if (_wordsFieldManager.WordsFieldData.SelectedItem != null)
                    _gameScreen.KeyboardPanel.ShowAsync().Forget();
            }

            _wordsFieldManager.ShowLetters(!_bPause);
            _vibrationService.Play();
        }

        private async UniTaskVoid PauseCooldownAsync()
        {
            _gameScreen.PauseButton.interactable = false;
            await UniTask.WaitForSeconds(_configService.Game.pauseCooldownSeconds);
            _gameScreen.PauseButton.interactable = true;
        }

        private async void OnGameGo(IGameEvent eventData)
        {
            if (!_bStart || _bPause || !_bModePlayOwner || _isSavingWord)
                return;

            if (!_bLetterPut)
            {
                EventBus.Raise(new PlayerErrorEvent { GameError = GameError.NoLetterInstalled });
                return;
            }

            string word = _gameScreen.GetTextWord();
            if (_wordsFieldManager.CheckWord(word))
            {
                if (!_dictionaryService.Contains(word))
                {
                    _audioService?.PlaySfxAsync(SoundsConfig.PopupQuestion);

                    await _missingWordPopupPresenter.ShowAsync(
                        word,
                        _dictionaryService.DictionaryConfig.languageCode);

                    Cancel();

                    if (!string.IsNullOrEmpty(_lastAcceptedWord))
                        _gameScreen.SetTextWordWithInfoIcon(_lastAcceptedWord);
                }
                else
                {
                    _analyticsReporter.TrackApplyWordClicked(word);
                    _audioService?.PlaySfxAsync(SoundsConfig.IMadeMove);
                    await SaveWordAndContinueGameAsync(word);
                }
            }
        }

        private void OnGameCancel(IGameEvent eventData)
        {
            if (!_bStart || _bPause || !_bLetterPut || !_bModePlayOwner || _isSavingWord)
                return;

            _analyticsReporter.TrackCancelClicked();
            Cancel();
        }

        private void Cancel()
        {
            _bLetterPut = false;
            _wordsFieldManager.Cancel();
            _gameScreen.SetTextWord("");
            _gameScreen.GoButton.SetActive(false);
            _gameScreen.CancelButton.SetActive(false);
        }

        internal void OnCellSelect(CellSelectEvent eventData)
        {
            if (!_bStart || _bPause || !_bModePlayOwner || _isSavingWord)
                return;

            _wordsFieldManager.TryCellSelect(eventData);
        }

        internal void OnCellSelectCancel(CellSelectCancelEvent eventData)
        {
            if (!_bStart || _bPause || !_bModePlayOwner)
                return;

            _analyticsReporter.TrackCellUnselected(eventData.index);
            _audioService?.PlaySfxAsync(SoundsConfig.ButtonClick);

            if (!eventData.keepKeyboardOpen)
                _gameScreen.KeyboardPanel.HideAsync().Forget();
        }

        private void OnCellSelectSuccess(CellSelectSuccessEvent eventData)
        {
            if (!_bStart || _bPause || !_bModePlayOwner)
                return;

            _audioService?.PlaySfxAsync(SoundsConfig.ButtonClick);

            if (eventData.isEraserSuccess)
            {
                _boosterController.OnCellSelectSuccess(eventData);

                if (!_gameScreen.KeyboardPanel.IsVisible)
                    _gameScreen.KeyboardPanel.ShowAsync().Forget();

                return;
            }

            if (eventData.isSwapSuccess)
            {
                _boosterController.OnCellSelectSuccess(eventData);
                return;
            }

            _analyticsReporter.TrackCellSelected(eventData.letter.Index);
            _boosterController.OnCellSelectSuccess(eventData);

            if (!_gameScreen.KeyboardPanel.IsVisible)
                _gameScreen.KeyboardPanel.ShowAsync().Forget();
        }

        private void OnLetterPutSuccess(LetterPutSuccessEvent eventData)
        {
            _analyticsReporter.TrackLetterPutSuccess(eventData.letter, eventData.index);
            _bLetterPut = true;
            _gameScreen.GoButton.SetActive(true);
            _gameScreen.CancelButton.SetActive(true);
            _audioService?.PlaySfxAsync(SoundsConfig.LetterPutSuccess);
            _vibrationService.Play(VibrationType.Heavy);
        }

        private void OnKeyboardLetterSelect(KeyboardLetterSelectEvent eventData)
        {
            if (!_bStart || _bPause || !_bModePlayOwner)
                return;

            _vibrationService.Play(VibrationType.Selection);
            _analyticsReporter.TrackKeyboardLetterClicked(eventData.letter);
        }

        private void OnLetterPutToWord(LetterPutToWordEvent eventData)
        {
            _gameScreen.AddLetterToWord(eventData.letter);
            _audioService?.PlaySfxAsync(SoundsConfig.LetterSelected);
        }

        private void OnLetterRemoveLastFromWord(LetterRemoveLastFromWordEvent eventData)
        {
            _gameScreen.RemoveLastLetter();
        }

        private async void OnErrorPlayer(PlayerErrorEvent eventData)
        {
            Debug.Log("[GameController] [OnErrorPlayer] " + eventData.GameError);

            var messageBoxData = new MessageBoxData
            {
                Error = eventData.GameError
            };

            var popup = await _ui.ShowPopupAsync<AdvicePopup, MessageBoxData>(AssetKey.AdvicePopup, messageBoxData);

            _audioService?.PlaySfxAsync(SoundsConfig.PopupWarning);
            _vibrationService.Play(VibrationType.Warning);
            
            await popup.WaitForResultAsync();

            switch (eventData.GameError)
            {
                case GameError.SetLetterNoSelected:
                    BlockUIAsync(true, BlockUIScreenMode.NoSpinner).Forget();
                    _gameScreen.CancelButton.SetActive(false);
                    _gameScreen.GoButton.SetActive(false);
                    _wordsFieldManager.BlinkNoSelectedLetter();
                    break;
                case GameError.WordAlreadyBeen:
                    Cancel();
                    break;
            }
        }

        private void OnModeBlinkEnd(ModeBlinkEndEvent eventData)
        {
            Cancel();
            BlockUIAsync(false).Forget();
        }

        private async UniTask SaveWordAndContinueGameAsync(string word)
        {
            if (_isSavingWord)
                return;

            _isSavingWord = true;

            try
            {
                _boosterController.StopSlowdown();

                _gameScreen.TimerBar.ResetTimer();
                if (_bModePlayOwner)
                {
                    _gameScreen.StatisticsPanel.StatisticPlayerPlayerPanelOwner.AddWord(word);
                    _gameScreen.PlayerPanelOwner.SetScore(_gameScreen.PlayerPanelOwner.Score + (uint)word.Length);
                }
                else
                {
                    _gameScreen.StatisticsPanel.StatisticPlayerPlayerPanelOpponent.AddWord(word);
                    _gameScreen.PlayerPanelOpponent.SetScore(_gameScreen.PlayerPanelOpponent.Score + (uint)word.Length);
                }

                _gameScreen.GoButton.SetActive(false);
                _gameScreen.CancelButton.SetActive(false);
                _gameScreen.PauseButton.interactable = false;
                _gameScreen.PassButton.interactable = false;
                _lastAcceptedWord = word;
                _gameScreen.SetTextWordWithInfoIcon(word);

                var selectedIndexes = _wordsFieldManager.WordsFieldData.GetSelectedIndexesSnapshot();
                var wordsField = _gameScreen.WordsField;
                wordsField.SetInputLocked(true);
                try
                {
                    await wordsField.PlayAcceptedWordScaleAnimationAsync(selectedIndexes);
                }
                finally
                {
                    if (wordsField)
                        wordsField.SetInputLocked(false);
                }

                _wordsFieldManager.SaveWord(word);
                _wordsFieldManager.Clear();

                _bLetterPut = false;

                CheckFinishGame();
            }
            finally
            {
                _isSavingWord = false;
            }
        }

        private void OnGameSkip(IGameEvent eventData)
        {
            if (!_bStart || _bPause || !_bModePlayOwner || _isSavingWord)
                return;

            if (_gameOpponent == GameOpponent.AI)
                _analyticsReporter.TrackPassGameClicked(GetGameSnapshotParams());

            TryConfirmPass().Forget();
        }

        private async UniTask TryConfirmPass()
        {
            if (PlayerPrefs.GetInt(PlayerPrefsKey.ConfirmPassDontShowAgainKey, 0) == 0)
            {
                var popup = await _ui.ShowPopupAsync<ConfirmPassPopup, MessageBoxData>(AssetKey.ConfirmPassPopup, null);

                _audioService?.PlaySfxAsync(SoundsConfig.PopupQuestion);

                var exitData = await popup.WaitForResultAsync();

                if (exitData.Result == PopupResult.Exit)
                    return;
            }

            _boosterController.StopSlowdown();
            PassedGame();
        }

        private void OnRepeatGame(RepeatGameEvent eventData)
        {
            _analyticsReporter.TrackReplayClicked();
            _gameScreen.RepeatGame.SetActive(false);
            RepeatGame().Forget();
        }

        private async UniTask RepeatGame()
        {
            await _interstitialService.TryShowAndWaitAsync(AnalyticsEvents.Placement.RepeatGame);
            await _dictionaryManager.EnsureCurrentLocaleLoadedAsync();

            _gameScreen.Reset();

            EventBus.Raise(new GameScreenStartEvent { Screen = _gameScreen, Opponent = _gameOpponent, AutoStart = true });
        }

        private void OnTimeExpired(IGameEvent eventData)
        {
            if (_gameOpponent == GameOpponent.AI)
                _analyticsReporter.TrackTimeExpired(GetGameSnapshotParams(), _bModePlayOwner);

            if (_bModePlayOwner)
                _timeExpiredStatus = true;

            _ai.AbortSearch();
            _boosterController.CancelEraserMode();
            _boosterController.CancelSwapMode();

            _gameScreen.StatisticsPanel.HideAsync().Forget();
            _gameScreen.KeyboardPanel.HideAsync().Forget();

            PassedGame();
        }

        private void PassedGame()
        {
            _gameScreen.TimerBar.ResetTimer();

            if (_bLetterPut)
                Cancel();

            if (_bModePlayOwner)
                _gameScreen.PlayerPanelOwner.SetPass(_gameScreen.PlayerPanelOwner.Pass + 1, _maxPasses);
            else
                _gameScreen.PlayerPanelOpponent.SetPass(_gameScreen.PlayerPanelOpponent.Pass + 1, _maxPasses);

            _audioService?.PlaySfxAsync(SoundsConfig.Pass);
            _vibrationService.Play(VibrationType.Error);

            CheckFinishGame(wasPass: true);
        }

        private void OnOpponentFindWordSuccess(OpponentFindWordEvent eventData)
        {
            _gameScreen.TimerBar.StopTimer();
            SaveWordAndContinueGameAsync(eventData.word).Forget();

            _audioService?.PlaySfxAsync(SoundsConfig.OpponentMadeMove);
        }

        private void OnOpponentFindWordFail(IGameEvent eventData)
        {
            _gameScreen.PlayerPanelOpponent.SetPass(_gameScreen.PlayerPanelOpponent.Pass + 1, _maxPasses);
            _audioService?.PlaySfxAsync(SoundsConfig.OpponentFindWordFail);
            CheckFinishGame(wasPass: true);
        }

        private void CheckFinishGame(bool wasPass = false)
        {
            bool shouldAutoMix = !_bModePlayOwner && wasPass && _ownerPassedLastTurn;
            _ownerPassedLastTurn = _bModePlayOwner && wasPass;

            if (GameDebug.IsAutoWin)
            {
                FinishGame();
                return;
            }

            if (MaxPassesReached(_gameScreen.PlayerPanelOwner.Pass) ||
                MaxPassesReached(_gameScreen.PlayerPanelOpponent.Pass) ||
                _wordsFieldManager.Filled())
            {
                FinishGame();
            }
            else
            {
                if (shouldAutoMix)
                {
                    Cancel();
                    // Reuse the mixer algorithm without spending or refunding a booster.
                    if (_wordsFieldManager.MixLetters(_dictionaryService.DictionaryConfig) != null)
                        _audioService?.PlaySfxAsync(SoundsConfig.BoosterSlowdownLaunch);
                }

                SwitchPlayer();
            }
        }

        private async UniTaskVoid SwitchPlayer()
        {
            _bModePlayOwner = !_bModePlayOwner;
            _gameScreen.TimerBar.SetTargetValue(_durationGame);
            _gameScreen.TimerBar.StartTimer();
            _vibrationService.Play(VibrationType.Medium);
            
            if (_bModePlayOwner)
            {
                BlockUIAsync(false).Forget();
                _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_GO_OWNER");
                _gameScreen.PauseButton.interactable = true;
                _gameScreen.PassButton.interactable = true;
            }
            else
            {
                _wordsFieldManager.SetModeSelect(false);
                _gameScreen.PauseButton.interactable = false;
                _gameScreen.PassButton.interactable = false;
                _gameScreen.CancelButton.SetActive(false);
                _gameScreen.GoButton.SetActive(false);
                _boosterController.ResetForOpponentTurn();
                
                if (_timeExpiredStatus)
                {
                    _timeExpiredStatus = false;
                    _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_TIME_EXPIRED_GO_OPPONENT");
                    await UniTask.WaitForSeconds(_configService.Game.delayAIPlayAfterPlayerPassSeconds);
                }
                else
                {
                    _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_GO_OPPONENT");
                }
                
                switch (_gameOpponent)
                {
                    case GameOpponent.AI:
                        AIPlayAsync().Forget();
                        break;
                }
            }
        }

        private void FinishGame()
        {
            _bStart = false;
            _bPause = false;
            _bLetterPut = false;
            _gameScreen.PauseButton.interactable = false;
            _gameScreen.PassButton.interactable = false;
            _gameScreen.CancelButton.SetActive(false);
            _gameScreen.GoButton.SetActive(false);

            ResultGame resultGame = ResultGame.Draw;
            bool bResultDetermined = false;

            if (MaxPassesReached(_gameScreen.PlayerPanelOwner.Pass))
            {
                bResultDetermined = true;
                resultGame = ResultGame.OwnerLose;
            }
            else if (MaxPassesReached(_gameScreen.PlayerPanelOpponent.Pass))
            {
                bResultDetermined = true;
                resultGame = ResultGame.OwnerWin;
            }

            if (!bResultDetermined)
            {
                if (_gameScreen.PlayerPanelOwner.Score > _gameScreen.PlayerPanelOpponent.Score)
                    resultGame = ResultGame.OwnerWin;
                else if (_gameScreen.PlayerPanelOwner.Score < _gameScreen.PlayerPanelOpponent.Score)
                    resultGame = ResultGame.OwnerLose;
            }

            var analyticsSnapshot = GetGameSnapshotParams();
            bool wasSavedGame = _isSavedGame;

            EventBus.Raise(new GameEndEvent());

            _boosterController.OnGameFinished();

            if (wasSavedGame)
            {
                _saveService.ClearAsync().Forget();
                _isSavedGame = false;
            }

            _analyticsReporter.TrackFinishGame(analyticsSnapshot, resultGame, wasSavedGame);

            ShowFinishGamePopup(resultGame).Forget();
        }
        
        private bool MaxPassesReached(uint currentPass) => 
            _maxPasses > 0 ? currentPass >= _maxPasses : false;

        protected async UniTaskVoid ShowFinishGamePopup(ResultGame resultGame)
        {
            _profile.AddScoreAsync((int)_gameScreen.PlayerPanelOwner.Score);

            await _ui.HideAllPopupsAsync();

            var data = new FinishGamePopupData(
                _gameScreen.PlayerPanelOwner.PlayerName,
                _gameScreen.PlayerPanelOpponent.PlayerName,
                _gameScreen.PlayerPanelOwner.Score,
                _gameScreen.PlayerPanelOpponent.Score,
                _gameScreen.PlayerPanelOwner.Pass,
                _gameScreen.PlayerPanelOpponent.Pass,
                _maxPasses,
                resultGame switch
                {
                    ResultGame.OwnerWin => AnalyticsEvents.Option.Win,
                    ResultGame.OwnerLose => AnalyticsEvents.Option.Lose,
                    _ => AnalyticsEvents.Option.Draft
                },
                await GetWinRewardAsync(resultGame)
            );

            FinishGamePopup finishPopup;
            switch (resultGame)
            {
                case ResultGame.OwnerWin:
                    finishPopup = await _ui.ShowPopupAsync<WinPopup, FinishGamePopupData>(AssetKey.WinPopup, data);
                    _audioService?.PlaySfxAsync(SoundsConfig.IWon);
                    break;

                case ResultGame.OwnerLose:
                    finishPopup = await _ui.ShowPopupAsync<FinishGamePopup, FinishGamePopupData>(AssetKey.LosePopup, data);
                    _audioService?.PlaySfxAsync(SoundsConfig.OpponentWon);
                    break;

                default:
                    finishPopup = await _ui.ShowPopupAsync<FinishGamePopup, FinishGamePopupData>(AssetKey.DrawPopup, data);
                    _audioService?.PlaySfxAsync(SoundsConfig.Draw);
                    break;
            }

            _vibrationService.Play(VibrationType.Success);
            
            await finishPopup.WaitForResultAsync();

            if (await _starterBonusService.TryGrantAsync())
            {
                _gameScreen.BoosterPanel.Refresh();

                var starterBonusPopup =
                    await _ui.ShowPopupAsync<StarterBonusPopup>(AssetKey.StarterBonusPopup);
                await starterBonusPopup.WaitForResultAsync();
            }

            _gameScreen.RepeatGame.SetActive(true);
        }
        
        private async UniTask<FinishRewardData> GetWinRewardAsync(ResultGame resultGame)
        {
            List<RewardDto> reward = null;
                
            if(resultGame == ResultGame.OwnerWin)
            {
                if (++_winsInSeries >= _configService.Game.WinsInSeries)
                {
                    var configuredReward = _configService.Game.GetWinReward();
                    var grantedReward = new List<RewardDto>();

                    foreach (var item in configuredReward)
                    {
                        bool granted = await _inventorySync.GrantBoosterAsync(item.ItemId, item.Amount);
                        if (granted)
                        {
                            grantedReward.Add(item);
                            continue;
                        }

                        Debug.LogWarning($"Failed to grant win-series reward: {item.ItemId} x{item.Amount}");
                    }

                    if (grantedReward.Count > 0)
                    {
                        reward = grantedReward;
                        _gameScreen.BoosterPanel.Refresh();
                        _winsInSeries = 0;
                    }
                    else
                    {
                        _winsInSeries = Math.Max(0, _configService.Game.WinsInSeries - 1);
                    }

                }
            }
            else
            {
                _winsInSeries = 0;
            }
            
            var data = new FinishRewardData(
                _winsInSeries,
                _configService.Game.WinsInSeries,
                reward
            );
            
            return data;
        }

        private async UniTaskVoid AIPlayAsync()
        {
            await UniTask.WaitForSeconds(_configService.Game.delayAIPlaySeconds);
            _analyticsReporter.TrackAiMoveStart(GetGameSnapshotParams());

            var res = await _ai.FindWordAsync(_complexityAISettings);

            if (res.Success)
            {
                _analyticsReporter.TrackAiMoveSuccess(GetGameSnapshotParams(), res.Word);
                EventBus.Raise(new OpponentFindWordEvent { word = res.Word });
            }
            else
            {
                _analyticsReporter.TrackAiMoveFail(GetGameSnapshotParams());
                EventBus.Raise(new OpponentFindWordFailEvent());
            }
        }

        private void OnPurchaseSuccessEvent(PurchaseSuccessEvent eventData)
        {
            if (_gameScreen)
                _gameScreen.BoosterPanel.Refresh();
        }

        private async void OnShowWordInfoEvent(ShowWordInfoEvent eventData)
        {
            _analyticsReporter.TrackWordInfoClicked(eventData.word);
            await _wordInfoPresenter.ShowAsync(
                eventData.word,
                _dictionaryService.DictionaryConfig.languageCode);
        }

        private async void OnActivateBooster(UseBoosterEvent eventData)
        {
            if (_isSavingWord)
                return;

            if (_gameOpponent == GameOpponent.AI)
                _analyticsReporter.TrackBoosterGameClicked(GetGameSnapshotParams(), eventData.boosterType);

            await _boosterController.HandleUseAsync(eventData, this);
        }

        private void TrackAiGameStarted()
        {
            _analyticsReporter.TrackAiGameStarted(
                _complexityAI,
                _durationGame,
                _inventory.Boosters,
                _localization.CurrentLocale.Identifier.Code,
                _firstWord);
        }

        private void TrackAiSavedGameStarted(SaveGameData savedGameData)
        {
            if (savedGameData == null)
                return;

            _analyticsReporter.TrackAiSavedGameStarted(savedGameData, _maxPasses, _inventory.Boosters);
        }

        private Dictionary<string, object> GetGameSnapshotParams()
        {
            return _analyticsPayloadFactory.CreateGameSnapshotPayload(
                _localization.CurrentLocale.Identifier.Code,
                _wordsFieldManager.WordsFieldData.GetBoardData(),
                _complexityAI,
                _durationGame,
                _gameScreen.TimerBar.GetCurrentValue(),
                _gameScreen.PlayerPanelOwner.Score,
                _gameScreen.PlayerPanelOpponent.Score,
                _gameScreen.PlayerPanelOwner.Pass,
                _gameScreen.PlayerPanelOpponent.Pass,
                _maxPasses,
                _inventory.Boosters);
        }

        internal async UniTask BlockUIAsync(bool isBlocked, BlockUIScreenMode mode = BlockUIScreenMode.Default)
        {
            await _blockUiLock.WaitAsync();
            try
            {
                if (isBlocked)
                    await _ui.ShowBlockUIScreenAsync(AssetKey.BlockUIScreen, mode);
                else
                    await _ui.HideScreenAsync<BlockUIScreen>(AssetKey.BlockUIScreen);
            }
            finally
            {
                _blockUiLock.Release();
            }
        }

        /// IGameBoosterHost 
        bool IGameBoosterHost.IsGameStarted => _bStart;
        bool IGameBoosterHost.IsPaused => _bPause;
        bool IGameBoosterHost.IsOwnerTurn => _bModePlayOwner;

        UniTask IGameBoosterHost.BlockUIAsync(bool isBlocked, BlockUIScreenMode mode)
        {
            return BlockUIAsync(isBlocked, mode);
        }

        void IGameBoosterHost.CancelCurrentMove()
        {
            Cancel();
        }

        UniTask IGameBoosterHost.SaveWordAndContinueGameAsync(string word)
        {
            return SaveWordAndContinueGameAsync(word);
        }

        void IGameBoosterHost.MarkLetterPlacedByBooster()
        {
            _bLetterPut = true;
        }
    }
}
