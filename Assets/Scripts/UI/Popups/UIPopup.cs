using System;
using Core.UI;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UI.SkinBindings;
using UnityEngine;

namespace UI.Popups
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPopup : MonoBehaviour
    {
        protected CanvasGroup _canvasGroup;
        protected RectTransform _rect;

        public bool IsVisible { get; protected set; }
        public event Action OnShowStarted;
        public event Action OnShowCompleted;
        public event Action OnHideStarted;
        public event Action OnHideCompleted;

        [Header("Animation Settings")]
        [SerializeField] private float _fadeDuration = 0.25f;
        [SerializeField] private float _scaleDuration = 0.3f;
        [SerializeField] private float _scalePunch = 0.05f;
        [SerializeField] private RectTransform _scaleTransform;

        private bool _initialized;

        protected virtual void Awake()
        {
            Initialize();
            gameObject.SetActive(false);
        }

        protected void Initialize()
        {
            if (_initialized) return;

            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            if (_scaleTransform != null)
                _rect = _scaleTransform;
            else if (_rect == null)
                _rect = GetComponent<RectTransform>();

            SetHiddenStateImmediate();

            _initialized = true;
        }

        protected virtual void SetHiddenStateImmediate()
        {
            _canvasGroup.alpha = 0f;
            _rect.localScale = Vector3.one * 0.8f;
        }

        protected virtual void SetShownStateImmediate()
        {
            _canvasGroup.alpha = 1f;
            _rect.localScale = Vector3.one;
        }

        public virtual async UniTask ShowAsync()
        {
            Initialize();
            await PrepareSkinBindingsAsync();

            OnShowStarted?.Invoke();
            gameObject.SetActive(true);

            SetHiddenStateImmediate();
            await BeforeShowAnimationAsync();

            var seq = DOTween.Sequence();
            seq.Join(_canvasGroup.DOFade(1f, _fadeDuration));
            seq.Join(_rect.DOScale(1f, _scaleDuration).SetEase(Ease.OutBack));

            await seq.AsyncWaitForCompletion();

            IsVisible = true;
            OnShowCompleted?.Invoke();
        }

        public virtual async UniTask HideAsync()
        {
            Initialize();

            OnHideStarted?.Invoke();

            var seq = DOTween.Sequence();
            seq.Join(_canvasGroup.DOFade(0f, _fadeDuration));
            seq.Join(_rect.DOScale(1f - _scalePunch, _scaleDuration));

            await seq.AsyncWaitForCompletion();

            gameObject.SetActive(false);
            IsVisible = false;
            OnHideCompleted?.Invoke();
        }

        protected virtual UniTask BeforeShowAnimationAsync()
        {
            return UniTask.CompletedTask;
        }

        private async UniTask PrepareSkinBindingsAsync()
        {
            SkinBindingGroup[] groups = GetComponentsInChildren<SkinBindingGroup>(true);
            foreach (SkinBindingGroup group in groups)
                await group.PrepareAsync();
        }
    }

    public abstract class UIPopup<TPayload> : UIPopup, IUIElement<TPayload>
    {
        public abstract UniTask PrepareAsync(TPayload payload);
    }
}
