using Core.Services;
using UnityEngine;
using Zenject;

namespace UI.SkinBindings
{
    public abstract class SkinBindingBase : MonoBehaviour, ISkinBinding
    {
        private ISkinsService _skinsService;

        [Inject]
        public void Construct(ISkinsService skinsService)
        {
            _skinsService = skinsService;
        }

        public abstract bool CanApply(SkinRuntime runtime);
        public abstract void Apply(SkinRuntime runtime);

        protected bool TryGetCurrentRuntime(out SkinRuntime runtime)
        {
            runtime = _skinsService?.CurrentRuntime;
            if (runtime != null &&
                !runtime.IsDisposed &&
                runtime.SkinType == _skinsService.SkinCurrent.SkinType)
                return true;

            Debug.LogError(
                $"[{GetType().Name}] Cannot apply a key before the current skin runtime is ready. " +
                "Ensure that the owning prefab was injected and PrepareAsync completed.",
                this);
            return false;
        }

        protected bool ReportMissing(string resourceType, object key, SkinRuntime runtime)
        {
            Debug.LogWarning(
                $"[{GetType().Name}] {resourceType} key '{key}' is not configured in skin '{runtime.SkinType}'. " +
                "The existing UI value was left unchanged.",
                this);
            return false;
        }
    }
}
