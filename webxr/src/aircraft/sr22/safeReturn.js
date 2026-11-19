import { MathUtils } from 'three';
import { KT } from './sr22Data.js';
import { FlightDirector, wrapAngle } from './autopilot.js';

// Emergency autoland, in the spirit of the SR22 G7+'s Safe Return: one button and the
// aircraft flies itself to the island runway, sets up a stabilised approach, lands,
// brakes to a stop on the centreline and shuts the engine down.
//
// Runway: along Z, landing toward -Z. The threshold is at z = +halfLength and the
// aircraft aims to touch down 150 m past it.

const GLIDE = Math.tan(MathUtils.degToRad(3));
const IAF_DIST = 5500; // m from the threshold, on the extended centreline
const IAF_HEIGHT = 330; // m above the runway

export class SafeReturn {
  constructor(world) {
    this.world = world;
    this.fd = new FlightDirector();
    this.active = false;
    this.phase = 'OFF'; // OFF | ENROUTE | APPROACH | FLARE | ROLLOUT | DONE
    this.powerInt = 0;
    this.listeners = {};
  }

  on(event, fn) {
    (this.listeners[event] ||= []).push(fn);
  }

  emit(event, data) {
    (this.listeners[event] || []).forEach((fn) => fn(data));
  }

  get threshold() {
    return this.world.runway.halfLength;
  }

  get aimZ() {
    return this.threshold - 150;
  }

  get runwayHeight() {
    return this.world.runway.height;
  }

  /** Engage. Returns false if it can't help (e.g. already on the ground). */
  activate(model) {
    if (this.active || model.onGround || model.crashed || model.chute !== 'stowed') return false;
    this.active = true;
    this.savedAssists = model.assists;
    model.assists = true;
    this.fd.reset();
    this.powerInt = 0;
    this.setPhase(this.onFinal(model) ? 'APPROACH' : 'ENROUTE');
    return true;
  }

  cancel(model) {
    if (!this.active) return;
    this.active = false;
    if (model) model.assists = this.savedAssists;
    this.setPhase('OFF');
  }

  setPhase(phase) {
    if (this.phase === phase) return;
    this.phase = phase;
    this.emit('phase', { phase });
  }

  /** Already lined up behind the runway, heading roughly toward it? */
  onFinal(model) {
    const p = model.position;
    const dz = p.z - this.threshold;
    return dz > 1200 && Math.abs(p.x) < dz * 0.35 && Math.abs(wrapAngle(model.heading)) < 0.7;
  }

  /** Highest terrain over the next ~2 km of the flight path. */
  terrainAhead(model) {
    const f = model.forward;
    let max = 0;
    for (const d of [0, 300, 700, 1200, 2000]) {
      max = Math.max(max, this.world.heightAt(model.position.x + f.x * d, model.position.z + f.z * d));
    }
    return max;
  }

  powerForSpeed(model, kt, dt) {
    const err = kt - model.iasKt;
    this.powerInt = MathUtils.clamp(this.powerInt + err * 0.004 * dt, -0.3, 0.4);
    return MathUtils.clamp(0.45 + err * 0.04 + this.powerInt, 0, 1);
  }

  /**
   * @returns {null | {pitch, roll, yaw, throttle, brake, flaps}} full control commands
   */
  update(dt, model) {
    if (!this.active) return null;
    if (model.crashed || model.chute !== 'stowed') {
      this.cancel(model);
      return null;
    }
    const p = model.position;
    const out = { pitch: 0, roll: 0, yaw: 0, throttle: 0, brake: 0, flaps: 0 };

    if (this.phase === 'ENROUTE') {
      const iafZ = this.threshold + IAF_DIST;
      const toX = 0 - p.x;
      const toZ = iafZ - p.z;
      const dist = Math.hypot(toX, toZ);
      const bearing = Math.atan2(toX, -toZ);
      const targetAlt = Math.max(this.runwayHeight + IAF_HEIGHT, this.terrainAhead(model) + 180);
      out.roll = this.fd.rollFor(model, this.fd.bankForHeading(model, bearing, 0.44));
      const vsCmd = MathUtils.clamp((targetAlt - model.altitude) * 0.08, -5, 6);
      out.pitch = this.fd.pitchForVs(model, vsCmd, dt);
      out.throttle = model.altitude < targetAlt - 40 ? 1 : this.powerForSpeed(model, 120, dt);
      out.flaps = 0;
      if (dist < 900 || this.onFinal(model)) this.setPhase('APPROACH');
    } else if (this.phase === 'APPROACH') {
      const distToAim = Math.max(0, p.z - this.aimZ);
      // Lateral: track the extended centreline.
      const hdgCmd = -MathUtils.clamp(p.x * 0.005 + model.velocity.x * 0.02, -0.6, 0.6);
      out.roll = this.fd.rollFor(model, this.fd.bankForHeading(model, hdgCmd, 0.35));
      // Vertical: 3° glidepath to the aim point (never below the terrain margin).
      const gpAlt = this.runwayHeight + distToAim * GLIDE;
      const vsCmd = MathUtils.clamp(-model.speed * GLIDE + (gpAlt - model.altitude) * 0.15, -6, 3);
      out.pitch = this.fd.pitchForVs(model, vsCmd, dt);
      const final = distToAim < 3500;
      out.flaps = final ? 1 : 0.5;
      out.throttle = this.powerForSpeed(model, final ? 80 : 100, dt);
      out.yaw = 0;
      const height = model.altitude - this.runwayHeight;
      if (height < 9 && p.z < this.threshold + 300) this.setPhase('FLARE');
    } else if (this.phase === 'FLARE') {
      out.throttle = 0;
      out.flaps = 1;
      out.roll = this.fd.rollFor(model, 0);
      out.pitch = this.fd.pitchForVs(model, -0.6, dt);
      out.yaw = MathUtils.clamp(-wrapAngle(model.heading) * 3 - p.x * 0.02, -1, 1);
      if (model.onGround) this.setPhase('ROLLOUT');
    } else if (this.phase === 'ROLLOUT') {
      out.throttle = 0;
      out.flaps = 1;
      out.pitch = -0.3;
      const steer = MathUtils.clamp(-wrapAngle(model.heading) * 4 - p.x * 0.06, -1, 1);
      out.yaw = steer;
      out.brake = model.groundSpeed > 25 ? 0.4 : 0.8;
      if (model.groundSpeed < 0.3) {
        model.engineRunning = false;
        model.parkingBrake = true;
        this.setPhase('DONE');
        this.emit('complete', { onRunway: this.world.isRunway(p.x, p.z) });
      }
    } else if (this.phase === 'DONE') {
      out.brake = 1;
    }
    return out;
  }

  /** Short status line for the PFD/MFD. */
  get status() {
    return {
      OFF: '',
      ENROUTE: 'SAFE RETURN · NAVIGATING TO RUNWAY',
      APPROACH: 'SAFE RETURN · APPROACH',
      FLARE: 'SAFE RETURN · LANDING',
      ROLLOUT: 'SAFE RETURN · BRAKING',
      DONE: 'SAFE RETURN · COMPLETE',
    }[this.phase];
  }
}

export { KT };
