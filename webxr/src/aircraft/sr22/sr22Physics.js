import { Quaternion, Vector3, MathUtils } from 'three';
import { SR22, KT, liftCoefficient, dragCoefficient, stallAlpha, densityRatio } from './sr22Data.js';

// Force-based flight model for the SR22-class aircraft (pure maths, unit tested in Node).
// Lift, drag and thrust come from angle of attack, airspeed, flaps, power and air density,
// so stall speeds, climb rate, cruise speed and glide all fall out of the aircraft data.
// Handling uses an "angle-of-attack command" model: stick position (plus trim) sets the
// angle of attack the aircraft settles at, which is how a stable light aircraft feels.
//
// Axes (Three.js): forward = -Z, up = +Y, right = +X. Origin = pilot's eye point.

const G = 9.81;
const WORLD_UP = new Vector3(0, 1, 0);
const _q = new Quaternion();
const _axis = new Vector3();
const _fwd = new Vector3();
const _right = new Vector3();
const _up = new Vector3();
const _vb = new Vector3();
const _dir = new Vector3();
const _liftDir = new Vector3();
const _force = new Vector3();
const _inv = new Quaternion();

const smoothstep = (a, b, x) => {
  const t = MathUtils.clamp((x - a) / (b - a), 0, 1);
  return t * t * (3 - 2 * t);
};

export class SR22Physics {
  constructor() {
    this.position = new Vector3();
    this.quaternion = new Quaternion();
    this.velocity = new Vector3();
    this.listeners = {};
    this.assists = true; // envelope protection, yaw damper, nosewheel steering with rudder
    this.reset(new Vector3(), 0);
  }

  on(event, fn) {
    (this.listeners[event] ||= []).push(fn);
  }

  emit(event, data) {
    (this.listeners[event] || []).forEach((fn) => fn(data));
  }

  reset(position, heading = 0, { onGround = true, speed = 0, throttle = 0, flaps = 0 } = {}) {
    this.position.copy(position);
    this.quaternion.setFromAxisAngle(WORLD_UP, heading);
    this.velocity.set(0, 0, -1).applyQuaternion(this.quaternion).multiplyScalar(speed);
    this.groundSpeed = onGround ? speed : 0;
    this.onGround = onGround;
    this.crashed = false;
    this.crashReason = '';
    this.done = false; // finished safely (e.g. landed under the parachute)
    this.stalling = false;
    this.stallWarning = false;
    this.espActive = false;
    this.throttle = throttle; // power lever 0..1
    this.engineRunning = true;
    this.rpm = SR22.idleRpm;
    this.fuelGal = 81;
    this.flaps = flaps;
    this.flapsTarget = flaps;
    this.trim = 0.12; // pitch trim, -1 nose down .. +1 nose up (take-off setting)
    this.rollTrim = 0;
    this.parkingBrake = false;
    this.chute = 'stowed'; // stowed | inflating | deployed | failed
    this.chuteT = 0;
    this.pitch = this.roll = this.yaw = 0; // smoothed control inputs
    this.rollRate = 0;
    this.alpha = 0;
    this.beta = 0;
    this.gLoad = 1;
    this.dropDir = 0;
    this.boostFuel = 0; // (jet-only; kept for a shared HUD interface)
    this.boosting = false;
  }

  // ------------------------------------------------------------------ derived values
  get forward() {
    return _fwd.set(0, 0, -1).applyQuaternion(this.quaternion);
  }

  get speed() {
    return this.velocity.length();
  }

  get altitude() {
    return this.position.y - SR22.gearHeight;
  }

  get densityRatio() {
    return densityRatio(this.altitude);
  }

  /** Indicated airspeed in knots. */
  get iasKt() {
    return (this.speed * Math.sqrt(this.densityRatio)) / KT;
  }

  get verticalSpeed() {
    return this.velocity.y;
  }

  get bank() {
    _right.set(1, 0, 0).applyQuaternion(this.quaternion);
    _up.set(0, 1, 0).applyQuaternion(this.quaternion);
    return Math.atan2(-_right.y, _up.y);
  }

  get pitchAngle() {
    const f = _fwd.set(0, 0, -1).applyQuaternion(this.quaternion);
    return Math.asin(MathUtils.clamp(f.y, -1, 1));
  }

  get heading() {
    const f = _fwd.set(0, 0, -1).applyQuaternion(this.quaternion);
    return Math.atan2(f.x, -f.z);
  }

  get stallAlpha() {
    return stallAlpha(this.flaps);
  }

  /** Manifold pressure (inHg) and fuel flow (gal/h) for the engine display. */
  get manifoldPressure() {
    return this.engineRunning ? 10 + 19.5 * this.throttle * this.densityRatio : 29.9 * this.densityRatio;
  }

  get fuelFlow() {
    return this.engineRunning ? 2.8 + 24 * this.throttle * this.densityRatio : 0;
  }

  get percentPower() {
    return this.engineRunning ? Math.round(this.throttle * this.densityRatio * 100) : 0;
  }

  // ------------------------------------------------------------------ helpers
  rotateLocal(x, y, z, angle) {
    _axis.set(x, y, z);
    _q.setFromAxisAngle(_axis, angle);
    this.quaternion.multiply(_q);
  }

  setFlapsDetent(index) {
    this.flapsTarget = SR22.flapDetents[MathUtils.clamp(index, 0, 2)];
  }

  flapsStep(dir) {
    const i = SR22.flapDetents.indexOf(this.flapsTarget);
    this.setFlapsDetent((i < 0 ? 0 : i) + dir);
    return this.flapsTarget;
  }

  /** Pull the parachute handle. Returns 'deployed', 'failed' or 'ground'. */
  deployChute() {
    if (this.chute !== 'stowed' || this.crashed || this.done) return this.chute;
    if (this.onGround) return 'ground';
    if (this.iasKt > SR22.chuteMaxDeploySpeed) {
      this.chute = 'failed';
      this.emit('chute', { ok: false });
      return 'failed';
    }
    this.chute = 'inflating';
    this.chuteT = 0;
    this.engineRunning = false;
    this.throttle = 0;
    this.emit('chute', { ok: true });
    return 'deployed';
  }

  // ------------------------------------------------------------------ simulation
  /**
   * @param {number} dt
   * @param {{pitch:number, roll:number, yaw:number, throttle:number, brake?:boolean|number,
   *          brakeLeft?:number, brakeRight?:number, trimRate?:number}} input
   * @param {(x:number, z:number) => {height:number, type:string}} sampleGround
   */
  update(dt, input, sampleGround) {
    if (this.crashed || this.done) return;
    dt = Math.min(dt, 0.05);

    const k = 1 - Math.exp(-10 * dt);
    this.pitch += (MathUtils.clamp(input.pitch || 0, -1, 1) - this.pitch) * k;
    this.roll += (MathUtils.clamp(input.roll || 0, -1, 1) - this.roll) * k;
    this.yaw += (MathUtils.clamp(input.yaw || 0, -1, 1) - this.yaw) * k;
    if (this.engineRunning) this.throttle = MathUtils.clamp(input.throttle ?? this.throttle, 0, 1);
    if (input.trimRate) this.trim = MathUtils.clamp(this.trim + input.trimRate * 0.22 * dt, -1, 1);
    if (input.rollTrimRate) this.rollTrim = MathUtils.clamp(this.rollTrim + input.rollTrimRate * 0.3 * dt, -1, 1);

    // Flaps travel toward the selected detent.
    const fd = this.flapsTarget - this.flaps;
    this.flaps += Math.sign(fd) * Math.min(Math.abs(fd), SR22.flapRate * dt);

    // Engine
    const rpmTarget = this.engineRunning ? SR22.idleRpm + (SR22.maxRpm - SR22.idleRpm) * Math.min(1, this.throttle * 1.6) : 0;
    this.rpm += (rpmTarget - this.rpm) * Math.min(1, dt * (this.engineRunning ? 3 : 0.8));
    if (this.engineRunning) {
      this.fuelGal = Math.max(0, this.fuelGal - (this.fuelFlow / 3600) * dt);
      if (this.fuelGal <= 0) {
        this.engineRunning = false;
        this.emit('engineOut', { reason: 'fuel' });
      }
    }

    // Parachute inflation
    if (this.chute === 'inflating' || this.chute === 'deployed') {
      this.chuteT += dt;
      if (this.chute === 'inflating' && this.chuteT >= SR22.chuteInflateTime) this.chute = 'deployed';
    }

    // Brakes (both pedals, differential, or parking brake)
    const both = typeof input.brake === 'number' ? input.brake : input.brake ? 1 : 0;
    this.brakeLeft = this.parkingBrake ? 1 : Math.max(both, input.brakeLeft || 0);
    this.brakeRight = this.parkingBrake ? 1 : Math.max(both, input.brakeRight || 0);

    // Fixed sub-steps keep the forces stable at any frame rate.
    const steps = Math.ceil(dt / (1 / 120));
    const h = dt / steps;
    for (let i = 0; i < steps && !this.crashed && !this.done; i++) {
      if (this.onGround) this.stepGround(h, sampleGround);
      else this.stepAir(h, sampleGround);
    }
  }

  /** Commanded stick inputs after envelope protection (when assists are on). */
  protectedInputs() {
    let pitch = this.pitch;
    let roll = this.roll;
    this.espActive = false;
    if (!this.assists || this.chute !== 'stowed' || this.onGround) return { pitch, roll };
    const bank = this.bank;
    const limitBank = MathUtils.degToRad(45);
    if (Math.abs(bank) > limitBank) {
      roll -= Math.sign(bank) * Math.min(1, (Math.abs(bank) - limitBank) / MathUtils.degToRad(15)) * 1.2;
      this.espActive = true;
    }
    const margin = this.stallAlpha - 0.045;
    if (this.alpha > margin) {
      pitch -= Math.min(2, (this.alpha - margin) * 30);
      this.espActive = true;
    }
    if (this.iasKt > SR22.vne) {
      pitch += Math.min(0.6, (this.iasKt - SR22.vne) * 0.03);
      this.espActive = true;
    }
    const pa = this.pitchAngle;
    if (pa > 0.44) {
      pitch -= (pa - 0.44) * 3;
      this.espActive = true;
    } else if (pa < -0.35) {
      pitch += (-0.35 - pa) * 3;
      this.espActive = true;
    }
    return { pitch: MathUtils.clamp(pitch, -1.5, 1.5), roll: MathUtils.clamp(roll, -1.5, 1.5) };
  }

  aero() {
    const sigma = this.densityRatio;
    const rho = 1.225 * sigma;
    const V = this.velocity.length();
    _inv.copy(this.quaternion).invert();
    _vb.copy(this.velocity).applyQuaternion(_inv);
    const u = Math.max(-_vb.z, 0.5);
    this.alpha = V > 2 ? Math.atan2(-_vb.y, u) : 0;
    this.beta = V > 2 ? Math.atan2(_vb.x, u) : 0;
    const qd = 0.5 * rho * V * V;
    const cl = liftCoefficient(this.alpha, this.flaps);
    const cd = dragCoefficient(cl, this.flaps, this.engineRunning ? this.throttle : 0);
    const S = SR22.wingArea;
    let thrust = 0;
    if (this.engineRunning) {
      const power = SR22.maxPower * this.throttle * Math.pow(sigma, 1.1);
      const eta = 0.55 + 0.3 * smoothstep(20, 80, V);
      thrust = Math.min(SR22.staticThrust * this.throttle * Math.sqrt(sigma), (eta * power) / Math.max(V, 1));
    }
    const qRef = 0.5 * 1.225 * SR22.refSpeed * SR22.refSpeed;
    const qf = MathUtils.clamp(qd / qRef, 0.03, 1.8);
    // Prop wash over the tail gives the elevator and rudder some authority even when slow.
    const wash = this.engineRunning ? this.throttle * 0.55 * (1 - smoothstep(20, 50, V)) : 0;
    this.stalling = this.alpha > this.stallAlpha && V > 5;
    this.stallWarning = this.alpha > this.stallAlpha - 0.06 && V > 5 && !this.onGround;
    return { V, qd, lift: qd * S * cl, drag: qd * S * cd, thrust, qf, qfTail: Math.min(1.8, qf + wash), S, rho };
  }

  stepAir(h, sampleGround) {
    const a = this.aero();
    const { pitch, roll } = this.protectedInputs();
    const chuteOut = this.chute === 'inflating' || this.chute === 'deployed';

    if (!chuteOut) {
      // Pitch: chase the commanded angle of attack (stick + trim).
      const trimAlpha = MathUtils.lerp(SR22.trimAlpha[0], SR22.trimAlpha[1], (this.trim + 1) / 2);
      const alphaCmd = trimAlpha + pitch * SR22.elevatorAlpha;
      let pitchRate = SR22.pitchGain * (alphaCmd - this.alpha) * a.qfTail;
      if (this.stalling) pitchRate -= (this.alpha - this.stallAlpha) * 1.5; // nose drops in a stall
      pitchRate = MathUtils.clamp(pitchRate, -1.2, 1.2);

      // Roll: aileron rate command with lag, plus dihedral and wing drop in a stall.
      const rollCmd = (roll + this.rollTrim * 0.1) * SR22.maxRollRate * Math.min(a.qf, 1.3);
      this.rollRate += (rollCmd - this.rollRate) * (1 - Math.exp(-4 * h));
      let rollRate = this.rollRate - SR22.dihedral * this.beta * a.qf;
      if (this.stalling && !this.assists) {
        if (!this.dropDir) this.dropDir = this.beta !== 0 ? -Math.sign(this.beta) : 1;
        rollRate += this.dropDir * Math.min(1.2, (this.alpha - this.stallAlpha) * 8);
      } else if (!this.stalling) this.dropDir = 0;

      // Yaw: rudder, weathervane stability, adverse yaw and prop torque / P-factor.
      let yawRate = this.yaw * SR22.maxYawRate * a.qfTail + SR22.weathervane * this.beta * a.qf;
      yawRate -= 0.12 * this.roll * a.qf;
      const torque = this.engineRunning ? this.throttle * 0.1 * (1 - smoothstep(30, 80, a.V)) : 0;
      yawRate -= this.assists ? torque * 0.2 : torque;
      if (this.assists) yawRate += 1.4 * this.beta * a.qf; // yaw damper / auto-coordination

      this.rotateLocal(1, 0, 0, pitchRate * h);
      this.rotateLocal(0, 0, 1, -rollRate * h);
      this.rotateLocal(0, 1, 0, -yawRate * h);
    } else {
      // Hanging under the canopy: the airframe settles slightly nose-down and wings level.
      const heading = this.heading;
      _q.setFromAxisAngle(WORLD_UP, heading);
      _axis.set(1, 0, 0);
      const target = _q.clone().multiply(new Quaternion().setFromAxisAngle(_axis, -0.1));
      this.quaternion.slerp(target, Math.min(1, h * 1.2));
    }
    this.quaternion.normalize();

    // Forces
    const V = a.V;
    const fwd = this.forward;
    _right.set(1, 0, 0).applyQuaternion(this.quaternion);
    if (V > 0.5) _dir.copy(this.velocity).divideScalar(V);
    else _dir.copy(fwd);
    _liftDir.crossVectors(_right, _dir);
    if (_liftDir.lengthSq() < 1e-6) _liftDir.set(0, 1, 0);
    _liftDir.normalize();
    _force.set(0, -SR22.mass * G, 0);
    _force.addScaledVector(_liftDir, a.lift);
    _force.addScaledVector(_dir, -a.drag);
    _force.addScaledVector(fwd, a.thrust);
    _force.addScaledVector(_right, -SR22.sideForce * this.beta * a.qd * a.S);
    if (chuteOut) {
      const inflation = this.chute === 'deployed' ? 1 : smoothstep(0, 1, this.chuteT / SR22.chuteInflateTime);
      _force.addScaledVector(_dir, -0.5 * a.rho * V * V * SR22.chuteCdA * inflation);
    }
    this.gLoad = a.lift / (SR22.mass * G);
    this.velocity.addScaledVector(_force, h / SR22.mass);
    this.position.addScaledVector(this.velocity, h);

    if (this.iasKt > SR22.structuralLimit) {
      this.crash(`Airframe overstressed at ${Math.round(this.iasKt)} kt (never exceed is ${SR22.vne} kt).`);
      return;
    }
    this.checkContact(sampleGround);
  }

  stepGround(h, sampleGround) {
    const a = this.aero();
    const g = sampleGround(this.position.x, this.position.z);
    if (g.type === 'water') {
      this.crash('You rolled off the island into the sea.');
      return;
    }
    const weight = SR22.mass * G;
    const normal = Math.max(0, weight - a.lift);
    const rolling = g.type === 'runway' ? SR22.rollingFriction : 0.07;
    const brake = (this.brakeLeft + this.brakeRight) / 2;
    let s = this.groundSpeed;
    let accel = (a.thrust - a.drag) / SR22.mass;
    if (s > 0.05 || accel > 0) {
      accel -= ((rolling + brake * SR22.brakeFriction) * normal) / SR22.mass;
    }
    s = Math.max(0, s + accel * h);
    if (s < 0.05 && a.thrust < (rolling + brake * SR22.brakeFriction) * normal) s = 0;
    this.groundSpeed = s;

    // Steering: free-castering nosewheel, so you steer with differential brakes and (at speed)
    // the rudder. With assists on, the rudder also steers at taxi speeds.
    let yawRate = (this.brakeRight - this.brakeLeft) * 0.7 * Math.min(1, s / 2);
    yawRate += this.yaw * SR22.maxYawRate * a.qfTail * 0.8;
    if (this.assists) yawRate += this.yaw * 0.45 * Math.min(1, s / 4);
    yawRate = MathUtils.clamp(yawRate, -0.6, 0.6);

    // Pitch: the nose only lifts once there's enough airflow over the elevator.
    const { pitch } = this.protectedInputs();
    const trimAlpha = MathUtils.lerp(SR22.trimAlpha[0], SR22.trimAlpha[1], (this.trim + 1) / 2);
    const alphaCmd = trimAlpha + pitch * SR22.elevatorAlpha;
    let theta = this.pitchAngle;
    const rotFactor = smoothstep(SR22.rotateMinSpeed, 36, s);
    if (alphaCmd > theta) theta += Math.min(0.5, SR22.pitchGain * (alphaCmd - theta) * rotFactor) * h;
    else theta -= Math.min(0.5, (theta - Math.max(alphaCmd, 0)) * 2 + 0.05) * h;
    theta = MathUtils.clamp(theta, 0, SR22.tailStrikePitch);

    const heading = this.heading + yawRate * h;
    this.quaternion.setFromAxisAngle(WORLD_UP, -heading);
    this.rotateLocal(1, 0, 0, theta);
    _fwd.set(Math.sin(heading), 0, -Math.cos(heading));
    this.velocity.copy(_fwd).multiplyScalar(s);
    this.position.addScaledVector(this.velocity, h);
    this.position.y = g.height + SR22.gearHeight;
    this.stallWarning = false;

    if (a.lift > weight * 1.02 && theta > 0.03) {
      this.onGround = false;
      this.velocity.y = 0.4;
      this.position.y += 0.05;
      this.emit('takeoff');
    }
  }

  checkContact(sampleGround) {
    const g = sampleGround(this.position.x, this.position.z);
    const surface = Math.max(g.height, 0);
    if (this.position.y - SR22.gearHeight > surface) return;
    const sink = -this.velocity.y;

    if (this.chute === 'inflating' || this.chute === 'deployed') {
      this.done = true;
      this.position.y = surface + SR22.gearHeight;
      this.velocity.set(0, 0, 0);
      this.emit('chuteLanded', { water: g.type === 'water', sink });
      return;
    }
    if (g.type === 'water') {
      this.crash('Ditched in the ocean.');
      return;
    }
    if (sink > SR22.hardSink) {
      this.crash(`Landing gear collapsed (sink rate ${Math.round(sink / 0.00508)} fpm).`);
      return;
    }
    const bank = Math.abs(this.bank);
    const pitch = this.pitchAngle;
    if (bank > 0.2) {
      this.crash('A wingtip hit the ground. Keep the wings level when you land.');
      return;
    }
    if (pitch < -0.07) {
      this.crash('Nose-first impact. Raise the nose (flare) just before touchdown.');
      return;
    }
    this.onGround = true;
    this.position.y = surface + SR22.gearHeight;
    _fwd.set(0, 0, -1).applyQuaternion(this.quaternion);
    _fwd.y = 0;
    this.groundSpeed = Math.max(0, this.velocity.dot(_fwd.normalize()));
    this.velocity.y = 0;
    this.emit('landed', { sink, hard: sink > SR22.safeSink, offRunway: g.type !== 'runway' });
  }

  crash(reason) {
    this.crashed = true;
    this.crashReason = reason;
    this.velocity.set(0, 0, 0);
    this.groundSpeed = 0;
    this.emit('crash', { reason });
  }
}

export { SR22 };
