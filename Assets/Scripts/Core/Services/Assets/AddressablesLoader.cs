using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Core.Services
{
    public class AddressablesLoader
    {
        private readonly Dictionary<string, AsyncOperationHandle> _loaded = new();

        public async UniTask<T> LoadCachedAsync<T>(string key) where T : UnityEngine.Object
            => await LoadAssetAsync<T>(key);

        /// <summary>
        /// Loads an Addressable asset and reuses an existing operation for the same key.
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(string key) where T : class
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("Addressables key cannot be null or empty.");
                return null;
            }

            if (_loaded.TryGetValue(key, out var cached))
                return await AwaitResultAsync<T>(key, cached);

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
            _loaded[key] = handle;

            return await AwaitResultAsync<T>(key, handle);
        }

        private async UniTask<T> AwaitResultAsync<T>(string key, AsyncOperationHandle handle) where T : class
        {
            try
            {
                await handle.ToUniTask();
            }
            catch (Exception exception)
            {
                ReleaseIfCurrent(key, handle);
                Debug.LogError($"Addressables failed to load key '{key}': {exception.Message}");
                return null;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                ReleaseIfCurrent(key, handle);
                Debug.LogError($"Addressables failed to load key: {key}");
                return null;
            }

            if (handle.Result is T result)
                return result;

            Debug.LogError(
                $"Addressable '{key}' was loaded as {handle.Result?.GetType().Name ?? "null"}, " +
                $"but {typeof(T).Name} was requested.");
            return null;
        }

        private void ReleaseIfCurrent(string key, AsyncOperationHandle handle)
        {
            if (!_loaded.TryGetValue(key, out var current) || !current.Equals(handle))
                return;

            _loaded.Remove(key);

            if (handle.IsValid())
                Addressables.Release(handle);
        }

        public bool IsLoaded(string key)
        {
            return _loaded.TryGetValue(key, out var handle)
                   && handle.IsValid()
                   && handle.IsDone
                   && handle.Status == AsyncOperationStatus.Succeeded;
        }

        public void Unload(string key)
        {
            if (_loaded.TryGetValue(key, out var handle))
            {
                if (handle.IsValid())
                    Addressables.Release(handle);

                _loaded.Remove(key);
            }
        }

        public void ReleaseAll()
        {
            foreach (var handle in _loaded.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }

            _loaded.Clear();
        }
    }
}
