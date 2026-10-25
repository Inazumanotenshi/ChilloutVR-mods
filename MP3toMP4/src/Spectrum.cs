using System;
using UnityEngine;

namespace MP3toMP4
{
    // FFT + logarithmic frequency bands + smoothing for the visualizer.
    internal sealed class Spectrum
    {
        internal const int FftSize = 2048;

        private static readonly float[] Window = BuildWindow();
        private static readonly float[] TwCos = new float[FftSize / 2];
        private static readonly float[] TwSin = new float[FftSize / 2];

        static Spectrum()
        {
            for (int k = 0; k < FftSize / 2; k++)
            {
                double a = -2.0 * Math.PI * k / FftSize;
                TwCos[k] = (float)Math.Cos(a);
                TwSin[k] = (float)Math.Sin(a);
            }
        }

        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];

        private int _bandCount = -1;
        private int _sampleRate = -1;
        private int[] _bandLo = new int[0];
        private int[] _bandHi = new int[0];
        private float[] _bandTilt = new float[0];
        private float[] _targets = new float[0];
        private float[] _hold = new float[0];
        private float _level = 0.05f;

        internal float[] Bands = new float[0];
        internal float[] Peaks = new float[0];
        internal float Bass;

        private static float[] BuildWindow()
        {
            var w = new float[FftSize];
            for (int i = 0; i < FftSize; i++)
                w[i] = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (FftSize - 1));
            return w;
        }

        internal void Reset()
        {
            Array.Clear(Bands, 0, Bands.Length);
            Array.Clear(Peaks, 0, Peaks.Length);
            Bass = 0f;
        }

        private void EnsureBands(int count, int sampleRate)
        {
            if (count == _bandCount && sampleRate == _sampleRate)
                return;
            _bandCount = count;
            _sampleRate = sampleRate;

            _bandLo = new int[count];
            _bandHi = new int[count];
            _bandTilt = new float[count];
            _targets = new float[count];
            _hold = new float[count];
            Bands = new float[count];
            Peaks = new float[count];

            float fMin = 40f;
            float fMax = Mathf.Min(16000f, sampleRate * 0.45f);
            float binHz = sampleRate / (float)FftSize;
            int maxBin = FftSize / 2;

            for (int b = 0; b < count; b++)
            {
                float f0 = fMin * Mathf.Pow(fMax / fMin, b / (float)count);
                float f1 = fMin * Mathf.Pow(fMax / fMin, (b + 1) / (float)count);
                int lo = Mathf.Clamp(Mathf.FloorToInt(f0 / binHz), 1, maxBin - 1);
                int hi = Mathf.Clamp(Mathf.CeilToInt(f1 / binHz), lo + 1, maxBin);
                _bandLo[b] = lo;
                _bandHi[b] = hi;
                // Music rolls off towards the highs; +3 dB/octave evens out the picture.
                float center = Mathf.Sqrt(f0 * f1);
                _bandTilt[b] = 3f * Mathf.Log(center / 1000f, 2f);
            }
        }

        // samples == null: no audio data (paused / direct output) -> bars fall off.
        internal void Process(float[] samples, int sampleRate, float dt)
        {
            int count = Mathf.Clamp(MP3toMP4Mod.BarCount.Value, 8, 128);
            EnsureBands(count, Mathf.Max(8000, sampleRate));

            float sens = Mathf.Clamp(MP3toMP4Mod.Sensitivity.Value, 0.1f, 5f);

            if (samples != null)
            {
                double sum = 0;
                for (int i = 0; i < FftSize; i++)
                    sum += samples[i] * samples[i];
                float rms = (float)Math.Sqrt(sum / FftSize);

                // Level: fast attack, slow release.
                if (rms > _level)
                    _level = Mathf.Lerp(_level, rms, 0.5f);
                else
                    _level = Mathf.Lerp(_level, rms, 1f - Mathf.Exp(-dt * 0.5f));

                float gain = 1f;
                if (MP3toMP4Mod.AutoGain.Value)
                    gain = Mathf.Clamp(0.18f / Mathf.Max(_level, 0.0001f), 0.5f, 30f);

                for (int i = 0; i < FftSize; i++)
                {
                    _re[i] = samples[i] * Window[i] * gain;
                    _im[i] = 0f;
                }
                Fft(_re, _im);

                const float norm = 4f / FftSize;
                for (int b = 0; b < count; b++)
                {
                    float max = 0f;
                    for (int k = _bandLo[b]; k < _bandHi[b]; k++)
                    {
                        float m = _re[k] * _re[k] + _im[k] * _im[k];
                        if (m > max)
                            max = m;
                    }
                    float mag = Mathf.Sqrt(max) * norm;
                    float db = 20f * Mathf.Log10(mag + 1e-7f) + _bandTilt[b];
                    _targets[b] = Mathf.Clamp01((db + 50f) / 50f * sens);
                }
            }
            else
            {
                Array.Clear(_targets, 0, count);
            }

            float attack = 1f - Mathf.Exp(-dt * 25f);
            float bass = 0f;
            int bassBands = Mathf.Max(1, count / 8);

            for (int b = 0; b < count; b++)
            {
                float cur = Bands[b];
                float t = _targets[b];
                if (t > cur)
                    cur = Mathf.Lerp(cur, t, attack);
                else
                    cur = Mathf.Max(t, cur - dt * 1.6f);
                Bands[b] = cur;

                if (cur >= Peaks[b])
                {
                    Peaks[b] = cur;
                    _hold[b] = 0.35f;
                }
                else
                {
                    _hold[b] -= dt;
                    if (_hold[b] <= 0f)
                        Peaks[b] = Mathf.Max(cur, Peaks[b] - dt * 0.8f);
                }

                if (b < bassBands)
                    bass += cur;
            }
            Bass = bass / bassBands;
        }

        // Iterative Radix-2-FFT (in place).
        private static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1;
                int step = n / len;
                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        float wr = TwCos[k * step];
                        float wi = TwSin[k * step];
                        int a = i + k;
                        int b = a + half;
                        float xr = re[b] * wr - im[b] * wi;
                        float xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                    }
                }
            }
        }
    }
}
