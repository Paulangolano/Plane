using UnityEngine;

namespace Skyreach
{
    /// <summary>
    /// Fully synthesised audio (no sound files): a jet engine and wind generated in real time,
    /// plus procedurally built chime and crash clips.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class GameAudio : MonoBehaviour
    {
        AudioSource oneShots;
        AudioClip chimeLow, chimeHigh, crashClip;
        int sampleRate;

        // Shared with the audio thread
        volatile float rpm, wind, engineGain, windGain, master = 0.6f;
        double phase1, phase2;
        float lp1, lp2, bp1, bp2;
        uint noiseSeed = 22222;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var loop = GetComponent<AudioSource>();
            loop.playOnAwake = true;
            loop.loop = true;
            loop.spatialBlend = 0;
            loop.clip = AudioClip.Create("EngineCarrier", sampleRate, 1, sampleRate, false);
            var ones = new float[sampleRate];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            loop.clip.SetData(ones, 0);
            loop.Play();

            // One-shots live on a child object so the engine filter below doesn't process them.
            var fx = new GameObject("SoundEffects");
            fx.transform.SetParent(transform, false);
            oneShots = fx.AddComponent<AudioSource>();
            oneShots.playOnAwake = false;
            oneShots.spatialBlend = 0;
            chimeLow = MakeChime(new[] { 660f, 880f });
            chimeHigh = MakeChime(new[] { 784f, 988f, 1175f, 1568f });
            crashClip = MakeCrash();
        }

        public void SetMuted(bool muted)
        {
            master = muted ? 0 : 0.6f;
            if (oneShots) oneShots.volume = muted ? 0 : 1;
        }

        public void Tick(float throttle, float speed, bool boosting, bool crashed)
        {
            float targetRpm = crashed ? 0 : 0.25f + throttle * 0.75f + (boosting ? 0.25f : 0);
            float k = Mathf.Min(1, Time.deltaTime * 6);
            rpm += (targetRpm - rpm) * k;
            float w = Mathf.Min(1, speed / 170f);
            wind += ((crashed ? 0 : w) - wind) * k;
            engineGain = crashed ? 0 : 0.08f + rpm * 0.12f;
            windGain = crashed ? 0 : wind * 0.35f + (boosting ? 0.15f : 0);
        }

        public void Chime(bool high) => oneShots.PlayOneShot(high ? chimeHigh : chimeLow);

        public void Crash() => oneShots.PlayOneShot(crashClip);

        float Noise()
        {
            noiseSeed ^= noiseSeed << 13;
            noiseSeed ^= noiseSeed >> 17;
            noiseSeed ^= noiseSeed << 5;
            return noiseSeed / 2147483648f - 1f;
        }

        // Runs on the audio thread.
        void OnAudioFilterRead(float[] data, int channels)
        {
            float f1 = 45 + rpm * 85, f2 = 90 + rpm * 170;
            float cutoff = 250 + rpm * 1400;
            float a = Mathf.Clamp01(2 * Mathf.PI * cutoff / sampleRate);
            float windCut = 400 + wind * 1800;
            float wa = Mathf.Clamp01(2 * Mathf.PI * windCut / sampleRate);
            double step1 = f1 / sampleRate, step2 = f2 / sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                phase1 = (phase1 + step1) % 1.0;
                phase2 = (phase2 + step2) % 1.0;
                float saw = (float)(phase1 * 2 - 1);
                float tri = (float)(1 - 4 * System.Math.Abs(phase2 - 0.5));
                // Two one-pole low-passes in series = smooth engine rumble.
                lp1 += a * ((saw + tri) * 0.5f - lp1);
                lp2 += a * (lp1 - lp2);
                // Band-limited noise for wind.
                float n = Noise();
                bp1 += wa * (n - bp1);
                bp2 += wa * 0.15f * (bp1 - bp2);
                float sample = (lp2 * engineGain + (bp1 - bp2) * windGain) * master;
                for (int c = 0; c < channels; c++) data[i + c] = sample * data[i + c];
            }
        }

        AudioClip MakeChime(float[] notes)
        {
            int len = (int)(sampleRate * (0.7f + notes.Length * 0.09f));
            var buf = new float[len];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * 0.09f * sampleRate);
                for (int i = 0; start + i < len && i < sampleRate * 0.6f; i++)
                {
                    float t = i / (float)sampleRate;
                    float env = Mathf.Min(1, t / 0.02f) * Mathf.Exp(-t * 7f);
                    buf[start + i] += Mathf.Sin(2 * Mathf.PI * notes[n] * t) * env * 0.25f;
                }
            }
            var clip = AudioClip.Create("Chime", len, 1, sampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        AudioClip MakeCrash()
        {
            int len = (int)(sampleRate * 1.5f);
            var buf = new float[len];
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)sampleRate;
                float cutoff = Mathf.Lerp(1200, 80, t / 1.4f);
                float a = Mathf.Clamp01(2 * Mathf.PI * cutoff / sampleRate);
                lp += a * (Noise() - lp);
                buf[i] = lp * 2.5f * Mathf.Exp(-t * 3f);
            }
            var clip = AudioClip.Create("Crash", len, 1, sampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }
    }
}
