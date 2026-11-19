import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Vector3 } from 'three';
import { SR22Physics } from '../../../src/aircraft/sr22/sr22Physics.js';
import { SR22, KT } from '../../../src/aircraft/sr22/sr22Data.js';
import { WorldData } from '../../../src/world/worldData.js';

// Checks the flight model against the real aircraft's published numbers.

const world = new WorldData();
const ground = (x, z) => world.sampleGround(x, z);
const highGround = () => ({ height: -1e6, type: 'land' }); // no terrain for up-and-away tests
const DT = 1 / 72;

function onRunway() {
  const p = new SR22Physics();
  p.reset(new Vector3(0, world.runway.height + SR22.gearHeight, world.runway.halfLength - 40), 0);
  return p;
}

function aloft({ alt = 1500, kt = 110, throttle = 0.6, flaps = 0, assists = false } = {}) {
  const p = new SR22Physics();
  p.assists = assists;
  p.reset(new Vector3(0, alt, 0), 0, { onGround: false, speed: kt * KT, throttle, flaps });
  return p;
}

function fly(p, seconds, input, sample = highGround) {
  for (let t = 0; t < seconds && !p.crashed && !p.done; t += DT) p.update(DT, typeof input === 'function' ? input(p) : input, sample);
}

const clamp = (v) => Math.max(-1, Math.min(1, v));
// A gentle "pilot" that holds altitude with the stick and keeps wings level.
const holdAlt = (alt, throttle) => (p) => ({
  throttle,
  pitch: clamp((alt - p.position.y) * 0.01 - p.velocity.y * 0.08 + 0.05),
  roll: clamp(-p.bank * 2),
});

test('parked with parking brake on and idle power, it does not creep', () => {
  const p = onRunway();
  p.parkingBrake = true;
  const start = p.position.clone();
  fly(p, 5, { throttle: 0 }, ground);
  assert.ok(p.position.distanceTo(start) < 0.1);
});

test('take-off: lifts off around 70-75 kt within the runway', () => {
  const p = onRunway();
  let liftoffKt = null;
  let liftoffZ = null;
  p.on('takeoff', () => {
    liftoffKt = p.iasKt;
    liftoffZ = p.position.z;
  });
  fly(p, 60, (q) => ({ throttle: 1, pitch: q.iasKt > SR22.vr ? 0.5 : 0, roll: clamp(-q.bank * 2) }), ground);
  assert.ok(liftoffKt, 'lifted off');
  assert.ok(liftoffKt > 65 && liftoffKt < 80, `liftoff at ${liftoffKt.toFixed(0)} kt`);
  assert.ok(liftoffZ > -world.runway.halfLength, 'still over the runway at liftoff');
  assert.equal(p.crashed, false, p.crashReason);
});

test('the nose will not lift at low speed, however hard you pull', () => {
  const p = onRunway();
  fly(p, 4, { throttle: 0.4, pitch: 1 }, ground);
  assert.ok(p.iasKt < 35);
  assert.ok(p.pitchAngle < 0.01, `pitch ${p.pitchAngle}`);
  assert.equal(p.onGround, true);
});

test('stall speeds match the book: ~70 kt clean, ~65 kt flaps 50%, ~60 kt flaps 100%', () => {
  const expected = [70, 65, 60];
  [0, 0.5, 1].forEach((flaps, i) => {
    const p = aloft({ kt: 90, throttle: 0, flaps });
    let stallKt = null;
    fly(p, 120, (q) => {
      if (q.stalling && stallKt === null) stallKt = q.iasKt;
      return holdAlt(1500, 0)(q);
    });
    assert.ok(stallKt !== null, `stalled with flaps ${flaps}`);
    assert.ok(Math.abs(stallKt - expected[i]) < 3, `flaps ${flaps}: stalled at ${stallKt.toFixed(1)} kt`);
  });
});

test('climb at Vy with full power is about 1,200-1,400 fpm', () => {
  const p = aloft({ alt: 300, kt: 108, throttle: 1 });
  let vs = 0;
  let n = 0;
  fly(p, 60, (q) => {
    if (q.position.y > 0) {
      vs += q.velocity.y;
      n++;
    }
    return { throttle: 1, pitch: clamp((q.iasKt - SR22.vy) * 0.04 - (q.pitchAngle - 0.12) * 0), roll: clamp(-q.bank * 2) };
  });
  const fpm = vs / n / 0.00508;
  assert.ok(fpm > 1100 && fpm < 1500, `climb ${fpm.toFixed(0)} fpm`);
});

test('full-power level speed is around 165-185 kt', () => {
  const p = aloft({ alt: 600, kt: 150, throttle: 1 });
  fly(p, 150, holdAlt(600, 1));
  assert.ok(p.iasKt > 160 && p.iasKt < 190, `${p.iasKt.toFixed(0)} kt`);
});

test('engine-off glide ratio is roughly 9:1 near best-glide speed', () => {
  const p = aloft({ alt: 2500, kt: 88, throttle: 0 });
  p.engineRunning = false;
  p.trim = 0.02;
  fly(p, 60, { throttle: 0, roll: 0 }); // settle
  const start = p.position.clone();
  const e0 = p.position.y + p.velocity.lengthSq() / (2 * 9.81);
  let kt = 0;
  let n = 0;
  fly(p, 120, (q) => {
    kt += q.iasKt;
    n++;
    return { throttle: 0, roll: clamp(-q.bank * 2) };
  });
  const dist = Math.hypot(p.position.x - start.x, p.position.z - start.z);
  const lost = e0 - (p.position.y + p.velocity.lengthSq() / (2 * 9.81));
  const ratio = dist / lost;
  assert.ok(ratio > 8 && ratio < 11.5, `glide ratio ${ratio.toFixed(1)} at ${(kt / n).toFixed(0)} kt`);
});

test('hands-off it is stable: disturbances die away', () => {
  const p = aloft({ kt: 120, throttle: 0.6 });
  p.rotateLocal(1, 0, 0, 0.15); // nose kicked up
  fly(p, 90, { throttle: 0.6, roll: 0 });
  assert.equal(p.crashed, false);
  assert.ok(Math.abs(p.pitchAngle) < 0.15, `pitch ${p.pitchAngle}`);
});

test('banking turns the aircraft (coordinated turn)', () => {
  const p = aloft({ kt: 120, throttle: 0.6 });
  fly(p, 1.2, { throttle: 0.6, roll: 1 });
  assert.ok(p.bank > 0.3, `bank ${p.bank}`);
  fly(p, 8, (q) => ({ throttle: 0.6, roll: clamp((0.5 - q.bank) * 2), pitch: 0.15 }));
  assert.ok(p.forward.x > 0.3, `turned right, forward.x=${p.forward.x.toFixed(2)}`);
  assert.ok(Math.abs(p.beta) < 0.08, `sideslip ${p.beta}`);
});

test('trim changes the hands-off speed (nose-up trim = slower)', () => {
  const speeds = [-0.2, 0.3].map((trim) => {
    const p = aloft({ kt: 120, throttle: 0.6 });
    p.trim = trim;
    fly(p, 120, (q) => ({ throttle: 0.6, roll: clamp(-q.bank * 2) }));
    return p.iasKt;
  });
  assert.ok(speeds[0] > speeds[1] + 15, `nose-down ${speeds[0].toFixed(0)} kt vs nose-up ${speeds[1].toFixed(0)} kt`);
});

test('flaps extend gradually and add lift + drag', () => {
  const p = aloft({ kt: 100, throttle: 0.5 });
  p.flapsStep(1);
  fly(p, 1, holdAlt(1500, 0.5));
  assert.ok(p.flaps > 0.05 && p.flaps < 0.5, `flaps moving: ${p.flaps}`);
  fly(p, 6, holdAlt(1500, 0.5));
  assert.equal(p.flaps, 0.5);
  p.flapsStep(1);
  fly(p, 8, holdAlt(1500, 0.5));
  assert.equal(p.flaps, 1);
  p.flapsStep(1); // already fully down
  assert.equal(p.flapsTarget, 1);
});

test('envelope protection (assists) pushes the nose down before a stall', () => {
  const p = aloft({ kt: 90, throttle: 0, assists: true });
  let stalled = false;
  fly(p, 40, (q) => {
    stalled ||= q.stalling;
    return { throttle: 0, pitch: 1, roll: 0 };
  });
  assert.equal(stalled, false, 'should not stall with protection on');
});

test('envelope protection rolls back from steep banks', () => {
  const p = aloft({ kt: 120, throttle: 0.6, assists: true });
  fly(p, 6, { throttle: 0.6, roll: 1, pitch: 0.2 });
  assert.ok(Math.abs(p.bank) < 1.0, `bank ${(p.bank * 57.3).toFixed(0)}°`);
});

test('without assists, holding full back stick stalls and drops a wing', () => {
  const p = aloft({ kt: 80, throttle: 0 });
  let maxBank = 0;
  let stalled = false;
  fly(p, 12, (q) => {
    stalled ||= q.stalling;
    maxBank = Math.max(maxBank, Math.abs(q.bank));
    return { throttle: 0, pitch: 1, roll: 0 };
  });
  assert.ok(stalled);
  assert.ok(maxBank > 0.3, `wing drop ${(maxBank * 57.3).toFixed(0)}°`);
});

test('a stabilised approach with a flare lands on the runway', () => {
  const p = new SR22Physics();
  const startZ = world.runway.halfLength + 900;
  const gs = Math.tan((3 * Math.PI) / 180);
  p.reset(new Vector3(0, world.runway.height + SR22.gearHeight + 900 * gs + 15, startZ), 0, {
    onGround: false,
    speed: 80 * KT,
    throttle: 0.25,
    flaps: 1,
  });
  let landed = null;
  p.on('landed', (e) => (landed = e));
  fly(
    p,
    90,
    (q) => {
      const hgt = q.altitude - world.runway.height;
      const vsTarget = hgt > 8 ? -80 * KT * gs : -0.6;
      return {
        throttle: hgt > 8 ? clamp(0.25 + (80 - q.iasKt) * 0.03) : 0,
        pitch: clamp((vsTarget - q.velocity.y) * 0.25),
        roll: clamp(-q.bank * 2 - q.position.x * 0.01),
        brake: q.onGround ? 1 : 0,
      };
    },
    ground,
  );
  assert.ok(landed, p.crashReason || 'no touchdown');
  assert.equal(landed.offRunway, false);
  assert.equal(landed.hard, false, `sink ${landed.sink.toFixed(1)} m/s`);
  assert.equal(p.crashed, false);
  assert.ok(p.groundSpeed < 1, 'braked to a stop');
});

test('arriving too hard collapses the gear', () => {
  const p = new SR22Physics();
  p.reset(new Vector3(0, world.runway.height + SR22.gearHeight + 3, 0), 0, { onGround: false, speed: 30, flaps: 1 });
  p.velocity.y = -8; // dropping like a stone just above the runway
  fly(p, 3, { throttle: 0 }, ground);
  assert.equal(p.crashed, true);
  assert.match(p.crashReason, /gear/);
});

test('parachute: deploys below the limit speed and descends at ~1,500-2,000 fpm', () => {
  const p = aloft({ alt: 1200, kt: 100, throttle: 0.5 });
  assert.equal(p.deployChute(), 'deployed');
  assert.equal(p.engineRunning, false);
  fly(p, 25, { throttle: 0 });
  assert.equal(p.chute, 'deployed');
  const fpm = -p.velocity.y / 0.00508;
  assert.ok(fpm > 1500 && fpm < 2000, `descent ${fpm.toFixed(0)} fpm`);
  assert.ok(p.speed < 13, `total speed ${p.speed.toFixed(1)} m/s`);
});

test('parachute landing ends the flight safely', () => {
  const p = aloft({ alt: 300, kt: 100, throttle: 0.5 });
  p.position.set(0, 300, 0); // over the home island
  let landed = null;
  p.on('chuteLanded', (e) => (landed = e));
  p.deployChute();
  fly(p, 120, { throttle: 0 }, ground);
  assert.ok(landed, 'touched down under canopy');
  assert.equal(p.crashed, false);
  assert.equal(p.done, true);
});

test('parachute fails if pulled far too fast, and cannot be pulled on the ground', () => {
  const fast = aloft({ kt: 185, throttle: 1 });
  assert.equal(fast.deployChute(), 'failed');
  const parked = onRunway();
  assert.equal(parked.deployChute(), 'ground');
});

test('diving past the structural limit breaks the airframe', () => {
  const p = aloft({ alt: 3000, kt: 200, throttle: 1 });
  fly(p, 30, { throttle: 1, pitch: -1 });
  assert.equal(p.crashed, true);
  assert.match(p.crashReason, /overstressed/);
});

test('engine stops when the fuel runs out', () => {
  const p = aloft({ kt: 120, throttle: 0.7 });
  p.fuelGal = 0.01;
  let out = false;
  p.on('engineOut', () => (out = true));
  fly(p, 10, holdAlt(1500, 0.7));
  assert.ok(out);
  assert.equal(p.engineRunning, false);
});
