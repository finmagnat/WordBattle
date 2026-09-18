using System;
using System.Collections.Generic;
using System.Threading;
using Core.Config;
using Core.Data;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Core.Services
{
    public sealed class SkinRuntimeProvider
    {
        private readonly AddressablesLoader _addressables;

        public SkinRuntimeProvider(AddressablesLoader addressables)
        {
            _addressables = addressables;
        }

        public async UniTask<SkinRuntime> CreateAsync(
            SkinData skinData,
            CancellationToken cancellationToken = default)
        {
            Dictionary<SkinSpriteKey, string> spriteAliases = ValidateSprites(skinData);
            Dictionary<SkinPrefabKey, string> prefabAliases = ValidatePrefabs(skinData);
            Dictionary<SkinColorKey, Color> colors = ValidateColors(skinData);

            var sprites = new Dictionary<SkinSpriteKey, Sprite>();
            var prefabs = new Dictionary<SkinPrefabKey, GameObject>();
            var leases = new List<IDisposable>();
            var spritesByAlias = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var prefabsByAlias = new Dictionary<string, GameObject>(StringComparer.Ordinal);

            try
            {
                foreach (KeyValuePair<SkinSpriteKey, string> entry in spriteAliases)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!spritesByAlias.TryGetValue(entry.Value, out Sprite sprite))
                    {
                        AddressableLease<Sprite> lease =
                            await _addressables.AcquireAsync<Sprite>(entry.Value, cancellationToken);
                        leases.Add(lease);
                        sprite = lease.Asset;
                        spritesByAlias.Add(entry.Value, sprite);
                    }

                    sprites.Add(entry.Key, sprite);
                }

                foreach (KeyValuePair<SkinPrefabKey, string> entry in prefabAliases)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!prefabsByAlias.TryGetValue(entry.Value, out GameObject prefab))
                    {
                        AddressableLease<GameObject> lease =
                            await _addressables.AcquireAsync<GameObject>(entry.Value, cancellationToken);
                        leases.Add(lease);
                        prefab = lease.Asset;
                        prefabsByAlias.Add(entry.Value, prefab);
                    }

                    prefabs.Add(entry.Key, prefab);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new SkinRuntime(skinData.SkinType, sprites, prefabs, colors, leases);
            }
            catch
            {
                for (int i = leases.Count - 1; i >= 0; i--)
                    leases[i]?.Dispose();

                throw;
            }
        }

        private static Dictionary<SkinSpriteKey, string> ValidateSprites(SkinData skinData)
        {
            var result = new Dictionary<SkinSpriteKey, string>();
            if (skinData.Sprites == null)
                return result;

            foreach (SkinSpriteEntry entry in skinData.Sprites)
            {
                if (entry.Key == SkinSpriteKey.None)
                    throw ConfigurationError(skinData, "sprite entry uses the None key");
                if (string.IsNullOrWhiteSpace(entry.Alias))
                    throw ConfigurationError(skinData, $"sprite '{entry.Key}' has an empty alias");
                if (!result.TryAdd(entry.Key, entry.Alias))
                    throw ConfigurationError(skinData, $"sprite key '{entry.Key}' is duplicated");
            }

            return result;
        }

        private static Dictionary<SkinPrefabKey, string> ValidatePrefabs(SkinData skinData)
        {
            var result = new Dictionary<SkinPrefabKey, string>();
            if (skinData.Prefabs == null)
                return result;

            foreach (SkinPrefabEntry entry in skinData.Prefabs)
            {
                if (entry.Key == SkinPrefabKey.None)
                    throw ConfigurationError(skinData, "prefab entry uses the None key");
                if (string.IsNullOrWhiteSpace(entry.Alias))
                    throw ConfigurationError(skinData, $"prefab '{entry.Key}' has an empty alias");
                if (!result.TryAdd(entry.Key, entry.Alias))
                    throw ConfigurationError(skinData, $"prefab key '{entry.Key}' is duplicated");
            }

            return result;
        }

        private static Dictionary<SkinColorKey, Color> ValidateColors(SkinData skinData)
        {
            var result = new Dictionary<SkinColorKey, Color>();
            if (skinData.Colors == null)
                return result;

            foreach (SkinColorEntry entry in skinData.Colors)
            {
                if (entry.Key == SkinColorKey.None)
                    throw ConfigurationError(skinData, "color entry uses the None key");
                if (!result.TryAdd(entry.Key, entry.Value))
                    throw ConfigurationError(skinData, $"color key '{entry.Key}' is duplicated");
            }

            return result;
        }

        private static InvalidOperationException ConfigurationError(SkinData skinData, string details)
            => new($"Invalid generic skin data for '{skinData.SkinType}': {details}.");
    }
}
