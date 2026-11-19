import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { startServer, openGame, waitFor, game } from './helpers.js';

// VR mode, using Meta's IWER to emulate a Quest 2 headset and Touch controllers.

let server;
let ctx;

before(async () => {
  server = await startServer(4313);
  ctx = await openGame(`${server.url}?emulate=quest2`);
});

after(async () => {
  await ctx?.browser.close();
  await server?.stop();
});

const device = (fn, arg) => ctx.page.evaluate(fn, arg);

/** Put a controller at a cockpit position given relative to the player's head. */
function hand(side, x, y, z) {
  return device(
    ([s, p]) => {
      const d = window.__xrDevice;
      d.controllers[s].position.set(d.position.x + p[0], d.position.y + p[1], d.position.z + p[2]);
    },
    [side, [x, y, z]],
  );
}

test('Enter VR is offered and starts an immersive session', async () => {
  const { page } = ctx;
  await waitFor(page, () => !document.querySelector('#enter-vr').disabled);
  assert.match(await page.textContent('#vr-note'), /Headset detected/);
  await page.click('#enter-vr');
  await waitFor(page, () => window.__skyreach.renderer.xr.isPresenting);
  await waitFor(page, () => Object.keys(window.__skyreach.xrInput.hands).length === 2);
  assert.equal(await page.isVisible('#overlay'), false);
  assert.match(await game(page, () => window.__skyreach.message.current), /Skyreach VR/);
});

test('seat calibration puts the head at the pilot eye point', async () => {
  await waitFor(ctx.page, () => window.__skyreach.calibrateIn <= 0);
  await ctx.page.waitForTimeout(300);
  const local = await game(ctx.page, () => {
    const g = window.__skyreach;
    const cam = g.renderer.xr.getCamera();
    const head = g.flight.position.clone().setFromMatrixPosition(cam.matrixWorld);
    return g.jet.group.worldToLocal(head).toArray();
  });
  for (const v of local) assert.ok(Math.abs(v) < 0.05, `head offset ${local}`);
});

test('both controllers render gloves inside the cockpit rig', async () => {
  const hands = await game(ctx.page, () => {
    const h = window.__skyreach.xrInput.hands;
    return Object.fromEntries(Object.entries(h).map(([k, v]) => [k, v.grip.parent?.name]));
  });
  assert.deepEqual(hands, { left: 'rig', right: 'rig' });
});

test('left thumbstick forward raises the throttle', async () => {
  await device(() => window.__xrDevice.controllers.left.updateAxes('thumbstick', 0, -1));
  await waitFor(ctx.page, () => window.__skyreach.input.throttle > 0.5);
  await device(() => window.__xrDevice.controllers.left.updateAxes('thumbstick', 0, 0));
});

test('grabbing the throttle with the left grip and pushing forward sets full power', async () => {
  await hand('left', -0.3, -0.42, -0.2);
  await ctx.page.waitForTimeout(200);
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('squeeze', 1));
  await waitFor(ctx.page, () => window.__skyreach.input.throttleGrabbed);
  await hand('left', -0.3, -0.42, -0.45);
  await waitFor(ctx.page, () => window.__skyreach.input.throttle > 0.99);
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('squeeze', 0));
  await waitFor(ctx.page, () => !window.__skyreach.input.throttleGrabbed);
  // Throttle stays where you left it.
  assert.ok((await game(ctx.page, () => window.__skyreach.input.throttle)) > 0.99);
});

test('grabbing the stick with the right grip and pulling back takes off', async () => {
  await waitFor(ctx.page, () => window.__skyreach.flight.speed > 47, null, 60000);
  await hand('right', 0, -0.5, -0.36);
  await ctx.page.waitForTimeout(200);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('squeeze', 1));
  await waitFor(ctx.page, () => window.__skyreach.input.stickGrabbed);
  // Pull the hand 7 cm back toward the body = nose up.
  await hand('right', 0, -0.5, -0.29);
  await waitFor(ctx.page, () => window.__skyreach.input.pitch > 0.7);
  await waitFor(ctx.page, () => window.__skyreach.state === 'flying', null, 30000);
  await hand('right', 0, -0.5, -0.36);
  const s = await game(ctx.page, () => ({ crashed: window.__skyreach.flight.crashed, alt: window.__skyreach.flight.altitude }));
  assert.equal(s.crashed, false);
});

test('moving the grabbed stick right rolls right', async () => {
  await hand('right', 0.08, -0.5, -0.36);
  await waitFor(ctx.page, () => window.__skyreach.input.roll > 0.8);
  await waitFor(ctx.page, () => window.__skyreach.flight.bank > 0.3);
  await hand('right', 0, -0.5, -0.36);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('squeeze', 0));
});

test('right trigger fires the afterburner', async () => {
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('trigger', 1));
  await waitFor(ctx.page, () => window.__skyreach.flight.boosting);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('trigger', 0));
  await waitFor(ctx.page, () => !window.__skyreach.flight.boosting);
});

test('B cycles the comfort vignette', async () => {
  const before = await game(ctx.page, () => window.__skyreach.vignette.levelIndex);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('b-button', 1));
  await waitFor(ctx.page, (b) => window.__skyreach.vignette.levelIndex !== b, before);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('b-button', 0));
  assert.match(await game(ctx.page, () => window.__skyreach.message.current), /Comfort/);
});

test('A twice in the air restarts on the runway', async () => {
  const press = () =>
    device(async () => {
      const c = window.__xrDevice.controllers.right;
      c.updateButtonValue('a-button', 1);
      await new Promise((r) => setTimeout(r, 250));
      c.updateButtonValue('a-button', 0);
      await new Promise((r) => setTimeout(r, 250));
    });
  await press();
  assert.match(await game(ctx.page, () => window.__skyreach.message.current), /Restart\?/);
  await press();
  await waitFor(ctx.page, () => window.__skyreach.state === 'ready');
});

test('VR frames render with the cockpit camera inside the jet', async () => {
  await ctx.page.waitForTimeout(500); // let a few frames render after the restart
  const info = await game(ctx.page, () => {
    const g = window.__skyreach;
    const cam = g.renderer.xr.getCamera();
    const head = g.flight.position.clone().setFromMatrixPosition(cam.matrixWorld);
    const local = g.jet.group.worldToLocal(head.clone());
    return {
      views: cam.cameras.length,
      local: local.toArray().map((v) => +v.toFixed(2)),
      inJet: head.distanceTo(g.jet.group.position) < 2,
    };
  });
  assert.equal(info.views, 2, 'stereo rendering (one view per eye)');
  assert.ok(info.inJet, `head is at ${info.local} relative to the pilot seat`);
});

test('clicking the left thumbstick re-centres the seat', async () => {
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('thumbstick', 1));
  await waitFor(ctx.page, () => /re-centred/.test(window.__skyreach.message.current));
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('thumbstick', 0));
});

test('no runtime errors in VR', () => {
  assert.deepEqual(ctx.errors, []);
});
