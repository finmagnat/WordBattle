using System.Collections.Generic;
using Core.Data;
using Core.Services.DataDictionary;
using Core.Events;
using Core.Generated;
using Core.Services;
using Core.UI;
using Cysharp.Threading.Tasks;
using Game.Logic;
using UI.Popups;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using Zenject;

namespace UI.Screens
{
    public class MainMenuScreen : UIScreen
    {
        [SerializeField] private Button _playAIButton;
        [SerializeField] private Button _loadAndplayAIButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _infoButton;
        [SerializeField] private Button _skinsButton;
        [SerializeField] private Button _shopButton;
        [SerializeField] private Button _dailyBonusButton;
        [SerializeField] private RectTransform _mainScreenBackgroung;
        
        [Inject] private IUIManager _ui;
        [Inject] private ILoadingUI _loadingUI;
        [Inject] private ISaveService _saveService;
        [Inject] private SkinsService _skinsService;
        [Inject] private ISpriteService _spritesService;
        [Inject] private IPrefabService _prefabService;
        [Inject] private GameController _gameController;
        [Inject] private LocalizationService _localization;
        [Inject] private ConfigService _configService;
        [Inject] private AnalyticsService _analytics;
        [Inject] private DictionaryManager _dictionaryManager;
        [Inject] private IDailyBonusService _dailyBonusService;

        private bool _isProcessing;
        private GameObject _backgroundInstance;

        private void Start()
        {
            _localization.OnLocaleChanged += OnLocaleChanged; 
            _skinsService.OnSkinChanged += OnSkinChanged; 
            _dailyBonusService.StateChanged += OnDailyBonusStateChanged;
            UpdateDailyBonusButton();
            
            _playAIButton.onClick.AddListener(async () =>
            {
                if (_isProcessing) return;
                _isProcessing = true;
                
                _analytics.TrackEvent(AnalyticsEvents.Navigation.PlayMainMenuClicked);

                var popup = await _ui.ShowPopupAsync<AIGameSetupPopup, GameSetupData>(AssetKey.AIGameSetupPopup, new GameSetupData());
                var data = await popup.WaitForResultAsync();

                if (data.Result == PopupResult.Play)
                {
                    Debug.Log($"🎮 Начинаем игру: Difficulty={data.Difficulty}, Time={data.TurnTime}s");
                    await StartGame();
                }
                else if (data.Result == PopupResult.GotoShop)
                {
                    Debug.Log($"🎮 Открыть Магазин");
                    var shopPopup = await _ui.ShowPopupAsync<ShopPopup>(AssetKey.ShopPopup);
                    var shopPopupData = await popup.WaitForResultAsync();
                }
                else
                {
                    Debug.Log("❌ Игрок отменил или закрыл попап");
                }

                _isProcessing = false;
            });
            
            _loadAndplayAIButton.onClick.AddListener(async () =>
            {
                if (_isProcessing) return;
                _isProcessing = true;

                _analytics.TrackEvent(AnalyticsEvents.Navigation.ContinueMainMenuClicked);
                
                var popup = await _ui.ShowPopupAsync<LoadSavedGamePopup, NoPayload>(AssetKey.LoadSavedGamePopup, NoPayload.Value);
                var data = await popup.WaitForResultAsync();

                if (data.Result == PopupResult.Play)
                {
                    Debug.Log($"🎮 Продолжаем сохраненную игру");
                    await StartGame(true);
                }
                else if (data.Result == PopupResult.RemoveAndExit)
                {
                    Debug.Log($"❌ Удаляем сохраненную игру");
                    await _saveService.ClearAsync();
                    _loadAndplayAIButton.gameObject.SetActive(false);
                }
                else
                {
                    Debug.Log("❌ Игрок отменил или закрыл попап");
                }

                _isProcessing = false;
            });

            _settingsButton.onClick.AddListener(async () =>
            {
                if (_isProcessing) return;
                _isProcessing = true;

                _analytics.TrackEvent(AnalyticsEvents.Navigation.SettingsMainMenuClicked);
                
                await _ui.ShowPopupAsync<SettingsPopup>(AssetKey.SettingsPopup);

                _isProcessing = false;
            });


            _infoButton.onClick.AddListener(async () =>
            {
                if (_isProcessing) return;
                _isProcessing = true;

                _analytics.TrackEvent(AnalyticsEvents.Navigation.InfoMainMenuClicked);
                
                await _ui.ShowPopupAsync<InfoPopup>(AssetKey.InfoPopup);

                _isProcessing = false;
            });
            
            _skinsButton.onClick.AddListener(async () =>
            {
                if (_isProcessing) 
                    return;
                _isProcessing = true;

                _analytics.TrackEvent(AnalyticsEvents.Navigation.SkinsMainMenuClicked);
                
                await _ui.ShowPopupAsync<SkinsPopup>(AssetKey.SkinsPopup);

                _isProcessing = false;
            });
            
            _shopButton.onClick.AddListener(async () =>
            {
                if (_isProcessing) return;
                _isProcessing = true;

                _analytics.TrackEvent(AnalyticsEvents.Navigation.ShopMainMenuClicked);
                
                await _ui.ShowPopupAsync<ShopPopup>(AssetKey.ShopPopup);

                _isProcessing = false;
            });
            
            _dailyBonusButton.onClick.AddListener(async () =>
            {
                if (_isProcessing) return;
                _isProcessing = true;

                _analytics.TrackEvent(AnalyticsEvents.Navigation.DailyBonusMainMenuClicked);
                
                await _ui.ShowPopupAsync<DailyBonusPopup>(AssetKey.DailyBonusPopup);

                _isProcessing = false;
            });
            
            UpdateSkin();
        }

        private async UniTask StartGame(bool isLoadSavedGame = false)
        {
            // Показ *правильного* in-game loading
            await _loadingUI.ShowLoadingAsync<BannerLoadingScreen>(AssetKey.BannerLoadingScreen);

            if (_skinsService.SkinRandomSelect)
                _skinsService.SetSkinRandom();
            
            // Засекаем время после фактического показа экрана загрузки с баннерами
            float startTime = Time.realtimeSinceStartup;

            if (isLoadSavedGame)
            {
                SaveGameData gameData = await _saveService.LoadAsync();
                _gameController.SetGameData(gameData);
            }

            // Скрываем главное меню
            await _dictionaryManager.EnsureCurrentLocaleLoadedAsync();

            await _ui.HideAllScreensAsync();

            // Переход на экран игры с ИИ
            await _ui.ShowScreenAsync<AIGameScreen>(AssetKey.AIGameScreen);

            // Сколько уже показывался лоадинг
            int elapsedMs = Mathf.RoundToInt((Time.realtimeSinceStartup - startTime) * 1000f);
            int remainingMs = Mathf.Max(0, _configService.Game.minLoadingScreenDurationMs - elapsedMs);

            // Если экран загрузился слишком быстро — добиваем остаток
            if (remainingMs > 0)
                await UniTask.Delay(remainingMs, DelayType.UnscaledDeltaTime);
                    
            // Убираем лоадинг
            await _loadingUI.HideLoadingAsync();
                    
            EventBus.Raise(new GameScreenReadyEvent());
        }

        private void OnDestroy()
        {
            _localization.OnLocaleChanged -= OnLocaleChanged;
            _skinsService.OnSkinChanged -= OnSkinChanged;
            _dailyBonusService.StateChanged -= OnDailyBonusStateChanged;
        }
        
        private void OnLocaleChanged(Locale locale)
        {
            _loadAndplayAIButton.gameObject.SetActive(_saveService.HasSave());
        }
        
        private void OnSkinChanged(SkinData skinCurrent)
        {
            UpdateSkin();
        }

        private void OnDailyBonusStateChanged(DailyBonusState state)
        {
            UpdateDailyBonusButton();
        }

        public override UniTask ShowAsync()
        {
            _loadAndplayAIButton.gameObject.SetActive(_saveService.HasSave());
            UpdateDailyBonusButton();
            SendAnalyticsShown();
            return base.ShowAsync();
        } 

        private void UpdateDailyBonusButton()
        {
            if (_dailyBonusButton == null)
                return;

            _dailyBonusButton.gameObject.SetActive(_dailyBonusService is { IsAvailable: true });
        }
        
        protected async UniTask UpdateSkin()
        {
            var skin = _skinsService.SkinCurrent;
            _playAIButton.image.sprite = await _spritesService.GetSpriteAsync(skin.DefaultButtonAlias);
            _loadAndplayAIButton.image.sprite = await _spritesService.GetSpriteAsync(skin.DefaultButtonAlias);

            _settingsButton.image.sprite = await _spritesService.GetSpriteAsync(skin.MainScreenTheme.SettingsButtonAlias);
            _infoButton.image.sprite = await _spritesService.GetSpriteAsync(skin.MainScreenTheme.InfoButtonAlias);
            _skinsButton.image.sprite = await _spritesService.GetSpriteAsync(skin.MainScreenTheme.SkinButtonAlias);
            _shopButton.image.sprite = await _spritesService.GetSpriteAsync(skin.MainScreenTheme.ShopButtonAlias);

            var prefab = await _prefabService.GetPrefabAsync(
                skin.MainScreenTheme.HomeBackgroundAlias);

            if (prefab == null)
                return;

            if (_backgroundInstance != null)
                Destroy(_backgroundInstance);

            _backgroundInstance = Instantiate(
                prefab,
                _mainScreenBackgroung,
                false);

            _backgroundInstance.transform.SetAsFirstSibling();
        }

        private void SendAnalyticsShown()
        {
            _analytics.TrackEvent(AnalyticsEvents.Navigation.MainMenuShown, new Dictionary<string, object>
            {
                [AnalyticsEvents.Parameter.Locale] = _localization.CurrentLocale.Identifier.Code,
                [AnalyticsEvents.Parameter.Skin] = _skinsService.SkinCurrent.SkinType.ToString(),
            });
        }
    }
}
