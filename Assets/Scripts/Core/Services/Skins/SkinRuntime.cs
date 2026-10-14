using System;
using System.Collections.Generic;
using Core.Config;
using Core.Data;
using UnityEngine;

namespace Core.Services
{
    public sealed class SkinRuntime : IDisposable
    {
        private readonly Dictionary<SkinSpriteKey, Sprite> _sprites;
        private readonly Dictionary<SkinPrefabKey, GameObject> _prefabs;
        private readonly Dictionary<SkinColorKey, Color> _colors;
        private readonly List<IDisposable> _leases;
        private readonly object _lifetimeGate = new();
        private int _referenceCount = 1;
        private bool _ownerReleased;
        private bool _isDisposed;

        internal SkinRuntime(
            SkinType skinType,
            Dictionary<SkinSpriteKey, Sprite> sprites,
            Dictionary<SkinPrefabKey, GameObject> prefabs,
            Dictionary<SkinColorKey, Color> colors,
            List<IDisposable> leases)
        {
            SkinType = skinType;
            _sprites = sprites;
            _prefabs = prefabs;
            _colors = colors;
            _leases = leases;
        }

        public SkinType SkinType { get; }
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// Keeps this runtime's assets alive for a synchronous UI consumer even after the service
        /// releases its owner reference during a later skin switch.
        /// </summary>
        public IDisposable Retain()
        {
            lock (_lifetimeGate)
            {
                if (_ownerReleased || _isDisposed)
                    throw new ObjectDisposedException(nameof(SkinRuntime), $"Skin runtime '{SkinType}' is released.");

                ++_referenceCount;
                return new Retention(this);
            }
        }

        public Sprite GetSprite(SkinSpriteKey key)
        {
            ThrowIfDisposed();
            return _sprites.TryGetValue(key, out Sprite sprite)
                ? sprite
                : throw new KeyNotFoundException($"Skin sprite key '{key}' is absent in runtime '{SkinType}'.");
        }

        public GameObject GetPrefab(SkinPrefabKey key)
        {
            ThrowIfDisposed();
            return _prefabs.TryGetValue(key, out GameObject prefab)
                ? prefab
                : throw new KeyNotFoundException($"Skin prefab key '{key}' is absent in runtime '{SkinType}'.");
        }

        public Color GetColor(SkinColorKey key)
        {
            ThrowIfDisposed();
            return _colors.TryGetValue(key, out Color color)
                ? color
                : throw new KeyNotFoundException($"Skin color key '{key}' is absent in runtime '{SkinType}'.");
        }

        public bool TryGetSprite(SkinSpriteKey key, out Sprite sprite)
        {
            ThrowIfDisposed();
            return _sprites.TryGetValue(key, out sprite);
        }

        public bool TryGetPrefab(SkinPrefabKey key, out GameObject prefab)
        {
            ThrowIfDisposed();
            return _prefabs.TryGetValue(key, out prefab);
        }

        public bool TryGetColor(SkinColorKey key, out Color color)
        {
            ThrowIfDisposed();
            return _colors.TryGetValue(key, out color);
        }

        public void Dispose()
        {
            bool releaseResources;
            lock (_lifetimeGate)
            {
                if (_ownerReleased)
                    return;

                _ownerReleased = true;
                releaseResources = --_referenceCount == 0;
                if (releaseResources)
                    _isDisposed = true;
            }

            if (releaseResources)
                ReleaseResources();
        }

        private void ReleaseRetention()
        {
            bool releaseResources;
            lock (_lifetimeGate)
            {
                if (_referenceCount <= 0)
                    return;

                releaseResources = --_referenceCount == 0;
                if (releaseResources)
                    _isDisposed = true;
            }

            if (releaseResources)
                ReleaseResources();
        }

        private void ReleaseResources()
        {
            for (int i = _leases.Count - 1; i >= 0; i--)
                _leases[i]?.Dispose();

            _leases.Clear();
            _sprites.Clear();
            _prefabs.Clear();
            _colors.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkinRuntime), $"Skin runtime '{SkinType}' is disposed.");
        }

        private sealed class Retention : IDisposable
        {
            private SkinRuntime _runtime;

            public Retention(SkinRuntime runtime)
            {
                _runtime = runtime;
            }

            public void Dispose()
            {
                SkinRuntime runtime = _runtime;
                if (runtime == null)
                    return;

                _runtime = null;
                runtime.ReleaseRetention();
            }
        }
    }
}
