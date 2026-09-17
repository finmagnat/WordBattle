using Core.Services;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Zenject;

namespace UI.Popups
{
    public sealed class ForceUpdatePopup : UIPopup
    {
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _updateButton;
        [SerializeField] private TextMeshProUGUI _updateButtonText;
        [SerializeField] private GameObject _closeButton;

        [Inject] private LocalizationService _localization;

        protected override void Awake()
        {
            base.Awake();

            if (_closeButton != null)
                _closeButton.SetActive(false);

            if (_updateButton != null)
            {
                _updateButton.onClick.RemoveAllListeners();
                _updateButton.onClick.AddListener(OpenGooglePlay);
            }
        }

        private void OnDestroy()
        {
            if (_updateButton != null)
                _updateButton.onClick.RemoveListener(OpenGooglePlay);
        }

        protected override UniTask BeforeShowAnimationAsync()
        {
            _titleText.text = _localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyForceUpdateTitle);
            _messageText.text = _localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyForceUpdateText);
            _updateButtonText.text = _localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyForceUpdateButton);
            return UniTask.CompletedTask;
        }

        private static void OpenGooglePlay()
        {
            string url = GetGooglePlayUrl(Application.identifier);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("[UpdateGate] Application.identifier is empty; cannot open Google Play.");
                return;
            }

            Application.OpenURL(url);
        }

        public static string GetGooglePlayUrl(string packageId)
        {
            return string.IsNullOrWhiteSpace(packageId)
                ? string.Empty
                : $"https://play.google.com/store/apps/details?id={UnityWebRequest.EscapeURL(packageId)}";
        }
    }
}
