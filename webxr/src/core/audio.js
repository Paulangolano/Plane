// Synthesised audio (no sound files to download): jet engine, wind, chimes, crash.

export class GameAudio {
  constructor() {
    this.ctx = null;
    this.muted = false;
  }

  /** Must be called from a user gesture (e.g. the Enter VR click). */
  start() {
    if (this.ctx) {
      this.ctx.resume?.();
      return;
    }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    const ctx = new AC();
    this.ctx = ctx;
    this.master = ctx.createGain();
    this.master.gain.value = 0.6;
    this.master.connect(ctx.destination);

    // Engine: two detuned oscillators through a low-pass filter.
    this.engineFilter = ctx.createBiquadFilter();
    this.engineFilter.type = 'lowpass';
    this.engineFilter.frequency.value = 400;
    this.engineGain = ctx.createGain();
    this.engineGain.gain.value = 0;
    this.engineFilter.connect(this.engineGain).connect(this.master);
    this.osc1 = ctx.createOscillator();
    this.osc1.type = 'sawtooth';
    this.osc2 = ctx.createOscillator();
    this.osc2.type = 'triangle';
    this.osc1.connect(this.engineFilter);
    this.osc2.connect(this.engineFilter);
    this.osc1.start();
    this.osc2.start();

    // Wind / jet roar: looping noise through a band-pass filter.
    const noise = ctx.createBufferSource();
    noise.buffer = this.noiseBuffer(2);
    noise.loop = true;
    this.windFilter = ctx.createBiquadFilter();
    this.windFilter.type = 'bandpass';
    this.windFilter.Q.value = 0.7;
    this.windGain = ctx.createGain();
    this.windGain.gain.value = 0;
    noise.connect(this.windFilter).connect(this.windGain).connect(this.master);
    noise.start();
  }

  noiseBuffer(seconds) {
    const len = this.ctx.sampleRate * seconds;
    const buf = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
    const d = buf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
    return buf;
  }

  setMuted(m) {
    this.muted = m;
    if (this.master) this.master.gain.value = m ? 0 : 0.6;
  }

  update({ throttle, speed, boosting, crashed }) {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const rpm = crashed ? 0 : 0.25 + throttle * 0.75 + (boosting ? 0.25 : 0);
    this.osc1.frequency.setTargetAtTime(45 + rpm * 85, t, 0.15);
    this.osc2.frequency.setTargetAtTime(90 + rpm * 170, t, 0.15);
    this.engineFilter.frequency.setTargetAtTime(250 + rpm * 1400, t, 0.15);
    this.engineGain.gain.setTargetAtTime(crashed ? 0 : 0.08 + rpm * 0.12, t, 0.2);
    const wind = Math.min(1, speed / 170);
    this.windFilter.frequency.setTargetAtTime(400 + wind * 1800 + (boosting ? 600 : 0), t, 0.2);
    this.windGain.gain.setTargetAtTime(crashed ? 0 : wind * 0.35 + (boosting ? 0.15 : 0), t, 0.2);
  }

  chime(high = false) {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const notes = high ? [784, 988, 1175, 1568] : [660, 880];
    notes.forEach((f, i) => {
      const o = this.ctx.createOscillator();
      const g = this.ctx.createGain();
      o.type = 'sine';
      o.frequency.value = f;
      g.gain.setValueAtTime(0, t + i * 0.09);
      g.gain.linearRampToValueAtTime(0.25, t + i * 0.09 + 0.02);
      g.gain.exponentialRampToValueAtTime(0.001, t + i * 0.09 + 0.6);
      o.connect(g).connect(this.master);
      o.start(t + i * 0.09);
      o.stop(t + i * 0.09 + 0.7);
    });
  }

  crash() {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const src = this.ctx.createBufferSource();
    src.buffer = this.noiseBuffer(1.5);
    const f = this.ctx.createBiquadFilter();
    f.type = 'lowpass';
    f.frequency.setValueAtTime(1200, t);
    f.frequency.exponentialRampToValueAtTime(80, t + 1.4);
    const g = this.ctx.createGain();
    g.gain.setValueAtTime(0.9, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 1.5);
    src.connect(f).connect(g).connect(this.master);
    src.start();
  }
}
