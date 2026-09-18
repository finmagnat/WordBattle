using Core.Data;
using Core.Services;
using UnityEngine;
using UnityEngine.UI;

namespace UI.SkinBindings
{
    [RequireComponent(typeof(Image))]
    public sealed class SkinImage : SkinBindingBase, ISkinBinding<SkinSpriteKey>
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

        public bool Apply(SkinSpriteKey key)
        {
            if (!TryGetCurrentRuntime(out SkinRuntime runtime))
                return false;

            SkinSpriteKey previousKey = _key;
            _key = key;

            if (!CanApply(runtime))
            {
                _key = previousKey;
                return false;
            }

            Apply(runtime);
            return true;
        }

        private void Reset() => EnsureReferences();
        private void OnValidate() => EnsureReferences();

        private void EnsureReferences()
        {
            // SkinImage always controls the Image on the same GameObject. Re-resolving it also
            // prevents copied components from retaining a reference to another button's Image.
            _image = GetComponent<Image>();
        }
    }
}
