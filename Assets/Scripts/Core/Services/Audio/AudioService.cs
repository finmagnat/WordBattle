using System.Collections.Generic;
using Core.Config;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Core.Services
{
    public class AudioService : IAudioService
    {
        public float MasterVolume;

        private readonly AddressablesLoader _loader;
        private readonly ConfigService _configService;
        private readonly Dictionary<string, AudioClip> _sfxCache = new();

        private AudioSource _sfxSource;
        private float _sfxVolume = 1f;
        
        public AudioService(AddressablesLoader loader, ConfigService configService)
        {
            _loader = loader;
            _configService = configService;
        }

        public async UniTask InitializeAsync()
        {
            MasterVolume = PlayerPrefs.GetFloat(PlayerPrefsKey.MasterVolume, 1f);
            SetSfxVolume(MasterVolume);

            var go = new GameObject("[AudioService]");
            Object.DontDestroyOnLoad(go);

            _sfxSource = go.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = _sfxVolume;

            if (_configService.Sounds.IsUseSoundsConfig)
                InitDebugCache();

            await PreloadSfxAsync(SoundsConfig.ButtonClick);
        }

        public async UniTask PlaySfxAsync(string addressKey)
        {
            if (string.IsNullOrEmpty(addressKey) || _sfxVolume < 0.1f)
                return;

            if (TryPlayCachedSfx(addressKey))
                return;

            var clip = await GetOrLoadSfxAsync(addressKey);
            if (clip == null)
                return;

            await EnsureAudioDataLoadedAsync(clip);
            PlayClip(clip);
        }

        public async UniTask PreloadSfxAsync(string addressKey)
        {
            if (string.IsNullOrEmpty(addressKey))
                return;

            var clip = await GetOrLoadSfxAsync(addressKey);
            await EnsureAudioDataLoadedAsync(clip);
        }

        private async UniTask<AudioClip> GetOrLoadSfxAsync(string addressKey)
        {
            if (_sfxCache.TryGetValue(addressKey, out var cachedClip))
                return cachedClip;

            
            var clip = await _loader.LoadAssetAsync<AudioClip>(_configService.Sounds.GetAddressKey(addressKey));
            if (clip == null)
            {
                Debug.LogWarning($"Audio clip not found: {addressKey}");
                return null;
            }

            _sfxCache[addressKey] = clip;
            return clip;
        }

        private bool TryPlayCachedSfx(string addressKey)
        {
            if (!_sfxCache.TryGetValue(addressKey, out var clip) || clip == null)
                return false;

            if (clip.loadState != AudioDataLoadState.Loaded)
                return false;

            PlayClip(clip);
            return true;
        }

        private void PlayClip(AudioClip clip)
        {
            if (_sfxSource == null || clip == null)
                return;

            //Debug.LogWarning($"[PlayClip] Audio clip: {clip.name}");
            _sfxSource.PlayOneShot(clip, _sfxVolume);
        }

        private async UniTask EnsureAudioDataLoadedAsync(AudioClip clip)
        {
            if (clip == null || clip.loadState == AudioDataLoadState.Loaded)
                return;

            if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData())
            {
                Debug.LogWarning($"Audio data failed to start loading: {clip.name}");
                return;
            }

            if (clip.loadState == AudioDataLoadState.Loading)
                await UniTask.WaitUntil(() => clip.loadState != AudioDataLoadState.Loading);

            if (clip.loadState == AudioDataLoadState.Failed)
                Debug.LogWarning($"Audio data failed to load: {clip.name}");
        }

        private void InitDebugCache()
        {
            foreach (var sound in _configService.Sounds.Sounds)
                _sfxCache[sound.Id] = sound.Clip;
        }

        public void SetSfxVolume(float value)
        {
            _sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(PlayerPrefsKey.MasterVolume, value);

            if (_sfxSource != null)
                _sfxSource.volume = _sfxVolume;
        }
    }
}
