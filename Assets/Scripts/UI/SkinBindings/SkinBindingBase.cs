using Core.Services;
using UnityEngine;

namespace UI.SkinBindings
{
    public abstract class SkinBindingBase : MonoBehaviour, ISkinBinding
    {
        public abstract bool CanApply(SkinRuntime runtime);
        public abstract void Apply(SkinRuntime runtime);

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
