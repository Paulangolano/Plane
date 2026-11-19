import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Vector3 } from 'three';
import { FlightModel, GEAR_HEIGHT } from '../../../src/aircraft/jet/flightModel.js';
import { WorldData } from '../../../src/world/worldData.js';
import { FLIGHT } from '../../../src/config.js';

const world = new WorldData();
const ground = (x, z) => world.sampleGround(x, z);
const idle = { pitch: 0, roll: 0, yaw: 0, throttle: 0, boost: false, brake: false };
const DT = 1 / 72;

function spawn() {
  const fm = new FlightModel();
  fm.reset(new Vector3(0, world.runway.height + GEAR_HEIGHT, world.runway.halfLength - 30), 0);
  return fm;
}

function run(fm, seconds, input) {
  for (let t = 0; t < seconds; t += DT) {
    fm.update(DT, typeof input === 'function' ? input(fm, t) : input, ground);
    if (fm.crashed) break;
  }
}

test('jet sits still on the runway at idle', () => {
  const fm = spawn();
  const start = fm.position.clone();
  run(fm, 3, idle);
  assert.equal(fm.crashed, false);
  assert.equal(fm.onGround, true);
  assert.ok(fm.position.distanceTo(start) < 0.5, 'should not move at idle');
});

test('full throttle + pull back takes off before the end of the runway', () => {
  const fm = spawn();
  let tookOff = false;
  fm.on('takeoff', () => (tookOff = true));
  run(fm, 20, (f) => ({ ...idle, throttle: 1, pitch: f.speed > FLIGHT.takeoffSpeed && f.pitchAngle < 0.25 ? 0.8 : 0 }));
  assert.equal(fm.crashed, false, fm.crashReason);
  assert.ok(tookOff, 'should leave the ground');
  assert.ok(fm.altitude > 30, `should be climbing, altitude=${fm.altitude.toFixed(1)}`);
});

test('without pulling back the jet stays on the ground', () => {
  const fm = spawn();
  run(fm, 6, { ...idle, throttle: 1 });
  assert.equal(fm.onGround, true);
});

test('rolling right turns the jet to the right', () => {
  const fm = new FlightModel();
  fm.reset(new Vector3(0, 600, 0), 0, { onGround: false, speed: 100, throttle: 0.8 });
  run(fm, 0.5, { ...idle, throttle: 0.8, roll: 1 });
  assert.ok(fm.bank > 0.3, `bank should be positive (right wing down), got ${fm.bank}`);
  run(fm, 3, { ...idle, throttle: 0.8 });
  const f = fm.forward;
  assert.ok(f.x > 0.2, `heading should swing right (+X), forward.x=${f.x.toFixed(2)}`);
});

test('pulling back pitches the nose up', () => {
  const fm = new FlightModel();
  fm.reset(new Vector3(0, 600, 0), 0, { onGround: false, speed: 100, throttle: 0.8 });
  run(fm, 0.6, { ...idle, throttle: 0.8, pitch: 1 });
  assert.ok(fm.pitchAngle > 0.2, `pitch angle ${fm.pitchAngle}`);
});

test('slow flight stalls and sinks', () => {
  const fm = new FlightModel();
  fm.reset(new Vector3(0, 900, 0), 0, { onGround: false, speed: 20, throttle: 0 });
  const y0 = fm.position.y;
  run(fm, 3, idle);
  assert.ok(fm.stalling, 'should be stalling');
  assert.ok(fm.position.y < y0 - 10, 'should lose altitude');
});

test('diving into the sea crashes', () => {
  const fm = new FlightModel();
  fm.reset(new Vector3(3000, 80, 3000), 0, { onGround: false, speed: 110, throttle: 1 });
  let crashed = false;
  fm.on('crash', () => (crashed = true));
  run(fm, 10, { ...idle, throttle: 1, pitch: -1 });
  assert.ok(crashed);
  assert.match(fm.crashReason, /ocean|island/);
});

test('a gentle approach lands on the runway', () => {
  const fm = new FlightModel();
  // 200 m out on final, 12 m up, slow descent.
  fm.reset(new Vector3(0, world.runway.height + GEAR_HEIGHT + 12, world.runway.halfLength - 50), 0, {
    onGround: false,
    speed: 50,
    throttle: 0.4,
  });
  fm.rotateLocal(1, 0, 0, -0.05);
  let landed = false;
  fm.on('landed', () => (landed = true));
  run(fm, 8, (f) => ({ ...idle, throttle: 0.4, brake: f.onGround }));
  assert.equal(fm.crashed, false, fm.crashReason);
  assert.ok(landed, 'should register a landing');
});

test('boost drains and recharges', () => {
  const fm = new FlightModel();
  fm.reset(new Vector3(0, 800, 0), 0, { onGround: false, speed: 100, throttle: 1 });
  run(fm, 2, { ...idle, throttle: 1, boost: true });
  assert.ok(fm.boostFuel < FLIGHT.boostDuration - 1.5);
  const low = fm.boostFuel;
  run(fm, 2, { ...idle, throttle: 1 });
  assert.ok(fm.boostFuel > low);
});
