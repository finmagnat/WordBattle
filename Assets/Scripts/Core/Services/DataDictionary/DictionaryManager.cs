using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;
using UnityEngine.Localization;

namespace Core.Services.DataDictionary
{
    public class DictionaryManager
    {
        private readonly DictionaryService _dictionaryService;
        private readonly DictionaryPatchService _patchService;
        private readonly LocalizationService _localization;
        private readonly Dictionary<string, LanguageDictionaryConfig> _configs = new();

        public DictionaryService Service => _dictionaryService;

        [Inject]
        public DictionaryManager(
            DictionaryService dictionaryService,
            DictionaryPatchService patchService,
            LocalizationService localization,
            List<LanguageDictionaryConfig> configs)
        {
            _dictionaryService = dictionaryService;
            _patchService = patchService;
            _localization = localization;
            
            foreach (var cfg in configs)
                _configs[cfg.languageCode] = cfg;
        }

        public UniTask InitializeAsync()
        {
            // подписываемся на смену языка
            _localization.OnLocaleChanged += OnLocaleChanged;
            return UniTask.CompletedTask;
        }

        public void Destroy()
        {
            _localization.OnLocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged(Locale _)
        {
            EnsureCurrentLocaleLoadedAsync().Forget();
        }

        public async UniTask EnsureCurrentLocaleLoadedAsync()
        {
            string code = _localization.CurrentLocale.Identifier.Code;

            if (!TryGetConfigForLocale(code, out var cfg))
            {
                Debug.LogError($"❌ No dictionary config for locale '{code}' (also tried language fallback).");
                return;
            }

            await _dictionaryService.InitializeAsync(cfg);
            if (_dictionaryService.IsLoaded)
                await _patchService.ApplyLatestPatchAsync(cfg.languageCode, _dictionaryService);

            Debug.Log($"📚 Dictionary switched → {cfg.languageCode} (locale was {code})");
        }
        
        private bool TryGetConfigForLocale(string localeCode, out LanguageDictionaryConfig cfg)
        {
            // 1) точное совпадение: "en-US"
            if (_configs.TryGetValue(localeCode, out cfg))
                return true;

            // 2) fallback по языку: "en-US" -> "en"
            var dash = localeCode.IndexOf('-');
            if (dash > 0)
            {
                var langOnly = localeCode.Substring(0, dash);
                if (_configs.TryGetValue(langOnly, out cfg))
                    return true;
            }

            cfg = null;
            return false;
        }

    }
}
