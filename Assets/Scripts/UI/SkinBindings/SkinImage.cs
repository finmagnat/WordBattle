using Core.Data;
using Core.Services;
using UnityEngine;
using UnityEngine.UI;

namespace UI.SkinBindings
{
    [RequireComponent(typeof(Image))]
    public sealed class SkinImage : SkinBindingBase
    {
        [SerializeField] private SkinSpriteKey _key;
        [SerializeField] private Image _image;

        public override bool CanApply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_image == null)
                return ReportMissing("Image component", nameof(_image), runtime);
            if (_key == SkinSpriteKey.None)
                return ReportMissing("sprite", SkinSpriteKey.None, runtime);

            return runtime.TryGetSprite(_key, out _) || ReportMissing("sprite", _key, runtime);
        }

        public override void Apply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_image != null && runtime.TryGetSprite(_key, out Sprite sprite))
                _image.sprite = sprite;
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
