using Core.Events;
using DG.Tweening;
using UnityEngine;

namespace UI.Components
{
    [DisallowMultipleComponent]
    public class TimerWarningReaction : MonoBehaviour
    {
        [SerializeField] private TimerProgressBar _timer;
        [SerializeField] private Transform _target;
        [SerializeField, Min(1f)] private float _scale = 1.12f;

        // Two 90 ms beats separated by 70 ms, matching Warning vibration.
        private const float HalfBeatDuration = 0.045f;
        private const float BeatGap = 0.07f;

        private Vector3 _baseScale;
        private Sequence _sequence;

        private void Awake()
        {
            if (_target == null)
                _target = transform;
            _baseScale = _target.localScale;
        }

        private void OnEnable() => EventBus.Subscribe<TimerWarningEvent>(OnTimerWarning);

        private void OnDisable()
        {
            EventBus.Unsubscribe<TimerWarningEvent>(OnTimerWarning);
            StopPulse();
        }

        private void OnTimerWarning(TimerWarningEvent evt)
        {
            if (_timer == null || evt.Timer != _timer)
                return;

            StopPulse();
            if (!evt.IsHeartbeat)
                return;

            _sequence = DOTween.Sequence();
            _sequence.Append(_target.DOScale(_baseScale * _scale, HalfBeatDuration).SetEase(Ease.OutSine));
            _sequence.Append(_target.DOScale(_baseScale, HalfBeatDuration).SetEase(Ease.InSine));
            _sequence.AppendInterval(BeatGap);
            _sequence.Append(_target.DOScale(_baseScale * _scale, HalfBeatDuration).SetEase(Ease.OutSine));
            _sequence.Append(_target.DOScale(_baseScale, HalfBeatDuration).SetEase(Ease.InSine));
        }

        private void StopPulse()
        {
            _sequence?.Kill();
            _sequence = null;
            if (_target != null)
                _target.localScale = _baseScale;
        }
    }
}
