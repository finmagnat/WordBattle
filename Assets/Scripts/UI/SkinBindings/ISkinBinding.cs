using Core.Services;

namespace UI.SkinBindings
{
    public interface ISkinBinding
    {
        bool CanApply(SkinRuntime runtime);
        void Apply(SkinRuntime runtime);
    }
}
