import { Quaternion, Vector3, MathUtils } from 'three';
import { FLIGHT } from '../../config.js';

// Arcade flight model. Pure maths with no rendering, so it can be unit tested in Node.
// Axis convention (Three.js): forward = -Z, up = +Y, right = +X.

const GEAR_HEIGHT = 1.6;
const CEILING = 3200;

const _fwd = new Vector3();
const _up = new Vector3();
const _right = new Vector3();
const _q = new Quaternion();
const _axis = new Vector3();
const WORLD_UP = new Vector3(0, 1, 0);

export class FlightModel {
  constructor() {
    this.position = new Vector3();
    this.quaternion = new Quaternion();
    this.velocity = new Vector3();
    this.speed = 0; // forward airspeed (m/s)
    this.sink = 0; // extra downward speed while stalling (m/s)
    this.throttle = 0;
    this.boostFuel = FLIGHT.boostDuration;
    this.boosting = false;
    this.onGround = true;
    this.crashed = false;
    this.crashReason = '';
    this.stalling = false;
    // Smoothed control surfaces (-1..1)
    this.pitch = 0;
    this.roll = 0;
    this.yaw = 0;
    this.listeners = {};
  }

  on(event, fn) {
    (this.listeners[event] ||= []).push(fn);
  }

  emit(event, data) {
    (this.listeners[event] || []).forEach((fn) => fn(data));
  }

  reset(position, heading = 0, { onGround = true, speed = 0, throttle = 0 } = {}) {
    this.position.copy(position);
    this.quaternion.setFromAxisAngle(WORLD_UP, heading);
    this.speed = speed;
    this.sink = 0;
    this.throttle = throttle;
    this.boostFuel = FLIGHT.boostDuration;
    this.onGround = onGround;
    this.crashed = false;
    this.crashReason = '';
    this.pitch = this.roll = this.yaw = 0;
    this.velocity.set(0, 0, 0);
  }

  get forward() {
    return _fwd.set(0, 0, -1).applyQuaternion(this.quaternion);
  }

  /** Altitude above sea level of the gear. */
  get altitude() {
    return this.position.y - GEAR_HEIGHT;
  }

  /** Bank angle in radians (+ = right wing down). */
  get bank() {
    _right.set(1, 0, 0).applyQuaternion(this.quaternion);
    _up.set(0, 1, 0).applyQuaternion(this.quaternion);
    return Math.atan2(-_right.y, _up.y);
  }

  /** Nose pitch angle in radians (+ = nose up). */
  get pitchAngle() {
    const f = _fwd.set(0, 0, -1).applyQuaternion(this.quaternion);
    return Math.asin(MathUtils.clamp(f.y, -1, 1));
  }

  rotateLocal(x, y, z, angle) {
    _axis.set(x, y, z);
    _q.setFromAxisAngle(_axis, angle);
    this.quaternion.multiply(_q);
  }

  rotateWorld(axis, angle) {
    _q.setFromAxisAngle(axis, angle);
    this.quaternion.premultiply(_q);
  }

  /**
   * Advance the simulation.
   * @param {number} dt seconds
   * @param {{pitch:number, roll:number, yaw:number, throttle:number, boost:boolean, brake:boolean}} input
   * @param {(x:number, z:number) => {height:number, type:'water'|'land'|'runway'}} sampleGround
   */
  update(dt, input, sampleGround) {
    if (this.crashed) return;
    dt = Math.min(dt, 0.05);

    // Smooth the stick so the jet never snaps around (important for VR comfort).
    const k = 1 - Math.exp(-FLIGHT.controlSmoothing * dt);
    this.pitch += (MathUtils.clamp(input.pitch, -1, 1) - this.pitch) * k;
    this.roll += (MathUtils.clamp(input.roll, -1, 1) - this.roll) * k;
    this.yaw += (MathUtils.clamp(input.yaw, -1, 1) - this.yaw) * k;
    this.throttle = MathUtils.clamp(input.throttle, 0, 1);

    // Afterburner
    this.boosting = !!input.boost && this.boostFuel > 0 && this.throttle > 0.2;
    if (this.boosting) this.boostFuel = Math.max(0, this.boostFuel - dt);
    else this.boostFuel = Math.min(FLIGHT.boostDuration, this.boostFuel + FLIGHT.boostRecharge * dt);

    // Airspeed chases the throttle target; climbing costs speed, diving adds it.
    const fwd = this.forward;
    const target = this.boosting ? FLIGHT.boostSpeed : this.throttle * FLIGHT.maxSpeed;
    this.speed += (target - this.speed) * FLIGHT.accel * dt;
    if (!this.onGround) this.speed -= fwd.y * FLIGHT.gravityClimbPenalty * dt;
    if (this.onGround) {
      const friction = input.brake ? FLIGHT.brakeFriction : FLIGHT.groundFriction;
      this.speed -= this.speed * friction * dt * (this.throttle < 0.05 || input.brake ? 1 : 0.1);
    }
    this.speed = MathUtils.clamp(this.speed, 0, FLIGHT.boostSpeed * 1.15);

    // Control authority grows with airspeed.
    const authority = MathUtils.clamp(this.speed / FLIGHT.minFlySpeed, 0.12, 1);

    if (this.onGround) {
      this.updateGround(dt, sampleGround);
    } else {
      this.rotateLocal(1, 0, 0, this.pitch * FLIGHT.pitchRate * authority * dt);
      this.rotateLocal(0, 0, 1, -this.roll * FLIGHT.rollRate * authority * dt);
      this.rotateLocal(0, 1, 0, -this.yaw * FLIGHT.yawRate * authority * dt);

      // Arcade "coordinated turn": banking swings the nose around.
      _right.set(1, 0, 0).applyQuaternion(this.quaternion);
      this.rotateWorld(WORLD_UP, _right.y * 0.9 * authority * dt);

      // Stall: lose lift, nose drops gently.
      this.stalling = this.speed < FLIGHT.minFlySpeed;
      const sinkTarget = this.stalling ? (1 - this.speed / FLIGHT.minFlySpeed) * FLIGHT.stallSinkRate : 0;
      this.sink += (sinkTarget - this.sink) * Math.min(1, dt * 2);
      if (this.stalling && this.pitchAngle > -0.6) this.rotateLocal(1, 0, 0, -0.35 * (1 - authority) * dt);
    }
    this.quaternion.normalize();

    // Integrate position
    this.velocity.copy(this.forward).multiplyScalar(this.speed);
    this.velocity.y -= this.sink;
    this.position.addScaledVector(this.velocity, dt);
    if (this.position.y > CEILING) this.position.y = CEILING;

    if (!this.onGround) this.checkCollision(sampleGround);
  }

  updateGround(dt, sampleGround) {
    this.stalling = false;
    this.sink = 0;
    // Level the wings and steer with yaw/roll like a nosewheel.
    const steer = MathUtils.clamp(this.yaw + this.roll * 0.5, -1, 1);
    this.rotateWorld(WORLD_UP, -steer * 0.6 * MathUtils.clamp(this.speed / 20, 0, 1) * dt);
    const f = this.forward;
    const heading = Math.atan2(-f.x, -f.z);
    let pitch = this.pitchAngle;
    // Rotate for takeoff only above takeoff speed.
    const canRotate = this.speed >= FLIGHT.takeoffSpeed * 0.85;
    const desired = canRotate ? MathUtils.clamp(this.pitch, 0, 1) * 0.25 : 0;
    pitch += (desired - pitch) * Math.min(1, dt * 3);
    this.quaternion.setFromAxisAngle(WORLD_UP, heading);
    this.rotateLocal(1, 0, 0, pitch);

    const g = sampleGround(this.position.x, this.position.z);
    if (g.type !== 'runway' && g.type !== 'land') {
      this.crash('You rolled off the island into the sea!');
      return;
    }
    this.position.y = g.height + GEAR_HEIGHT;

    if (this.speed >= FLIGHT.takeoffSpeed && pitch > 0.08) {
      this.onGround = false;
      this.position.y += 0.2;
      this.emit('takeoff');
    }
  }

  checkCollision(sampleGround) {
    const g = sampleGround(this.position.x, this.position.z);
    const surface = Math.max(g.height, 0);
    if (this.position.y - GEAR_HEIGHT > surface) return;

    const descent = -this.velocity.y;
    const roll = Math.abs(this.bank);
    const pitch = this.pitchAngle;
    const gentle =
      descent < FLIGHT.safeTouchdownVSpeed && roll < FLIGHT.safeTouchdownRoll && pitch > -0.12 && pitch < 0.4;
    if (g.type === 'runway' && gentle) {
      this.onGround = true;
      this.position.y = g.height + GEAR_HEIGHT;
      this.emit('landed', { descent });
      return;
    }
    if (g.type === 'land' && gentle && this.speed < 70) {
      // A soft belly-landing on flat grass is forgiven too.
      this.onGround = true;
      this.position.y = g.height + GEAR_HEIGHT;
      this.emit('landed', { descent, offRunway: true });
      return;
    }
    this.crash(g.type === 'water' ? 'Splashdown! You hit the ocean.' : 'You crashed into the island.');
  }

  crash(reason) {
    this.crashed = true;
    this.crashReason = reason;
    this.speed = 0;
    this.sink = 0;
    this.velocity.set(0, 0, 0);
    this.emit('crash', { reason });
  }
}

export { GEAR_HEIGHT };
