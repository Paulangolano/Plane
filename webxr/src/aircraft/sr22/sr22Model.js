import * as THREE from 'three';
import { SR22 } from './sr22Data.js';
import { mergeStatic } from '../../core/mergeStatic.js';

// 3D model of the SR22-class aircraft: exterior (for the chase camera and what you see from the
// seat: cowling, prop, low wing) plus the left-seat cockpit with its controls.
// Local origin = pilot's eye point (left seat). Forward = -Z. Cabin centre line at x = +0.30.
// No manufacturer branding: generic paint and labels only.

const CX = 0.3; // cabin centre line

const PANEL_TILT = -0.17; // rad about X: negative = top of the panel leans away from the pilot
const PANEL_CENTER = new THREE.Vector3(CX, -0.42, -0.725);
const PANEL_HALF_DEPTH = 0.025;

/** A point on the instrument panel's front face at roughly (x, y), standing `out` metres proud of it. */
export function panelPoint(x, y, out = 0) {
  const dy = (y - PANEL_CENTER.y) / Math.cos(PANEL_TILT);
  const n = PANEL_HALF_DEPTH + out;
  return new THREE.Vector3(
    x,
    PANEL_CENTER.y + dy * Math.cos(PANEL_TILT) - Math.sin(PANEL_TILT) * n,
    PANEL_CENTER.z + dy * Math.sin(PANEL_TILT) + Math.cos(PANEL_TILT) * n,
  );
}

export const SR22_COCKPIT = {
  yokeGrip: new THREE.Vector3(-0.33, -0.48, -0.4),
  powerGrip: new THREE.Vector3(CX - 0.06, -0.52, -0.31),
  capsHandle: new THREE.Vector3(CX, 0.25, -0.1),
  pfd: { center: panelPoint(0.02, -0.36, 0.006), width: 0.36, height: 0.24 },
  mfd: { center: panelPoint(0.43, -0.36, 0.006), width: 0.36, height: 0.24 },
  panelTilt: PANEL_TILT,
};

const mat = (color, opts = {}) => new THREE.MeshLambertMaterial({ color, flatShading: true, ...opts });
const M = {
  paint: mat(0xf2f4f6),
  stripe: mat(0x2e5d8a),
  accent: mat(0x9aa7b4),
  interior: mat(0x3a3f45),
  headliner: mat(0xb9b3a8, { emissive: 0x4a4740 }),
  leather: mat(0x6b5440),
  panel: mat(0x1c2026),
  glare: mat(0x15181c),
  metal: mat(0x7c858f),
  tyre: mat(0x1a1a1a),
  red: new THREE.MeshBasicMaterial({ color: 0xd8262b }),
  green: new THREE.MeshBasicMaterial({ color: 0x2fd65a }),
  white: new THREE.MeshBasicMaterial({ color: 0xffffff }),
  glass: new THREE.MeshLambertMaterial({ color: 0x223344, transparent: true, opacity: 0.35, depthWrite: false }),
};

function box(w, h, d, material, x, y, z, parent) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
  m.position.set(x, y, z);
  parent?.add(m);
  return m;
}

function cyl(rt, rb, len, material, segs = 16) {
  const g = new THREE.CylinderGeometry(rt, rb, len, segs, 1);
  g.rotateX(Math.PI / 2); // axis along Z
  return new THREE.Mesh(g, material);
}

/** Small text label texture for cockpit buttons and placards. */
export function labelMesh(text, { w = 0.05, h = 0.018, color = '#e8eef4', bg = null, bold = true } = {}) {
  const canvas = document.createElement('canvas');
  canvas.width = 256;
  canvas.height = Math.round((256 * h) / w);
  const c = canvas.getContext('2d');
  if (bg) {
    c.fillStyle = bg;
    c.fillRect(0, 0, canvas.width, canvas.height);
  }
  c.fillStyle = color;
  c.textAlign = 'center';
  c.textBaseline = 'middle';
  let size = canvas.height * 0.72;
  c.font = `${bold ? 'bold ' : ''}${size}px system-ui, sans-serif`;
  while (c.measureText(text).width > canvas.width * 0.92 && size > 8) {
    size -= 2;
    c.font = `${bold ? 'bold ' : ''}${size}px system-ui, sans-serif`;
  }
  c.fillText(text, canvas.width / 2, canvas.height / 2 + 1);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  const m = new THREE.Mesh(
    new THREE.PlaneGeometry(w, h),
    new THREE.MeshBasicMaterial({ map: tex, transparent: !bg, toneMapped: false }),
  );
  return m;
}

export function createSR22Model() {
  const root = new THREE.Group();
  root.name = 'sr22';

  // ------------------------------------------------------------------ exterior
  const ext = new THREE.Group();
  ext.name = 'exterior';
  root.add(ext);
  const FY = -0.6; // fuselage centre height

  const cabin = cyl(0.68, 0.7, 2.5, M.paint);
  cabin.position.set(CX, FY - 0.05, -0.15);
  cabin.scale.set(1, 0.95, 1);
  cabin.name = 'cabinShell';
  const cowl = cyl(0.38, 0.68, 1.3, M.paint);
  cowl.position.set(CX, FY - 0.08, -2.05);
  const spinner = new THREE.Mesh(new THREE.ConeGeometry(0.16, 0.4, 16).rotateX(-Math.PI / 2), M.paint);
  spinner.position.set(CX, FY - 0.08, -2.88);
  const tailCone = cyl(0.7, 0.16, 4.2, M.paint);
  tailCone.position.set(CX, FY + 0.12, 3.2);
  tailCone.rotation.x = 0.06;
  const stripe = cyl(0.705, 0.705, 0.9, M.stripe);
  stripe.position.set(CX, FY - 0.05, 0.9);
  stripe.scale.set(1, 0.96, 1);
  ext.add(cowl, spinner, tailCone, stripe);

  // Low wing with dihedral; root under the cabin, leading edge about at the pilot's knees.
  const wingGeo = new THREE.BufferGeometry();
  const half = SR22.wingSpan / 2;
  const rootChord = 1.6;
  const tipChord = 0.9;
  const dihedral = Math.tan(THREE.MathUtils.degToRad(4.5));
  const wingPts = (side) => {
    const s = side;
    const r = 0.55;
    const le0 = -0.55;
    const le1 = -0.35;
    const y0 = -1.12;
    const y1 = y0 + (half - r) * dihedral;
    return [
      [CX + s * r, y0, le0], [CX + s * half, y1, le1], [CX + s * half, y1, le1 + tipChord], [CX + s * r, y0, le0 + rootChord],
    ];
  };
  const verts = [];
  for (const s of [1, -1]) {
    const [a, b, c, d] = wingPts(s);
    const t = 0.12; // thickness
    const up = (p) => [p[0], p[1] + t / 2, p[2]];
    const dn = (p) => [p[0], p[1] - t / 2, p[2]];
    const quad = (p, q, r, w) => verts.push(...p, ...q, ...r, ...p, ...r, ...w);
    if (s === 1) {
      quad(up(a), up(d), up(c), up(b));
      quad(dn(a), dn(b), dn(c), dn(d));
      quad(up(a), up(b), dn(b), dn(a));
      quad(up(c), up(d), dn(d), dn(c));
      quad(up(b), up(c), dn(c), dn(b));
    } else {
      quad(up(a), up(b), up(c), up(d));
      quad(dn(a), dn(d), dn(c), dn(b));
      quad(up(a), dn(a), dn(b), up(b));
      quad(up(c), dn(c), dn(d), up(d));
      quad(up(b), dn(b), dn(c), up(c));
    }
  }
  wingGeo.setAttribute('position', new THREE.Float32BufferAttribute(verts, 3));
  wingGeo.computeVertexNormals();
  const wing = new THREE.Mesh(wingGeo, M.paint);
  wing.name = 'wing';
  ext.add(wing);
  const tipY = -1.12 + (half - 0.55) * dihedral;
  box(0.08, 0.08, 0.18, M.red, CX - half - 0.02, tipY, -0.2, ext); // left nav light
  box(0.08, 0.08, 0.18, M.green, CX + half + 0.02, tipY, -0.2, ext); // right nav light

  // Ailerons (animated)
  const ailerons = [1, -1].map((s) => {
    const pivot = new THREE.Group();
    pivot.position.set(CX + s * (half - 1.9), -1.12 + (half - 2.5) * dihedral, 0.5);
    box(2.6, 0.04, 0.32, M.accent, 0, 0, 0.16, pivot);
    ext.add(pivot);
    return pivot;
  });

  // Tail
  const hstab = box(3.9, 0.08, 0.9, M.paint, CX, FY + 0.25, 4.65, ext);
  hstab.name = 'hstab';
  const elevator = new THREE.Group();
  elevator.position.set(CX, FY + 0.25, 5.1);
  box(3.7, 0.05, 0.4, M.accent, 0, 0, 0.2, elevator);
  ext.add(elevator);
  const fin = new THREE.Mesh(new THREE.BoxGeometry(0.09, 1.35, 1.1), M.paint);
  fin.position.set(CX, FY + 0.95, 4.65);
  fin.rotation.x = -0.25;
  ext.add(fin);
  const rudder = new THREE.Group();
  rudder.position.set(CX, FY + 0.95, 5.25);
  box(0.06, 1.2, 0.35, M.stripe, 0, 0, 0.17, rudder);
  ext.add(rudder);

  // Fixed tricycle gear with wheel pants
  const gearY = -SR22.gearHeight;
  for (const s of [1, -1]) {
    const strut = box(0.06, 0.55, 0.08, M.metal, CX + s * 1.35, gearY + 0.45, 0.15, ext);
    strut.rotation.z = s * 0.35;
    const pant = cyl(0.17, 0.12, 0.75, M.paint, 10);
    pant.position.set(CX + s * 1.48, gearY + 0.2, 0.15);
    ext.add(pant);
    const tyre = new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.2, 0.12, 14).rotateZ(Math.PI / 2), M.tyre);
    tyre.position.set(CX + s * 1.48, gearY + 0.2, 0.15);
    ext.add(tyre);
  }
  box(0.05, 0.6, 0.05, M.metal, CX, gearY + 0.45, -1.95, ext);
  const noseTyre = new THREE.Mesh(new THREE.CylinderGeometry(0.17, 0.17, 0.1, 14).rotateZ(Math.PI / 2), M.tyre);
  noseTyre.position.set(CX, gearY + 0.17, -1.95);
  ext.add(noseTyre);

  // Propeller: three blades when slow, a translucent disc when spinning fast.
  const prop = new THREE.Group();
  prop.position.set(CX, FY - 0.08, -2.75);
  for (let i = 0; i < 3; i++) {
    const blade = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.95, 0.03), M.glare);
    blade.position.y = 0.5;
    const holder = new THREE.Group();
    holder.rotation.z = (i * Math.PI * 2) / 3;
    blade.rotation.y = 0.35;
    holder.add(blade);
    prop.add(holder);
  }
  const discMat = new THREE.MeshBasicMaterial({ color: 0x20262c, transparent: true, opacity: 0.08, depthWrite: false, side: THREE.DoubleSide });
  const disc = new THREE.Mesh(new THREE.CircleGeometry(0.98, 32), discMat);
  disc.position.set(CX, FY - 0.08, -2.76);
  disc.visible = false;
  ext.add(prop, disc);

  // Parachute canopy (shown after deployment)
  const chute = new THREE.Group();
  const canopy = new THREE.Mesh(
    new THREE.SphereGeometry(5.5, 16, 8, 0, Math.PI * 2, 0, Math.PI / 2.3),
    new THREE.MeshLambertMaterial({ color: 0xff6a1a, side: THREE.DoubleSide, flatShading: true }),
  );
  canopy.position.y = 18;
  canopy.scale.set(1, 0.6, 1);
  chute.add(canopy);
  const lineMat = new THREE.LineBasicMaterial({ color: 0xdddddd });
  const lines = [];
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    lines.push(new THREE.Vector3(CX + Math.cos(a) * 5, 18, Math.sin(a) * 5), new THREE.Vector3(CX, 0.2, -0.3));
  }
  chute.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(lines), lineMat));
  canopy.position.x = CX;
  chute.visible = false;
  chute.name = 'parachute';
  ext.add(chute);

  // ------------------------------------------------------------------ cockpit
  const cp = new THREE.Group();
  cp.name = 'cockpit';
  root.add(cp);
  // The exterior cabin shell would block the view from the seat, so the game shows it only
  // in the chase camera (see setCockpitView).

  // Floor, side walls with big windows, roof, windscreen frame
  box(1.3, 0.04, 2.2, M.interior, CX, -1.0, -0.1, cp);
  for (const s of [1, -1]) {
    const x = CX + s * 0.66;
    box(0.04, 0.55, 2.0, M.interior, x, -0.7, -0.05, cp); // lower wall
    box(0.04, 0.05, 1.9, M.paint, x, -0.4, -0.05, cp); // window sill
    box(0.03, 0.03, 1.2, M.interior, x, 0.32, 0.15, cp); // upper frame
    // Windscreen pillar: from the glare shield's front corners back up to the roof.
    const aPillar = box(0.03, 0.8, 0.03, M.interior, x, -0.02, -0.8, cp);
    aPillar.rotation.x = 0.62;
    box(0.05, 0.7, 0.06, M.paint, x, -0.08, 0.72, cp); // B-pillar behind the seats
  }
  box(1.36, 0.03, 1.2, M.headliner, CX, 0.34, 0.15, cp); // roof
  const windscreenTop = box(1.3, 0.03, 0.05, M.interior, CX, 0.33, -0.46, cp);
  windscreenTop.name = 'windscreenFrame';

  // Glare shield and instrument panel
  const glare = box(1.32, 0.04, 0.32, M.glare, CX, -0.17, -0.82, cp);
  glare.rotation.x = 0.05;
  const panel = box(1.3, 0.5, PANEL_HALF_DEPTH * 2, M.panel, PANEL_CENTER.x, PANEL_CENTER.y, PANEL_CENTER.z, cp);
  panel.rotation.x = SR22_COCKPIT.panelTilt;
  panel.name = 'panel';

  // Display anchors (the avionics module draws onto planes here)
  const pfdAnchor = new THREE.Object3D();
  pfdAnchor.position.copy(SR22_COCKPIT.pfd.center);
  pfdAnchor.rotation.x = SR22_COCKPIT.panelTilt;
  const mfdAnchor = new THREE.Object3D();
  mfdAnchor.position.copy(SR22_COCKPIT.mfd.center);
  mfdAnchor.rotation.x = SR22_COCKPIT.panelTilt;
  cp.add(pfdAnchor, mfdAnchor);
  // Bezels
  for (const a of [SR22_COCKPIT.pfd, SR22_COCKPIT.mfd]) {
    const bp = panelPoint(a.center.x, a.center.y, 0.003);
    const bez = box(a.width + 0.025, a.height + 0.025, 0.006, M.glare, bp.x, bp.y, bp.z, cp);
    bez.rotation.x = SR22_COCKPIT.panelTilt;
  }

  // Centre console between the seats
  box(0.2, 0.22, 0.75, M.panel, CX, -0.75, -0.25, cp);
  const consoleTop = box(0.2, 0.02, 0.75, M.glare, CX, -0.63, -0.25, cp);
  consoleTop.name = 'console';

  // Seats
  for (const s of [-1, 1]) {
    box(0.48, 0.12, 0.5, M.leather, CX + s * 0.3, -0.92, 0.05, cp);
    box(0.48, 0.75, 0.12, M.leather, CX + s * 0.3, -0.55, 0.36, cp);
  }

  // Side yoke (left), sliding fore/aft for pitch and twisting for roll.
  const yokeBase = box(0.06, 0.06, 0.25, M.glare, -0.4, -0.48, -0.55, cp);
  yokeBase.name = 'yokeBase';
  const yoke = new THREE.Group();
  yoke.position.set(-0.36, -0.48, -0.42);
  const yokeShaft = cyl(0.015, 0.015, 0.16, M.metal, 8);
  yokeShaft.position.z = -0.05;
  const yokeGrip = new THREE.Group();
  yokeGrip.position.set(0.03, 0, 0.02);
  const gripBar = box(0.11, 0.035, 0.035, M.glare, 0, 0, 0, yokeGrip);
  gripBar.name = 'yokeGrip';
  box(0.035, 0.11, 0.04, M.glare, 0.055, -0.04, 0, yokeGrip);
  const trimHat = box(0.015, 0.01, 0.015, M.metal, 0.055, 0.018, 0, yokeGrip);
  trimHat.name = 'trimHat';
  const apDisc = box(0.012, 0.012, 0.012, M.red, 0.02, 0.022, 0, yokeGrip);
  apDisc.name = 'apDisconnect';
  yoke.add(yokeShaft, yokeGrip);
  cp.add(yoke);

  // Power lever on the console
  const power = new THREE.Group();
  power.position.set(CX - 0.06, -0.62, -0.31);
  const powerArm = box(0.018, 0.09, 0.018, M.metal, 0, 0.045, 0, power);
  powerArm.name = 'powerArm';
  const powerKnob = box(0.055, 0.035, 0.05, M.glare, 0, 0.1, 0, power);
  powerKnob.name = 'powerKnob';
  cp.add(power);
  const powerSlot = box(0.02, 0.005, 0.16, M.glare, CX - 0.06, -0.618, -0.31, cp);
  powerSlot.name = 'powerSlot';
  const powerLabel = labelMesh('POWER', { w: 0.05, h: 0.014 });
  powerLabel.position.set(CX - 0.06, -0.617, -0.2);
  powerLabel.rotation.x = -Math.PI / 2;
  cp.add(powerLabel);

  // Flap selector (lever moves between UP / 50% / 100%)
  const flapLever = new THREE.Group();
  flapLever.position.copy(panelPoint(0.7, -0.56, 0.01));
  flapLever.rotation.x = SR22_COCKPIT.panelTilt;
  const flapHandle = box(0.04, 0.02, 0.03, M.white, 0, 0, 0.015, flapLever);
  flapHandle.name = 'flapHandle';
  cp.add(flapLever);

  // Overhead: parachute handle under its cover, and the emergency autoland button.
  const capsCover = box(0.16, 0.012, 0.1, M.red, CX, 0.322, -0.1, cp);
  capsCover.name = 'capsCover';
  const caps = new THREE.Group();
  caps.position.copy(SR22_COCKPIT.capsHandle);
  box(0.11, 0.02, 0.025, M.red, 0, 0, 0, caps);
  box(0.012, 0.07, 0.012, M.metal, 0, 0.04, 0, caps);
  cp.add(caps);
  const capsLabel = labelMesh('PULL ▼ PARACHUTE', { w: 0.15, h: 0.022, color: '#ffffff', bg: '#a01418' });
  capsLabel.position.set(CX, 0.318, -0.03);
  capsLabel.rotation.x = Math.PI / 2;
  cp.add(capsLabel);

  // Fewer draw calls: merge everything that never moves (the cabin shell stays separate
  // because it is hidden in the cockpit view).
  mergeStatic(ext, { skip: new Set([cabin]) });
  mergeStatic(cp);

  return {
    root,
    exterior: ext,
    cockpit: cp,
    cabinShell: cabin,
    pfdAnchor,
    mfdAnchor,
    yoke,
    power,
    flapLever,
    caps,
    chute,
    prop,
    disc,
    ailerons,
    elevator,
    rudder,
    propAngle: 0,
    setCockpitView(inCockpit) {
      this.cabinShell.visible = !inCockpit;
    },
    /** Animate controls, prop and surfaces from the physics state. */
    update(dt, m, input) {
      // Side yoke: forward/aft travel for pitch, twist for roll.
      this.yoke.position.z = -0.42 + m.pitch * 0.045;
      this.yoke.rotation.z = -m.roll * 0.6;
      this.power.position.z = -0.25 - m.throttle * 0.13;
      this.flapLever.children[0].position.y = 0.025 - m.flapsTarget * 0.05;
      // Parachute handle follows a pulling hand.
      this.caps.position.y = SR22_COCKPIT.capsHandle.y - (input?.capsPull || 0);
      // Propeller
      const rps = m.rpm / 60;
      this.propAngle += rps * Math.PI * 2 * dt;
      this.prop.rotation.z = this.propAngle;
      const fast = m.rpm > 500;
      this.prop.visible = !fast;
      this.disc.visible = fast;
      // Control surfaces
      // Rolling right: right aileron trailing edge up, left down. Right rudder: trailing edge right.
      this.ailerons[0].rotation.x = -m.roll * 0.35;
      this.ailerons[1].rotation.x = m.roll * 0.35;
      this.elevator.rotation.x = -m.pitch * 0.4;
      this.rudder.rotation.y = m.yaw * 0.4;
      this.chute.visible = m.chute === 'inflating' || m.chute === 'deployed';
      if (this.chute.visible) {
        const s = Math.min(1, m.chuteT / SR22.chuteInflateTime);
        this.chute.scale.set(0.2 + 0.8 * s, 0.3 + 0.7 * s, 0.2 + 0.8 * s);
        // Keep the canopy above the aircraft in world terms.
        this.chute.quaternion.copy(this.root.quaternion).invert();
      }
    },
  };
}

export { CX as SR22_CABIN_CENTER };
