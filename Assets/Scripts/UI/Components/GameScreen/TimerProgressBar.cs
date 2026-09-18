using Core.Events;
using Core.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Components
{
    public class TimerProgressBar : MonoBehaviour
    {
        private const float DtDelay = 1.0f; // Интервал 1 секунда
        
        [SerializeField] private Slider _slider;
        [SerializeField] private Image _mainBackground;
        [SerializeField] private TextMeshProUGUI _progressText;
        [SerializeField] private int _timeExpire = 10;
        [SerializeField] private Color _timeColor = Color.white;
        [SerializeField] private Color _timeColorExpire = Color.coral;

        [Inject] private IVibrationService _vibrationService;
        
        private float _dtTimer = 0.0f; // Инкрементный счетчик времени (дельтатайм) (при достижении DtDelay увеличивается _secondsCounter)
        private bool _bRun = false; // Старт/Пауза таймера

        private void Awake()
        {
            _slider.value = 0;
            //SetTargetValue(10, true); //test
            _progressText.text = "";
            _progressText.color = _timeColor;
        }

        // Update is called once per frame
        private void Update()
        {
            if (_bRun)
            {
                _dtTimer += Time.deltaTime;
                if (_dtTimer >= DtDelay)
                {
                    _dtTimer = 0;
                    ++_slider.value;
                    SetFormatMMSS((int)(_slider.maxValue - _slider.value));
                    if (_slider.value >= _slider.maxValue)
                    {
                        StopTimer();
                        EventBus.Raise(new TimeExpiredEvent()); 
                    }
                }
            }
        }
        
        public void SetTargetValue(float value, bool autostartTimer = false)
        {
            if (value > 0)
            {
                _slider.maxValue = value;
                SetFormatMMSS((int)value);
                
                if (autostartTimer)
                    StartTimer();
            }
        }
                
        public void StartTimer() => _bRun = true;

        public void StopTimer() => _bRun = false;

        public void ResetTimer()
        {
            _bRun = false;
            _dtTimer = 0;
            _slider.value = 0;
            _progressText.text = "";
            _progressText.color = _timeColor;
        }

        public void SetCurrentValue(float value)
        {
            _slider.value = value;
            SetFormatMMSS((int)(_slider.maxValue - value));
        }

        public float GetCurrentValue() => _slider.value;
        
        private void SetFormatMMSS(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int m = seconds / 60;
            int s = seconds % 60;
            _progressText.text = $"{m:00}:{s:00}";
            if (seconds <= _timeExpire)
            {
                _progressText.color = _timeColorExpire;
                _vibrationService.Play(VibrationType.Warning);
            }
        }
    }
}