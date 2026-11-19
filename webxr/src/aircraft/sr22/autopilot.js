import { MathUtils } from 'three';
import { FT, FPM } from './sr22Data.js';

// A small digital autopilot in the style of the SR22's: AP master, heading (HDG),
// altitude hold (ALT) with altitude capture, vertical speed (VS), pitch/roll hold (PIT/ROL)
// and the blue LVL button that levels the wings and stops the climb or descent.
// It moves the same stick the pilot does; power stays with the pilot (no autothrottle).

export function wrapAngle(a) {
  return Math.atan2(Math.sin(a), Math.cos(a));
}

/** Shared inner loops: bank tracking with the ailerons, vertical-speed tracking with the elevator. */
export class FlightDirector {
  constructor() {
    this.pitchInt = 0;
  }

  reset() {
    this.pitchInt = 0;
  }

  rollFor(model, bankCmd) {
    return MathUtils.clamp((bankCmd - model.bank) * 2.2 - (model.rollRate || 0) * 0.4, -1, 1);
  }

  /** Bank angle that turns toward a heading (rad), limited to ±maxBank. */
  bankForHeading(model, heading, maxBank = 0.44) {
    return MathUtils.clamp(wrapAngle(heading - model.heading) * 1.3, -maxBank, maxBank);
  }

  pitchForAttitude(model, thetaCmd, dt) {
    const err = thetaCmd - model.pitchAngle;
    this.pitchInt = MathUtils.clamp(this.pitchInt + err * 0.9 * dt, -0.7, 0.7);
    return MathUtils.clamp(err * 3 + this.pitchInt, -1, 1);
  }

  pitchForVs(model, vsCmd, dt) {
    const V = Math.max(model.speed, 20);
    const gamma = Math.asin(MathUtils.clamp(vsCmd / V, -0.5, 0.5));
    // Feed back the vertical speed error too, so it settles on the exact rate.
    const thetaCmd = gamma + model.alpha + MathUtils.clamp((vsCmd - model.velocity.y) * 0.02, -0.08, 0.08);
    return this.pitchForAttitude(model, thetaCmd, dt);
  }
}

export class Autopilot {
  constructor() {
    this.fd = new FlightDirector();
    this.engaged = false;
    this.lateral = 'ROL'; // ROL | HDG | LVL
    this.vertical = 'PIT'; // PIT | ALT | VS | LVL
    this.altArmed = false;
    this.hdgBug = 0; // rad, compass heading
    this.altSel = 1500 * FT; // m
    this.vsSel = 500 * FPM; // m/s
    this.rollHold = 0;
    this.pitchHold = 0;
    this.listeners = {};
  }

  on(event, fn) {
    (this.listeners[event] ||= []).push(fn);
  }

  emit(event, data) {
    (this.listeners[event] || []).forEach((fn) => fn(data));
  }

  engage(model) {
    if (this.engaged) return;
    this.engaged = true;
    this.fd.reset();
    if (this.lateral === 'ROL') this.rollHold = Math.abs(model.bank) < 0.1 ? 0 : MathUtils.clamp(model.bank, -0.44, 0.44);
    if (this.vertical === 'PIT') this.pitchHold = MathUtils.clamp(model.pitchAngle, -0.17, 0.26);
    this.emit('engage');
  }

  disconnect(reason = '') {
    if (!this.engaged) return;
    this.engaged = false;
    if (this.lateral === 'LVL') this.lateral = 'ROL';
    if (this.vertical === 'LVL') this.vertical = 'PIT';
    this.emit('disconnect', { reason });
  }

  toggle(model) {
    if (this.engaged) this.disconnect();
    else this.engage(model);
  }

  /** HDG button: fly the heading bug (engages the autopilot if it's off). */
  heading(model) {
    this.lateral = this.lateral === 'HDG' ? 'ROL' : 'HDG';
    if (this.lateral === 'ROL') this.rollHold = 0;
    this.engage(model);
  }

  /** ALT button: hold the current altitude. */
  altitudeHold(model) {
    this.vertical = 'ALT';
    this.altSel = model.altitude;
    this.altArmed = false;
    this.engage(model);
  }

  /** VS button: climb or descend at the selected rate, then capture the selected altitude. */
  verticalSpeed(model) {
    this.vertical = 'VS';
    this.vsSel = MathUtils.clamp(model.velocity.y, -1500 * FPM, 1500 * FPM);
    this.vsSel = Math.round(this.vsSel / (100 * FPM)) * 100 * FPM;
    this.altArmed = true;
    this.engage(model);
  }

  /** The blue LVL button: wings level, zero vertical speed. */
  level(model) {
    this.lateral = 'LVL';
    this.vertical = 'LVL';
    this.altArmed = false;
    this.engage(model);
    this.fd.reset();
  }

  hdgBugStep(deg) {
    this.hdgBug = wrapAngle(this.hdgBug + MathUtils.degToRad(deg));
  }

  altSelStep(feet) {
    this.altSel = Math.max(0, Math.round((this.altSel / FT + feet) / 100) * 100 * FT);
    if (this.vertical === 'VS') this.altArmed = true;
  }

  vsStep(fpm) {
    this.vsSel = MathUtils.clamp(this.vsSel + fpm * FPM, -2000 * FPM, 2000 * FPM);
  }

  /** @returns {{pitch:number, roll:number} | null} stick commands while engaged */
  update(dt, model) {
    if (!this.engaged) return null;
    if (model.stalling || model.onGround || model.crashed || model.chute !== 'stowed') {
      this.disconnect(model.stalling ? 'stall' : 'auto');
      return null;
    }
    let bankCmd;
    if (this.lateral === 'HDG') bankCmd = this.fd.bankForHeading(model, this.hdgBug);
    else if (this.lateral === 'LVL') bankCmd = 0;
    else bankCmd = this.rollHold;
    const roll = this.fd.rollFor(model, bankCmd);

    // Altitude capture from VS mode.
    if (this.vertical === 'VS' && this.altArmed) {
      const toGo = this.altSel - model.altitude;
      const closing = Math.sign(toGo) === Math.sign(this.vsSel) || this.vsSel === 0;
      if (Math.abs(toGo) < Math.max(15, Math.abs(model.velocity.y) * 5) || !closing) {
        if (closing) {
          this.vertical = 'ALT';
          this.altArmed = false;
          this.emit('capture');
        }
      }
    }

    let pitch;
    if (this.vertical === 'ALT') {
      const vsCmd = MathUtils.clamp((this.altSel - model.altitude) * 0.12, -5, 5);
      pitch = this.fd.pitchForVs(model, vsCmd, dt);
    } else if (this.vertical === 'VS') pitch = this.fd.pitchForVs(model, this.vsSel, dt);
    else if (this.vertical === 'LVL') pitch = this.fd.pitchForVs(model, 0, dt);
    else pitch = this.fd.pitchForAttitude(model, this.pitchHold, dt);
    return { pitch, roll };
  }

  /** Short labels for the PFD mode annunciator. */
  get annunciation() {
    return {
      ap: this.engaged,
      lateral: this.lateral,
      vertical: this.vertical,
      armed: this.altArmed ? 'ALTS' : '',
    };
  }
}
