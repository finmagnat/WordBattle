using System;
using System.Collections.Generic;
using Core.Services;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace UI.SkinBindings
{
    public sealed class SkinBindingGroup : MonoBehaviour
    {
        private readonly List<ISkinBinding> _bindings = new();
        private ISkinsService _skinsService;
        private SkinRuntime _appliedRuntime;
        private IDisposable _runtimeRetention;
        private bool _isInjected;

        [Inject]
        public void Construct(ISkinsService skinsService)
        {
            if (_skinsService != null)
                _skinsService.OnRuntimeChanged -= OnRuntimeChanged;

            _skinsService = skinsService;
            _skinsService.OnRuntimeChanged += OnRuntimeChanged;
            _isInjected = true;
            CacheBindings();
        }

        public async UniTask PrepareAsync()
        {
            if (!_isInjected || _skinsService == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SkinBindingGroup)} on '{name}' must be injected before PrepareAsync is called.");
            }

            SkinRuntime runtime = await _skinsService.EnsureCurrentRuntimeAsync(
                this.GetCancellationTokenOnDestroy());

            if (!ApplyRuntime(runtime))
            {
                throw new InvalidOperationException(
                    $"Skin bindings on '{name}' cannot apply runtime '{runtime.SkinType}'. See preceding warnings.");
            }
        }

        public void RefreshBindings() => CacheBindings();

        private void OnDestroy()
        {
            if (_skinsService != null)
                _skinsService.OnRuntimeChanged -= OnRuntimeChanged;

            _runtimeRetention?.Dispose();
            _runtimeRetention = null;
            _appliedRuntime = null;
        }

        private void OnRuntimeChanged(SkinRuntime runtime)
        {
            ApplyRuntime(runtime);
        }

        private bool ApplyRuntime(SkinRuntime runtime)
        {
            if (runtime == null)
            {
                Debug.LogError($"[{nameof(SkinBindingGroup)}] Cannot apply a null runtime on '{name}'.", this);
                return false;
            }

            CacheBindings();

            bool canApplyAll = true;
            foreach (ISkinBinding binding in _bindings)
                canApplyAll &= binding.CanApply(runtime);

            if (!canApplyAll)
            {
                Debug.LogError(
                    $"[{nameof(SkinBindingGroup)}] Runtime '{runtime.SkinType}' was not applied to '{name}' " +
                    "because at least one binding is invalid. Existing values were left unchanged.",
                    this);
                return false;
            }

            IDisposable newRetention = null;
            try
            {
                newRetention = runtime.Retain();

                foreach (ISkinBinding binding in _bindings)
                    binding.Apply(runtime);
            }
            catch (Exception exception)
            {
                newRetention?.Dispose();
                Debug.LogException(exception, this);
                RestorePreviousRuntime();
                return false;
            }

            IDisposable previousRetention = _runtimeRetention;
            _runtimeRetention = newRetention;
            _appliedRuntime = runtime;
            previousRetention?.Dispose();

            return true;
        }

        private void RestorePreviousRuntime()
        {
            if (_appliedRuntime == null || _appliedRuntime.IsDisposed)
                return;

            try
            {
                foreach (ISkinBinding binding in _bindings)
                {
                    if (binding.CanApply(_appliedRuntime))
                        binding.Apply(_appliedRuntime);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[{nameof(SkinBindingGroup)}] Failed to restore previous runtime " +
                    $"'{_appliedRuntime.SkinType}' on '{name}': {exception}",
                    this);
            }
        }

        private void CacheBindings()
        {
            _bindings.Clear();

            MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour component in components)
            {
                if (component != this && component is ISkinBinding binding)
                    _bindings.Add(binding);
            }
        }
    }
}
