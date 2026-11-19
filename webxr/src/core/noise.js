// Deterministic random numbers and 2D Perlin noise.
// Ported 1:1 to C# in the Unity version (SkyreachNoise.cs) so both worlds match.

export function mulberry32(seed) {
  let a = seed >>> 0;
  return function rand() {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export class Perlin2D {
  constructor(seed = 1) {
    const rand = mulberry32(seed);
    const p = new Uint8Array(256);
    for (let i = 0; i < 256; i++) p[i] = i;
    for (let i = 255; i > 0; i--) {
      const j = Math.floor(rand() * (i + 1));
      const t = p[i];
      p[i] = p[j];
      p[j] = t;
    }
    this.perm = new Uint8Array(512);
    for (let i = 0; i < 512; i++) this.perm[i] = p[i & 255];
  }

  static fade(t) {
    return t * t * t * (t * (t * 6 - 15) + 10);
  }

  static grad(hash, x, y) {
    switch (hash & 7) {
      case 0: return x + y;
      case 1: return -x + y;
      case 2: return x - y;
      case 3: return -x - y;
      case 4: return x;
      case 5: return -x;
      case 6: return y;
      default: return -y;
    }
  }

  /** Returns roughly -1..1 */
  noise(x, y) {
    const xi = Math.floor(x);
    const yi = Math.floor(y);
    const X = xi & 255;
    const Y = yi & 255;
    const xf = x - xi;
    const yf = y - yi;
    const u = Perlin2D.fade(xf);
    const v = Perlin2D.fade(yf);
    const p = this.perm;
    const aa = p[p[X] + Y];
    const ab = p[p[X] + Y + 1];
    const ba = p[p[X + 1] + Y];
    const bb = p[p[X + 1] + Y + 1];
    const x1 = lerp(Perlin2D.grad(aa, xf, yf), Perlin2D.grad(ba, xf - 1, yf), u);
    const x2 = lerp(Perlin2D.grad(ab, xf, yf - 1), Perlin2D.grad(bb, xf - 1, yf - 1), u);
    return lerp(x1, x2, v);
  }

  /** Fractal Brownian motion, roughly -1..1 */
  fbm(x, y, octaves = 5) {
    let amp = 1;
    let freq = 1;
    let sum = 0;
    let norm = 0;
    for (let i = 0; i < octaves; i++) {
      sum += this.noise(x * freq, y * freq) * amp;
      norm += amp;
      amp *= 0.5;
      freq *= 2.03;
    }
    return sum / norm;
  }
}

export function lerp(a, b, t) {
  return a + (b - a) * t;
}

export function smoothstep(edge0, edge1, x) {
  const t = Math.min(1, Math.max(0, (x - edge0) / (edge1 - edge0)));
  return t * t * (3 - 2 * t);
}
