using System.Collections.Generic;
using Core.Generated;
using Core.Services;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;
using UI.Popups;
using UI.Screens;

namespace Core.UI
{
    public class UIManager : MonoBehaviour, IUIManager
    {
        [Header("UI Roots")]
        [SerializeField] private Transform _screensRoot;
        [SerializeField] private Transform _popupsRoot;
        [SerializeField] private Transform _loadingRoot;

        private readonly Dictionary<string, UIScreen> _loadedScreens = new();
        private readonly Dictionary<string, UIPopup> _loadedPopups = new();

        private IPrefabService _prefabService;
        private DiContainer _container;

        [Inject]
        public void Construct(IPrefabService prefabService, DiContainer container)
        {
            _prefabService = prefabService;
            _container = container;
        }

        private void Awake()
        {
            Debug.Log("🎨 UIManager initialized.");
        }

        // ---------------------------------------------------------
        // SCREENS
        // ---------------------------------------------------------

        public async UniTask<T> ShowScreenAsync<T>(AssetKey assetKey) where T : UIScreen
        {
            var screen = await GetOrLoadScreenAsync<T>(assetKey);
            if (screen == null)
                return null;

            if (screen is IUIScreenPreparable preparableScreen)
                await preparableScreen.PrepareAsync();

            await screen.ShowAsync();
            return screen;
        }

        public async UniTask<T> ShowScreenAsync<T, TPayload>(AssetKey assetKey, TPayload payload)
            where T : UIScreen<TPayload>
        {
            var screen = await GetOrLoadScreenAsync<T>(assetKey);
            if (screen == null)
                return null;

            await screen.PrepareAsync(payload);
            await screen.ShowAsync();
            return screen;
        }

        public async UniTask<T> HideScreenAsync<T>(AssetKey assetKey) where T : UIScreen
        {
            if (_loadedScreens.TryGetValue(assetKey.ToString(), out var screen) && screen != null)
            {
                await screen.HideAsync();
                return screen as T;
            }

            return null;
        }

        public async UniTask HideAllScreensAsync()
        {
            foreach (var pair in _loadedScreens)
            {
                if (pair.Value != null && pair.Value.gameObject.activeSelf)
                    await pair.Value.HideAsync();
            }
        }
        
        public UniTask<BlockUIScreen> ShowBlockUIScreenAsync(AssetKey assetKey, BlockUIScreenMode mode)
        {
            return ShowScreenAsync<BlockUIScreen, BlockUIScreenMode>(assetKey, mode);
        }

        private async UniTask<T> GetOrLoadScreenAsync<T>(AssetKey assetKey) where T : UIScreen
        {
            var strAssetKey = assetKey.ToString();

            if (_loadedScreens.TryGetValue(strAssetKey, out var existing))
                return existing as T;

            var prefab = await _prefabService.GetPrefabAsync(strAssetKey);
            if (prefab == null)
            {
                Debug.LogError($"❌ Failed to load screen prefab: {strAssetKey}");
                return null;
            }

            var instance = Instantiate(prefab, _screensRoot);
            _container.InjectGameObject(instance);

            var screen = instance.GetComponent<T>();
            if (screen == null)
            {
                Debug.LogError($"❌ Screen component {typeof(T).Name} not found on prefab: {strAssetKey}");
                Destroy(instance);
                return null;
            }

            _loadedScreens[strAssetKey] = screen;
            return screen;
        }

        // ---------------------------------------------------------
        // POPUPS
        // ---------------------------------------------------------

        public async UniTask<T> ShowPopupAsync<T>(AssetKey assetKey) where T : UIPopup
        {
            var popup = await GetOrLoadPopupAsync<T>(assetKey);
            if (popup == null)
                return null;

            await popup.ShowAsync();
            return popup;
        }

        public async UniTask<T> ShowPopupAsync<T, TPayload>(AssetKey assetKey, TPayload payload)
            where T : UIPopup<TPayload>
        {
            var popup = await GetOrLoadPopupAsync<T>(assetKey);
            if (popup == null)
                return null;

            await popup.PrepareAsync(payload);
            await popup.ShowAsync();
            return popup;
        }

        public async UniTask HidePopupAsync<T>() where T : UIPopup
        {
            foreach (var pair in _loadedPopups)
            {
                if (pair.Value is T popup)
                {
                    await popup.HideAsync();
                    return;
                }
            }
        }

        public async UniTask HideAllPopupsAsync()
        {
            foreach (var pair in _loadedPopups)
            {
                if (pair.Value != null && pair.Value.gameObject.activeSelf)
                    await pair.Value.HideAsync();
            }
        }

        private async UniTask<T> GetOrLoadPopupAsync<T>(AssetKey assetKey) where T : UIPopup
        {
            var strAssetKey = assetKey.ToString();

            if (_loadedPopups.TryGetValue(strAssetKey, out var existing))
                return existing as T;

            var prefab = await _prefabService.GetPrefabAsync(strAssetKey);
            if (prefab == null)
            {
                Debug.LogError($"❌ Failed to load popup prefab: {strAssetKey}");
                return null;
            }

            var instance = Instantiate(prefab, _popupsRoot);
            _container.InjectGameObject(instance);

            var popup = instance.GetComponent<T>();
            if (popup == null)
            {
                Debug.LogError($"❌ Popup component {typeof(T).Name} not found on prefab: {strAssetKey}");
                Destroy(instance);
                return null;
            }

            _loadedPopups[strAssetKey] = popup;
            return popup;
        }
    }
}
