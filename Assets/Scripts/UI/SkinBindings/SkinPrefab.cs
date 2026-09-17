using System;
using Core.Data;
using Core.Services;
using UnityEngine;

namespace UI.SkinBindings
{
    public sealed class SkinPrefab : SkinBindingBase
    {
        [SerializeField] private SkinPrefabKey _key;
        [SerializeField] private Transform _container;

        private GameObject _instance;

        public override bool CanApply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_container == null)
                return ReportMissing("container", nameof(_container), runtime);
            if (_key == SkinPrefabKey.None)
                return ReportMissing("prefab", SkinPrefabKey.None, runtime);

            return runtime.TryGetPrefab(_key, out _) || ReportMissing("prefab", _key, runtime);
        }

        public override void Apply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_container == null || !runtime.TryGetPrefab(_key, out GameObject prefab))
                return;

            GameObject newInstance;
            try
            {
                newInstance = Instantiate(prefab, _container, false);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[SkinPrefab] Could not instantiate prefab '{_key}' for skin '{runtime.SkinType}': {exception}",
                    this);
                return;
            }

            if (newInstance == null)
            {
                Debug.LogError(
                    $"[SkinPrefab] Instantiation returned null for prefab '{_key}' in skin '{runtime.SkinType}'.",
                    this);
                return;
            }

            GameObject previousInstance = _instance;
            _instance = newInstance;

            if (previousInstance != null)
            {
                previousInstance.SetActive(false);
                Destroy(previousInstance);
            }
        }

        private void Reset() => EnsureReferences();
        private void OnValidate() => EnsureReferences();

        private void EnsureReferences()
        {
            if (_container == null)
                _container = transform;
        }
    }
}
