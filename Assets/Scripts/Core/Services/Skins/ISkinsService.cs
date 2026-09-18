using System;
using System.Threading;
using Core.Config;
using Core.Data;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Core.Services
{
    public interface ISkinsService : IService
    {
        event Action<SkinData> OnSkinChanged;
        event Action<SkinRuntime> OnRuntimeChanged;

        SkinData SkinCurrent { get; }
        SkinRuntime CurrentRuntime { get; }
        bool SkinRandomSelect { get; }
        SkinsConfig Config { get; }

        UniTask InitializeAsync();
        UniTask<bool> ApplySkinAsync(SkinType skinType, CancellationToken cancellationToken = default);
        UniTask<SkinRuntime> EnsureCurrentRuntimeAsync(CancellationToken cancellationToken = default);

        Sprite GetSprite(SkinSpriteKey key);
        GameObject GetPrefab(SkinPrefabKey key);
        Color GetColor(SkinColorKey key);
        bool TryGetSprite(SkinSpriteKey key, out Sprite sprite);
        bool TryGetPrefab(SkinPrefabKey key, out GameObject prefab);
        bool TryGetColor(SkinColorKey key, out Color color);

        void SaveSkinCurrent(SkinType skinType);
        void TrySaveRandomSelect(bool value);
        void SetSkinRandom();
    }
}
