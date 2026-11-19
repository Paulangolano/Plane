import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { startServer, openGame, waitFor, game } from './helpers.js';

let server;
let ctx;

before(async () => {
  server = await startServer(4311);
  ctx = await openGame(server.url);
});

after(async () => {
  await ctx?.browser.close();
  await server?.stop();
});

test('start screen loads without errors', async () => {
  const { page, errors } = ctx;
  assert.equal(await page.textContent('h1'), 'Skyreach VR');
  // No headset in the test browser, so Enter VR stays disabled with a helpful note.
  assert.equal(await page.isDisabled('#enter-vr'), true);
  assert.match(await page.textContent('#vr-note'), /Quest|https/);
  assert.deepEqual(errors, []);
});

test('scene stays inside the Quest 2 performance budget', async () => {
  const info = await game(ctx.page, () => {
    const r = window.__skyreach.renderer.info.render;
    return { calls: r.calls, triangles: r.triangles };
  });
  assert.ok(info.calls > 10 && info.calls < 120, `draw calls ${info.calls}`);
  assert.ok(info.triangles < 260000, `triangles ${info.triangles}`);
});

test('keyboard takeoff: throttle up, pull back, get airborne', async () => {
  const { page } = ctx;
  await page.click('#play-desktop');
  assert.equal(await page.isVisible('#overlay'), false);
  assert.equal(await game(page, () => window.__skyreach.state), 'ready');

  await page.keyboard.down('ShiftLeft');
  await waitFor(page, () => window.__skyreach.flight.speed * 3.6 > 175, null, 60000);
  await page.keyboard.down('KeyS'); // pull back
  await waitFor(page, () => window.__skyreach.state === 'flying', null, 30000);
  await waitFor(page, () => window.__skyreach.flight.altitude > 25, null, 30000);
  await page.keyboard.up('KeyS');
  await page.keyboard.up('ShiftLeft');

  const s = await game(page, () => ({
    running: window.__skyreach.course.running,
    crashed: window.__skyreach.flight.crashed,
  }));
  assert.equal(s.running, true, 'race timer starts on takeoff');
  assert.equal(s.crashed, false);
});

test('chase camera toggles with V', async () => {
  const { page } = ctx;
  await page.keyboard.press('KeyV');
  assert.equal(await game(page, () => window.__skyreach.viewMode), 'chase');
  await page.keyboard.press('KeyV');
  assert.equal(await game(page, () => window.__skyreach.viewMode), 'cockpit');
});

test('no runtime errors during flight', async () => {
  assert.deepEqual(ctx.errors, []);
});
