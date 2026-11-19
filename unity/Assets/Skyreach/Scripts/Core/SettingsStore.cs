using UnityEngine;

namespace Skyreach
{
    /// <summary>Per-device settings saved with PlayerPrefs: best time, comfort level, mute.</summary>
    public sealed class SettingsStore
    {
        const string BestKey = "skyreach.bestTime";
        const string ComfortKey = "skyreach.comfort";
        const string MuteKey = "skyreach.muted";
        const string AircraftKey = "skyreach.aircraft";
        const string AssistsKey = "skyreach.assists";

        public float BestTime = -1; // -1 = no record yet
        public int ComfortLevel = 2;
        public bool Muted;
        public string Aircraft = "jet";
        public bool Assists = true;

        public static SettingsStore Load() => new SettingsStore
        {
            BestTime = PlayerPrefs.GetFloat(BestKey, -1),
            ComfortLevel = PlayerPrefs.GetInt(ComfortKey, 2),
            Muted = PlayerPrefs.GetInt(MuteKey, 0) == 1,
            Aircraft = PlayerPrefs.GetString(AircraftKey, "jet"),
            Assists = PlayerPrefs.GetInt(AssistsKey, 1) == 1,
        };

        /// <summary>Best lap time per aircraft (-1 = none yet).</summary>
        public float GetBest(string aircraft) => aircraft == "jet" ? BestTime : PlayerPrefs.GetFloat(BestKey + "." + aircraft, -1);

        public void SetBest(string aircraft, float t)
        {
            if (aircraft == "jet") BestTime = t;
            else PlayerPrefs.SetFloat(BestKey + "." + aircraft, t);
            Save();
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(BestKey, BestTime);
            PlayerPrefs.SetInt(ComfortKey, ComfortLevel);
            PlayerPrefs.SetInt(MuteKey, Muted ? 1 : 0);
            PlayerPrefs.SetString(AircraftKey, Aircraft);
            PlayerPrefs.SetInt(AssistsKey, Assists ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
