import * as THREE from 'three';
import { SR22Physics } from './sr22Physics.js';
import { SR22, FT, FPM, KT, flapIndex } from './sr22Data.js';
import { createSR22Model, SR22_COCKPIT, SR22_CABIN_CENTER as CX, labelMesh, panelPoint } from './sr22Model.js';
import { createAvionics } from './avionics.js';
import { Autopilot } from './autopilot.js';
import { SafeReturn } from './safeReturn.js';
import { formatTime } from '../../ui/format.js';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

// The SR22-class aircraft: realistic physics, side yoke + power lever, flaps, trim,
// differential brakes, glass cockpit, autopilot, parachute and emergency autoland.

const tilt = SR22_COCKPIT.panelTilt;
const onPanel = (x, y) => panelPoint(x, y, 0.007);

// Cockpit buttons: id, label, position, action, colour, optional hold time (s)
const PANEL_BUTTONS = [
  // Autopilot controller under the displays
  { id: 'ap', label: 'AP', x: -0.1, y: -0.54, action: 'apToggle' },
  { id: 'hdg', label: 'HDG', x: -0.03, y: -0.54, action: 'apHdg' },
  { id: 'alt', label: 'ALT', x: 0.04, y: -0.54, action: 'apAlt' },
  { id: 'vs', label: 'VS', x: 0.11, y: -0.54, action: 'apVs' },
  { id: 'lvl', label: 'LVL', x: 0.18, y: -0.54, action: 'apLvl', color: 0x1f6fe0 },
  { id: 'hdgDec', label: 'HDG ◀', x: 0.27, y: -0.54, action: 'hdgDec' },
  { id: 'hdgInc', label: 'HDG ▶', x: 0.33, y: -0.54, action: 'hdgInc' },
  { id: 'altDec', label: 'ALT ▼', x: 0.4, y: -0.54, action: 'altDec' },
  { id: 'altInc', label: 'ALT ▲', x: 0.46, y: -0.54, action: 'altInc' },
  { id: 'vsDec', label: 'VS ▼', x: 0.53, y: -0.54, action: 'vsDec' },
  { id: 'vsInc', label: 'VS ▲', x: 0.59, y: -0.54, action: 'vsInc' },
  // Flaps (beside the flap lever)
  { id: 'flapsUp', label: 'FLAP ▲', x: 0.7, y: -0.5, action: 'flapsUp' },
  { id: 'flapsDown', label: 'FLAP ▼', x: 0.7, y: -0.62, action: 'flapsDown' },
  // Lower panel: systems and settings
  { id: 'park', label: 'PARK BRK', x: -0.1, y: -0.61, action: 'parkBrake' },
  { id: 'assist', label: 'ASSIST', x: -0.02, y: -0.61, action: 'toggleAssists' },
  { id: 'comfort', label: 'COMFORT', x: 0.06, y: -0.61, action: 'cycleComfort' },
  { id: 'sound', label: 'SOUND', x: 0.14, y: -0.61, action: 'toggleMute' },
  { id: 'restart', label: 'RESTART', x: 0.22, y: -0.61, action: 'restart', hold: 1, color: 0x8a2a2a },
];

export function createSR22Aircraft(world, settings = {}) {
  const model = new SR22Physics();
  model.assists = settings.assists ?? true;
  const m3d = createSR22Model();
  const avionics = createAvionics(m3d, world);
  const ap = new Autopilot();
  const sr = new SafeReturn(world);
  const lights = {};
  let ctxRef = null;

  // ---- panel buttons: caps merged per colour, one label atlas, instanced indicator lights
  const pokeTargets = [];
  const { atlas, uvFor } = buildLabelAtlas(PANEL_BUTTONS.map((b) => b.label));
  const capGeos = new Map();
  const labelGeos = [];
  const ledMesh = new THREE.InstancedMesh(new THREE.BoxGeometry(0.02, 0.004, 0.004), new THREE.MeshBasicMaterial(), PANEL_BUTTONS.length);
  const tmp = new THREE.Object3D();
  PANEL_BUTTONS.forEach((b, i) => {
    const pos = onPanel(b.x, b.y);
    const rot = new THREE.Euler(tilt, 0, 0);
    const color = b.color ?? 0x2c333b;
    const cap = new THREE.BoxGeometry(0.052, 0.03, 0.014);
    cap.deleteAttribute('uv');
    cap.applyMatrix4(new THREE.Matrix4().makeRotationFromEuler(rot).setPosition(pos));
    if (!capGeos.has(color)) capGeos.set(color, []);
    capGeos.get(color).push(cap.toNonIndexed());
    const label = new THREE.PlaneGeometry(0.046, 0.016);
    const uv = uvFor(i);
    const uvs = label.attributes.uv;
    for (let k = 0; k < uvs.count; k++) uvs.setXY(k, uv.u0 + uvs.getX(k) * (uv.u1 - uv.u0), uv.v0 + uvs.getY(k) * (uv.v1 - uv.v0));
    label.applyMatrix4(new THREE.Matrix4().makeTranslation(0, 0, 0.0075));
    label.applyMatrix4(new THREE.Matrix4().makeRotationFromEuler(rot).setPosition(pos));
    labelGeos.push(label);
    tmp.position.copy(pos).add(new THREE.Vector3(0, 0.0115, 0.007).applyEuler(rot));
    tmp.rotation.copy(rot);
    tmp.updateMatrix();
    ledMesh.setMatrixAt(i, tmp.matrix);
    ledMesh.setColorAt(i, new THREE.Color(0x1a2a1a));
    lights[b.id] = i;
    pokeTargets.push({ id: b.id, position: pos.clone().add(new THREE.Vector3(0, 0, 0.01)), radius: 0.028, action: b.action, hold: b.hold });
  });
  for (const [color, geos] of capGeos) {
    m3d.cockpit.add(new THREE.Mesh(mergeGeometries(geos), new THREE.MeshLambertMaterial({ color })));
  }
  const labels = new THREE.Mesh(mergeGeometries(labelGeos), new THREE.MeshBasicMaterial({ map: atlas, transparent: true, toneMapped: false }));
  labels.name = 'panelLabels';
  m3d.cockpit.add(labels, ledMesh);
  ledMesh.name = 'panelLights';

  // Overhead: emergency autoland button (press and hold)
  const srPos = new THREE.Vector3(CX + 0.12, 0.31, -0.22);
  const srButton = new THREE.Mesh(new THREE.CylinderGeometry(0.022, 0.022, 0.018, 20), new THREE.MeshLambertMaterial({ color: 0xd8262b }));
  srButton.position.copy(srPos);
  m3d.cockpit.add(srButton);
  const srLabel = labelMesh('SAFE RETURN · HOLD', { w: 0.12, h: 0.02, bg: '#1b1f24' });
  srLabel.position.set(srPos.x, 0.318, srPos.z + 0.05);
  srLabel.rotation.x = Math.PI / 2;
  m3d.cockpit.add(srLabel);
  pokeTargets.push({ id: 'safeReturn', position: srPos.clone().add(new THREE.Vector3(0, -0.012, 0)), radius: 0.035, action: 'safeReturn', hold: 1 });

  const layout = {
    stick: { hand: 'left', grip: SR22_COCKPIT.yokeGrip, range: 0.08 },
    throttle: { hand: 'right', grip: SR22_COCKPIT.powerGrip, range: 0.14 },
    trimHat: true,
    triggers: { left: 'brakeLeft', right: 'brakeRight' },
    buttons: {
      right: { 4: 'flapsDown', 5: 'flapsUp' },
      left: { 3: 'recenter', 4: 'apToggle' },
    },
    holdButtons: { left: { 5: { action: 'restart', seconds: 1.2 } } },
    pokeTargets,
    grabTargets: [{ id: 'caps', position: SR22_COCKPIT.capsHandle, radius: 0.12, pullDistance: 0.1, action: 'caps' }],
  };

  const keyMap = {
    keys: {
      BracketLeft: 'flapsUp',
      BracketRight: 'flapsDown',
      KeyZ: 'apToggle',
      KeyH: 'apHdg',
      KeyK: 'apAlt',
      KeyU: 'apVs',
      KeyL: 'apLvl',
      Minus: 'hdgDec',
      Equal: 'hdgInc',
      PageDown: 'altDec',
      PageUp: 'altInc',
      KeyN: 'vsDec',
      KeyJ: 'vsInc',
      KeyP: 'parkBrake',
      KeyO: 'toggleAssists',
    },
    holdKeys: {
      Digit9: { action: 'caps', seconds: 1 },
      Digit0: { action: 'safeReturn', seconds: 1 },
    },
  };

  const say = (...args) => ctxRef?.message.show(...args);

  function attach(ctx) {
    ctxRef = ctx;
  }

  model.on('chute', ({ ok }) => {
    ctxRef?.audio.whoosh();
    if (ok) say('PARACHUTE DEPLOYED', 'Engine off. Brace — you will drift down at about 1,700 fpm.', 6, '#ffb21a');
    else say('Parachute failed', `Deployed far too fast (${Math.round(model.iasKt)} kt). It is only certified up to ${SR22.vpd} kt.`, 6, '#ff7a5a');
    ap.disconnect('chute');
    sr.cancel(model);
  });
  model.on('chuteLanded', ({ water }) => {
    if (ctxRef) ctxRef.game.state = 'ended';
    say(
      water ? 'Splashdown under canopy' : 'Down safely',
      water ? 'You landed in the water under the parachute. The aircraft floats and rescue is on the way. Hold Y to fly again.' : 'You landed under the parachute and everyone walks away. Hold Y to fly again.',
      Infinity,
      '#7fe08a',
    );
  });
  model.on('engineOut', () => say('ENGINE FAILURE', 'Out of fuel. Glide at 88 kt toward land, or pull the parachute.', 6, '#ff7a5a'));
  ap.on('disconnect', ({ reason }) => {
    ctxRef?.audio.apDisconnect();
    say('Autopilot off', reason === 'stall' ? 'Disconnected: stall.' : reason === 'override' ? 'You took the controls.' : '', 1.6);
  });
  ap.on('capture', () => say('Altitude captured', '', 1.2, '#3be36a'));
  sr.on('phase', ({ phase }) => {
    const text = {
      ENROUTE: ['SAFE RETURN', 'Hands off. Flying to the island runway.'],
      APPROACH: ['SAFE RETURN', 'Approach: flaps, slowing down, glidepath.'],
      FLARE: ['SAFE RETURN', 'Landing.'],
      ROLLOUT: ['SAFE RETURN', 'Touchdown. Braking.'],
    }[phase];
    if (text) say(text[0], text[1], 3, '#3be36a');
  });
  sr.on('complete', () => {
    if (ctxRef) ctxRef.game.state = 'ended';
    say('Safe Return complete', 'Landed and stopped on the runway. Engine off. Hold Y to fly again.', Infinity, '#3be36a');
  });

  function handleAction(action) {
    const m = model;
    switch (action) {
      case 'flapsUp':
      case 'flapsDown': {
        const t = m.flapsStep(action === 'flapsDown' ? 1 : -1);
        say(`Flaps ${['UP', '50%', '100%'][flapIndex(t)]}`, '', 1);
        return true;
      }
      case 'apToggle':
      case 'apDisconnect':
        if (sr.active) {
          sr.cancel(m);
          say('Safe Return cancelled', 'You have the controls.', 2);
        } else if (!m.onGround) ap.toggle(m);
        return true;
      case 'apHdg':
        if (!m.onGround) {
          if (ap.lateral !== 'HDG') ap.hdgBug = Math.round(m.heading / (Math.PI / 36)) * (Math.PI / 36);
          ap.heading(m);
        }
        return true;
      case 'apAlt':
        if (!m.onGround) ap.altitudeHold(m);
        return true;
      case 'apVs':
        if (!m.onGround) ap.verticalSpeed(m);
        return true;
      case 'apLvl':
        if (!m.onGround) ap.level(m);
        return true;
      case 'hdgDec':
        ap.hdgBugStep(-5);
        return true;
      case 'hdgInc':
        ap.hdgBugStep(5);
        return true;
      case 'altDec':
        ap.altSelStep(-100);
        return true;
      case 'altInc':
        ap.altSelStep(100);
        return true;
      case 'vsDec':
        ap.vsStep(-100);
        return true;
      case 'vsInc':
        ap.vsStep(100);
        return true;
      case 'parkBrake':
        m.parkingBrake = !m.parkingBrake;
        say(m.parkingBrake ? 'Parking brake ON' : 'Parking brake OFF', '', 1);
        return true;
      case 'toggleAssists':
        m.assists = !m.assists;
        if (ctxRef) {
          ctxRef.game.settings.assists = m.assists;
          ctxRef.saveSettings();
        }
        say(m.assists ? 'Assists ON' : 'Assists OFF', m.assists ? 'Envelope protection, yaw damper, rudder steering.' : 'Full manual. Watch your airspeed!', 2);
        return true;
      case 'caps': {
        const r = m.deployChute();
        if (r === 'ground') say('Parachute', 'Not available on the ground.', 1.5);
        return true;
      }
      case 'safeReturn':
        if (!sr.activate(m)) say('Safe Return', m.onGround ? 'Not available on the ground.' : 'Not available now.', 1.5);
        else ap.disconnect('safeReturn');
        return true;
      default:
        return false;
    }
  }

  function flightInput(dt, input) {
    const out = {
      pitch: input.pitch,
      roll: input.roll,
      yaw: input.yaw,
      throttle: input.throttle,
      brake: input.brake,
      brakeLeft: input.brakeLeft,
      brakeRight: input.brakeRight,
      trimRate: input.trimRate,
      rollTrimRate: input.rollTrimRate,
    };
    if (sr.active) {
      const c = sr.update(dt, model);
      if (c) {
        model.flapsTarget = c.flaps;
        input.throttle = c.throttle; // keep the power lever in sync when you take back control
        return { ...out, pitch: c.pitch, roll: c.roll, yaw: c.yaw, throttle: c.throttle, brake: c.brake, brakeLeft: 0, brakeRight: 0, trimRate: 0 };
      }
    }
    const cmd = ap.update(dt, model);
    if (cmd) {
      if (Math.abs(input.pitch) > 0.4 || Math.abs(input.roll) > 0.4) ap.disconnect('override');
      else return { ...out, pitch: cmd.pitch, roll: cmd.roll };
    }
    return out;
  }

  function reset() {
    ap.disconnect();
    ap.lateral = 'ROL';
    ap.vertical = 'PIT';
    ap.altSel = 1500 * FT;
    ap.hdgBug = 0;
    sr.cancel(model);
    model.assists = settings.assists ?? model.assists;
  }

  function avionicsState(ctx) {
    const m = model;
    const course = ctx.course;
    const ring = course.current;
    let ringBearingAbs = null;
    let ringDistance = 0;
    if (ring && !course.finished) {
      const dx = ring.center.x - m.position.x;
      const dz = ring.center.z - m.position.z;
      ringBearingAbs = Math.atan2(dx, -dz);
      ringDistance = Math.hypot(dx, dz);
    }
    const ias = m.iasKt;
    return {
      time: ctx.time,
      pitch: m.pitchAngle,
      bank: m.bank,
      beta: m.beta,
      ias,
      tas: m.speed / KT,
      altFt: m.altitude / FT,
      altSelFt: ap.altSel / FT,
      vsFpm: m.velocity.y / FPM,
      vsSelFpm: ap.vertical === 'VS' ? ap.vsSel / FPM : null,
      heading: m.heading,
      hdgBug: ap.hdgBug,
      ringBearingAbs,
      ringDistance,
      ap: ap.annunciation,
      espActive: m.espActive,
      flaps: m.flaps,
      flapsTarget: m.flapsTarget,
      trim: m.trim,
      flapOverspeed: (m.flaps > 0.6 && ias > SR22.vfe100 + 5) || (m.flaps > 0.25 && ias > SR22.vfe50 + 5),
      stallWarning: m.stallWarning,
      safeReturn: sr.status,
      chute: m.chute,
      engineRunning: m.engineRunning,
      parkingBrake: m.parkingBrake,
      onGround: m.onGround,
      rpm: m.rpm,
      percentPower: m.percentPower,
      manifold: m.manifoldPressure,
      fuelFlow: m.fuelFlow,
      fuelGal: m.fuelGal,
      gLoad: m.gLoad,
      assists: m.assists,
      groundSpeedKt: Math.hypot(m.velocity.x, m.velocity.z) / KT,
      x: m.position.x,
      z: m.position.z,
      rings: course.rings.map((r) => ({ x: r.center.x, z: r.center.z })),
      ringIndex: course.index,
      timeText: formatTime(course.elapsed),
    };
  }

  const LIT = new THREE.Color(0x3be36a);
  const DARK = new THREE.Color(0x1a2a1a);
  const AMBER = new THREE.Color(0xffb21a);
  const TOUCHED = new THREE.Color(0xffffff);

  return {
    id: 'sr22',
    name: 'SR22-class piston single',
    model,
    group: m3d.root,
    visual: m3d,
    autopilot: ap,
    safeReturn: sr,
    audioProfile: 'prop',
    gearHeight: SR22.gearHeight,
    startMessage: ['SR22 ready', 'Right hand: power lever. Left hand: side yoke. Full power, rotate at 72 kt with gentle back pressure.'],
    readyMessage: ['Ready for take-off', 'Power up, rotate at 72 kt, climb at 108 kt (Vy).'],
    layout,
    keyMap,
    attach,
    reset,
    handleAction,
    flightInput,
    setCockpitView: (inCockpit) => m3d.setCockpitView(inCockpit),
    landedMessage({ sink, hard, offRunway }, finished) {
      const fpm = Math.round(sink / FPM);
      if (hard) return ['Hard landing', `${fpm} fpm at touchdown. Flare a little more next time.`, 3, '#ffb21a'];
      if (finished) return ['Touchdown!', `${fpm} fpm${offRunway ? ' on the grass' : ''}. Brake to a stop, or hold Y to fly again.`, 6, '#7fe08a'];
      return [offRunway ? 'Landed on grass' : 'Smooth landing', `${fpm} fpm at touchdown.`, 3];
    },
    update(dt, ctx) {
      const input = ctx.input;
      m3d.update(dt, model, input);
      // Grab highlights reuse the button LED colour scheme.
      for (const b of PANEL_BUTTONS) {
        let on = false;
        if (b.id === 'ap') on = ap.engaged;
        else if (b.id === 'hdg') on = ap.engaged && ap.lateral === 'HDG';
        else if (b.id === 'alt') on = ap.engaged && ap.vertical === 'ALT';
        else if (b.id === 'vs') on = ap.engaged && ap.vertical === 'VS';
        else if (b.id === 'lvl') on = ap.engaged && ap.lateral === 'LVL';
        else if (b.id === 'park') on = model.parkingBrake;
        else if (b.id === 'assist') on = model.assists;
        // A touched button's light glows white as press feedback.
        const color = input.touching.has(b.id) ? TOUCHED : on ? (b.id === 'park' ? AMBER : LIT) : DARK;
        ledMesh.setColorAt(lights[b.id], color);
      }
      ledMesh.instanceColor.needsUpdate = true;
      srButton.position.y = srPos.y + (input.touching.has('safeReturn') ? 0.006 : 0);
      avionics.update(dt, avionicsState(ctx));
      ctx.audio.setStallHorn(model.stallWarning && !model.onGround);
    },
    dispose() {
      ctxRef?.audio.setStallHorn(false);
    },
  };
}

/** Draw all button labels into one texture (one draw call for every label on the panel). */
function buildLabelAtlas(texts) {
  const cols = 4;
  const cellW = 256;
  const cellH = 88;
  const rows = Math.ceil(texts.length / cols);
  const canvas = document.createElement('canvas');
  canvas.width = cols * cellW;
  canvas.height = rows * cellH;
  const c = canvas.getContext('2d');
  c.fillStyle = '#e8eef4';
  c.textAlign = 'center';
  c.textBaseline = 'middle';
  texts.forEach((t, i) => {
    let size = 62;
    c.font = `bold ${size}px system-ui, sans-serif`;
    while (c.measureText(t).width > cellW * 0.9 && size > 10) {
      size -= 2;
      c.font = `bold ${size}px system-ui, sans-serif`;
    }
    c.fillText(t, (i % cols) * cellW + cellW / 2, Math.floor(i / cols) * cellH + cellH / 2 + 2);
  });
  const atlas = new THREE.CanvasTexture(canvas);
  atlas.colorSpace = THREE.SRGBColorSpace;
  atlas.anisotropy = 4;
  return {
    atlas,
    uvFor(i) {
      const col = i % cols;
      const row = Math.floor(i / cols);
      return { u0: col / cols, u1: (col + 1) / cols, v0: 1 - (row + 1) / rows, v1: 1 - row / rows };
    },
  };
}
