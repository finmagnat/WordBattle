using Core.Config;
using Core.Services;
using TMPro;
using UnityEngine;

namespace UI.SkinBindings
{
    public sealed class SkinTMPColor : SkinBindingBase
    {
        [SerializeField] private SkinColorKey _key;
        [SerializeField] private TMP_Text _text;

        public override bool CanApply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_text == null)
                return ReportMissing("TMP_Text component", nameof(_text), runtime);
            if (_key == SkinColorKey.None)
                return ReportMissing("color", SkinColorKey.None, runtime);

            return runtime.TryGetColor(_key, out _) || ReportMissing("color", _key, runtime);
        }

        public override void Apply(SkinRuntime runtime)
        {
            EnsureReferences();
            if (_text != null && runtime.TryGetColor(_key, out Color color))
                _text.color = color;
        }

        private void Reset() => EnsureReferences();
        private void OnValidate() => EnsureReferences();

        private void EnsureReferences()
        {
            if (_text == null)
                _text = GetComponent<TMP_Text>();
        }
    }
}
