using System;
using System.Diagnostics;
using UnityEngine;

namespace MP3toMP4
{
    // Added to the GameObject of a video player AudioSource. Unity calls OnAudioFilterRead in
    // component order; since we are added after AVPro's AudioOutput / CVR's VLCAudioOutput,
    // we see the finished samples. The data is NOT modified.
    internal sealed class AudioTap : MonoBehaviour
    {
        private const int Size = 8192; // power of two
        private const int Mask = Size - 1;

        private readonly float[] _ring = new float[Size];
        private readonly object _lock = new object();
        private int _write;
        private int _filled;
        private long _lastStamp;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || channels <= 0)
                return;

            int frames = data.Length / channels;
            float inv = 1f / channels;

            lock (_lock)
            {
                int i = 0;
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++, i++)
                        sum += data[i];
                    _ring[_write] = sum * inv;
                    _write = (_write + 1) & Mask;
                }
                _filled = Math.Min(Size, _filled + frames);
                _lastStamp = Stopwatch.GetTimestamp();
            }
        }

        private void OnDisable()
        {
            lock (_lock)
                _filled = 0;
        }

        // Copies the latest dst.Length mono samples. false if nothing recent is available.
        internal bool CopyLatest(float[] dst, double maxAgeSeconds)
        {
            lock (_lock)
            {
                if (_filled < dst.Length)
                    return false;
                double age = (Stopwatch.GetTimestamp() - _lastStamp) / (double)Stopwatch.Frequency;
                if (age > maxAgeSeconds)
                    return false;
                int start = (_write - dst.Length) & Mask;
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = _ring[(start + i) & Mask];
            }
            return true;
        }
    }
}
