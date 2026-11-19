import * as THREE from 'three';
import { SR22, FT, FPM, flapIndex } from './sr22Data.js';

// Glass cockpit in the style of modern light-aircraft avionics (generic, no brand marks):
//  PFD: attitude with pitch ladder and bank scale, airspeed and altitude tapes, VSI,
//       compass/HSI with heading bug, autopilot mode annunciator, flap + trim indicators.
//  MFD: track-up moving map of the archipelago (runway, rings, range ring) and an engine strip.
// Each display is a canvas texture on a plane in the panel, redrawn ~12 times a second.

const W = 1024;
const H = 683;
const C = {
  sky: '#2f6fc0',
  ground: '#7a4f24',
  white: '#ffffff',
  text: '#e9eef2',
  dim: '#8fa0b0',
  cyan: '#2fe0ff',
  magenta: '#ff4fd8',
  green: '#3be36a',
  amber: '#ffb21a',
  red: '#ff3b30',
  tape: 'rgba(30,38,48,0.78)',
  bg: '#0a0d10',
};

class Display {
  constructor(anchor, width, height, name) {
    this.canvas = document.createElement('canvas');
    this.canvas.width = W;
    this.canvas.height = H;
    this.ctx = this.canvas.getContext('2d');
    this.texture = new THREE.CanvasTexture(this.canvas);
    this.texture.colorSpace = THREE.SRGBColorSpace;
    this.texture.anisotropy = 8;
    this.texture.minFilter = THREE.LinearMipmapLinearFilter;
    this.mesh = new THREE.Mesh(
      new THREE.PlaneGeometry(width, height),
      new THREE.MeshBasicMaterial({ map: this.texture, toneMapped: false }),
    );
    this.mesh.name = name;
    this.mesh.position.z = 0.004;
    anchor.add(this.mesh);
    this.accum = 1;
  }

  tick(dt, rate, state) {
    this.accum += dt;
    if (this.accum < 1 / rate) return;
    this.accum = 0;
    this.draw(state);
    this.texture.needsUpdate = true;
  }
}

const font = (px, bold = true) => `${bold ? 'bold ' : ''}${px}px "Roboto Condensed", system-ui, sans-serif`;

function boxText(c, text, x, y, w, h, { fg = C.white, bg = '#000', border = C.white, size = 34, align = 'center' } = {}) {
  c.fillStyle = bg;
  c.fillRect(x, y, w, h);
  c.strokeStyle = border;
  c.lineWidth = 2;
  c.strokeRect(x, y, w, h);
  c.fillStyle = fg;
  c.font = font(size);
  c.textAlign = align;
  c.textBaseline = 'middle';
  c.fillText(text, align === 'center' ? x + w / 2 : x + w - 8, y + h / 2 + 1);
}

// --------------------------------------------------------------------------- PFD
export class PFD extends Display {
  draw(s) {
    const c = this.ctx;
    const cx = W / 2;
    const cy = 300;
    const pxPerDeg = 9;

    // Attitude
    c.save();
    c.fillStyle = C.bg;
    c.fillRect(0, 0, W, H);
    c.translate(cx, cy);
    c.rotate(-s.bank);
    const off = (s.pitch * 180) / Math.PI * pxPerDeg;
    c.fillStyle = C.sky;
    c.fillRect(-1400, -1400 + off, 2800, 1400);
    c.fillStyle = C.ground;
    c.fillRect(-1400, off, 2800, 1400);
    c.strokeStyle = C.white;
    c.lineWidth = 3;
    c.beginPath();
    c.moveTo(-1400, off);
    c.lineTo(1400, off);
    c.stroke();
    // Pitch ladder
    c.font = font(22);
    c.fillStyle = C.white;
    c.textAlign = 'left';
    c.textBaseline = 'middle';
    for (let d = -30; d <= 30; d += 2.5) {
      if (d === 0) continue;
      const y = off - d * pxPerDeg;
      if (y < -260 || y > 220) continue;
      const major = d % 10 === 0;
      const half = major ? 90 : d % 5 === 0 ? 50 : 22;
      c.lineWidth = 2;
      c.beginPath();
      c.moveTo(-half, y);
      c.lineTo(half, y);
      c.stroke();
      if (major) {
        c.fillText(String(Math.abs(d)), half + 8, y);
        c.textAlign = 'right';
        c.fillText(String(Math.abs(d)), -half - 8, y);
        c.textAlign = 'left';
      }
    }
    c.restore();

    // Bank scale (fixed) with roll pointer + slip/skid indicator
    c.save();
    c.translate(cx, cy);
    c.strokeStyle = C.white;
    c.lineWidth = 3;
    c.beginPath();
    c.arc(0, 0, 230, (-150 * Math.PI) / 180, (-30 * Math.PI) / 180);
    c.stroke();
    for (const d of [-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60]) {
      const a = ((d - 90) * Math.PI) / 180;
      const r2 = Math.abs(d) % 30 === 0 ? 255 : 245;
      c.beginPath();
      c.moveTo(Math.cos(a) * 230, Math.sin(a) * 230);
      c.lineTo(Math.cos(a) * r2, Math.sin(a) * r2);
      c.stroke();
    }
    c.rotate(-s.bank);
    c.fillStyle = Math.abs(s.bank) > 0.785 ? C.amber : C.white;
    c.beginPath();
    c.moveTo(0, -228);
    c.lineTo(-14, -204);
    c.lineTo(14, -204);
    c.closePath();
    c.fill();
    const slip = THREE.MathUtils.clamp(-s.beta * 600, -30, 30);
    c.fillRect(-16 + slip, -198, 32, 8);
    c.restore();

    // Aircraft symbol
    c.fillStyle = '#ffd400';
    c.strokeStyle = '#000';
    c.lineWidth = 2;
    c.beginPath();
    c.moveTo(cx, cy);
    c.lineTo(cx - 95, cy + 34);
    c.lineTo(cx - 60, cy + 34);
    c.closePath();
    c.moveTo(cx, cy);
    c.lineTo(cx + 95, cy + 34);
    c.lineTo(cx + 60, cy + 34);
    c.closePath();
    c.fill();
    c.stroke();

    this.drawSpeedTape(c, s);
    this.drawAltTape(c, s);
    this.drawVsi(c, s);
    this.drawHsi(c, s);
    this.drawAnnunciator(c, s);
    this.drawStatus(c, s);
  }

  drawSpeedTape(c, s) {
    const x = 40;
    const w = 130;
    const top = 70;
    const bottom = 520;
    const mid = 300;
    const pxPerKt = 4.2;
    c.fillStyle = C.tape;
    c.fillRect(x, top, w, bottom - top);
    c.save();
    c.beginPath();
    c.rect(x, top, w + 20, bottom - top);
    c.clip();
    // Speed bands: white flap arc, green normal, yellow caution, red never-exceed.
    const band = (from, to, color, bx, bw) => {
      const y1 = mid - (to - s.ias) * pxPerKt;
      const y2 = mid - (from - s.ias) * pxPerKt;
      c.fillStyle = color;
      c.fillRect(bx, y1, bw, y2 - y1);
    };
    band(SR22.vso, SR22.vfe100, C.white, x + w - 10, 5);
    band(SR22.vs1, SR22.vno, C.green, x + w - 5, 5);
    band(SR22.vno, SR22.vne, '#ffd400', x + w - 5, 5);
    band(SR22.vne, SR22.vne + 60, C.red, x + w - 10, 10);
    band(0, SR22.vso, C.red, x + w - 10, 10);
    c.strokeStyle = C.white;
    c.fillStyle = C.white;
    c.font = font(28);
    c.textAlign = 'right';
    c.textBaseline = 'middle';
    const start = Math.floor((s.ias - 60) / 10) * 10;
    for (let v = Math.max(0, start); v <= s.ias + 60; v += 10) {
      const y = mid - (v - s.ias) * pxPerKt;
      c.beginPath();
      c.moveTo(x + w - 22, y);
      c.lineTo(x + w - 10, y);
      c.lineWidth = 2;
      c.stroke();
      if (v % 20 === 0) c.fillText(String(v), x + w - 28, y);
    }
    c.restore();
    const warn = s.ias < SR22.vs1 && !s.onGround ? C.red : s.ias > SR22.vne ? C.red : C.white;
    boxText(c, String(Math.round(s.ias)), x - 4, mid - 26, w + 10, 52, { size: 40, fg: warn });
    c.fillStyle = C.dim;
    c.font = font(24, false);
    c.textAlign = 'left';
    c.fillText(`TAS ${Math.round(s.tas)}KT`, x, bottom + 26);
  }

  drawAltTape(c, s) {
    const x = W - 250;
    const w = 150;
    const top = 70;
    const bottom = 520;
    const mid = 300;
    const pxPerFt = 0.42;
    const alt = s.altFt;
    c.fillStyle = C.tape;
    c.fillRect(x, top, w, bottom - top);
    c.save();
    c.beginPath();
    c.rect(x - 10, top, w + 10, bottom - top);
    c.clip();
    c.strokeStyle = C.white;
    c.fillStyle = C.white;
    c.font = font(26);
    c.textAlign = 'left';
    c.textBaseline = 'middle';
    const start = Math.floor((alt - 600) / 100) * 100;
    for (let a = start; a <= alt + 600; a += 100) {
      const y = mid - (a - alt) * pxPerFt;
      c.lineWidth = 2;
      c.beginPath();
      c.moveTo(x, y);
      c.lineTo(x + (a % 500 === 0 ? 20 : 12), y);
      c.stroke();
      if (a % 200 === 0) c.fillText(String(a), x + 26, y);
    }
    // Selected altitude bug
    const by = THREE.MathUtils.clamp(mid - (s.altSelFt - alt) * pxPerFt, top + 6, bottom - 6);
    c.fillStyle = C.cyan;
    c.fillRect(x - 2, by - 14, 10, 28);
    c.restore();
    boxText(c, String(Math.round(alt / 10) * 10), x - 6, mid - 26, w + 12, 52, { size: 38 });
    boxText(c, String(Math.round(s.altSelFt)), x, top - 50, w, 40, { size: 28, fg: C.cyan, border: C.cyan });
  }

  drawVsi(c, s) {
    const x = W - 90;
    const mid = 300;
    const range = 2000;
    const scale = 200 / range;
    c.fillStyle = C.tape;
    c.fillRect(x, mid - 210, 60, 420);
    c.strokeStyle = C.white;
    c.fillStyle = C.white;
    c.font = font(20);
    c.textAlign = 'left';
    c.textBaseline = 'middle';
    for (const v of [-2000, -1000, -500, 0, 500, 1000, 2000]) {
      const y = mid - v * scale;
      c.beginPath();
      c.moveTo(x, y);
      c.lineTo(x + 12, y);
      c.lineWidth = 2;
      c.stroke();
      if (v && Math.abs(v) !== 500) c.fillText(String(Math.abs(v) / 1000), x + 16, y);
    }
    if (s.vsSelFpm !== null) {
      const sy = mid - THREE.MathUtils.clamp(s.vsSelFpm, -range, range) * scale;
      c.fillStyle = C.cyan;
      c.fillRect(x, sy - 6, 12, 12);
    }
    const vy = mid - THREE.MathUtils.clamp(s.vsFpm, -range, range) * scale;
    c.fillStyle = C.white;
    c.beginPath();
    c.moveTo(x + 4, vy);
    c.lineTo(x + 28, vy - 14);
    c.lineTo(x + 60, vy - 14);
    c.lineTo(x + 60, vy + 14);
    c.lineTo(x + 28, vy + 14);
    c.closePath();
    c.fill();
    c.fillStyle = '#000';
    c.font = font(18);
    c.textAlign = 'center';
    c.fillText(String(Math.round(Math.abs(s.vsFpm) / 100)), x + 42, vy + 1);
  }

  drawHsi(c, s) {
    const cx = W / 2;
    const cy = H + 150;
    const r = 260;
    c.save();
    c.beginPath();
    c.rect(200, 535, W - 400, H - 535);
    c.clip();
    c.fillStyle = 'rgba(10,14,18,0.9)';
    c.beginPath();
    c.arc(cx, cy, r, 0, Math.PI * 2);
    c.fill();
    c.translate(cx, cy);
    const hdg = (s.heading * 180) / Math.PI;
    c.rotate((-hdg * Math.PI) / 180);
    c.strokeStyle = C.white;
    c.fillStyle = C.white;
    c.textAlign = 'center';
    c.textBaseline = 'middle';
    for (let d = 0; d < 360; d += 5) {
      const a = ((d - 90) * Math.PI) / 180;
      const len = d % 10 === 0 ? 18 : 10;
      c.lineWidth = 2;
      c.beginPath();
      c.moveTo(Math.cos(a) * r, Math.sin(a) * r);
      c.lineTo(Math.cos(a) * (r - len), Math.sin(a) * (r - len));
      c.stroke();
      if (d % 30 === 0) {
        c.save();
        c.rotate((d * Math.PI) / 180);
        c.font = font(26);
        c.fillText({ 0: 'N', 90: 'E', 180: 'S', 270: 'W' }[d] ?? String(d / 10), 0, -r + 40);
        c.restore();
      }
    }
    // Heading bug
    c.save();
    c.rotate(s.hdgBug);
    c.fillStyle = C.cyan;
    c.fillRect(-14, -r - 2, 28, 12);
    c.restore();
    // Bearing to the next ring
    if (s.ringBearingAbs !== null) {
      c.save();
      c.rotate(s.ringBearingAbs);
      c.strokeStyle = C.magenta;
      c.lineWidth = 5;
      c.beginPath();
      c.moveTo(0, -r + 60);
      c.lineTo(0, -r + 110);
      c.stroke();
      c.restore();
    }
    c.restore();
    boxText(c, `${String(Math.round((hdg + 360) % 360)).padStart(3, '0')}°`, cx - 55, 538, 110, 42, { size: 32 });
    c.fillStyle = C.cyan;
    c.font = font(24);
    c.textAlign = 'left';
    c.fillText(`HDG ${String(Math.round(((s.hdgBug * 180) / Math.PI + 360) % 360)).padStart(3, '0')}°`, 210, 620);
  }

  drawAnnunciator(c, s) {
    c.fillStyle = 'rgba(0,0,0,0.75)';
    c.fillRect(0, 0, W, 48);
    c.font = font(28);
    c.textBaseline = 'middle';
    c.textAlign = 'center';
    const ap = s.ap;
    c.fillStyle = ap.ap ? C.green : '#4a5560';
    c.fillText(ap.ap ? 'AP' : 'AP OFF', W / 2, 25);
    c.fillStyle = ap.ap ? C.green : C.dim;
    c.fillText(ap.lateral, W / 2 - 200, 25);
    c.fillText(ap.vertical, W / 2 + 200, 25);
    if (ap.armed) {
      c.fillStyle = C.white;
      c.fillText(ap.armed, W / 2 + 330, 25);
    }
    if (ap.vertical === 'VS') {
      c.fillStyle = C.cyan;
      c.fillText(`${Math.round(s.vsSelFpm)}FPM`, W / 2 + 90, 25);
    }
    if (s.espActive) {
      c.fillStyle = C.amber;
      c.fillText('ESP', 70, 25);
    }
  }

  drawStatus(c, s) {
    // Flaps + trim (lower left)
    c.font = font(22);
    c.textAlign = 'left';
    c.textBaseline = 'middle';
    c.fillStyle = C.dim;
    c.fillText('FLAPS', 30, 590);
    const fl = ['UP', '50%', '100%'][flapIndex(s.flapsTarget)];
    c.fillStyle = Math.abs(s.flaps - s.flapsTarget) > 0.01 ? C.amber : C.white;
    c.fillText(fl, 110, 590);
    c.fillStyle = C.dim;
    c.fillText('TRIM', 30, 630);
    c.strokeStyle = C.dim;
    c.strokeRect(100, 620, 80, 20);
    c.fillStyle = C.green;
    c.fillRect(100 + 40 + s.trim * 38 - 3, 618, 6, 24);
    if (s.flapOverspeed) {
      c.fillStyle = C.amber;
      c.fillText('FLAP OVERSPEED', 30, 665);
    }
    // Warnings (centre)
    c.textAlign = 'center';
    c.font = font(40);
    let warn = null;
    if (s.stallWarning && Math.floor(s.time * 3) % 2 === 0) warn = ['STALL', C.red];
    else if (s.safeReturn) warn = [s.safeReturn, C.green];
    else if (s.chute === 'deployed' || s.chute === 'inflating') warn = ['PARACHUTE DEPLOYED', C.amber];
    else if (!s.engineRunning) warn = ['ENGINE OFF', C.amber];
    else if (s.parkingBrake && s.onGround) warn = ['PARKING BRAKE', C.amber];
    if (warn) {
      c.font = font(warn[0].length > 18 ? 26 : 40);
      const tw = c.measureText(warn[0]).width + 30;
      c.fillStyle = 'rgba(0,0,0,0.7)';
      c.fillRect(W / 2 - tw / 2, 80, tw, 52);
      c.fillStyle = warn[1];
      c.fillText(warn[0], W / 2, 107);
    }
  }
}

// --------------------------------------------------------------------------- MFD
const MAP_EXTENT = 7000; // metres either side of the island centre covered by the map image

export class MFD extends Display {
  constructor(anchor, width, height, name, world) {
    super(anchor, width, height, name);
    this.world = world;
    this.rangeNm = 5;
    this.mapImage = this.renderTerrainMap(world);
  }

  renderTerrainMap(world) {
    const size = 512;
    const img = document.createElement('canvas');
    img.width = img.height = size;
    const c = img.getContext('2d');
    const data = c.createImageData(size, size);
    for (let j = 0; j < size; j++) {
      for (let i = 0; i < size; i++) {
        const x = (i / size) * 2 * MAP_EXTENT - MAP_EXTENT;
        const z = (j / size) * 2 * MAP_EXTENT - MAP_EXTENT;
        const h = world.heightAt(x, z);
        let col;
        if (h < -8) col = [12, 38, 70];
        else if (h < 0.3) col = [24, 92, 120];
        else if (h < 6) col = [190, 170, 110];
        else if (h < 150) col = [40 + h * 0.3, 110 - h * 0.2, 45];
        else col = [130 + Math.min(h - 150, 200) * 0.4, 110 + Math.min(h - 150, 200) * 0.3, 90];
        const k = (j * size + i) * 4;
        data.data[k] = col[0];
        data.data[k + 1] = col[1];
        data.data[k + 2] = col[2];
        data.data[k + 3] = 255;
      }
    }
    c.putImageData(data, 0, 0);
    return img;
  }

  draw(s) {
    const c = this.ctx;
    c.fillStyle = C.bg;
    c.fillRect(0, 0, W, H);
    this.drawMap(c, s);
    this.drawEngine(c, s);
  }

  drawMap(c, s) {
    const x0 = 250;
    const w = W - x0;
    const cx = x0 + w / 2;
    const cy = H * 0.62;
    const pxPerM = (H * 0.5) / (this.rangeNm * 1852);
    c.save();
    c.beginPath();
    c.rect(x0, 0, w, H);
    c.clip();
    c.translate(cx, cy);
    c.rotate(-s.heading);
    c.scale(pxPerM, pxPerM);
    c.translate(-s.x, -s.z);
    c.imageSmoothingEnabled = true;
    c.drawImage(this.mapImage, -MAP_EXTENT, -MAP_EXTENT, MAP_EXTENT * 2, MAP_EXTENT * 2);
    // Runway
    const rw = this.world.runway;
    c.fillStyle = '#e8e8e8';
    c.fillRect(-rw.halfWidth * 2, -rw.halfLength, rw.halfWidth * 4, rw.halfLength * 2);
    // Extended centreline toward the approach
    c.strokeStyle = 'rgba(255,255,255,0.5)';
    c.lineWidth = 6 / pxPerM;
    c.setLineDash([60 / pxPerM, 40 / pxPerM]);
    c.beginPath();
    c.moveTo(0, rw.halfLength);
    c.lineTo(0, rw.halfLength + 6000);
    c.stroke();
    c.setLineDash([]);
    // Rings
    s.rings.forEach((r, i) => {
      if (i < s.ringIndex) return;
      c.fillStyle = i === s.ringIndex ? C.magenta : 'rgba(255,255,255,0.75)';
      c.beginPath();
      c.arc(r.x, r.z, (i === s.ringIndex ? 14 : 9) / pxPerM, 0, Math.PI * 2);
      c.fill();
    });
    if (s.ringIndex < s.rings.length) {
      const r = s.rings[s.ringIndex];
      c.strokeStyle = C.magenta;
      c.lineWidth = 4 / pxPerM;
      c.beginPath();
      c.moveTo(s.x, s.z);
      c.lineTo(r.x, r.z);
      c.stroke();
    }
    c.restore();

    // Range ring + ownship
    c.strokeStyle = 'rgba(255,255,255,0.6)';
    c.lineWidth = 2;
    c.beginPath();
    c.arc(cx, cy, H * 0.25, Math.PI, Math.PI * 2);
    c.stroke();
    c.fillStyle = C.white;
    c.font = font(22);
    c.textAlign = 'left';
    c.fillText(`${this.rangeNm / 2} NM`, cx + H * 0.25 * 0.72, cy - H * 0.25 * 0.72);
    c.fillStyle = C.white;
    c.beginPath();
    c.moveTo(cx, cy - 22);
    c.lineTo(cx - 16, cy + 18);
    c.lineTo(cx, cy + 8);
    c.lineTo(cx + 16, cy + 18);
    c.closePath();
    c.fill();
    // Header
    c.fillStyle = 'rgba(0,0,0,0.75)';
    c.fillRect(x0, 0, w, 46);
    c.fillStyle = C.text;
    c.font = font(26);
    c.textAlign = 'left';
    c.textBaseline = 'middle';
    c.fillText(`MAP · TRK UP · ${this.rangeNm} NM`, x0 + 14, 24);
    c.textAlign = 'right';
    c.fillStyle = C.magenta;
    if (s.ringIndex < s.rings.length) c.fillText(`RING ${s.ringIndex + 1}  ${(s.ringDistance / 1852).toFixed(1)} NM`, W - 14, 24);
    else c.fillText('COURSE COMPLETE', W - 14, 24);
    // Footer: race info + emergency systems
    c.fillStyle = 'rgba(0,0,0,0.75)';
    c.fillRect(x0, H - 46, w, 46);
    c.textAlign = 'left';
    c.fillStyle = C.text;
    c.fillText(`RINGS ${Math.min(s.ringIndex, s.rings.length)}/${s.rings.length}   TIME ${s.timeText}`, x0 + 14, H - 22);
    c.textAlign = 'right';
    c.fillStyle = s.chute === 'stowed' ? C.dim : C.amber;
    c.fillText(s.chute === 'stowed' ? 'CAPS ARMED' : `CAPS ${s.chute.toUpperCase()}`, W - 14, H - 22);
  }

  drawEngine(c, s) {
    c.fillStyle = '#11161b';
    c.fillRect(0, 0, 250, H);
    c.strokeStyle = '#26303a';
    c.strokeRect(0, 0, 250, H);
    // RPM arc gauge
    const gx = 125;
    const gy = 120;
    const r = 80;
    const a0 = Math.PI * 0.8;
    const a1 = Math.PI * 2.2;
    c.lineWidth = 12;
    c.strokeStyle = '#2a333c';
    c.beginPath();
    c.arc(gx, gy, r, a0, a1);
    c.stroke();
    c.strokeStyle = C.green;
    c.beginPath();
    c.arc(gx, gy, r, a0 + (a1 - a0) * (500 / 2800), a0 + (a1 - a0) * (2700 / 2800));
    c.stroke();
    const ra = a0 + (a1 - a0) * Math.min(1, s.rpm / 2800);
    c.strokeStyle = C.white;
    c.lineWidth = 5;
    c.beginPath();
    c.moveTo(gx, gy);
    c.lineTo(gx + Math.cos(ra) * (r - 4), gy + Math.sin(ra) * (r - 4));
    c.stroke();
    c.fillStyle = C.white;
    c.font = font(34);
    c.textAlign = 'center';
    c.textBaseline = 'middle';
    c.fillText(String(Math.round(s.rpm / 10) * 10), gx, gy + 50);
    c.fillStyle = C.dim;
    c.font = font(20);
    c.fillText('RPM', gx, gy - 30);
    const row = (label, value, y, color = C.white) => {
      c.textAlign = 'left';
      c.fillStyle = C.dim;
      c.font = font(22);
      c.fillText(label, 16, y);
      c.textAlign = 'right';
      c.fillStyle = color;
      c.font = font(28);
      c.fillText(value, 234, y);
    };
    row('% POWER', `${s.percentPower}`, 230, C.green);
    row('MAN IN', s.manifold.toFixed(1), 275);
    row('FUEL FLOW', s.fuelFlow.toFixed(1), 320);
    row('FUEL GAL', s.fuelGal.toFixed(0), 365, s.fuelGal < 15 ? C.amber : C.white);
    row('OIL °F', s.engineRunning ? '195' : '120', 410);
    row('OIL PSI', s.engineRunning ? '52' : '0', 455, s.engineRunning ? C.white : C.red);
    row('G LOAD', s.gLoad.toFixed(1), 500, Math.abs(s.gLoad) > 3.8 ? C.red : C.white);
    row('OAT °C', String(Math.round(15 - s.altFt * 0.002)), 545);
    c.textAlign = 'center';
    c.font = font(22);
    c.fillStyle = s.assists ? C.green : C.dim;
    c.fillText(s.assists ? 'ASSISTS ON' : 'ASSISTS OFF', 125, 600);
    c.fillStyle = C.dim;
    c.fillText(`GS ${Math.round(s.groundSpeedKt)} KT`, 125, 640);
  }
}

export function createAvionics(model3d, world) {
  const pfd = new PFD(model3d.pfdAnchor, 0.36, 0.24, 'PFD');
  const mfd = new MFD(model3d.mfdAnchor, 0.36, 0.24, 'MFD', world);
  return {
    pfd,
    mfd,
    update(dt, state) {
      pfd.tick(dt, 15, state);
      mfd.tick(dt, 8, state);
    },
  };
}

export { FT, FPM };
