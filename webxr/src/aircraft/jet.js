import * as THREE from 'three';

// The player's jet: exterior shell you can see from the seat (nose, wings, tail)
// and the cockpit with a grabbable stick and throttle.
// Local origin = pilot's eye point. Forward = -Z.

export const COCKPIT = {
  stickPivot: new THREE.Vector3(0, -0.72, -0.36),
  stickGrip: new THREE.Vector3(0, -0.5, -0.36),
  throttlePivot: new THREE.Vector3(-0.3, -0.58, -0.2),
  throttleGrip: new THREE.Vector3(-0.3, -0.42, -0.2),
  throttleTravel: 0.55, // radians of lever travel from idle to full
  stickMaxAngle: 0.38,
};

const paint = new THREE.MeshLambertMaterial({ color: 0xdfe6ee, flatShading: true });
const stripe = new THREE.MeshLambertMaterial({ color: 0xff7a1a, flatShading: true });
const dark = new THREE.MeshLambertMaterial({ color: 0x23282f, flatShading: true });
const panel = new THREE.MeshLambertMaterial({ color: 0x343b45, flatShading: true });
const glassFrame = new THREE.MeshLambertMaterial({ color: 0x4a5560, flatShading: true });

export function createJet() {
  const jet = new THREE.Group();
  jet.name = 'jet';

  // ---- Exterior --------------------------------------------------------
  const body = new THREE.Group();
  body.name = 'exterior';

  // Fuselage is split around the open cockpit so nothing blocks the pilot's view.
  const FUSE_Y = -1.15;
  const frontBody = new THREE.Mesh(new THREE.CylinderGeometry(0.9, 0.92, 2.2, 12, 1), paint);
  frontBody.rotation.x = Math.PI / 2;
  frontBody.position.set(0, FUSE_Y, -2.1);
  const rearBody = new THREE.Mesh(new THREE.CylinderGeometry(0.92, 0.75, 6.6, 12, 1), paint);
  rearBody.rotation.x = Math.PI / 2;
  rearBody.position.set(0, FUSE_Y, 4.0);
  const nose = new THREE.Mesh(new THREE.ConeGeometry(0.9, 4.0, 12), paint);
  nose.rotation.x = -Math.PI / 2;
  nose.position.set(0, FUSE_Y, -5.2);
  const noseTip = new THREE.Mesh(new THREE.ConeGeometry(0.3, 1.1, 10), dark);
  noseTip.rotation.x = -Math.PI / 2;
  noseTip.position.set(0, FUSE_Y, -7.6);

  const wingShape = new THREE.Shape();
  wingShape.moveTo(0, 0);
  wingShape.lineTo(6.5, 2.2);
  wingShape.lineTo(6.5, 3.3);
  wingShape.lineTo(0, 4.6);
  wingShape.closePath();
  const wingGeo = new THREE.ExtrudeGeometry(wingShape, { depth: 0.16, bevelEnabled: false });
  wingGeo.rotateX(Math.PI / 2);
  const wingR = new THREE.Mesh(wingGeo, paint);
  wingR.position.set(0.7, -1.45, 0.6);
  const wingL = wingR.clone();
  wingL.scale.x = -1;
  wingL.position.x = -0.7;
  // Orange wingtips, visible from the seat, give a sense of roll.
  const tipGeo = new THREE.BoxGeometry(0.25, 0.22, 1.2);
  const tipR = new THREE.Mesh(tipGeo, stripe);
  tipR.position.set(7.15, -1.53, 3.35);
  const tipL = tipR.clone();
  tipL.position.x = -7.15;

  const tailFin = new THREE.Mesh(new THREE.BoxGeometry(0.14, 2.6, 2.2), paint);
  tailFin.position.set(0, 0.35, 6.6);
  tailFin.rotation.x = -0.35;
  const stabR = new THREE.Mesh(new THREE.BoxGeometry(3.4, 0.1, 1.4), paint);
  stabR.position.set(1.9, -1.05, 6.8);
  const stabL = stabR.clone();
  stabL.position.x = -1.9;
  const exhaust = new THREE.Mesh(new THREE.CylinderGeometry(0.7, 0.6, 0.6, 12), dark);
  exhaust.rotation.x = Math.PI / 2;
  exhaust.position.set(0, FUSE_Y, 7.4);

  // Afterburner flame (scaled by boost)
  const flame = new THREE.Mesh(
    new THREE.ConeGeometry(0.55, 3, 10, 1, true),
    new THREE.MeshBasicMaterial({ color: 0xffa040, transparent: true, opacity: 0.85, depthWrite: false }),
  );
  flame.rotation.x = -Math.PI / 2;
  flame.position.set(0, FUSE_Y, 9.1);
  flame.name = 'flame';

  const noseStripe = new THREE.Mesh(new THREE.CylinderGeometry(0.94, 0.94, 0.3, 12), stripe);
  noseStripe.rotation.x = Math.PI / 2;
  noseStripe.position.set(0, FUSE_Y, -3.15);

  body.add(frontBody, rearBody, nose, noseTip, wingR, wingL, tipR, tipL, tailFin, stabR, stabL, exhaust, flame, noseStripe);
  jet.add(body);

  // ---- Cockpit ----------------------------------------------------------
  const cockpit = new THREE.Group();
  cockpit.name = 'cockpit';

  // Cockpit tub sides
  const sideGeo = new THREE.BoxGeometry(0.08, 0.75, 2.6);
  const sideL = new THREE.Mesh(sideGeo, paint);
  sideL.position.set(-0.6, -0.62, 0.0);
  const sideR = sideL.clone();
  sideR.position.x = 0.6;
  // Coaming (top edge of the dashboard)
  const coaming = new THREE.Mesh(new THREE.BoxGeometry(1.0, 0.04, 0.14), dark);
  coaming.position.set(0, -0.215, -0.95);
  // Cockpit floor
  const floor = new THREE.Mesh(new THREE.BoxGeometry(1.15, 0.05, 2.4), dark);
  floor.position.set(0, -1.0, 0);
  // Instrument panel
  const dash = new THREE.Mesh(new THREE.BoxGeometry(1.0, 0.42, 0.06), panel);
  dash.position.set(0, -0.44, -0.8);
  dash.rotation.x = -0.25;
  // Side consoles
  const consoleL = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.08, 0.9), panel);
  consoleL.position.set(-0.4, -0.66, -0.1);
  const consoleR = consoleL.clone();
  consoleR.position.x = 0.4;
  // Seat back (behind the player; visible if they look around)
  const seat = new THREE.Mesh(new THREE.BoxGeometry(0.62, 1.1, 0.14), dark);
  seat.position.set(0, -0.55, 0.42);
  const headrest = new THREE.Mesh(new THREE.BoxGeometry(0.34, 0.3, 0.14), dark);
  headrest.position.set(0, 0.12, 0.48);

  // Canopy frame: arches only (no glass, keeps the view crisp).
  const arch = new THREE.Mesh(new THREE.TorusGeometry(0.64, 0.016, 6, 24, Math.PI), glassFrame);
  arch.position.set(0, -0.22, -1.05);
  const arch2 = arch.clone();
  arch2.position.z = 0.55;
  const spine = new THREE.Mesh(new THREE.BoxGeometry(0.035, 0.035, 1.5), glassFrame);
  spine.position.set(0, 0.42, -0.2);
  const railL = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.04, 2.4), glassFrame);
  railL.position.set(-0.6, -0.22, 0.1);
  const railR = railL.clone();
  railR.position.x = 0.6;

  cockpit.add(sideL, sideR, coaming, floor, dash, consoleL, consoleR, seat, headrest, arch, arch2, spine, railL, railR);

  // HUD screen mount point (the HUD module draws onto it).
  const hudAnchor = new THREE.Object3D();
  hudAnchor.position.set(0, -0.42, -0.765);
  hudAnchor.rotation.x = -0.25;
  hudAnchor.name = 'hudAnchor';
  cockpit.add(hudAnchor);

  // Flight stick
  const stickPivot = new THREE.Group();
  stickPivot.position.copy(COCKPIT.stickPivot);
  const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.014, 0.018, 0.22, 8), dark);
  shaft.position.y = 0.11;
  const grip = new THREE.Mesh(new THREE.CapsuleGeometry(0.028, 0.08, 4, 8), new THREE.MeshLambertMaterial({ color: 0x1b1d20 }));
  grip.position.y = 0.25;
  const button = new THREE.Mesh(new THREE.SphereGeometry(0.012, 8, 6), new THREE.MeshBasicMaterial({ color: 0xff3030 }));
  button.position.set(0, 0.31, -0.012);
  const boot = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.07, 0.05, 10), dark);
  stickPivot.add(shaft, grip, button, boot);
  cockpit.add(stickPivot);

  // Throttle lever on the left console
  const throttlePivot = new THREE.Group();
  throttlePivot.position.copy(COCKPIT.throttlePivot);
  const lever = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.16, 0.02), dark);
  lever.position.y = 0.08;
  const knob = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.05, 0.08), stripe);
  knob.position.y = 0.17;
  throttlePivot.add(lever, knob);
  const track = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.02, 0.22), panel);
  track.position.copy(COCKPIT.throttlePivot).add(new THREE.Vector3(0, 0, 0));
  cockpit.add(throttlePivot, track);

  // Highlight rings shown when a hand is close enough to grab.
  const stickHighlight = makeHighlight();
  stickHighlight.position.y = 0.25;
  stickPivot.add(stickHighlight);
  const throttleHighlight = makeHighlight();
  throttleHighlight.position.y = 0.17;
  throttlePivot.add(throttleHighlight);

  jet.add(cockpit);

  return {
    group: jet,
    cockpit,
    hudAnchor,
    flame,
    stickHighlight,
    throttleHighlight,
    /** Animate the cockpit controls to match the current input. */
    setControls(pitch, roll, throttle, boosting, time) {
      stickPivot.rotation.set(pitch * COCKPIT.stickMaxAngle, 0, -roll * COCKPIT.stickMaxAngle);
      throttlePivot.rotation.x = -(throttle - 0.5) * COCKPIT.throttleTravel;
      const f = boosting ? 1.6 + Math.sin(time * 40) * 0.15 : 0.25 + throttle * 0.6;
      flame.scale.set(1, f, 1);
      flame.material.color.setHex(boosting ? 0x6fb6ff : 0xffa040);
    },
  };
}

function makeHighlight() {
  const m = new THREE.Mesh(
    new THREE.TorusGeometry(0.06, 0.006, 6, 20),
    new THREE.MeshBasicMaterial({ color: 0x7fe0ff, transparent: true, opacity: 0.85 }),
  );
  m.rotation.x = Math.PI / 2;
  m.visible = false;
  return m;
}
