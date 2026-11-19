import * as THREE from 'three';
import { deadzone, fire } from './inputState.js';

// Quest Touch controller input, driven by the active aircraft's control layout:
//  - grabbing a flight stick / side yoke and a throttle / power lever with the grip buttons,
//  - thumbstick fallbacks (right stick = pitch/roll, left stick = power/rudder),
//  - the thumbstick of the hand on the stick becomes a trim hat (if the layout has one),
//  - face buttons and triggers mapped per aircraft (tap or hold),
//  - fingertip "poke" buttons in the cockpit and pullable handles (e.g. the parachute).

const GRAB_THRESHOLD = 0.6;
const NEAR_DISTANCE = 0.16;
const FINGERTIP_REACH = 0.04; // m from the controller's ray origin to the fingertip
const BUTTON_IDS = { 3: 'thumbstick', 4: 'primary', 5: 'secondary' };

export class XRInput {
  constructor(renderer, rig, cockpitSpace) {
    this.renderer = renderer;
    this.cockpitSpace = cockpitSpace; // grab maths happens in the aircraft's local frame
    this.hands = {};
    this.prevButtons = { left: [], right: [] };
    this.grabs = { left: null, right: null }; // { kind: 'stick'|'throttle'|'target', origin, ... }
    this.holds = {}; // button/poke hold timers
    this.touchStart = {};
    this.layout = null;

    for (let i = 0; i < 2; i++) {
      // Target-ray space points where the index finger points; grip space is the palm.
      const ray = renderer.xr.getController(i);
      rig.add(ray);
      const grip = renderer.xr.getControllerGrip(i);
      const glove = createGlove();
      grip.add(glove);
      rig.add(grip);
      grip.addEventListener('connected', (e) => {
        const hand = e.data.handedness;
        if (hand === 'left' || hand === 'right') {
          this.hands[hand] = { grip, ray, source: e.data, glove };
          glove.userData.setColour(hand === 'right' ? 0x3a8dde : 0xf08a24);
        }
      });
      grip.addEventListener('disconnected', (e) => {
        const hand = e.data?.handedness;
        if (hand && this.hands[hand]?.grip === grip) delete this.hands[hand];
      });
    }
  }

  setLayout(layout, cockpitSpace) {
    this.layout = layout;
    if (cockpitSpace) this.cockpitSpace = cockpitSpace;
    this.grabs = { left: null, right: null };
    this.holds = {};
    this.touchStart = {};
  }

  get connected() {
    return Object.keys(this.hands).length > 0;
  }

  pressed(hand, gamepad, index) {
    const now = !!gamepad.buttons[index]?.pressed;
    const was = !!this.prevButtons[hand][index];
    this.prevButtons[hand][index] = now;
    return now && !was;
  }

  update(dt, state) {
    if (!this.renderer.xr.isPresenting || !this.layout) return false;
    const L = this.layout;
    state.stickGrabbed = state.throttleGrabbed = state.stickNear = state.throttleNear = false;
    state.trimRate = 0;
    state.rollTrimRate = 0;
    state.capsPull = 0;
    state.hold = null;
    state.touching.clear();
    let active = false;

    for (const hand of ['left', 'right']) {
      const h = this.hands[hand];
      if (!h?.source.gamepad) continue;
      active = true;
      const gp = h.source.gamepad;
      const squeeze = gp.buttons[1]?.value ?? 0;
      const pos = this.toCockpit(h.grip, hand === 'left' ? _left : _right);
      if (L.stick.hand === hand) state.stickNear = pos.distanceTo(L.stick.grip) < NEAR_DISTANCE;
      if (L.throttle.hand === hand) state.throttleNear = pos.distanceTo(L.throttle.grip) < NEAR_DISTANCE;

      // Start / end grabs.
      if (squeeze > GRAB_THRESHOLD) {
        if (!this.grabs[hand]) {
          const target = (L.grabTargets || []).find((t) => pos.distanceTo(t.position) < t.radius);
          if (target) this.grabs[hand] = { kind: 'target', target, origin: pos.clone(), fired: false };
          else if (L.stick.hand === hand) this.grabs[hand] = { kind: 'stick', origin: pos.clone() };
          else if (L.throttle.hand === hand) this.grabs[hand] = { kind: 'throttle', origin: pos.clone(), start: state.throttle };
          if (this.grabs[hand]) this.pulse(hand, 0.25, 30);
        }
      } else this.grabs[hand] = null;

      const grab = this.grabs[hand];
      if (grab?.kind === 'stick') {
        const d = _v.subVectors(pos, grab.origin);
        const range = L.stick.range ?? 0.09;
        state.roll = THREE.MathUtils.clamp(d.x / range, -1, 1);
        state.pitch = THREE.MathUtils.clamp(d.z / range, -1, 1); // pulling back = nose up
        state.stickGrabbed = true;
        if (L.trimHat) {
          state.trimRate = deadzone(gp.axes[3] ?? 0); // hat back = nose-up trim
          state.rollTrimRate = deadzone(gp.axes[2] ?? 0);
        }
      } else if (grab?.kind === 'throttle') {
        const push = -(pos.z - grab.origin.z); // forward = more power
        state.throttle = THREE.MathUtils.clamp(grab.start + push / (L.throttle.range ?? 0.2), 0, 1);
        state.throttleGrabbed = true;
      } else if (grab?.kind === 'target') {
        const pull = Math.max(0, grab.origin.y - pos.y);
        state[grab.target.pullState || 'capsPull'] = Math.min(pull, 0.2);
        if (!grab.fired && pull > grab.target.pullDistance) {
          grab.fired = true;
          fire(state, grab.target.action);
          this.pulse(hand, 1, 200);
        }
      }

      // Thumbsticks (when that hand isn't using its stick as a trim hat).
      // Right stick flies (unless a hand is on the stick/yoke); left stick sets power and
      // rudder (unless it's being used as the trim hat).
      const usingHat = grab?.kind === 'stick' && L.trimHat;
      const stickHeld = this.grabs[L.stick.hand]?.kind === 'stick';
      if (hand === 'right' && !usingHat && !stickHeld) {
        state.roll = deadzone(gp.axes[2] ?? 0);
        state.pitch = deadzone(gp.axes[3] ?? 0); // pull back (+y) = nose up
      }
      if (hand === 'left' && !usingHat) {
        if (this.grabs[L.throttle.hand]?.kind !== 'throttle') {
          const ly = deadzone(gp.axes[3] ?? 0);
          state.throttle = THREE.MathUtils.clamp(state.throttle - ly * 0.55 * dt, 0, 1);
        }
        state.yaw = deadzone(gp.axes[2] ?? 0);
      }

      // Triggers
      const trig = gp.buttons[0]?.value ?? 0;
      const trigAction = L.triggers?.[hand];
      if (trigAction === 'boost') state.boost = trig > 0.5;
      else if (trigAction === 'brake') state.brake = trig > 0.5;
      else if (trigAction === 'brakeLeft') state.brakeLeft = trig;
      else if (trigAction === 'brakeRight') state.brakeRight = trig;

      // Face buttons + thumbstick click: tap actions, and hold actions.
      for (const idx of [3, 4, 5]) {
        const tap = L.buttons?.[hand]?.[idx];
        const hold = L.holdButtons?.[hand]?.[idx];
        if (tap && this.pressed(hand, gp, idx)) fire(state, tap);
        else if (!tap) this.pressed(hand, gp, idx);
        if (hold) {
          const key = `${hand}-${BUTTON_IDS[idx]}`;
          if (gp.buttons[idx]?.pressed) {
            this.holds[key] = (this.holds[key] || 0) + dt;
            state.hold = { id: hold.action, progress: Math.min(1, this.holds[key] / hold.seconds) };
            if (this.holds[key] >= hold.seconds && this.holds[key] - dt < hold.seconds) {
              fire(state, hold.action);
              this.pulse(hand, 0.8, 120);
            }
          } else this.holds[key] = 0;
        }
      }

      // Fingertip pokes on cockpit buttons (not while that hand is gripping something).
      if (!grab && L.pokeTargets?.length) this.poke(hand, h, dt, state);
      h.glove.userData.setGrab(squeeze);
    }
    return active;
  }

  /** Index fingertip position in the cockpit frame: a few cm along the pointing ray. */
  fingertip(hand, out = new THREE.Vector3()) {
    const h = this.hands[hand];
    if (!h) return null;
    h.ray.updateWorldMatrix(true, false);
    out.set(0, 0, -FINGERTIP_REACH).applyMatrix4(h.ray.matrixWorld);
    return this.cockpitSpace.worldToLocal(out);
  }

  poke(hand, h, dt, state) {
    this.fingertip(hand, _tip);
    for (const t of this.layout.pokeTargets) {
      const key = `${hand}-${t.id}`;
      if (_tip.distanceTo(t.position) < t.radius) {
        state.touching.add(t.id);
        if (this.touchStart[key] === undefined) {
          this.touchStart[key] = 0;
          if (!t.hold) {
            fire(state, t.action);
            this.pulse(hand, 0.4, 40);
          }
        } else this.touchStart[key] += dt;
        if (t.hold) {
          const held = this.touchStart[key];
          state.hold = { id: t.action, progress: Math.min(1, held / t.hold) };
          if (held >= t.hold && held - dt < t.hold) {
            fire(state, t.action);
            this.pulse(hand, 0.9, 150);
          }
        }
      } else delete this.touchStart[key];
    }
  }

  /** Controller position in the aircraft's frame (so "forward" is always the nose). */
  toCockpit(grip, out) {
    grip.updateWorldMatrix(true, false);
    out.setFromMatrixPosition(grip.matrixWorld);
    return this.cockpitSpace.worldToLocal(out);
  }

  pulse(hand, intensity, ms) {
    const actuator = this.hands[hand]?.source.gamepad?.hapticActuators?.[0];
    try {
      actuator?.pulse?.(intensity, ms);
    } catch {
      /* haptics are optional */
    }
  }
}

const _v = new THREE.Vector3();
const _left = new THREE.Vector3();
const _right = new THREE.Vector3();
const _tip = new THREE.Vector3();

/** A simple low-poly glove so players can see where their hands are. */
function createGlove() {
  const group = new THREE.Group();
  const mat = new THREE.MeshLambertMaterial({ color: 0x3a8dde, flatShading: true });
  const palm = new THREE.Mesh(new THREE.BoxGeometry(0.07, 0.03, 0.09), mat);
  palm.position.set(0, -0.01, 0.02);
  const fingers = new THREE.Group();
  for (let i = 0; i < 4; i++) {
    const f = new THREE.Mesh(new THREE.BoxGeometry(0.014, 0.018, 0.06), mat);
    f.position.set(-0.025 + i * 0.017, -0.01, -0.05);
    fingers.add(f);
  }
  const thumb = new THREE.Mesh(new THREE.BoxGeometry(0.016, 0.018, 0.05), mat);
  thumb.position.set(0.045, -0.005, 0.0);
  thumb.rotation.y = -0.6;
  group.add(palm, fingers, thumb);
  group.userData.setColour = (hex) => mat.color.setHex(hex);
  group.userData.setGrab = (amount) => {
    fingers.rotation.x = -amount * 1.2;
    fingers.position.y = -amount * 0.02;
  };
  return group;
}
