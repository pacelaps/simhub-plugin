using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PaceLaps.Plugin
{
    /// <summary>Plays a call's tone (Core.Tones) on the default output device through SimHub's NAudio.</summary>
    internal static class TonePlayer
    {
        public static void Play(IReadOnlyList<(double Hz, double Seconds)> steps, int volumePercent)
        {
            if (volumePercent <= 0)
            {
                return;
            }

            try
            {
                var output = new WaveOutEvent();
                output.Init(new SampleToWaveProvider(new ToneSequence(steps, Math.Min(100, volumePercent) / 100f)));
                output.PlaybackStopped += (_, _) => output.Dispose();
                output.Play();
            }
            catch (Exception e)
            {
                // No output device, or it is busy: the call still shows.
                Log.Info("sound: " + e.Message);
            }
        }

        /// <summary>Sine steps with a short fade in and out (no clicks) and a gap between them.</summary>
        private sealed class ToneSequence : ISampleProvider
        {
            private const int Rate = 44_100;
            private const double Fade = 0.015;
            private readonly List<(double Hz, int Start, int Length)> _steps = new List<(double, int, int)>();
            private readonly int _total;
            private readonly float _gain;
            private int _position;

            public ToneSequence(IReadOnlyList<(double Hz, double Seconds)> steps, float volume)
            {
                _gain = 0.3f * volume;
                var at = 0;
                foreach (var (hz, seconds) in steps)
                {
                    var length = (int)(seconds * Rate);
                    _steps.Add((hz, at, length));
                    at += length + (int)(Core.Tones.Gap * Rate);
                }

                _total = at;
            }

            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

            public int Read(float[] buffer, int offset, int count)
            {
                var n = Math.Min(count, _total - _position);
                for (var i = 0; i < n; i++)
                {
                    buffer[offset + i] = Sample(_position + i);
                }

                _position += n;
                return n;
            }

            private float Sample(int index)
            {
                foreach (var (hz, start, length) in _steps)
                {
                    if (index >= start && index < start + length)
                    {
                        var t = (index - start) / (double)Rate;
                        var left = (start + length - index) / (double)Rate;
                        var envelope = Math.Min(1, Math.Min(t, left) / Fade);
                        return (float)(Math.Sin(2 * Math.PI * hz * t) * envelope * _gain);
                    }
                }

                return 0;
            }
        }
    }
}
