using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace MP3toMP4
{
    // Added to the GameObject of a video player AudioSource. Unity calls OnAudioFilterRead in
    // component order; since we are added after AVPro's AudioOutput / CVR's VLCAudioOutput,
    // we see the finished samples. The data is NOT modified.
    //
    // Safety rules (OnAudioFilterRead runs on Unity's audio thread):
    // - The audio thread never waits: if the main thread holds the buffer, this block is simply skipped
    // - The component is attached once and never enabled/disabled afterwards (that would change the DSP chain);
    //   instead the volatile flag Active is checked
    // - Every exception is swallowed, an exception on the audio thread must never reach Unity
    internal sealed class AudioTap : MonoBehaviour
    {
        private const int Size = 8192; // power of two
        private const int Mask = Size - 1;

        private readonly float[] _ring = new float[Size];
        private readonly object _lock = new object();
        private int _write;
        private int _filled;
        private long _lastStamp;

        // Set from the main thread. When false the audio callback returns immediately.
        internal volatile bool Active;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Active || data == null || channels <= 0)
                return;

            // Never block the audio thread.
            if (!Monitor.TryEnter(_lock))
                return;
            try
            {
                int frames = data.Length / channels;
                float inv = 1f / channels;
                int i = 0;
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++, i++)
                        sum += data[i];
                    float s = sum * inv;
                    // Drop NaN/Infinity so they can't poison the FFT.
                    if (float.IsNaN(s) || float.IsInfinity(s))
                        s = 0f;
                    _ring[_write] = s;
                    _write = (_write + 1) & Mask;
                }
                _filled = Math.Min(Size, _filled + frames);
                _lastStamp = Stopwatch.GetTimestamp();
            }
            catch
            {
                // never let anything escape on the audio thread
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }

        // Copies the latest dst.Length mono samples. false if nothing recent is available
        // or the audio thread is writing right now (then this frame is simply skipped).
        internal bool CopyLatest(float[] dst, double maxAgeSeconds)
        {
            if (!Monitor.TryEnter(_lock))
                return false;
            try
            {
                if (_filled < dst.Length)
                    return false;
                double age = (Stopwatch.GetTimestamp() - _lastStamp) / (double)Stopwatch.Frequency;
                if (age > maxAgeSeconds)
                    return false;
                int start = (_write - dst.Length) & Mask;
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = _ring[(start + i) & Mask];
                return true;
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
    }
}
