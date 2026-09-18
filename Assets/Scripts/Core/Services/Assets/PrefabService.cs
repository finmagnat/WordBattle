using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Core.Services
{
    public class PrefabService : IPrefabService
    {
        private readonly AddressablesLoader _loader;

        public PrefabService(AddressablesLoader loader)
        {
            _loader = loader;
        }

        public async UniTask<GameObject> GetPrefabAsync(string key)
            => await _loader.LoadAssetAsync<GameObject>(key);

        public bool IsLoaded(string key)
            => _loader.IsLoaded(key);

        public void Unload(string key)
            => _loader.Unload(key);
    }

    public interface IPrefabService
    {
        UniTask<GameObject> GetPrefabAsync(string key);
        bool IsLoaded(string key);
        void Unload(string key);
    }
}
