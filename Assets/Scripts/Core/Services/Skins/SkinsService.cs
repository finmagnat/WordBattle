using System;
using System.Threading;
using Core.Config;
using Core.Data;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace Core.Services
{
    public class SkinsService : ISkinsService, IDisposable
    {
        public event Action<SkinData> OnSkinChanged;
        public event Action<SkinRuntime> OnRuntimeChanged;

        public SkinData SkinCurrent { get; private set; }
        public SkinRuntime CurrentRuntime { get; private set; }
        public bool SkinRandomSelect { get; private set; }
        public SkinsConfig Config => _skinsConfig;

        [Inject] private ConfigService _configService;
        [Inject] private SkinRuntimeProvider _runtimeProvider;

        private readonly object _requestGate = new();
        private SkinsConfig _skinsConfig;
        private CancellationTokenSource _activeRequest;
        private int _requestGeneration;
        private bool _isDisposed;

        public async UniTask InitializeAsync()
        {
            ThrowIfDisposed();

            if (CurrentRuntime != null)
                return;

            _skinsConfig = _configService.Skins;
            SkinRandomSelect = PlayerPrefs.GetInt(PlayerPrefsKey.SkinSelectRandomKey, 1) == 1;

            SkinType savedType = (SkinType)PlayerPrefs.GetInt(
                PlayerPrefsKey.SkinCurrent,
                (int)_skinsConfig.SkinByDefault);

            if (!_skinsConfig.TryGetSkinByType(savedType, out SkinData selectedData))
            {
                Debug.LogWarning(
                    $"[SkinsService] Saved skin '{savedType}' is absent. Falling back to '{_skinsConfig.SkinByDefault}'.");
                selectedData = GetConfiguredSkinOrThrow(_skinsConfig.SkinByDefault);
            }

            SkinRuntime runtime;
            try
            {
                runtime = await _runtimeProvider.CreateAsync(selectedData);
            }
            catch (Exception exception) when (selectedData.SkinType != _skinsConfig.SkinByDefault)
            {
                Debug.LogWarning(
                    $"[SkinsService] Failed to prepare saved skin '{selectedData.SkinType}': {exception.Message}. " +
                    $"Falling back to '{_skinsConfig.SkinByDefault}'.");
                selectedData = GetConfiguredSkinOrThrow(_skinsConfig.SkinByDefault);
                runtime = await _runtimeProvider.CreateAsync(selectedData);
            }

            SkinRuntime previousRuntime = CurrentRuntime;
            CurrentRuntime = runtime;
            SkinCurrent = selectedData;
            previousRuntime?.Dispose();

            if (selectedData.SkinType != savedType)
                SaveSelection(selectedData.SkinType);
        }

        /// <summary>
        /// Prepares a complete candidate runtime and commits it only if this is still the latest request.
        /// Returns false when the request fails, is canceled, or is superseded by a newer request.
        /// </summary>
        public async UniTask<bool> ApplySkinAsync(
            SkinType skinType,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationTokenSource previousRequest;
            int generation;

            lock (_requestGate)
            {
                generation = ++_requestGeneration;
                previousRequest = _activeRequest;
                _activeRequest = request;
            }

            CancelSafely(previousRequest);

            SkinRuntime candidate = null;

            try
            {
                request.Token.ThrowIfCancellationRequested();

                if (_skinsConfig == null)
                    throw new InvalidOperationException("SkinsService must be initialized before applying a skin.");

                if (!_skinsConfig.TryGetSkinByType(skinType, out SkinData skinData))
                {
                    Debug.LogError($"[SkinsService] Skin '{skinType}' is absent from SkinsConfig.");
                    return false;
                }

                // A -> B -> A can retain the already complete A runtime while invalidating B.
                if (CurrentRuntime != null && CurrentRuntime.SkinType == skinType)
                {
                    // This also repairs a temporary legacy SkinCurrent/CurrentRuntime mismatch.
                    if (SkinCurrent.SkinType != skinType)
                    {
                        SkinCurrent = skinData;
                        SaveSelection(skinType);
                        NotifyRuntimeChanged(CurrentRuntime);
                        NotifySkinChanged(SkinCurrent);
                    }

                    return true;
                }

                candidate = await _runtimeProvider.CreateAsync(skinData, request.Token);
                request.Token.ThrowIfCancellationRequested();

                SkinRuntime previousRuntime;
                lock (_requestGate)
                {
                    if (generation != _requestGeneration || !ReferenceEquals(_activeRequest, request))
                        return false;

                    previousRuntime = CurrentRuntime;
                    CurrentRuntime = candidate;
                    SkinCurrent = skinData;
                    candidate = null;
                }

                SaveSelection(skinType);

                try
                {
                    NotifyRuntimeChanged(CurrentRuntime);
                    NotifySkinChanged(SkinCurrent);
                }
                finally
                {
                    // Synchronous subscribers have finished replacing references/instances by this point.
                    previousRuntime?.Dispose();
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SkinsService] Failed to prepare skin '{skinType}': {exception}");
                return false;
            }
            finally
            {
                candidate?.Dispose();
                FinishRequest(request);
            }
        }

        public async UniTask<SkinRuntime> EnsureCurrentRuntimeAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (CurrentRuntime != null && CurrentRuntime.SkinType == SkinCurrent.SkinType)
                return CurrentRuntime;

            if (_skinsConfig == null)
                throw new InvalidOperationException("SkinsService has not been initialized yet.");

            SkinType type = SkinCurrent.SkinType != 0
                ? SkinCurrent.SkinType
                : _skinsConfig.SkinByDefault;

            bool applied = await ApplySkinAsync(type, cancellationToken);
            if (!applied || CurrentRuntime == null || CurrentRuntime.SkinType != SkinCurrent.SkinType)
                throw new InvalidOperationException($"Skin runtime '{type}' could not be prepared.");

            return CurrentRuntime;
        }

        public Sprite GetSprite(SkinSpriteKey key) => GetReadyRuntime().GetSprite(key);
        public GameObject GetPrefab(SkinPrefabKey key) => GetReadyRuntime().GetPrefab(key);
        public Color GetColor(SkinColorKey key) => GetReadyRuntime().GetColor(key);

        public bool TryGetSprite(SkinSpriteKey key, out Sprite sprite)
        {
            sprite = null;
            return CurrentRuntime != null &&
                   !CurrentRuntime.IsDisposed &&
                   CurrentRuntime.SkinType == SkinCurrent.SkinType &&
                   CurrentRuntime.TryGetSprite(key, out sprite);
        }

        public bool TryGetPrefab(SkinPrefabKey key, out GameObject prefab)
        {
            prefab = null;
            return CurrentRuntime != null &&
                   !CurrentRuntime.IsDisposed &&
                   CurrentRuntime.SkinType == SkinCurrent.SkinType &&
                   CurrentRuntime.TryGetPrefab(key, out prefab);
        }

        public bool TryGetColor(SkinColorKey key, out Color color)
        {
            color = default;
            return CurrentRuntime != null &&
                   !CurrentRuntime.IsDisposed &&
                   CurrentRuntime.SkinType == SkinCurrent.SkinType &&
                   CurrentRuntime.TryGetColor(key, out color);
        }

        // Legacy synchronous API. Keep until its two existing call sites move to ApplySkinAsync.
        public void SaveSkinCurrent(SkinType skinType)
        {
            InvalidatePendingApply();
            SkinCurrent = _skinsConfig.GetSkinByType(skinType);
            SaveSelection(skinType);
            OnSkinChanged?.Invoke(SkinCurrent);
        }

        public void TrySaveRandomSelect(bool value)
        {
            SkinRandomSelect = value;
            PlayerPrefs.SetInt(PlayerPrefsKey.SkinSelectRandomKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        // Legacy synchronous API. Keep until MainMenuScreen starts awaiting ApplySkinAsync.
        public void SetSkinRandom()
        {
            SaveSkinCurrent(_skinsConfig.GetSkinRandom().SkinType);
            Debug.Log($"[SkinsService][SetSkinRandom] SkinCurrent = {SkinCurrent.SkinType}");
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            CancellationTokenSource activeRequest;
            lock (_requestGate)
            {
                ++_requestGeneration;
                activeRequest = _activeRequest;
                _activeRequest = null;
            }

            CancelSafely(activeRequest);
            CurrentRuntime?.Dispose();
            CurrentRuntime = null;
        }

        private SkinData GetConfiguredSkinOrThrow(SkinType skinType)
        {
            if (_skinsConfig.TryGetSkinByType(skinType, out SkinData skinData))
                return skinData;

            throw new InvalidOperationException($"Skin '{skinType}' is absent from SkinsConfig.");
        }

        private SkinRuntime GetReadyRuntime()
        {
            ThrowIfDisposed();

            if (CurrentRuntime == null ||
                CurrentRuntime.IsDisposed ||
                CurrentRuntime.SkinType != SkinCurrent.SkinType)
            {
                throw new InvalidOperationException(
                    "Current skin runtime is not ready. Await EnsureCurrentRuntimeAsync or ApplySkinAsync first.");
            }

            return CurrentRuntime;
        }

        private static void SaveSelection(SkinType skinType)
        {
            PlayerPrefs.SetInt(PlayerPrefsKey.SkinCurrent, (int)skinType);
            PlayerPrefs.Save();
        }

        private void NotifyRuntimeChanged(SkinRuntime runtime)
        {
            if (OnRuntimeChanged == null)
                return;

            foreach (Action<SkinRuntime> subscriber in OnRuntimeChanged.GetInvocationList())
            {
                try
                {
                    subscriber(runtime);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void NotifySkinChanged(SkinData skinData)
        {
            if (OnSkinChanged == null)
                return;

            foreach (Action<SkinData> subscriber in OnSkinChanged.GetInvocationList())
            {
                try
                {
                    subscriber(skinData);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void FinishRequest(CancellationTokenSource request)
        {
            lock (_requestGate)
            {
                if (ReferenceEquals(_activeRequest, request))
                    _activeRequest = null;
            }

            request.Dispose();
        }

        private void InvalidatePendingApply()
        {
            CancellationTokenSource request;
            lock (_requestGate)
            {
                ++_requestGeneration;
                request = _activeRequest;
                _activeRequest = null;
            }

            CancelSafely(request);
        }

        private static void CancelSafely(CancellationTokenSource request)
        {
            try
            {
                request?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request completed between being observed and canceled.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkinsService));
        }
    }
}
