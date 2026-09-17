using Core.Config;
using Core.Services;
using UnityEngine;
using UnityEngine.UI;

namespace UI.SkinBindings
{
    [RequireComponent(typeof(Image))]
    public sealed class SkinImageColor : SkinBindingBase
    {
        [SerializeField] private SkinColorKey _key;
        [SerializeField] private Image _image;

        public override bool CanApply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_image == null)
                return ReportMissing("Image component", nameof(_image), runtime);
            if (_key == SkinColorKey.None)
                return ReportMissing("color", SkinColorKey.None, runtime);

            return runtime.TryGetColor(_key, out _) || ReportMissing("color", _key, runtime);
        }

        public override void Apply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_image != null && runtime.TryGetColor(_key, out Color color))
                _image.color = color;
        }

        private void Reset() => EnsureReferences();
        private void OnValidate() => EnsureReferences();

        private void EnsureReferences()
        {
            if (_image == null)
                _image = GetComponent<Image>();
        }
    }
}
