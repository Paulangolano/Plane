import * as THREE from 'three';
import { fire } from './inputState.js';

// Desktop controls (shared by both aircraft):
//  W/S or Up/Down    pitch (W = nose down, like pushing a stick)
//  A/D or Left/Right roll          Q/E  rudder
//  Shift / Ctrl (or R / F)  throttle / power up / down
//  Space afterburner (jet)          B  brakes   , / .  left / right brake
//  T / G  pitch trim (nose down / up)
//  Enter restart   V cockpit/chase camera   M mute   C comfort vignette
// Aircraft-specific keys (flaps, autopilot, parachute…) come from the aircraft's key map.

export class KeyboardInput {
  constructor(target = window) {
    this.keys = new Set();
    this.edges = new Set();
    this.holdTimes = {};
    this.used = false;
    this.keyMap = {};
    this.holdMap = {};
    target.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      this.keys.add(e.code);
      this.edges.add(e.code);
      this.used = true;
      if (e.code === 'Space' || e.code.startsWith('Arrow') || e.code.startsWith('Page')) e.preventDefault();
    });
    target.addEventListener('keyup', (e) => this.keys.delete(e.code));
    target.addEventListener('blur', () => this.keys.clear());
  }

  /** @param {{keys?: Record<string,string>, holdKeys?: Record<string,{action:string, seconds:number}>}} map */
  setKeyMap(map = {}) {
    this.keyMap = map.keys || {};
    this.holdMap = map.holdKeys || {};
    this.holdTimes = {};
  }

  axis(neg, pos) {
    let v = 0;
    if (neg.some((k) => this.keys.has(k))) v -= 1;
    if (pos.some((k) => this.keys.has(k))) v += 1;
    return v;
  }

  update(dt, state) {
    if (!this.used) return false;
    state.pitch = this.axis(['KeyW', 'ArrowUp'], ['KeyS', 'ArrowDown']);
    state.roll = this.axis(['KeyA', 'ArrowLeft'], ['KeyD', 'ArrowRight']);
    state.yaw = this.axis(['KeyQ'], ['KeyE']);
    const t = this.axis(['ControlLeft', 'ControlRight', 'KeyF'], ['ShiftLeft', 'ShiftRight', 'KeyR']);
    state.throttle = THREE.MathUtils.clamp(state.throttle + t * 0.6 * dt, 0, 1);
    state.boost = this.keys.has('Space');
    state.brake = this.keys.has('KeyB');
    state.brakeLeft = this.keys.has('Comma') ? 1 : 0;
    state.brakeRight = this.keys.has('Period') ? 1 : 0;
    state.trimRate = this.axis(['KeyT'], ['KeyG']);
    if (this.edges.has('Enter')) fire(state, 'restart');
    if (this.edges.has('KeyC')) fire(state, 'cycleComfort');
    if (this.edges.has('KeyM')) fire(state, 'toggleMute');
    if (this.edges.has('KeyV')) fire(state, 'toggleView');
    for (const code of this.edges) if (this.keyMap[code]) fire(state, this.keyMap[code]);
    state.hold = null;
    for (const [code, h] of Object.entries(this.holdMap)) {
      if (this.keys.has(code)) {
        const before = this.holdTimes[code] || 0;
        this.holdTimes[code] = before + dt;
        state.hold = { id: h.action, progress: Math.min(1, this.holdTimes[code] / h.seconds) };
        if (before < h.seconds && this.holdTimes[code] >= h.seconds) fire(state, h.action);
      } else this.holdTimes[code] = 0;
    }
    this.edges.clear();
    return true;
  }
}
