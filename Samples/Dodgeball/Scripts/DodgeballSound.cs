using UnityEngine;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// The match's sound effects, synthesised when the scene starts rather than shipped as files, so
    /// the sample stays one folder of code with nothing to license. Each is a few lines of
    /// arithmetic: a tone with a pitch slide, filtered noise, or both, under a short envelope.
    /// </summary>
    public static class DodgeballSound
    {
        private const int Rate = 44100;

        public static AudioClip Throw() => Make("throw", 0.18f, (t, n) =>
            Noise(n, 0.5f) * Env(t, 0.01f, 0.17f) * (0.6f + 0.4f * Mathf.Sin(t * 90f)) * 0.5f);

        public static AudioClip Hit() => Make("hit", 0.32f, (t, n) =>
            (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(150f, 50f, t / 0.32f) * t) * 0.9f + Noise(n, 0.8f) * Env(t, 0.001f, 0.06f) * 0.6f)
            * Env(t, 0.002f, 0.3f) * 0.8f);

        public static AudioClip Catch() => Make("catch", 0.32f, (t, n) =>
            (Tone(t, 880f) * Env(t, 0.003f, 0.14f) + Tone(t, 1320f) * Env(t - 0.09f, 0.003f, 0.2f)) * 0.35f +
            Noise(n, 0.6f) * Env(t, 0.001f, 0.03f) * 0.4f);

        public static AudioClip Pickup() => Make("pickup", 0.1f, (t, n) =>
            Tone(t, Mathf.Lerp(520f, 900f, t / 0.1f)) * Env(t, 0.003f, 0.09f) * 0.3f);

        public static AudioClip Dodge() => Make("dodge", 0.22f, (t, n) =>
            Noise(n, 0.25f) * Env(t, 0.02f, 0.2f) * 0.35f);

        public static AudioClip Wall() => Make("wall", 0.14f, (t, n) =>
            (Tone(t, 110f) * 0.7f + Noise(n, 0.7f) * 0.4f) * Env(t, 0.001f, 0.12f) * 0.45f);

        public static AudioClip Buzzer() => Make("buzzer", 0.22f, (t, n) =>
            Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 140f * t)) * Env(t, 0.005f, 0.2f) * 0.18f);

        public static AudioClip Beep() => Make("beep", 0.14f, (t, n) => Tone(t, 660f) * Env(t, 0.005f, 0.12f) * 0.3f);

        public static AudioClip GoBeep() => Make("go", 0.3f, (t, n) => Tone(t, 990f) * Env(t, 0.005f, 0.28f) * 0.32f);

        public static AudioClip Point() => Make("point", 0.6f, (t, n) =>
            (Tone(t, 523f) * Env(t, 0.005f, 0.16f) + Tone(t, 659f) * Env(t - 0.12f, 0.005f, 0.16f) +
             Tone(t, 784f) * Env(t - 0.24f, 0.005f, 0.32f)) * 0.3f);

        public static AudioClip Crowd() => Make("crowd", 1.4f, (t, n) =>
            Noise(n, 0.15f) * Env(t, 0.25f, 1.1f) * (0.7f + 0.3f * Mathf.Sin(t * 23f)) * 0.22f);

        // ---------------------------------------------------------------- building blocks

        private delegate float Sample(float t, int n);

        private static AudioClip Make(string name, float seconds, Sample sample)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            float[] data = new float[count];
            noiseState = 12345u;
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
