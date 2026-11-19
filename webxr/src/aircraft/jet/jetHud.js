import * as THREE from 'three';
import { FLIGHT, COURSE } from '../../config.js';
import { formatTime } from '../../ui/format.js';

// Glass-cockpit display drawn to a canvas on the dashboard.
// Redrawn ~15 times a second (texture uploads are expensive on Quest).

const W = 1024;
const H = 440;

export class CockpitHUD {
  constructor(anchor) {
    this.canvas = document.createElement('canvas');
    this.canvas.width = W;
    this.canvas.height = H;
    this.ctx = this.canvas.getContext('2d');
    this.texture = new THREE.CanvasTexture(this.canvas);
    this.texture.colorSpace = THREE.SRGBColorSpace;
    this.texture.anisotropy = 4;
    this.texture.generateMipmaps = true;
    this.texture.minFilter = THREE.LinearMipmapLinearFilter;
    const mat = new THREE.MeshBasicMaterial({ map: this.texture, toneMapped: false });
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(0.92, 0.92 * (H / W)), mat);
    this.mesh.name = 'hud';
    anchor.add(this.mesh);
    this.accum = 1;
  }

  update(dt, s) {
    this.accum += dt;
    if (this.accum < 1 / 15) return;
    this.accum = 0;
    this.draw(s);
    this.texture.needsUpdate = true;
  }

  draw(s) {
    const c = this.ctx;
    c.fillStyle = '#0b1118';
    c.fillRect(0, 0, W, H);

    // --- Left: speed + throttle -------------------------------------------
    panel(c, 12, 12, 300, H - 24);
    label(c, 'AIRSPEED', 32, 52);
    big(c, `${Math.round(s.speed * 3.6)}`, 32, 128, s.stalling ? '#ff5050' : '#e8f4ff');
    small(c, 'km/h', 230, 126);
    label(c, 'THROTTLE', 32, 190);
    bar(c, 32, 204, 260, 26, s.throttle, '#46c3ff');
    label(c, 'BOOST', 32, 268);
    bar(c, 32, 282, 260, 26, s.boostFuel / FLIGHT.boostDuration, s.boosting ? '#7fd4ff' : '#ffb21a');
    if (s.stalling && !s.onGround) warn(c, 'STALL', 32, 382);
    else if (s.brake && s.onGround) warn(c, 'BRAKES', 32, 382, '#ffb21a');
    else if (s.onGround) label(c, s.speed < 1 ? 'PARKED' : 'TAXI', 32, 382, '#7fe08a');

    // --- Centre: artificial horizon + heading -----------------------------
    const cx = 512;
    const cy = 200;
    const r = 160;
    c.save();
    c.beginPath();
    c.arc(cx, cy, r, 0, Math.PI * 2);
    c.clip();
    c.translate(cx, cy);
    c.rotate(-s.bank);
    const off = s.pitchAngle * 260;
    c.fillStyle = '#3c87d6';
    c.fillRect(-r * 2, -r * 2 + off, r * 4, r * 2);
    c.fillStyle = '#8a5a2b';
    c.fillRect(-r * 2, off, r * 4, r * 2);
    c.strokeStyle = '#ffffff';
    c.lineWidth = 3;
    c.beginPath();
    c.moveTo(-r * 2, off);
    c.lineTo(r * 2, off);
    c.stroke();
    c.lineWidth = 2;
    c.font = '18px system-ui, sans-serif';
    c.fillStyle = '#fff';
    for (let deg = -30; deg <= 30; deg += 10) {
      if (!deg) continue;
      const y = off - (deg * Math.PI) / 180 * 260;
      c.beginPath();
      c.moveTo(-40, y);
      c.lineTo(40, y);
      c.stroke();
      c.fillText(String(Math.abs(deg)), 48, y + 6);
    }
    c.restore();
    // Fixed aircraft symbol
    c.strokeStyle = '#ffd23a';
    c.lineWidth = 6;
    c.beginPath();
    c.moveTo(cx - 80, cy);
    c.lineTo(cx - 25, cy);
    c.lineTo(cx, cy + 18);
    c.lineTo(cx + 25, cy);
    c.lineTo(cx + 80, cy);
    c.stroke();
    c.strokeStyle = '#5f6d7c';
    c.lineWidth = 4;
    c.beginPath();
    c.arc(cx, cy, r, 0, Math.PI * 2);
    c.stroke();
    // Heading tape
    const heading = ((s.heading * 180) / Math.PI + 360) % 360;
    c.fillStyle = '#e8f4ff';
    c.font = 'bold 30px system-ui, sans-serif';
    c.textAlign = 'center';
    c.fillText(`${String(Math.round(heading)).padStart(3, '0')}°`, cx, H - 26);
    c.textAlign = 'left';

    // --- Right: altitude + course ---------------------------------------
    panel(c, W - 312, 12, 300, H - 24);
    label(c, 'ALTITUDE', W - 292, 52);
    big(c, `${Math.max(0, Math.round(s.altitude))}`, W - 292, 128, s.altitude < 40 && !s.onGround ? '#ffb21a' : '#e8f4ff');
    small(c, 'm', W - 70, 126);
    const vs = s.verticalSpeed;
    small(c, `${vs >= 0 ? '▲' : '▼'} ${Math.abs(vs).toFixed(0)} m/s`, W - 292, 164);

    label(c, 'RINGS', W - 292, 214);
    big(c, `${Math.min(s.ringIndex, COURSE.ringCount)}/${COURSE.ringCount}`, W - 292, 270, '#ffb21a', 52);
    label(c, 'TIME', W - 292, 316);
    big(c, formatTime(s.elapsed), W - 292, 362, '#e8f4ff', 44);

    // Arrow pointing to the next ring (relative to the nose).
    if (s.ringBearing !== null) {
      const ax = W - 70;
      const ay = 248;
      c.save();
      c.translate(ax, ay);
      c.rotate(s.ringBearing);
      c.fillStyle = '#ffb21a';
      c.beginPath();
      c.moveTo(0, -34);
      c.lineTo(20, 14);
      c.lineTo(0, 4);
      c.lineTo(-20, 14);
      c.closePath();
      c.fill();
      c.restore();
      small(c, `${(s.ringDistance / 1000).toFixed(1)} km`, W - 120, 300);
    }
  }
}

function panel(c, x, y, w, h) {
  c.fillStyle = '#131c26';
  c.strokeStyle = '#26394d';
  c.lineWidth = 3;
  roundRect(c, x, y, w, h, 18);
  c.fill();
  c.stroke();
}

function roundRect(c, x, y, w, h, r) {
  c.beginPath();
  c.moveTo(x + r, y);
  c.arcTo(x + w, y, x + w, y + h, r);
  c.arcTo(x + w, y + h, x, y + h, r);
  c.arcTo(x, y + h, x, y, r);
  c.arcTo(x, y, x + w, y, r);
  c.closePath();
}

function label(c, text, x, y, colour = '#7f97ad') {
  c.fillStyle = colour;
  c.font = 'bold 24px system-ui, sans-serif';
  c.fillText(text, x, y);
}

function big(c, text, x, y, colour = '#e8f4ff', size = 72) {
  c.fillStyle = colour;
  c.font = `bold ${size}px system-ui, sans-serif`;
  c.fillText(text, x, y);
}

function small(c, text, x, y) {
  c.fillStyle = '#9fb3c6';
  c.font = '26px system-ui, sans-serif';
  c.fillText(text, x, y);
}

function bar(c, x, y, w, h, v, colour) {
  c.fillStyle = '#0b1118';
  c.fillRect(x, y, w, h);
  c.fillStyle = colour;
  c.fillRect(x, y, w * THREE.MathUtils.clamp(v, 0, 1), h);
  c.strokeStyle = '#3a5068';
  c.lineWidth = 2;
  c.strokeRect(x, y, w, h);
}

function warn(c, text, x, y, colour = '#ff5050') {
  c.fillStyle = colour;
  c.font = 'bold 40px system-ui, sans-serif';
  c.fillText(text, x, y);
}
