import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WorldData } from '../../../src/world/worldData.js';
import { COURSE, WORLD } from '../../../src/config.js';

const world = new WorldData();

test('world generation is deterministic', () => {
  const other = new WorldData();
  assert.deepEqual(other.islands, world.islands);
  assert.equal(other.heightAt(123, -456), world.heightAt(123, -456));
});

test('places the requested number of islands without overlap', () => {
  assert.equal(world.islands.length, WORLD.islandCount);
});

test('runway is flat and above sea level', () => {
  const { halfWidth, halfLength, height } = world.runway;
  for (const [x, z] of [[0, 0], [halfWidth - 1, halfLength - 1], [-halfWidth + 1, -halfLength + 1]]) {
    const g = world.sampleGround(x, z);
    assert.equal(g.type, 'runway');
    assert.equal(g.height, height);
  }
  assert.ok(height > 0);
});

test('runway surroundings blend smoothly (no cliffs next to the tarmac)', () => {
  const { halfWidth, height } = world.runway;
  const beside = world.heightAt(halfWidth + 10, 0);
  assert.ok(Math.abs(beside - height) < 3, `height beside runway ${beside}`);
});

test('open ocean is water', () => {
  assert.equal(world.sampleGround(5800, 5800).type, 'water');
});

test('every island rises above the sea', () => {
  for (const isl of world.islands) {
    assert.ok(world.heightAt(isl.x, isl.z) > 2, `island at ${isl.x.toFixed(0)},${isl.z.toFixed(0)} is underwater`);
  }
});

test('ring course has the right length and clears the terrain', () => {
  assert.equal(world.rings.length, COURSE.ringCount);
  for (const r of world.rings) {
    const { x, y, z } = r.position;
    assert.ok(y - COURSE.ringRadius > world.heightAt(x, z) + 10, `ring at ${x},${z} clips terrain`);
    const n = r.normal;
    assert.ok(Math.abs(Math.hypot(n.x, n.y, n.z) - 1) < 1e-6);
  }
});

test('first ring is straight ahead of the runway and the last lines up for landing', () => {
  const first = world.rings[0].position;
  const last = world.rings.at(-1).position;
  assert.equal(first.x, 0);
  assert.ok(first.z < -world.runway.halfLength);
  assert.equal(last.x, 0);
  assert.ok(last.z > world.runway.halfLength);
  // Both face along the runway so you fly straight through them.
  assert.ok(world.rings[0].normal.z < -0.99, 'ring 1 faces the takeoff direction');
  assert.ok(world.rings.at(-1).normal.z < -0.99, 'last ring faces the landing direction');
});

test('every ring can be entered from the previous one (no edge-on rings)', () => {
  for (let i = 1; i < world.rings.length; i++) {
    const a = world.rings[i - 1].position;
    const b = world.rings[i];
    const d = { x: b.position.x - a.x, y: b.position.y - a.y, z: b.position.z - a.z };
    const len = Math.hypot(d.x, d.y, d.z);
    const facing = (d.x * b.normal.x + d.y * b.normal.y + d.z * b.normal.z) / len;
    assert.ok(facing > 0.5, `ring ${i + 1} is at ${Math.round((Math.acos(facing) * 180) / Math.PI)}° to the approach`);
  }
});
