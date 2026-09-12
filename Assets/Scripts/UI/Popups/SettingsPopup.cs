using System.Collections.Generic;
using Core.Config;
using Core.Events;
using Core.Services;
using Cysharp.Threading.Tasks;
using PlayFab;
using TMPro;
using UI.Parallax;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Networking;
using UnityEngine.UI;
using Zenject;

namespace UI.Popups
{
    public class SettingsPopup : UIPopup
    {
        [Header("UI Elements")]
        [SerializeField] private GameObject _languageBar;
        [SerializeField] private TMP_Dropdown _landDropdown;
        [SerializeField] private GameObject _soundOn;
        [SerializeField] private GameObject _vibrationOn;
        [SerializeField] private GameObject _giroParallaxOn;
        [SerializeField] private Button _closeButton;
        
        [Inject] private LocalizationService _localization;
        [Inject] private AudioService _audioService;
        [Inject] private IVibrationService _vibrationService;
        [Inject] private PlayFabAuthService _playFabAuthService;
        [Inject] private ConfigService _configService;
        [Inject] private DiContainer _container;
        [Inject] private AnalyticsService _analytics;
        
        private Locale _newLanguage;
        private Locale _oldLanguage;
        private bool _gyroEnabled;
        private List<Locale> _locales;

        public override async UniTask ShowAsync()
        {
            if (_configService.Game.enableChangeLocalization)
            {
                _languageBar.SetActive(true);
                _oldLanguage = _localization.CurrentLocale;

                if (_locales == null)
                {
                    _locales = _localization.GetAvailableLocales();
                    var options = new List<TMP_Dropdown.OptionData>(_locales.Count);
                    int curIndex = 0, i = 0;
                    foreach (var locale in _locales)
                    {
                        options.Add(new TMP_Dropdown.OptionData(locale.LocaleName));
                        if (locale.LocaleName == _oldLanguage.LocaleName)
                            curIndex = i;
                        i++;
                    }

                    _landDropdown.ClearOptions();
                    _landDropdown.AddOptions(options);
                    _landDropdown.value = curIndex;
                    _landDropdown.RefreshShownValue();
                    _landDropdown.onValueChanged.AddListener(OnDropdownChanged);
                }
            }
            else
            {
                _languageBar.SetActive(false);
            }

            UpdateSoundView();
            
            _gyroEnabled = PlayerPrefs.GetInt(PlayerPrefsKey.GyroKey, 1) == 1;
            UpdateGyroView();
            
            SendAnalytics(AnalyticsEvents.Navigation.SettingsPopupShown);
            
            await base.ShowAsync();
        }

        private void OnDestroy()
        {
            _landDropdown.onValueChanged.RemoveAllListeners();
        }

        private void OnDropdownChanged(int index)
        {
            Debug.Log($"Выбран пункт: {index}");
            _audioService?.PlaySfxAsync(SoundsConfig.ButtonClick);
            SelectLanguage(_locales[index]);
        }
        
        private void Start()
        {
            _closeButton.onClick.AddListener(async () =>
            {
                SendAnalytics(AnalyticsEvents.Navigation.CloseSettingsClicked);
                await HideAsync();
                //_completionSource?.TrySetResult(new GameSetupData { Result = PopupResult.Close });
            });
        }
        
        public void OnSoundButtonClick()
        {
            _audioService.MasterVolume = _audioService.MasterVolume < 0.1f ? 1f : 0f;
            _audioService.SetSfxVolume(_audioService.MasterVolume);
           
            UpdateSoundView();
        }
        
        public void OnVibrationOnButtonClick()
        {
            _vibrationService.EnableVibration(!_vibrationService.IsEnabled);
           
            UpdateVibrationOnView();
        }
        
        private void SelectLanguage(Locale locale)
        {
            _newLanguage = locale;

            if (_localization.CurrentLocale == _newLanguage) return;

            _localization.SetLocale(_newLanguage.Identifier.Code);
            
            SendAnalytics(AnalyticsEvents.Navigation.LocaleSettingsClicked);
            
            HideAsync().Forget();
        }
        
        public void OnTermsButtonClick()
        {
            SendAnalytics(AnalyticsEvents.Navigation.TermsOfServiceSettingsClicked);
            HideAsync().Forget();

            Application.OpenURL(_configService.Game.Terms);
        }

        public void OnPrivacyButtonClick()
        {
            SendAnalytics(AnalyticsEvents.Navigation.PrivacyPolicySettingsClicked);
            HideAsync().Forget();

            Application.OpenURL(_configService.Game.Privacy);
        }

        public void OnSupportButtonClick()
        {
            SendAnalytics(AnalyticsEvents.Navigation.SupportSettingsClicked);

            HideAsync().Forget();

            string body = string.Empty;
            if (PlayFabClientAPI.IsClientLoggedIn())
            {
                body += "Cloud ID: " + _playFabAuthService.PlayFabId;
            }

            Application.OpenURL("mailto:" + _configService.Game.Support + "?&body=" + MyEscapeURL(body));
        }

        private string MyEscapeURL(string url)
        {
            return UnityWebRequest.EscapeURL(url).Replace("+","%20");
        }
        
        private void UpdateSoundView() =>
            _soundOn.SetActive(_audioService.MasterVolume > 0.1f);
        
        private void UpdateVibrationOnView() =>
            _vibrationOn.SetActive(_vibrationService.IsEnabled);
        
        private void UpdateGyroView() =>
            _giroParallaxOn.SetActive(_gyroEnabled);
        
        public void OnGyroButtonClick()
        {
            _gyroEnabled = !_gyroEnabled;
            
            PlayerPrefs.SetInt(PlayerPrefsKey.GyroKey, _gyroEnabled ? 1 : 0);
            PlayerPrefs.Save();
            
            EventBus.Raise(new GyroEnableEvent{ ParallaxMode = _gyroEnabled
                ? UIParallaxMode.TouchAndGyro
                : UIParallaxMode.Touch});
           
            UpdateGyroView();
        }
        
        private void SendAnalytics(string eventName)
        {
            Dictionary<string, object> parameters = null;
            switch (eventName)
            {
                case AnalyticsEvents.Navigation.SettingsPopupShown:
                case AnalyticsEvents.Navigation.CloseSettingsClicked:
                case AnalyticsEvents.Navigation.LocaleSettingsClicked:
                    parameters = new Dictionary<string, object>
                    {
                        [AnalyticsEvents.Parameter.Locale] = _localization.CurrentLocale.Identifier.Code,
                        [AnalyticsEvents.Parameter.Sound] = _audioService.MasterVolume > 0.1f ? AnalyticsEvents.Option.On : AnalyticsEvents.Option.Off,
                        [AnalyticsEvents.Parameter.Giro] = _gyroEnabled ? AnalyticsEvents.Option.On : AnalyticsEvents.Option.Off,
                    };
                    break;
            }
            
            _analytics.TrackEvent(eventName, parameters);
        }
    }
}