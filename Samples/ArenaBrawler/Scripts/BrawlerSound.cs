using UnityEngine;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// The fight's sound effects, synthesised when the scene starts rather than shipped as files,
    /// so the sample has no audio to license. Each is a tone with a pitch slide, filtered noise, or
    /// both, under a short envelope.
    /// </summary>
    public static class BrawlerSound
    {
        private const int Rate = 44100;

        public static AudioClip Whoosh() => Make("whoosh", 0.2f, (t, n) =>
            Noise(n, Mathf.Lerp(0.15f, 0.5f, t / 0.2f)) * Env(t, 0.06f, 0.2f) * 0.55f);

        public static AudioClip Jab() => Make("jab", 0.16f, (t, n) =>
            (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 90f, t / 0.16f) * t) * 0.8f + Noise(n, 0.7f) * Env(t, 0.001f, 0.04f))
            * Env(t, 0.001f, 0.15f) * 0.75f);

        public static AudioClip Hook() => Make("hook", 0.38f, (t, n) =>
            (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(140f, 42f, t / 0.38f) * t) + Noise(n, 0.85f) * Env(t, 0.001f, 0.07f) * 0.8f)
            * Env(t, 0.001f, 0.36f) * 0.9f);

        public static AudioClip Block() => Make("block", 0.2f, (t, n) =>
            (Tone(t, 520f) * 0.4f + Tone(t, 780f) * 0.3f + Noise(n, 0.9f) * Env(t, 0.001f, 0.03f)) * Env(t, 0.001f, 0.18f) * 0.45f);

        public static AudioClip GuardBreak() => Make("break", 0.5f, (t, n) =>
            (Noise(n, 0.95f) * Env(t, 0.001f, 0.25f) + Tone(t, Mathf.Lerp(600f, 200f, t / 0.5f)) * 0.4f) * Env(t, 0.001f, 0.48f) * 0.6f);

        public static AudioClip Roll() => Make("roll", 0.3f, (t, n) =>
            Noise(n, 0.2f) * Env(t, 0.03f, 0.28f) * 0.4f);

        public static AudioClip KO() => Make("ko", 1.1f, (t, n) =>
            (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 30f, t / 1.1f) * t) + Noise(n, 0.3f) * 0.5f) * Env(t, 0.002f, 1.05f) * 0.9f);

        public static AudioClip Bell() => Make("bell", 1.2f, (t, n) =>
            (Tone(t, 880f) + Tone(t, 1320f) * 0.5f + Tone(t, 2210f) * 0.25f) * Mathf.Exp(-t * 3.2f) * 0.3f);

        public static AudioClip Crowd() => Make("crowd", 2.2f, (t, n) =>
            Noise(n, 0.12f) * Env(t, 0.3f, 2.1f) * (0.7f + 0.3f * Mathf.Sin(t * 19f)) * 0.3f);

        public static AudioClip Fanfare() => Make("fanfare", 1.4f, (t, n) =>
            (Tone(t, 523f) * Env(t, 0.005f, 0.2f) + Tone(t, 659f) * Env(t - 0.15f, 0.005f, 0.2f) +
             Tone(t, 784f) * Env(t - 0.3f, 0.005f, 0.2f) + (Tone(t, 1047f) + Tone(t, 784f) * 0.5f) * Env(t - 0.45f, 0.01f, 0.9f)) * 0.26f);

        // ---------------------------------------------------------------- building blocks

        private delegate float Sample(float t, int n);

        private static AudioClip Make(string name, float seconds, Sample sample)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            float[] data = new float[count];
            noiseState = 2463534242u;
            lowpass = 0f;
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate, i), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Tone(float t, float hz) => t < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * hz * t);

        /// <summary>Attack-decay envelope: 0 before t=0, up over <paramref name="attack"/>, down to 0 by <paramref name="length"/>.</summary>
        private static float Env(float t, float attack, float length)
        {
            if (t < 0f || t > length) return 0f;
            if (t < attack) return t / attack;
            return Mathf.Pow(1f - (t - attack) / Mathf.Max(0.0001f, length - attack), 2f);
        }

        private static uint noiseState;
        private static float lowpass;

        /// <summary>White noise through a one-pole low-pass; <paramref name="brightness"/> 1 is unfiltered.</summary>
        private static float Noise(int n, float brightness)
        {
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            float white = (noiseState / (float)uint.MaxValue) * 2f - 1f;
            lowpass += (white - lowpass) * Mathf.Clamp01(brightness);
            return lowpass;
        }
    }
}
