using System;
using Core.Config;
using Core.Data;
using Core.Services.DataDictionary;
using Core.Events;
using Core.Generated;
using Core.UI;
using Cysharp.Threading.Tasks;
using Game.AI;
using Core.Services.Inventory;
using System.Threading;
using Core.Services;
using Core.Services.Common;
using Game.Logic.Mixer;
using UI.Popups;
using UI.Screens;
using UI.Components;
using UnityEngine;
using Zenject;

namespace Game.Logic
{
    public interface IGameBoosterHost
    {
        bool IsGameStarted { get; }
        bool IsPaused { get; }
        bool IsOwnerTurn { get; }
        int RoundDurationSeconds { get; }
        string LocaleCode { get; }

        UniTask BlockUIAsync(bool isBlocked, BlockUIScreenMode mode = BlockUIScreenMode.Default);
        void CancelCurrentMove();
        UniTask SaveWordAndContinueGameAsync(string word);
        void MarkLetterPlacedByBooster();
        void SaveRecoveryCheckpoint();
    }

    public sealed class GameBoosterController
    {
        private const string MixerFailNoValidPattern = "no_valid_pattern";
        // Temporary experiment. Set to false to restore the original Letter booster.
        private static readonly bool UseCluePrototype = true;

        [Inject] private InventorySyncService _inventorySync;
        [Inject] private ConfigService _configService;
        [Inject] private DictionaryService _dictionaryService;
        [Inject] private AudioService _audioService;
        [Inject] private IUIManager _ui;
        [Inject] private IGamePauseService _pauseService;
        [Inject] private BoosterAnalyticsReporter _analyticsReporter;
        [Inject] private LocalizationService _localization;

        private WordsFieldManager _wordsFieldManager;
        private AIGameController _ai;
        private GameScreenBase _gameScreen;
        private IGameBoosterHost _activeEraserHost;
        private IGameBoosterHost _activeSwapHost;
        private IGameBoosterHost _activeSlowdownHost;
        private CancellationTokenSource _slowdownCts;
        private float _slowdownEndsAt;
        private float _restoredSlowdownSeconds;

        private bool _bModeEraser;
        private bool _bModeSwap;
        private bool _bLetterRemoved;
        private SelectableLetter _erasedCell;
        private string _erasedLetter;
        private bool _boosterProcessing;
        private bool _shopOpen;

        public bool IsSlowdownActive => _gameScreen != null && _gameScreen.BoosterPanel.IsActive(BoosterType.Slowdown);

        internal void CaptureSaveState(SaveGameData data)
        {
            data.eraserUsedThisTurn = _bLetterRemoved;
            data.erasedCellIndex = _erasedCell != null ? _erasedCell.Index : 0;
            data.erasedLetter = _erasedLetter;
            data.eraserSelectionPending = _bModeEraser;
            data.swapSelectionPending = _bModeSwap;
            data.slowdownRemainingSeconds = IsSlowdownActive ? Mathf.Max(0, _slowdownEndsAt - Time.time) : _restoredSlowdownSeconds;
        }

        internal void RestoreSaveState(SaveGameData data, IGameBoosterHost host)
        {
            _bLetterRemoved = data.eraserUsedThisTurn;
            if (_bLetterRemoved)
            {
                _erasedCell = _wordsFieldManager.WordsFieldData.Items[data.erasedCellIndex];
                _erasedLetter = data.erasedLetter;
            }
            _bModeEraser = data.eraserSelectionPending;
            _bModeSwap = data.swapSelectionPending;
            _activeEraserHost = _bModeEraser ? host : null;
            _activeSwapHost = _bModeSwap ? host : null;
            _restoredSlowdownSeconds = data.slowdownRemainingSeconds;
        }

        internal void ResumeSavedEffects(IGameBoosterHost host)
        {
            if (_restoredSlowdownSeconds > 0)
            {
                float seconds = _restoredSlowdownSeconds;
                _restoredSlowdownSeconds = 0;
                ActivateBoosterSlowdownAsync(host, seconds, isRecovery: true).Forget();
            }
            if (_bModeEraser || _bModeSwap)
            {
                _wordsFieldManager.SetModeEraser(_bModeEraser);
                _wordsFieldManager.SetModeSwap(_bModeSwap);
                _gameScreen.WordsField.SetModeEraser(_bModeEraser);
                _gameScreen.WordsField.SetModeSwap(_bModeSwap);
                _gameScreen.HoleOverlay.SetHintText(_localization.Get(LocalizationConst.TableUI,
                    _bModeEraser ? LocalizationConst.KeyPopupHintEraser : LocalizationConst.KeyPopupHintSwap));
                _gameScreen.HoleOverlay.ShowAsync().Forget();
            }
            else if (_bLetterRemoved && _erasedCell != null && _erasedCell.Empty())
            {
                _wordsFieldManager.WordsFieldData.SetSelectedCell(_erasedCell);
                _erasedCell.HighlightCell();
                _gameScreen.KeyboardPanel.ShowAsync().Forget();
            }
        }

        public void Attach(GameScreenBase gameScreen, WordsFieldManager wordsFieldManager, AIGameController ai)
        {
            _gameScreen?.CancelLetterThinking();
            if (_gameScreen?.HoleOverlay != null)
                _gameScreen.HoleOverlay.CloseButtonClicked -= OnHoleOverlayCloseButtonClicked;

            _gameScreen = gameScreen;
            _wordsFieldManager = wordsFieldManager;
            _ai = ai;

            if (_gameScreen?.HoleOverlay != null)
                _gameScreen.HoleOverlay.CloseButtonClicked += OnHoleOverlayCloseButtonClicked;
        }

        public void ResetForNewGame()
        {
            _gameScreen?.CancelLetterThinking();
            _bModeEraser = false;
            _bModeSwap = false;
            _bLetterRemoved = false;
            ClearErasedLetter();
            _boosterProcessing = false;
            _activeEraserHost = null;
            _activeSwapHost = null;
            CancelSlowdownTracking();
            _activeSlowdownHost = null;
            _restoredSlowdownSeconds = 0;
            _slowdownEndsAt = 0;
            _gameScreen?.BoosterPanel.SlowdownStop();
        }

        public void ResetForOpponentTurn()
        {
            _bLetterRemoved = false;
            ClearErasedLetter();
        }

        public void OnGameFinished()
        {
            _gameScreen?.CancelLetterThinking();
            CancelEraserMode();
            CancelSwapMode();
            _bLetterRemoved = false;
            ClearErasedLetter();
            _boosterProcessing = false;
            _activeEraserHost = null;
            _activeSwapHost = null;
            StopSlowdown();
        }

        public void CancelEraserMode()
        {
            if (!_bModeEraser || _gameScreen == null || _wordsFieldManager == null)
                return;

            _bModeEraser = false;
            _activeEraserHost = null;
            _wordsFieldManager.SetModeEraser(false);
            _gameScreen.WordsField.SetModeEraser(false);
            _gameScreen.HoleOverlay.HideAsync().Forget();
        }
        
        public void CancelSwapMode()
        {
            if (!_bModeSwap || _gameScreen == null || _wordsFieldManager == null)
                return;

            _bModeSwap = false;
            _activeSwapHost = null;
            _wordsFieldManager.SetModeSwap(false);
            _gameScreen.WordsField.SetModeSwap(false);
            _gameScreen.HoleOverlay.HideAsync().Forget();
        }

        public void OnCellSelectSuccess(CellSelectSuccessEvent eventData)
        {
            if (_gameScreen == null || _wordsFieldManager == null)
                return;

            if (_bModeEraser)
            {
                var host = _activeEraserHost;
                _erasedCell = eventData.letter;
                _erasedLetter = eventData.erasedLetter;
                TrackEraserBoosterSuccess(_activeEraserHost, eventData);
                CancelEraserMode();
                _bLetterRemoved = true;
                host?.SaveRecoveryCheckpoint();
                return;
            }

            if (_bModeSwap)
            {
                var host = _activeSwapHost;
                TrackSwapBoosterSuccess(_activeSwapHost, eventData);
                CancelSwapMode();
                host?.SaveRecoveryCheckpoint();
            }
        }

        public void StopSlowdown()
        {
            EndSlowdown(restartTimer: false);
        }

        // The current move must be cancelled first so its replacement letter is removed.
        public async UniTask RollbackEraserAsync(IGameBoosterHost host)
        {
            if (!_bLetterRemoved || _erasedCell == null || string.IsNullOrEmpty(_erasedLetter))
                return;

            _erasedCell.SetLetter(_erasedLetter);
            _erasedCell.UnHighlight();
            ClearErasedLetter();
            _bLetterRemoved = false;
            // Persist the rollback before requesting a refund; do not replay a completed refund on recovery.
            host.SaveRecoveryCheckpoint();

            if (!await _inventorySync.GrantBoosterAsync(BoosterType.Eraser, 1))
                Debug.LogError("[BOOSTER ERASER] Failed to refund eraser after a missing word.");

            _gameScreen?.BoosterPanel.Refresh();
        }

        private void ClearErasedLetter()
        {
            _erasedCell = null;
            _erasedLetter = null;
        }

        public async UniTask HandleUseAsync(UseBoosterEvent eventData, IGameBoosterHost host)
        {
            if (eventData.isEmpty)
            {
                await ShowShopAsync(host);
                return;
            }

            if (_gameScreen == null || _wordsFieldManager == null || _ai == null)
                return;

            if (!host.IsGameStarted || host.IsPaused || !host.IsOwnerTurn || _boosterProcessing)
                return;

            if (_gameScreen.BoosterPanel.IsActive(eventData.boosterType))
                return;

            if (_bLetterRemoved && eventData.boosterType == BoosterType.Eraser)
            {
                Debug.Log("[BOOSTER ERASER] ERROR: Можно стереть только одну букву за ход");
                if (!_gameScreen.EraseBubble.IsVisible)
                    _gameScreen.EraseBubble.ShowAsync().Forget();
                return;
            }

            _boosterProcessing = true;
            try
            {
                await host.BlockUIAsync(true);
                bool ok = await _inventorySync.TryUseBoosterAsync(eventData.boosterType);
                _gameScreen.BoosterPanel.Refresh();

                if (!ok)
                {
                    return;
                }

                switch (eventData.boosterType)
                {
                    case BoosterType.Letter:
                        await ActivateBoosterLetterAsync(host);
                        break;

                    case BoosterType.Slowdown:
                        ActivateBoosterSlowdownAsync(host).Forget();
                        break;

                    case BoosterType.Eraser:
                        await ActivateBoosterEraserAsync(host);
                        break;

                    case BoosterType.Mixer:
                        await ActivateBoosterMixerAsync(host);
                        break;

                    case BoosterType.Swap:
                        await ActivateBoosterSwapAsync(host);
                        break;
                }
            }
            finally
            {
                try
                {
                    await host.BlockUIAsync(false);
                }
                finally
                {
                    _boosterProcessing = false;
                }
            }
        }

        private async UniTask ShowShopAsync(IGameBoosterHost host)
        {
            if (_shopOpen)
                return;

            _shopOpen = true;
            var pauseToken = host.IsGameStarted ? new object() : null;

            try
            {
                if (pauseToken != null)
                    _pauseService.PushPause(pauseToken);

                var popup = await _ui.ShowPopupAsync<ShopPopup>(AssetKey.ShopPopup);
                if (popup != null)
                    await popup.WaitForResultAsync()
                        .AttachExternalCancellation(popup.GetCancellationTokenOnDestroy());
            }
            finally
            {
                if (pauseToken != null)
                    _pauseService.PopPause(pauseToken);

                _shopOpen = false;
            }
        }

        private async UniTask ActivateBoosterMixerAsync(IGameBoosterHost host)
        {
            if (!host.IsGameStarted || host.IsPaused || !host.IsOwnerTurn)
                return;

            _audioService?.PlaySfxAsync(SoundsConfig.BoosterMixerLaunch);

            host.CancelCurrentMove();

            string[] boardBefore = _wordsFieldManager.WordsFieldData.GetBoardData();
            MixerResult result = _wordsFieldManager.MixLetters(_dictionaryService.DictionaryConfig);
            if (result == null)
            {
                TrackMixerBoosterFail(host, boardBefore, MixerFailNoValidPattern);
                await _inventorySync.GrantBoosterAsync(BoosterType.Mixer, 1);
                _gameScreen.BoosterPanel.Refresh();
                return;
            }

            TrackMixerBoosterSuccess(host, boardBefore, result);
            host.SaveRecoveryCheckpoint();
        }
        
        private async UniTask ActivateBoosterEraserAsync(IGameBoosterHost host)
        {
            if (!host.IsGameStarted || host.IsPaused || !host.IsOwnerTurn)
                return;

            _audioService?.PlaySfxAsync(SoundsConfig.BoosterEraserLaunch);

            host.CancelCurrentMove();

            _bModeEraser = true;
            _activeEraserHost = host;
            _wordsFieldManager.SetModeEraser(true);
            _gameScreen.WordsField.SetModeEraser(true);
            _gameScreen.HoleOverlay.SetHintText(_localization.Get(
                LocalizationConst.TableUI,
                LocalizationConst.KeyPopupHintEraser));
            TrackEraserBoosterShown(host);
            await _gameScreen.HoleOverlay.ShowAsync();
            host.SaveRecoveryCheckpoint();
        }
        
        private async UniTask ActivateBoosterSwapAsync(IGameBoosterHost host)
        {
            if (!host.IsGameStarted || host.IsPaused || !host.IsOwnerTurn)
                return;

            _audioService?.PlaySfxAsync(SoundsConfig.BoosterSwapLaunch);

            host.CancelCurrentMove();

            _bModeSwap = true;
            _activeSwapHost = host;
            _wordsFieldManager.SetModeSwap(true);
            _gameScreen.WordsField.SetModeSwap(true);
            _gameScreen.HoleOverlay.SetHintText(_localization.Get(
                LocalizationConst.TableUI,
                LocalizationConst.KeyPopupHintSwap));
            TrackSwapBoosterShown(host);
            await _gameScreen.HoleOverlay.ShowAsync();
            host.SaveRecoveryCheckpoint();
        }

        private async UniTask ActivateBoosterLetterAsync(IGameBoosterHost host)
        {
            // Validation has finished. Keep input blocked, but remove the technical spinner.
            await host.BlockUIAsync(true, BlockUIScreenMode.NoSpinner);
            if (!host.IsGameStarted || !host.IsOwnerTurn || !_gameScreen.isActiveAndEnabled)
                return;

            _gameScreen.TimerBar.StopTimer();
            host.CancelCurrentMove();

            var screen = _gameScreen;
            var ai = _ai;
            var token = screen.BeginLetterThinking();
            AIWordResult res;
            try
            {
                using (token.Register(ai.AbortSearch))
                {
                    token.ThrowIfCancellationRequested();
                    res = await ai.FindWordAsync(_configService.Game.boosterLetterAiSettings, clueOnly: UseCluePrototype);
                    token.ThrowIfCancellationRequested();
                    if (!host.IsGameStarted || !host.IsOwnerTurn || !screen.isActiveAndEnabled)
                        return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                if (host.IsGameStarted && host.IsOwnerTurn && !host.IsPaused && !IsSlowdownActive && screen.isActiveAndEnabled)
                    screen.TimerBar.StartTimer();
                throw;
            }
            finally
            {
                screen.EndLetterThinking(token);
            }
            
            if (res.Success)
            {
                TrackLetterBoosterSuccess(host, res.Word);
                if (UseCluePrototype)
                {
                    _wordsFieldManager.SetModeSelect(true);
                    host.MarkLetterPlacedByBooster();
                    screen.GoButton.SetActive(true);
                    screen.CancelButton.SetActive(true);
                    screen.KeyboardPanel.HideAsync().Forget();
                    screen.SetStatusLocalizationKey("STATUS_LABEL_CLUE_SUCCESS");
                    _audioService?.PlaySfxAsync(SoundsConfig.BoosterFoundWord);
                    if (!host.IsPaused && !IsSlowdownActive)
                        screen.TimerBar.StartTimer();
                    host.SaveRecoveryCheckpoint();
                    return;
                }

                ShowBoosterLetterSuccess(host, res.Word);

                await UniTask.WaitForSeconds(_configService.Game.autoApplyDelay);

                await host.SaveWordAndContinueGameAsync(res.Word);
            }
            else
            {
                TrackLetterBoosterFail(host);
                await ShowBoosterLetterFailAsync();
                if (host.IsGameStarted && host.IsOwnerTurn && !host.IsPaused && !IsSlowdownActive && screen.isActiveAndEnabled)
                    screen.TimerBar.StartTimer();
            }
        }

        private void ShowBoosterLetterSuccess(IGameBoosterHost host, string resWord)
        {
            _gameScreen.SetTextWord(resWord);
            _wordsFieldManager.SetModeSelect(true);
            host.MarkLetterPlacedByBooster();
            _gameScreen.SetStatusLocalizationKey("STATUS_LABEL_BOOSTER_SUCCESS");

            host.BlockUIAsync(true, BlockUIScreenMode.NoSpinner).Forget();

            _audioService?.PlaySfxAsync(SoundsConfig.BoosterFoundWord);
        }

        private async UniTask ShowBoosterLetterFailAsync()
        {
            _gameScreen.SetStatusLocalizationKey(UseCluePrototype ? "STATUS_LABEL_CLUE_FAIL" : "STATUS_LABEL_BOOSTER_FAIL");
            _audioService?.PlaySfxAsync(SoundsConfig.BoosterNotFoundWord);

            await _inventorySync.GrantBoosterAsync(BoosterType.Letter, 1);
            _gameScreen.BoosterPanel.Refresh();
        }

        private void TrackLetterBoosterSuccess(IGameBoosterHost host, string word)
        {
            _analyticsReporter.TrackLetterBoosterSuccess(
                host,
                word,
                _wordsFieldManager.WordsFieldData.GetBoardData());
        }

        private void TrackLetterBoosterFail(IGameBoosterHost host)
        {
            _analyticsReporter.TrackLetterBoosterFail(
                host,
                _gameScreen.TimerBar.GetCurrentValue(),
                _wordsFieldManager.WordsFieldData.GetBoardData());
        }

        private async UniTaskVoid ActivateBoosterSlowdownAsync(IGameBoosterHost host, float remainingSeconds = -1, bool isRecovery = false)
        {
            if (!host.IsGameStarted || host.IsPaused || !host.IsOwnerTurn)
                return;

            CancelSlowdownTracking();
            _activeSlowdownHost = host;
            _gameScreen.TimerBar.StopTimer();
            _gameScreen.BoosterPanel.SlowdownStart();
            _slowdownEndsAt = Time.time + (remainingSeconds >= 0 ? remainingSeconds : _configService.Game.slowdownDelay);
            if (!isRecovery)
            {
                TrackSlowdownBoosterSuccess(host);
                _audioService?.PlaySfxAsync(SoundsConfig.BoosterSlowdownLaunch);
            }

            _slowdownCts = new CancellationTokenSource();
            if (!isRecovery)
                host.SaveRecoveryCheckpoint();

            try
            {
                await UniTask.Delay(System.TimeSpan.FromSeconds(Mathf.Max(0, _slowdownEndsAt - Time.time)), cancellationToken: _slowdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            EndSlowdown(restartTimer: !host.IsPaused && host.IsGameStarted && host.IsOwnerTurn);
        }

        private void OnHoleOverlayCloseButtonClicked()
        {
            if (_bModeEraser)
            {
                var host = _activeEraserHost;
                CancelEraserMode();
                host?.SaveRecoveryCheckpoint();
                ReturnEraserBoosterAsync(host).Forget();
            }
            
            if (_bModeSwap)
            {
                var host = _activeSwapHost;
                CancelSwapMode();
                host?.SaveRecoveryCheckpoint();
                ReturnSwapBoosterAsync(host).Forget();
            }
        }
        
        private async UniTaskVoid ReturnSwapBoosterAsync(IGameBoosterHost host)
        {
            await _inventorySync.GrantBoosterAsync(BoosterType.Swap, 1);
            _gameScreen?.BoosterPanel.Refresh();
            TrackSwapBoosterClosed(host);
        }
        
        private async UniTaskVoid ReturnEraserBoosterAsync(IGameBoosterHost host)
        {
            await _inventorySync.GrantBoosterAsync(BoosterType.Eraser, 1);
            _gameScreen?.BoosterPanel.Refresh();
            TrackEraserBoosterClosed(host);
        }

        private void TrackEraserBoosterShown(IGameBoosterHost host)
        {
            _analyticsReporter.TrackEraserBoosterShown(
                host,
                _wordsFieldManager.WordsFieldData.GetBoardData());
        }
        private void TrackSwapBoosterShown(IGameBoosterHost host)
        {
            _analyticsReporter.TrackSwapBoosterShown(
                host,
                _wordsFieldManager.WordsFieldData.GetBoardData());
        }

        private void TrackEraserBoosterClosed(IGameBoosterHost host)
        {
            _analyticsReporter.TrackEraserBoosterClosed(
                host,
                _gameScreen.TimerBar.GetCurrentValue());
        }
        
        private void TrackSwapBoosterClosed(IGameBoosterHost host)
        {
            _analyticsReporter.TrackSwapBoosterClosed(
                host,
                _gameScreen.TimerBar.GetCurrentValue());
        }

        private void TrackEraserBoosterSuccess(IGameBoosterHost host, CellSelectSuccessEvent eventData)
        {
            _analyticsReporter.TrackEraserBoosterSuccess(
                host,
                eventData,
                _gameScreen.TimerBar.GetCurrentValue(),
                _wordsFieldManager.WordsFieldData.GetBoardData());
        }

        private void TrackSwapBoosterSuccess(IGameBoosterHost host, CellSelectSuccessEvent eventData)
        {
            _analyticsReporter.TrackSwapBoosterSuccess(
                host,
                eventData,
                _gameScreen.TimerBar.GetCurrentValue());
        }

        private void TrackSlowdownBoosterSuccess(IGameBoosterHost host)
        {
            _analyticsReporter.TrackSlowdownBoosterSuccess(
                host,
                _configService.Game.slowdownDelay,
                _gameScreen.TimerBar.GetCurrentValue(),
                _wordsFieldManager.WordsFieldData.GetBoardData());
        }

        private void TrackSlowdownBoosterEnd()
        {
            _analyticsReporter.TrackSlowdownBoosterEnd();
        }

        private void TrackMixerBoosterSuccess(IGameBoosterHost host, string[] boardBefore, MixerResult result)
        {
            _analyticsReporter.TrackMixerBoosterSuccess(
                host,
                boardBefore,
                result,
                _gameScreen.TimerBar.GetCurrentValue());
        }

        private void TrackMixerBoosterFail(IGameBoosterHost host, string[] boardData, string reason)
        {
            _analyticsReporter.TrackMixerBoosterFail(
                host,
                boardData,
                reason,
                _gameScreen.TimerBar.GetCurrentValue());
        }

        private void EndSlowdown(bool restartTimer)
        {
            if (_gameScreen == null || _activeSlowdownHost == null || !_gameScreen.BoosterPanel.IsActive(BoosterType.Slowdown))
                return;

            CancelSlowdownTracking();
            _gameScreen.BoosterPanel.SlowdownStop();
            TrackSlowdownBoosterEnd();

            if (restartTimer)
                _gameScreen.TimerBar.StartTimer();

            var host = _activeSlowdownHost;
            _activeSlowdownHost = null;
            _slowdownEndsAt = 0;
            host.SaveRecoveryCheckpoint();
        }

        private void CancelSlowdownTracking()
        {
            if (_slowdownCts == null)
                return;

            _slowdownCts.Cancel();
            _slowdownCts.Dispose();
            _slowdownCts = null;
        }
    }
}
