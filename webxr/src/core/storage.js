// Per-device settings (best time, comfort level, mute). Storage can be unavailable
// (private browsing), so every access is guarded and the game works without it.

const KEY = 'skyreach-vr-settings';
const DEFAULTS = { bestTime: null, comfortLevel: 2, muted: false, aircraft: 'jet', assists: true };

export function loadSettings() {
  try {
    const raw = localStorage.getItem(KEY);
    return { ...DEFAULTS, ...(raw ? JSON.parse(raw) : {}) };
  } catch {
    return { ...DEFAULTS };
  }
}

export function saveSettings(settings) {
  try {
    localStorage.setItem(KEY, JSON.stringify(settings));
  } catch {
    /* ignore */
  }
}
