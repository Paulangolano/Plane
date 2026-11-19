import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Vector3, Quaternion, MathUtils } from 'three';
import { FlightModel, GEAR_HEIGHT } from '../../src/aircraft/flightModel.js';
import { WorldData } from '../../src/world/worldData.js';
import { RaceCourse } from '../../src/world/rings.js';
import { FLIGHT, COURSE } from '../../src/config.js';

// A simple autopilot flies the real course from a standing start, using only the same
// stick/throttle inputs a player has. Proves every ring is reachable and the course is
// completable without hitting terrain.

const world = new WorldData();
const ground = (x, z) => world.sampleGround(x, z);

function autopilot(f, course) {
  if (f.onGround) {
    return { pitch: f.speed > FLIGHT.takeoffSpeed ? 0.8 : 0, roll: 0, yaw: 0, throttle: 1, boost: false, brake: false };
  }
  const ring = course.current;
  const toRing = ring.center.clone().sub(f.position);
  const dist = toRing.length();
  // Aim at a point before the ring along its axis, so we arrive lined up.
  const lead = Math.min(350, dist * 0.45);
  const aim = ring.center.clone().addScaledVector(ring.normal, -lead);
  if (dist < 260) aim.copy(ring.center);
  const local = aim.sub(f.position).applyQuaternion(f.quaternion.clone().invert());
  const bearing = Math.atan2(local.x, -local.z);
  const elevation = Math.atan2(local.y, Math.hypot(local.x, local.z));
  const desiredBank = MathUtils.clamp(bearing * 2.2, -1.25, 1.25);
  const roll = MathUtils.clamp((desiredBank - f.bank) * 2.5, -1, 1);
  let pitch = MathUtils.clamp(elevation * 4 + Math.abs(f.bank) * 0.45, -1, 1);
  // Terrain safety
  const ahead = f.position.clone().addScaledVector(f.forward, 250);
  const clearance = f.position.y - Math.max(world.heightAt(ahead.x, ahead.z), world.heightAt(f.position.x, f.position.z), 0);
  if (clearance < 70) pitch = 1;
  return { pitch, roll, yaw: 0, throttle: 1, boost: false, brake: false };
}

test('autopilot takes off and completes the full ring course', () => {
  const f = new FlightModel();
  f.reset(new Vector3(0, world.runway.height + GEAR_HEIGHT, world.runway.halfLength - 40), 0);
  const course = new RaceCourse(world.rings);
  const passed = [];
  let tookOff = false;
  f.on('takeoff', () => {
    tookOff = true;
    course.start();
  });
  const dt = 1 / 60;
  const prev = new Vector3();
  let t = 0;
  for (; t < 900 && !course.finished && !f.crashed; t += dt) {
    prev.copy(f.position);
    f.update(dt, autopilot(f, course), ground);
    course.tick(dt);
    const ev = course.check(prev, f.position);
    if (ev) passed.push({ ring: ev.index + 1, t: +t.toFixed(1) });
  }
  assert.ok(tookOff, 'took off');
  assert.equal(f.crashed, false, `crashed after ring ${passed.length}: ${f.crashReason}`);
  assert.equal(passed.length, COURSE.ringCount, `only passed ${passed.length} rings in ${t.toFixed(0)} s`);
  assert.ok(course.finished);
  console.log(`autopilot lap: ${course.elapsed.toFixed(1)} s, rings at ${passed.map((p) => p.t).join(', ')}`);
  // A sensible lap time for an arcade course.
  assert.ok(course.elapsed > 60 && course.elapsed < 600, `lap time ${course.elapsed.toFixed(1)} s`);
});
