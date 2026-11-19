import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { startServer, openGame, waitFor, game } from './helpers.js';

// The SR22-class aircraft in VR on an emulated Quest 2: grabbing the side yoke and power
// lever, fingertip presses on panel buttons, trim hat, toe brakes, parachute and autoland.

let server;
let ctx;

before(async () => {
  server = await startServer(4314);
  ctx = await openGame(`${server.url}?emulate=quest2`);
});

after(async () => {
  await ctx?.browser.close();
  await server?.stop();
});

const device = (fn, arg) => ctx.page.evaluate(fn, arg);
const sr = (fn, arg) => ctx.page.evaluate(fn, arg);

/** Put a controller at a cockpit position (relative to the seated pilot's head), pointing forward. */
function hand(side, x, y, z) {
  return device(
    ([s, p]) => {
      const d = window.__xrDevice;
      const c = d.controllers[s];
      c.quaternion.set(0, 0, 0, 1);
      c.position.set(d.position.x + p[0], d.position.y + p[1], d.position.z + p[2]);
    },
    [side, [x, y, z]],
  );
}

/** Move a hand so its index fingertip touches a cockpit button (by poke-target id). */
async function touch(side, id) {
  const p = await sr((tid) => window.__skyreach.aircraft.layout.pokeTargets.find((t) => t.id === tid).position.toArray(), id);
  // Start from a hand position just behind the button, then correct by where the game
  // actually measures the fingertip (the controller's pointing ray depends on the device).
  await hand(side, p[0], p[1], p[2] + 0.2);
  await ctx.page.waitForTimeout(120);
  await device(
    ([s, target]) => {
      const g = window.__skyreach;
      const d = window.__xrDevice;
      const tip = g.xrInput.fingertip(s);
      const c = d.controllers[s];
      c.position.set(c.position.x + target[0] - tip.x, c.position.y + target[1] - tip.y, c.position.z + target[2] - tip.z);
    },
    [side, p],
  );
}

async function away(side) {
  await hand(side, side === 'left' ? -0.3 : 0.3, -0.75, 0.1);
}

async function tap(side, button) {
  await device(([s, b]) => window.__xrDevice.controllers[s].updateButtonValue(b, 1), [side, button]);
  await ctx.page.waitForTimeout(150);
  await device(([s, b]) => window.__xrDevice.controllers[s].updateButtonValue(b, 0), [side, button]);
  await ctx.page.waitForTimeout(150);
}

test('pick the SR22 on the start screen and enter VR', async () => {
  const { page } = ctx;
  await page.check('input[value="sr22"]');
  assert.equal(await game(page, () => window.__skyreach.aircraft.id), 'sr22');
  assert.equal(await page.isVisible('.controls[data-for="sr22"]'), true);
  assert.equal(await page.isVisible('.controls[data-for="jet"]'), false);
  await waitFor(page, () => !document.querySelector('#enter-vr').disabled);
  await page.click('#enter-vr');
  await waitFor(page, () => window.__skyreach.renderer.xr.isPresenting);
  await waitFor(page, () => window.__skyreach.calibrateIn <= 0);
  await away('left');
  await away('right');
  const parts = await game(page, () => {
    const g = window.__skyreach.aircraft.group;
    return ['PFD', 'MFD', 'yokeGrip', 'powerKnob', 'parachute'].filter((n) => g.getObjectByName(n));
  });
  assert.deepEqual(parts, ['PFD', 'MFD', 'yokeGrip', 'powerKnob', 'parachute']);
});

test('toe brakes: triggers set the left and right brakes', async () => {
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('trigger', 1));
  await waitFor(ctx.page, () => window.__skyreach.input.brakeLeft > 0.9 && window.__skyreach.input.brakeRight === 0);
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('trigger', 0));
  await waitFor(ctx.page, () => window.__skyreach.input.brakeLeft === 0);
});

test('A / B buttons set the flaps down and up', async () => {
  await tap('right', 'a-button');
  await waitFor(ctx.page, () => window.__skyreach.flight.flapsTarget === 0.5);
  await tap('right', 'b-button');
  await waitFor(ctx.page, () => window.__skyreach.flight.flapsTarget === 0);
});

test('touching the FLAP ▼ panel button with a fingertip lowers the flaps', async () => {
  await touch('right', 'flapsDown');
  await waitFor(ctx.page, () => window.__skyreach.flight.flapsTarget === 0.5);
  await away('right');
  await waitFor(ctx.page, () => !window.__skyreach.input.touching.has('flapsDown'));
  await touch('right', 'flapsUp');
  await waitFor(ctx.page, () => window.__skyreach.flight.flapsTarget === 0);
  await away('right');
});

test('PARK BRK button toggles the parking brake', async () => {
  await touch('right', 'park');
  await waitFor(ctx.page, () => window.__skyreach.flight.parkingBrake === true);
  await away('right');
  await ctx.page.waitForTimeout(200);
  await touch('right', 'park');
  await waitFor(ctx.page, () => window.__skyreach.flight.parkingBrake === false);
  await away('right');
});

test('right hand grabs the power lever and pushes it to full power', async () => {
  const grip = await sr(() => window.__skyreach.aircraft.layout.throttle.grip.toArray());
  await hand('right', ...grip);
  await ctx.page.waitForTimeout(200);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('squeeze', 1));
  await waitFor(ctx.page, () => window.__skyreach.input.throttleGrabbed);
  await hand('right', grip[0], grip[1], grip[2] - 0.16);
  await waitFor(ctx.page, () => window.__skyreach.input.throttle > 0.99);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('squeeze', 0));
  await away('right');
});

test('trim hat: the thumbstick of the hand on the yoke trims the aircraft', async () => {
  const grip = await sr(() => window.__skyreach.aircraft.layout.stick.grip.toArray());
  await hand('left', ...grip);
  await ctx.page.waitForTimeout(200);
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('squeeze', 1));
  await waitFor(ctx.page, () => window.__skyreach.input.stickGrabbed);
  const before = await sr(() => window.__skyreach.flight.trim);
  await device(() => window.__xrDevice.controllers.left.updateAxes('thumbstick', 0, 1)); // hat back = nose up
  await waitFor(ctx.page, (b) => window.__skyreach.flight.trim > b + 0.1, before);
  await device(() => window.__xrDevice.controllers.left.updateAxes('thumbstick', 0, 0));
});

test('take off by pulling back on the side yoke at rotation speed', async () => {
  // Software-rendered VR runs the simulation slower than real time here, so allow plenty of time.
  await waitFor(ctx.page, () => window.__skyreach.flight.iasKt > 72, null, 240000);
  const grip = await sr(() => window.__skyreach.aircraft.layout.stick.grip.toArray());
  await hand('left', grip[0], grip[1], grip[2] + 0.045); // pull back about 4.5 cm
  await waitFor(ctx.page, () => window.__skyreach.state === 'flying', null, 120000);
  await waitFor(ctx.page, () => window.__skyreach.flight.altitude > 40, null, 180000);
  await hand('left', ...grip);
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('squeeze', 0));
  await away('left');
  assert.equal(await sr(() => window.__skyreach.flight.crashed), false);
});

test('autopilot panel: AP, HDG, ALT and the blue LVL button', async () => {
  await touch('right', 'ap');
  await waitFor(ctx.page, () => window.__skyreach.aircraft.autopilot.engaged);
  await away('right');
  await ctx.page.waitForTimeout(150);
  await touch('right', 'hdg');
  await waitFor(ctx.page, () => window.__skyreach.aircraft.autopilot.lateral === 'HDG');
  await away('right');
  await ctx.page.waitForTimeout(150);
  await touch('right', 'alt');
  await waitFor(ctx.page, () => window.__skyreach.aircraft.autopilot.vertical === 'ALT');
  await away('right');
  await ctx.page.waitForTimeout(150);
  await touch('right', 'lvl');
  await waitFor(ctx.page, () => {
    const ap = window.__skyreach.aircraft.autopilot;
    return ap.lateral === 'LVL' && ap.vertical === 'LVL';
  });
  await away('right');
  await tap('left', 'x-button'); // X = autopilot off
  await waitFor(ctx.page, () => !window.__skyreach.aircraft.autopilot.engaged);
});

test('holding the overhead Safe Return button for a second starts the emergency autoland', async () => {
  await touch('right', 'safeReturn');
  await ctx.page.waitForTimeout(400);
  assert.equal(await sr(() => window.__skyreach.aircraft.safeReturn.active), false, 'needs a press-and-hold');
  // The hold timer counts simulated time, which runs slower than real time in software-rendered VR.
  await waitFor(ctx.page, () => window.__skyreach.aircraft.safeReturn.active, null, 30000);
  await away('right');
  assert.match(await sr(() => window.__skyreach.message.current), /SAFE RETURN/);
  await tap('left', 'x-button'); // cancel
  await waitFor(ctx.page, () => !window.__skyreach.aircraft.safeReturn.active);
});

test('grabbing the parachute handle and pulling it down deploys the parachute', async () => {
  const p = await sr(() => window.__skyreach.aircraft.layout.grabTargets[0].position.toArray());
  await hand('right', ...p);
  await ctx.page.waitForTimeout(200);
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('squeeze', 1));
  await ctx.page.waitForTimeout(200);
  await hand('right', p[0], p[1] - 0.13, p[2]);
  await waitFor(ctx.page, () => ['inflating', 'deployed'].includes(window.__skyreach.flight.chute));
  await device(() => window.__xrDevice.controllers.right.updateButtonValue('squeeze', 0));
  await away('right');
  assert.equal(await sr(() => window.__skyreach.flight.engineRunning), false);
  assert.equal(await sr(() => window.__skyreach.aircraft.group.getObjectByName('parachute').visible), true);
});

test('hold Y to restart on the runway', async () => {
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('y-button', 1));
  await ctx.page.waitForTimeout(600);
  assert.notEqual(await sr(() => window.__skyreach.state), 'ready', 'a short press does nothing');
  await waitFor(ctx.page, () => window.__skyreach.state === 'ready' || /Restart\?/.test(window.__skyreach.message.current), null, 30000);
  await device(() => window.__xrDevice.controllers.left.updateButtonValue('y-button', 0));
  await ctx.page.waitForTimeout(200);
  // In the air a second hold confirms.
  if ((await sr(() => window.__skyreach.state)) !== 'ready') {
    await device(() => window.__xrDevice.controllers.left.updateButtonValue('y-button', 1));
    await waitFor(ctx.page, () => window.__skyreach.state === 'ready', null, 30000);
    await device(() => window.__xrDevice.controllers.left.updateButtonValue('y-button', 0));
  }
  const s = await sr(() => ({ onGround: window.__skyreach.flight.onGround, chute: window.__skyreach.flight.chute }));
  assert.deepEqual(s, { onGround: true, chute: 'stowed' });
});

test('no runtime errors with the SR22', () => {
  assert.deepEqual(ctx.errors, []);
});
