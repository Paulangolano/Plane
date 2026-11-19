import * as THREE from 'three';
import { deadzone } from './inputState.js';
import { COCKPIT } from '../aircraft/jet.js';

// Quest Touch controller input.
//  Right hand: squeeze (grip) to grab the flight stick, or use the thumbstick.
//              Trigger = afterburner, A = restart, B = comfort vignette level.
//  Left hand:  squeeze to grab the throttle and push/pull it, or thumbstick up/down.
//              Thumbstick left/right = rudder, trigger = wheel brakes, Y = mute,
//              click the thumbstick to re-centre your seat.

const GRAB_THRESHOLD = 0.6;
const STICK_RANGE = 0.09; // metres of hand travel for full stick deflection
const THROTTLE_RANGE = 0.2; // metres of hand travel from idle to full throttle
const NEAR_DISTANCE = 0.16;

export class XRInput {
  constructor(renderer, rig, cockpitSpace) {
    this.renderer = renderer;
    this.cockpitSpace = cockpitSpace; // grab maths happens in the jet's local frame
    this.hands = {};
    this.prevButtons = { left: [], right: [] };
    this.stickGrab = null; // { origin: Vector3 }
    this.throttleGrab = null; // { origin: Vector3, start: number }

    for (let i = 0; i < 2; i++) {
      const grip = renderer.xr.getControllerGrip(i);
      const glove = createGlove();
      grip.add(glove);
      rig.add(grip);
      grip.addEventListener('connected', (e) => {
        const hand = e.data.handedness;
        if (hand === 'left' || hand === 'right') {
          this.hands[hand] = { grip, source: e.data, glove };
          glove.userData.setColour(hand === 'right' ? 0x3a8dde : 0xf08a24);
        }
      });
      grip.addEventListener('disconnected', (e) => {
        const hand = e.data?.handedness;
        if (hand && this.hands[hand]?.grip === grip) delete this.hands[hand];
      });
    }
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
    if (!this.renderer.xr.isPresenting) return false;
    const right = this.hands.right;
    const left = this.hands.left;
    let active = false;

    state.stickGrabbed = false;
    state.throttleGrabbed = false;
    state.stickNear = false;
    state.throttleNear = false;

    if (right?.source.gamepad) {
      active = true;
      const gp = right.source.gamepad;
      const squeeze = gp.buttons[1]?.value ?? 0;
      const pos = this.toCockpit(right.grip, _right);
      state.stickNear = pos.distanceTo(COCKPIT.stickGrip) < NEAR_DISTANCE;
      if (squeeze > GRAB_THRESHOLD) {
        if (!this.stickGrab) {
          this.stickGrab = { origin: pos.clone() };
          this.pulse('right', 0.25, 30);
        }
        const d = _v.subVectors(pos, this.stickGrab.origin);
        state.roll = THREE.MathUtils.clamp(d.x / STICK_RANGE, -1, 1);
        state.pitch = THREE.MathUtils.clamp(d.z / STICK_RANGE, -1, 1);
        state.stickGrabbed = true;
      } else {
        this.stickGrab = null;
        state.roll = deadzone(gp.axes[2] ?? 0);
        state.pitch = deadzone(gp.axes[3] ?? 0); // pull back (+y) = nose up
      }
      state.boost = (gp.buttons[0]?.value ?? 0) > 0.5;
      if (this.pressed('right', gp, 4)) state.restart = true;
      if (this.pressed('right', gp, 5)) state.cycleComfort = true;
      right.glove.userData.setGrab(squeeze);
    }

    if (left?.source.gamepad) {
      active = true;
      const gp = left.source.gamepad;
      const squeeze = gp.buttons[1]?.value ?? 0;
      const pos = this.toCockpit(left.grip, _left);
      state.throttleNear = pos.distanceTo(COCKPIT.throttleGrip) < NEAR_DISTANCE;
      if (squeeze > GRAB_THRESHOLD) {
        if (!this.throttleGrab) {
          this.throttleGrab = { origin: pos.clone(), start: state.throttle };
          this.pulse('left', 0.25, 30);
        }
        const push = -(pos.z - this.throttleGrab.origin.z); // forward = more power
        state.throttle = THREE.MathUtils.clamp(this.throttleGrab.start + push / THROTTLE_RANGE, 0, 1);
        state.throttleGrabbed = true;
      } else {
        this.throttleGrab = null;
        const ly = deadzone(gp.axes[3] ?? 0);
        state.throttle = THREE.MathUtils.clamp(state.throttle - ly * 0.55 * dt, 0, 1);
      }
      state.yaw = deadzone(gp.axes[2] ?? 0);
      state.brake = (gp.buttons[0]?.value ?? 0) > 0.5;
      if (this.pressed('left', gp, 5)) state.toggleMute = true;
      if (this.pressed('left', gp, 4)) state.cycleComfort = true;
      if (this.pressed('left', gp, 3)) state.recenter = true; // click the left thumbstick
      left.glove.userData.setGrab(squeeze);
    }
    return active;
  }

  /** Controller position in the jet's frame (so "forward" is always the nose). */
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
