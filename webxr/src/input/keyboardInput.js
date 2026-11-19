import * as THREE from 'three';

// Desktop controls, handy for testing without a headset.
//  W/S or Up/Down    pitch (W = nose down, like pushing a stick)
//  A/D or Left/Right roll
//  Q/E               rudder
//  Shift / Ctrl (or R / F)  throttle up / down
//  Space             afterburner      B  brakes
//  Enter             restart          V  toggle cockpit/chase camera
//  M                 mute             C  comfort vignette level

export class KeyboardInput {
  constructor(target = window) {
    this.keys = new Set();
    this.edges = new Set();
    this.used = false;
    target.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      this.keys.add(e.code);
      this.edges.add(e.code);
      this.used = true;
      if (e.code === 'Space' || e.code.startsWith('Arrow')) e.preventDefault();
    });
    target.addEventListener('keyup', (e) => this.keys.delete(e.code));
    target.addEventListener('blur', () => this.keys.clear());
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
    if (this.edges.has('Enter')) state.restart = true;
    if (this.edges.has('KeyC')) state.cycleComfort = true;
    if (this.edges.has('KeyM')) state.toggleMute = true;
    if (this.edges.has('KeyV')) state.toggleView = true;
    this.edges.clear();
    return true;
  }
}
