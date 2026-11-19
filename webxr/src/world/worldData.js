import { Perlin2D, mulberry32, smoothstep, lerp } from '../core/noise.js';
import { WORLD, SPAWN, COURSE } from '../config.js';

// Pure world description (no rendering): island layout, terrain height, ring course.
// Shared by the renderer, the flight model's collision checks and the unit tests.

export class WorldData {
  constructor(seed = WORLD.seed) {
    this.seed = seed;
    this.noise = new Perlin2D(seed);
    this.ridgeNoise = new Perlin2D(seed + 99);
    this.islands = this.generateIslands();
    this.runway = {
      halfWidth: SPAWN.runwayWidth / 2,
      halfLength: SPAWN.runwayLength / 2,
      height: SPAWN.runwayHeight,
    };
    this.rings = this.generateCourse();
  }

  generateIslands() {
    const rand = mulberry32(this.seed);
    // Home island: long and flat in the middle for the runway, hills to the east.
    const islands = [{ x: 0, z: 0, radius: 900, peak: 190, stretch: 0.68, ox: 11.3, oz: 7.1, home: true }];
    let attempts = 0;
    while (islands.length < WORLD.islandCount && attempts < 500) {
      attempts++;
      const angle = rand() * Math.PI * 2;
      const dist = 1300 + rand() * 1500;
      const radius = 230 + rand() * 420;
      const candidate = {
        x: Math.cos(angle) * dist,
        z: Math.sin(angle) * dist,
        radius,
        peak: 70 + rand() * 380,
        stretch: 1,
        ox: rand() * 100,
        oz: rand() * 100,
        home: false,
      };
      const clear = islands.every(
        (o) => Math.hypot(o.x - candidate.x, o.z - candidate.z) > (o.radius + candidate.radius) * 1.15 + 120,
      );
      if (clear) islands.push(candidate);
    }
    return islands;
  }

  /** Distance from (x,z) to the runway rectangle (0 when on it). */
  runwayDistance(x, z) {
    const dx = Math.max(Math.abs(x) - this.runway.halfWidth, 0);
    const dz = Math.max(Math.abs(z) - this.runway.halfLength, 0);
    return Math.hypot(dx, dz);
  }

  heightAt(x, z) {
    let h = -45;
    for (const isl of this.islands) {
      const dx = x - isl.x;
      const dz = (z - isl.z) * isl.stretch;
      const d = Math.sqrt(dx * dx + dz * dz) / isl.radius;
      if (d > 1.25) continue;
      const shape = smoothstep(1.2, 0.12, d);
      const n = this.noise.fbm((x + isl.ox) * 0.0032, (z + isl.oz) * 0.0032, 5);
      const r = 1 - Math.abs(this.ridgeNoise.fbm(x * 0.0055, z * 0.0055, 4));
      let mountain = isl.peak * Math.pow(shape, 1.7) * (0.5 + 0.5 * n) * (0.55 + 0.45 * r * r);
      if (isl.home) {
        // Keep the hills away from the runway, and push them east.
        const mask = smoothstep(140, 480, this.runwayDistance(x, z)) * smoothstep(-200, 400, x);
        mountain *= mask;
      }
      const hi = -14 + shape * 20 + mountain + n * 3;
      if (hi > h) h = hi;
    }
    // Flatten the runway and its shoulders.
    const rd = this.runwayDistance(x, z);
    if (rd < 120) {
      h = lerp(this.runway.height, h, smoothstep(0, 120, rd));
    }
    return h;
  }

  isRunway(x, z) {
    return Math.abs(x) <= this.runway.halfWidth && Math.abs(z) <= this.runway.halfLength;
  }

  sampleGround(x, z) {
    if (this.isRunway(x, z)) return { height: this.runway.height, type: 'runway' };
    const h = this.heightAt(x, z);
    return { height: h, type: h > 0.3 ? 'land' : 'water' };
  }

  /** Highest terrain under a ring (centre plus rim). */
  clearanceHeight(x, z, radius) {
    let max = this.heightAt(x, z);
    for (let i = 0; i < 8; i++) {
      const a = (i / 8) * Math.PI * 2;
      max = Math.max(max, this.heightAt(x + Math.cos(a) * radius * 2, z + Math.sin(a) * radius * 2));
    }
    return Math.max(max, 0);
  }

  generateCourse() {
    const rand = mulberry32(this.seed + 7);
    const pts = [];
    // Ring 1: straight off the end of the runway (takeoff heads toward -Z).
    pts.push({ x: 0, y: 110, z: -this.runway.halfLength - 650 });
    // Loop clockwise around the archipelago.
    const loopCount = COURSE.ringCount - 3;
    for (let i = 0; i < loopCount; i++) {
      const t = (i + 1) / (loopCount + 1);
      const theta = 0.12 * Math.PI + t * 1.25 * Math.PI;
      const radius = 1700 + (rand() - 0.5) * 900;
      const x = Math.sin(theta) * radius;
      const z = -Math.cos(theta) * radius;
      const y = 90 + rand() * 360;
      pts.push({ x, y, z });
    }
    // Approach: swing in from the south-west and line up with the runway.
    pts.push({ x: -500, y: 170, z: this.runway.halfLength + 1500 });
    pts.push({ x: 0, y: 75, z: this.runway.halfLength + 750 });

    // Make sure no ring sits inside a mountain.
    for (const p of pts) {
      const floor = this.clearanceHeight(p.x, p.z, COURSE.ringRadius) + COURSE.ringRadius + 35;
      if (p.y < floor) p.y = floor;
    }
    // Each ring faces the way you arrive at it, so you never meet one edge-on.
    // Ring 1 points straight off the runway; the last ring is lined up for landing.
    return pts.map((p, i) => {
      const prev = pts[i - 1];
      let dir;
      if (i === 0) dir = { x: 0, y: 0, z: -1 };
      else if (i === pts.length - 1) dir = { x: 0, y: -0.1, z: -1 };
      else dir = { x: p.x - prev.x, y: p.y - prev.y, z: p.z - prev.z };
      dir.y *= 0.3; // keep rings mostly upright
      const len = Math.hypot(dir.x, dir.y, dir.z) || 1;
      return { position: p, normal: { x: dir.x / len, y: dir.y / len, z: dir.z / len } };
    });
  }
}
