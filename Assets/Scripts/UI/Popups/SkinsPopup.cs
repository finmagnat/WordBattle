using System;
using System.Collections.Generic;
using Core.Config;
using Core.Data;
using Core.Generated;
using Core.Services;
using Core.UI;
using Core.UI.Components;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Popups
{
    public class SkinsPopup : UIPopup
    {
        [SerializeField] private SkinButton _buttonPrefab;
        [SerializeField] private Transform _scrollListContent;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _applyButton;
        [SerializeField] private Toggle _toggleRandom;
        
        [Inject] private SkinsService _skinsService;
        [Inject] private ISpriteService _spritesService;
        [Inject] private AudioService _audioService;
        [Inject] private DiContainer _container;
        [Inject] private AnalyticsService _analytics;
        [Inject] private ILoadingUI _loadingUI;
        
        private readonly List<SkinButton> _buttons = new();

        private SkinType _newSkin;
        private SkinType _oldSkin;
        private bool _isApplying;

        private void Start()
        {
            _closeButton.onClick.AddListener(async () =>
            {
                SendAnalytics(AnalyticsEvents.Navigation.CloseSkinsClicked);
                await HideAsync();
            });
            
            _applyButton.onClick.AddListener(() => ApplySelectedSkinAsync().Forget());
        }

        public override async UniTask ShowAsync()
        {
            _oldSkin = _skinsService.SkinCurrent.SkinType;
            if (_buttons == null || _buttons.Count == 0)
            {
                foreach (var skinItem in _skinsService.Config.Skins)
                {
                    var spritePreview = await _spritesService.GetSpriteAsync(skinItem.SkinPreviewAlias);
                    SkinButton skinButton = _container.InstantiatePrefabForComponent<SkinButton>(_buttonPrefab, _scrollListContent);
                    skinButton.SetSkinData(spritePreview, skinItem.SkinType);
                    skinButton.button.onClick.AddListener(() =>
                    {
                        SelectSkin(skinItem.SkinType);
                    });

                    _buttons.Add(skinButton);
                }
            }

            SelectSkin(_oldSkin);
            _toggleRandom.isOn = _skinsService.SkinRandomSelect;
            
            SendAnalytics(AnalyticsEvents.Navigation.SkinsPopupShown);
            
            await base.ShowAsync();
        }
        
        private void SelectSkin(SkinType skinType)
        {
            _newSkin = skinType;

            foreach (var button in _buttons)
                button.SetActiveStatus(button.SkinType == skinType);
        }

        private async UniTask ApplySelectedSkinAsync()
        {
            if (_isApplying)
                return;

            _isApplying = true;
            SetInteraction(false);

            SkinType selectedSkin = _newSkin;
            bool randomSelect = _toggleRandom.isOn;
            bool loadingShown = false;
            bool isSuccess = false;

            try
            {
                if (_skinsService.SkinCurrent.SkinType == selectedSkin)
                {
                    _audioService?.PlaySfxAsync(SoundsConfig.ButtonClick);
                    await HideAsync();
                    return;
                }

                InGameLoadingScreen loadingScreen =
                    await _loadingUI.ShowLoadingAsync<InGameLoadingScreen>(AssetKey.InGameLoadingScreen);
                if (loadingScreen == null)
                {
                    Debug.LogError("Cannot apply skin because InGameLoadingScreen could not be shown.", this);
                    return;
                }

                loadingShown = true;

                bool applied = await _skinsService.ApplySkinAsync(
                    selectedSkin,
                    this.GetCancellationTokenOnDestroy());
                if (!applied)
                {
                    Debug.LogError($"Failed to apply skin '{selectedSkin}'.", this);
                    return;
                }

                _skinsService.TrySaveRandomSelect(randomSelect);
                isSuccess = true;
                SendAnalytics(AnalyticsEvents.Navigation.ApplySkinsClicked);

                await HideAsync();
            }
            catch (OperationCanceledException)
            {
                // The popup was destroyed while the skin was being applied.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                try
                {
                    if (loadingShown)
                        await _loadingUI.HideLoadingAsync();
                    
                    if(isSuccess)
                        _audioService?.PlaySfxAsync(SoundsConfig.SkinChanged);
                }
                finally
                {
                    _isApplying = false;
                    if (this)
                        SetInteraction(true);
                }
            }
        }

        private void SetInteraction(bool interactable)
        {
            _closeButton.interactable = interactable;
            _applyButton.interactable = interactable;
            _toggleRandom.interactable = interactable;

            foreach (SkinButton button in _buttons)
                button.button.interactable = interactable;
        }
        
        private void SendAnalytics(string eventName)
        {
            Dictionary<string, object> parameters = null;
            switch (eventName)
            {
                case AnalyticsEvents.Navigation.SkinsPopupShown:
                case AnalyticsEvents.Navigation.ApplySkinsClicked:
                    parameters = new Dictionary<string, object>
                    {
                        [AnalyticsEvents.Parameter.Skin] = _skinsService.SkinCurrent.SkinType.ToString(),
                        [AnalyticsEvents.Parameter.SkinRandom] = _skinsService.SkinRandomSelect,
                    };
                    break;
            }
            
            _analytics.TrackEvent(eventName, parameters);
        }
    }
}
