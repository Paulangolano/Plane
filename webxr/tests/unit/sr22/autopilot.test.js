import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Vector3 } from 'three';
import { SR22Physics } from '../../../src/aircraft/sr22/sr22Physics.js';
import { KT, FT, FPM } from '../../../src/aircraft/sr22/sr22Data.js';
import { Autopilot, wrapAngle } from '../../../src/aircraft/sr22/autopilot.js';
import { SafeReturn } from '../../../src/aircraft/sr22/safeReturn.js';
import { WorldData } from '../../../src/world/worldData.js';

const world = new WorldData();
const ground = (x, z) => world.sampleGround(x, z);
const sky = () => ({ height: -1e6, type: 'land' });
const DT = 1 / 60;

function aloft({ alt = 900, kt = 120, heading = 0, throttle = 0.65, x = 0, z = 0 } = {}) {
  const p = new SR22Physics();
  // reset() takes a rotation angle; heading (compass, + = right) is its negative.
  p.reset(new Vector3(x, alt, z), -heading, { onGround: false, speed: kt * KT, throttle });
  return p;
}

function runAP(p, ap, seconds, throttle = 0.65, sample = sky) {
  for (let t = 0; t < seconds && !p.crashed; t += DT) {
    const cmd = ap.update(DT, p) || { pitch: 0, roll: 0 };
    p.update(DT, { ...cmd, throttle }, sample);
  }
}

test('HDG mode turns onto the heading bug and holds it', () => {
  const p = aloft({ heading: 0 });
  const ap = new Autopilot();
  ap.hdgBug = Math.PI / 2; // turn right to 090
  ap.altitudeHold(p);
  ap.heading(p);
  runAP(p, ap, 60);
  assert.ok(Math.abs(wrapAngle(p.heading - Math.PI / 2)) < 0.05, `heading ${(p.heading * 57.3).toFixed(1)}°`);
  assert.ok(Math.abs(p.bank) < 0.05);
});

test('ALT hold keeps altitude within 50 ft through a turn', () => {
  const p = aloft({ alt: 900 });
  const ap = new Autopilot();
  ap.altitudeHold(p);
  ap.hdgBug = Math.PI;
  ap.heading(p);
  let maxDev = 0;
  for (let t = 0; t < 60; t += DT) {
    const cmd = ap.update(DT, p);
    p.update(DT, { ...cmd, throttle: 0.65 }, sky);
    if (t > 5) maxDev = Math.max(maxDev, Math.abs(p.altitude - ap.altSel));
  }
  assert.ok(maxDev < 50 * FT, `max deviation ${(maxDev / FT).toFixed(0)} ft`);
});

test('VS mode climbs at the selected rate, then captures the selected altitude', () => {
  const p = aloft({ alt: 600 });
  const ap = new Autopilot();
  ap.altSel = 600 + 1000 * FT;
  ap.verticalSpeed(p);
  ap.vsSel = 700 * FPM;
  ap.altArmed = true;
  let vsSum = 0;
  let n = 0;
  let captured = false;
  ap.on('capture', () => (captured = true));
  for (let t = 0; t < 120; t += DT) {
    const cmd = ap.update(DT, p);
    p.update(DT, { ...cmd, throttle: 0.9 }, sky);
    if (t > 10 && t < 25) {
      vsSum += p.velocity.y;
      n++;
    }
  }
  const fpm = vsSum / n / FPM;
  assert.ok(Math.abs(fpm - 700) < 120, `climbing at ${fpm.toFixed(0)} fpm`);
  assert.ok(captured, 'captured altitude');
  assert.equal(ap.vertical, 'ALT');
  assert.ok(Math.abs(p.altitude - ap.altSel) < 60 * FT, `${((p.altitude - ap.altSel) / FT).toFixed(0)} ft off`);
});

test('LVL button recovers from a descending turn to wings level, level flight', () => {
  const p = aloft({ alt: 1200 });
  p.rotateLocal(0, 0, 1, -0.6); // 35° right bank
  p.rotateLocal(1, 0, 0, -0.15); // nose down
  const ap = new Autopilot();
  ap.level(p);
  runAP(p, ap, 20);
  assert.ok(Math.abs(p.bank) < 0.05, `bank ${(p.bank * 57.3).toFixed(1)}°`);
  assert.ok(Math.abs(p.velocity.y) < 1, `vs ${(p.velocity.y / FPM).toFixed(0)} fpm`);
});

test('autopilot disconnects in a stall', () => {
  const p = aloft({ kt: 75, throttle: 0 });
  p.assists = false;
  const ap = new Autopilot();
  let reason = null;
  ap.on('disconnect', (e) => (reason = e.reason));
  ap.altitudeHold(p);
  ap.altSel += 300; // ask for a climb with idle power
  runAP(p, ap, 60, 0);
  assert.equal(ap.engaged, false);
  assert.equal(reason, 'stall');
});

// ---------------------------------------------------------------- Safe Return

function safeReturnFrom(opts, seconds = 600) {
  const p = aloft(opts);
  const sr = new SafeReturn(world);
  let done = null;
  sr.on('complete', (e) => (done = e));
  assert.ok(sr.activate(p));
  for (let t = 0; t < seconds && !p.crashed && !done; t += DT) {
    const c = sr.update(DT, p);
    p.flapsTarget = c.flaps;
    p.update(DT, c, ground);
  }
  return { p, sr, done };
}

for (const [name, opts] of [
  ['already on final', { alt: 350, x: 150, z: world.runway.halfLength + 4000, heading: 0.1 }],
  ['downwind, heading away from the island', { alt: 500, x: 1500, z: -2000, heading: 0 }],
  ['north-east at low altitude', { alt: 250, x: 2500, z: 1500, heading: 2.5 }],
  ['just after take-off', { alt: 120, x: 0, z: -900, heading: 0, kt: 90 }],
]) {
  test(`Safe Return lands on the runway and stops: ${name}`, () => {
    const { p, sr, done } = safeReturnFrom(opts);
    assert.equal(p.crashed, false, `crashed in phase ${sr.phase}: ${p.crashReason}`);
    assert.ok(done, `still in phase ${sr.phase}`);
    assert.equal(done.onRunway, true, `stopped at x=${p.position.x.toFixed(0)} z=${p.position.z.toFixed(0)}`);
    assert.equal(p.engineRunning, false, 'engine shut down');
    assert.ok(Math.abs(p.position.x) < 12, `off centreline by ${p.position.x.toFixed(1)} m`);
  });
}

test('Safe Return will not engage on the ground', () => {
  const p = new SR22Physics();
  p.reset(new Vector3(0, 8, 0), 0);
  assert.equal(new SafeReturn(world).activate(p), false);
});
