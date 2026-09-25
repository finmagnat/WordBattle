using Core.Services;
using UnityEngine;
using Zenject;

namespace UI.SkinBindings
{
    public abstract class SkinBindingBase : MonoBehaviour, ISkinBinding
    {
        private ISkinsService _skinsService;
        private SkinBindingGroup _group;

        [Inject]
        public void Construct(ISkinsService skinsService)
        {
            _skinsService = skinsService;
        }

        public abstract bool CanApply(SkinRuntime runtime);
        public abstract void Apply(SkinRuntime runtime);

        protected virtual void Start()
        {
            // A binding can be instantiated after its parent group has already been prepared.
            // Registering here lets the group apply the same runtime it currently owns.
            _group = GetComponentInParent<SkinBindingGroup>();
            _group?.TryApplyNewBinding(this);
        }

        protected virtual void OnDestroy() => _group?.UnregisterBinding(this);

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
