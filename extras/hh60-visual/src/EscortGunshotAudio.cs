using System;
using System.Collections;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using UnityEngine;

namespace TscHh60Visual
{
    // One aircraft owns one native dependency-graph retain, including the bank's
    // dependencies. It never unloads a shared bundle or destroys shared clips.
    internal sealed class EscortGunshotAudio : IDisposable
    {
        internal const string BundleKey = "assets/content/audio/banks/ak74.bundle";
        internal const string BankName = "utes_tail";
        private const float TimeoutSeconds = 15f;
        private static readonly string[] ClipNames =
        {
            "kord_outdoor_close_loop_tail", "utes_outdoor_distant_loop_tail", "utes_outdoor_far_loop_tail"
        };
        private DependencyGraph<IEasyBundle>.Token _retain;
        private IEasyAssets _assets;
        private Task _loading;
        private AudioClip[] _clips;
        private bool _started, _disposed, _ready;
        private float _deadline;

        internal bool Available => !_disposed && _ready && _clips != null &&
            _clips[0] != null && _clips[1] != null && _clips[2] != null;
        internal AudioClip[] Clips => Available ? _clips : null;
        internal string Failure { get; private set; }

        internal IEnumerator Load(Func<bool> stillCurrent)
        {
            if (_started || _disposed) yield break;
            _started = true;
            try
            {
                if (stillCurrent == null || !stillCurrent()) yield break;
                if (!Begin()) yield break;
                while (!_disposed)
                {
                    if (!stillCurrent()) { Failure = "escort preparation cancelled"; yield break; }
                    if (Poll()) yield break;
                    yield return null;
                }
            }
            finally
            {
                // Also runs if the host disposes a suspended load coroutine.
                if (!_ready || _disposed) Release();
            }
        }

        private bool Begin()
        {
            try
            {
                _assets = Singleton<IEasyAssets>.Instance;
                if (_assets == null) throw new InvalidOperationException("native asset manager unavailable");
                _deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                _retain = _assets.Retain(new[] { BundleKey });
                if (_retain == null) throw new InvalidOperationException("native audio retain unavailable");
                _loading = EasyAssetsExtensions.LoadBundles(_retain);
                if (_loading == null) throw new InvalidOperationException("native audio loading job unavailable");
                return true;
            }
            catch (Exception error) { Failure = error.GetBaseException().Message; return false; }
        }

        // Returns true when complete, including a bounded nonfatal failure.
        private bool Poll()
        {
            try
            {
                if (Time.realtimeSinceStartup >= _deadline) throw new TimeoutException("native gunshot audio load exceeded 15 seconds");
                if (!_loading.IsCompleted) return false;
                _loading.GetAwaiter().GetResult();
                if (_clips == null)
                {
                    var bank = _assets.GetAsset<SoundBank>(BundleKey, BankName);
                    if (bank == null || bank.name != BankName || bank.Environments == null || bank.Environments.Length == 0 ||
                        bank.Environments[0].Clips == null || bank.Environments[0].Clips.Length < 3)
                        throw new InvalidOperationException("native NSV tail sound bank is incomplete");
                    var clips = new AudioClip[3];
                    for (int i = 0; i < clips.Length; i++)
                    {
                        var choices = bank.Environments[0].Clips[i]?.Clips;
                        if (choices == null || choices.Length == 0 || choices[0] == null || choices[0].name != ClipNames[i] ||
                            !EscortBenchSafetyPolicy.Finite(choices[0].length) || choices[0].length <= 0f)
                            throw new InvalidOperationException("native NSV tail clip unavailable: " + ClipNames[i]);
                        clips[i] = choices[0];
                        if (clips[i].loadState == AudioDataLoadState.Unloaded && !clips[i].LoadAudioData())
                            throw new InvalidOperationException("native NSV audio data refused loading: " + ClipNames[i]);
                    }
                    _clips = clips;
                }
                foreach (AudioClip clip in _clips)
                {
                    if (clip == null || clip.loadState == AudioDataLoadState.Failed)
                        throw new InvalidOperationException("native NSV audio data failed loading");
                    if (clip.loadState != AudioDataLoadState.Loaded) return false;
                }
                _ready = true;
                return true;
            }
            catch (Exception error) { Failure = error.GetBaseException().Message; return true; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ready = false;
            _clips = null;
            Release();
        }

        private void Release()
        {
            var token = _retain;
            _retain = null;
            _assets = null;
            if (_loading != null)
            {
                // Native loading can finish after a cancelled/expired preparation.
                // Observe errors without a continuation that touches Unity objects.
                _loading.ContinueWith(task => { var ignored = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                _loading = null;
            }
            if (token == null) return;
            try { token.Release(); }
            catch (Exception error) { Plugin.Log?.LogWarning("[EscortBench] Native gunshot audio retain release failed: " + error.Message); }
        }
    }
}
