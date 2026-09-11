using System.Collections.Generic;
using Core.Build;
using Core.Services.DataDictionary;
using Core.Generated;
using Core.Services;
using Core.Services.NewWords;
using Core.Services.ReportWord;
using Core.Services.Shop;
using Core.UI;
using Cysharp.Threading.Tasks;
using Game.Logic;
using Core.Services.Inventory;
using UI.Popups;
using UI.Screens;
using UI.UI;
using UnityEngine;
using Zenject;

namespace Core.Installers
{
    // регистрирует все сервисы.
    public class CoreInstaller : MonoInstaller
    {
        [Inject] private DictionaryManagerPresenter _dictPresenter;

        [Header("Scene UI")] [SerializeField]
        private StartLoadingScreen loading;

        [Header("Analytics")] [SerializeField]
        private AnalyticsInstallerSettings _analyticsSettings = new();

        public override void InstallBindings()
        {
            Debug.Log(
                $"<color=yellow>BUILD: {BuildInfo.VersionName} code={BuildInfo.AndroidVersionCode} utc={BuildInfo.Utc}</color>");

            Container.Bind<GameLogger>().AsSingle();

            Container.BindInterfacesAndSelfTo<ConfigService>().AsSingle().NonLazy();
            Container.Bind<LocalizationService>().AsSingle().NonLazy();

            Container.Bind<AddressablesLoader>().AsSingle();

            Container.Bind<ILoadingUI>().FromComponentInHierarchy().AsSingle().NonLazy();
            Container.Bind<IUIManager>().To<UIManager>().FromComponentInHierarchy().AsSingle();

            Container.Bind<IGamePauseService>().To<GamePauseService>().AsSingle();
            Container.Bind<IInternetConnectionService>().To<InternetConnectionService>().AsSingle();

            Container.Bind<ISpriteService>().To<SpriteService>().AsSingle();
            Container.Bind<IPrefabService>().To<PrefabService>().AsSingle();

            Container.Bind<AnalyticsPlayerContext>().AsSingle();
            Container.BindInstance(_analyticsSettings).AsSingle();
            Container.BindInterfacesAndSelfTo<AnalyticsService>().AsSingle();

            if (_analyticsSettings.EnableGameAnalytics)
                Container.BindInterfacesAndSelfTo<GameAnalyticsProvider>().AsSingle();

            Container.Bind<List<LanguageDictionaryConfig>>().FromInstance(_dictPresenter.configs).AsSingle();
            Container.Bind<DictionaryService>().AsSingle();
            Container.Bind<DictionaryManager>().AsSingle().NonLazy();

            Container.BindInterfacesAndSelfTo<AudioService>().AsSingle().NonLazy();
            Container.BindInterfacesAndSelfTo<VibrationService>().AsSingle();
            
            Container.Bind<SkinsService>().AsSingle().NonLazy();
            Container.Bind<GameAnalyticsPayloadFactory>().AsSingle();
            Container.Bind<GameAnalyticsReporter>().AsSingle();
            Container.Bind<BoosterAnalyticsReporter>().AsSingle();
            Container.Bind<GameBoosterController>().AsSingle();
            Container.Bind<GameController>().AsSingle();
            
            Container.Bind<ISaveService>().To<SaveService>().AsSingle().NonLazy();
            Container.Bind<IInventoryService>().To<InventoryService>().AsSingle().NonLazy();

            Container.BindInterfacesAndSelfTo<PlayFabAuthService>().AsSingle();
            Container.BindInterfacesAndSelfTo<ProfileService>().AsSingle();
            Container.BindInterfacesAndSelfTo<InventorySyncService>().AsSingle();
            Container.BindInterfacesAndSelfTo<StarterBonusService>().AsSingle();
            Container.BindInterfacesAndSelfTo<DailyBonusService>().AsSingle();

            Container.Bind<INewWordsProvider>().To<PlayFabNewWordsProvider>().AsSingle();
            Container.Bind<INewWordsService>().To<NewWordsService>().AsSingle();
            Container.BindInterfacesAndSelfTo<NewWordsLimitsService>().AsSingle().NonLazy();
            Container.Bind<MissingWordPopupPresenter>().AsSingle();

            Container.Bind<IReportWordProvider>().To<PlayFabReportWordProvider>().AsSingle();
            Container.Bind<IReportWordService>().To<ReportWordService>().AsSingle();
            Container.BindInterfacesAndSelfTo<ReportWordLimitsService>().AsSingle().NonLazy();
            Container.Bind<ShowWordInfoPresenter>().AsSingle();

#if UNITY_ANDROID && !UNITY_EDITOR
            Container.Bind<IShopService>().To<GooglePlayShopService>().AsSingle().NonLazy();
#else
            Container.Bind<IShopService>().To<StubShopService>().AsSingle().NonLazy();
#endif
            Container.BindInterfacesAndSelfTo<RewardedAdsService>().AsSingle().NonLazy();
            Container.BindInterfacesAndSelfTo<AdvertisingBoosterService>().AsSingle();
            Container.BindInterfacesAndSelfTo<RewardedBoosterGrantService>().AsSingle().NonLazy();
            Container.BindInterfacesAndSelfTo<RewardedLimitsService>().AsSingle().NonLazy();
            Container.BindInterfacesTo<AdsBootstrapService>().AsSingle();

            Container.Bind<AdsEntitlementService>().AsSingle();
            Container.Bind<InterstitialAdsService>().AsSingle();
            Container.Bind<InterstitialPolicyService>().AsSingle();
        }

        public override void Start()
        {
            InitializeAsync().Forget();
        }

        private async UniTaskVoid InitializeAsync()
        {
            var analytics = Container.Resolve<AnalyticsService>();
            
            var parameters = AdsAnalyticsHelper.GetWaitTimeParams();
            var bannerType = loading.CurrentBanner != null
                ? loading.CurrentBanner.BannerType.ToString()
                : "Unknown";
            parameters.Add(AnalyticsEvents.Parameter.Banner, bannerType);
            analytics.TrackEvent(AnalyticsEvents.Startup.LoadingStarted, parameters);
            
            loading.SetProgress(0.0f);
            await UniTask.NextFrame();
            global::Core.NativeSplashScreen.Hide();

            await Container.Resolve<LocalizationService>().InitializeAsync();
            loading.SetProgress(0.05f);

            await Container.Resolve<GameLogger>().InitializeAsync();
            loading.SetProgress(0.10f);

            await analytics.InitializeAsync();

            await Container.Resolve<ConfigService>().InitializeAsync();
            loading.SetProgress(0.15f);

            var ui = Container.Resolve<IUIManager>();
            var internetService = Container.Resolve<IInternetConnectionService>();
            await internetService.InitializeAsync();
            loading.SetProgress(0.20f);

            if (!await internetService.CheckNowAsync())
            {
                // апку запустили без интернета...
                await loading.HideAsync();

                while (!await internetService.CheckNowAsync())
                    await UniTask.Delay(500, ignoreTimeScale: true);

                analytics.TrackEvent(AnalyticsEvents.Startup.LoadingInternetConnectionRestored, AdsAnalyticsHelper.GetWaitTimeParams());
                
                await loading.ShowAsync();
                loading.SetProgress(0.20f);
            }

            await Container.Resolve<DictionaryManager>().InitializeAsync();
            loading.SetProgress(0.25f);

            await Container.Resolve<SkinsService>().InitializeAsync();
            loading.SetProgress(0.30f);

            await Container.Resolve<GameController>().InitializeAsync();
            loading.SetProgress(0.35f);

            await Container.Resolve<AudioService>().InitializeAsync();
            await Container.Resolve<VibrationService>().InitializeAsync();
            loading.SetProgress(0.40f);

            await Container.Resolve<IShopService>().InitializeAsync();
            loading.SetProgress(0.50f);

            await Container.Resolve<PlayFabAuthService>().InitializeAsync();
            loading.SetProgress(0.60f);

            var adsEntitlementInit = Container.Resolve<AdsEntitlementService>().InitializeAsync();
            InitializeProfileInBackgroundAsync(Container.Resolve<ProfileService>()).Forget();
            var inventorySyncInit = Container.Resolve<InventorySyncService>().InitializeAsync();

            await Container.Resolve<StarterBonusService>().InitializeAsync();
            await Container.Resolve<DailyBonusService>().InitializeAsync();
            await Container.Resolve<RewardedAdsService>().InitializeAsync();
            await Container.Resolve<AdvertisingBoosterService>().InitializeAsync();
            await Container.Resolve<InterstitialAdsService>().InitializeAsync();
            await Container.Resolve<InterstitialPolicyService>().InitializeAsync();
            loading.SetProgress(0.65f);

            var gameSettingsGo = new GameObject("SRDebugGameSettingsBridge");
            DontDestroyOnLoad(gameSettingsGo);
            Container.InstantiateComponent<DebugTools.SRDebugGameSettingsBridge>(gameSettingsGo);

            var go = new GameObject("SRDebugAdsBridge");
            DontDestroyOnLoad(go);
            Container.InstantiateComponent<DebugTools.SRDebugAdsBridge>(go);

            var dailyBonusGo = new GameObject("SRDebugDailyBonusBridge");
            DontDestroyOnLoad(dailyBonusGo);
            Container.InstantiateComponent<DebugTools.SRDebugDailyBonusBridge>(dailyBonusGo);

            var newWordsGo = new GameObject("SRDebugNewWordsBridge");
            DontDestroyOnLoad(newWordsGo);
            Container.InstantiateComponent<DebugTools.SRDebugNewWordsBridge>(newWordsGo);

            await Container.Resolve<NewWordsLimitsService>().InitializeAsync();

            var reportWordsGo = new GameObject("SRDebugReportWordBridge");
            DontDestroyOnLoad(reportWordsGo);
            Container.InstantiateComponent<DebugTools.SRDebugReportWordBridge>(reportWordsGo);

            await Container.Resolve<ReportWordLimitsService>().InitializeAsync();

            await Container.Resolve<RewardedBoosterGrantService>().InitializeAsync();
            await Container.Resolve<RewardedLimitsService>().InitializeAsync();
            loading.SetProgress(0.70f);

            await UniTask.WhenAll(adsEntitlementInit, inventorySyncInit);
            loading.SetProgress(0.80f);

            loading.SetProgress(1.0f);
            analytics.TrackEvent(AnalyticsEvents.Startup.LoadingCompleted, AdsAnalyticsHelper.GetWaitTimeParams());
            
            await ui.HideAllScreensAsync();
            await ui.ShowScreenAsync<MainMenuScreen>(AssetKey.MainMenuScreen);

            await loading.HideAsync();
            
            Destroy(loading.gameObject);
        }

        private static async UniTask InitializeProfileInBackgroundAsync(ProfileService profileService)
        {
            try
            {
                await profileService.InitializeAsync();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Profile initialization failed: {exception}");
            }
        }

        private void OnDestroy()
        {
            Container.Resolve<GameController>().Dispose();
        }
        
    }
}
