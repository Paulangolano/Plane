import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { startServer, openGame, waitFor, game } from './helpers.js';

// Gameplay rules: ring passes, finishing the course, crashing and restarting.

let server;
let ctx;

before(async () => {
  server = await startServer(4312);
  ctx = await openGame(server.url);
  await ctx.page.click('#play-desktop');
});

after(async () => {
  await ctx?.browser.close();
  await server?.stop();
});

/** Place the jet 250 m in front of ring `i`, pointing through it. */
function lineUpWithRing(i) {
  return game(
    ctx.page,
    (index) => {
      const g = window.__skyreach;
      const ring = g.course.rings[index];
      const start = ring.center.clone().addScaledVector(ring.normal, -250);
      const heading = Math.atan2(-ring.normal.x, -ring.normal.z);
      g.flight.reset(start, heading, { onGround: false, speed: 110, throttle: 0.9 });
      // Pitch the nose to match the ring's slope.
      g.flight.rotateLocal(1, 0, 0, Math.asin(ring.normal.y));
      g.input.throttle = 0.9;
      g.state = 'flying';
      g.course.running = true;
      g.course.index = index;
      g.prevPos.copy(start);
    },
    i,
  );
}

test('a real takeoff on autopilot climbs straight through ring 1', async () => {
  // Drive the game's own input state each frame (no teleporting), like a player would.
  await game(ctx.page, () => {
    const g = window.__skyreach;
    g.__savedKeyboard = g.keyboard.update;
    g.keyboard.update = (dt, s) => {
      const f = g.flight;
      s.throttle = 1;
      s.roll = Math.max(-1, Math.min(1, -f.bank * 2));
      if (f.onGround) {
        s.pitch = f.speed > 46 ? 0.8 : 0;
        return true;
      }
      const ring = g.course.current;
      const local = ring.center.clone().sub(f.position).applyQuaternion(f.quaternion.clone().invert());
      const elevation = Math.atan2(local.y, Math.hypot(local.x, local.z));
      s.pitch = Math.max(-1, Math.min(1, elevation * 4));
      return true;
    };
  });
  await waitFor(ctx.page, () => window.__skyreach.state === 'flying', null, 60000);
  await waitFor(ctx.page, () => window.__skyreach.course.index === 1, null, 90000);
  const s = await game(ctx.page, () => {
    const g = window.__skyreach;
    g.keyboard.update = g.__savedKeyboard;
    return { crashed: g.flight.crashed, msg: g.message.current };
  });
  assert.equal(s.crashed, false);
  assert.match(s.msg, /Ring 1/);
});

test('flying through the active ring counts it', async () => {
  await lineUpWithRing(0);
  await waitFor(ctx.page, () => window.__skyreach.course.index === 1, null, 20000);
  const msg = await game(ctx.page, () => window.__skyreach.message.current);
  assert.match(msg, /Ring 1/);
});

test('rings must be flown in order (skipping ahead does not count)', async () => {
  // Fly through ring 3 while ring 2 (index 1) is active.
  await lineUpWithRing(2);
  await game(ctx.page, () => {
    window.__skyreach.course.index = 1;
  });
  await ctx.page.waitForTimeout(3500);
  assert.equal(await game(ctx.page, () => window.__skyreach.course.index), 1);
});

test('passing the last ring finishes the course', async () => {
  const last = await game(ctx.page, () => window.__skyreach.course.rings.length - 1);
  await lineUpWithRing(last);
  await waitFor(ctx.page, () => window.__skyreach.state === 'finished', null, 20000);
  const s = await game(ctx.page, () => ({
    finished: window.__skyreach.course.finished,
    best: window.__skyreach.settings.bestTime,
  }));
  assert.equal(s.finished, true);
  assert.ok(s.best !== null, 'best time saved');
});

test('diving into the sea crashes, Enter restarts on the runway', async () => {
  await game(ctx.page, () => {
    const g = window.__skyreach;
    const V = g.flight.position.constructor;
    g.flight.reset(new V(3500, 60, 3500), 0, { onGround: false, speed: 120, throttle: 1 });
    g.flight.rotateLocal(1, 0, 0, -0.6);
    g.state = 'flying';
  });
  await waitFor(ctx.page, () => window.__skyreach.state === 'crashed', null, 20000);
  assert.match(await game(ctx.page, () => window.__skyreach.message.current), /Crashed/);

  await ctx.page.keyboard.press('Enter');
  await waitFor(ctx.page, () => window.__skyreach.state === 'ready');
  const s = await game(ctx.page, () => ({
    onGround: window.__skyreach.flight.onGround,
    ring: window.__skyreach.course.index,
    z: window.__skyreach.flight.position.z,
  }));
  assert.equal(s.onGround, true);
  assert.equal(s.ring, 0);
  assert.ok(s.z > 0, 'back at the start of the runway');
});

test('no runtime errors', () => {
  assert.deepEqual(ctx.errors, []);
});
