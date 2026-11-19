import * as THREE from 'three';
import { mulberry32 } from '../core/noise.js';
import { WORLD } from '../config.js';

// Fluffy low-poly cumulus clouds: every puff of every cloud in a single instanced draw call.

export function createClouds(seed = WORLD.seed) {
  const rand = mulberry32(seed + 31);
  const puffGeo = new THREE.IcosahedronGeometry(1, 1);
  const material = new THREE.MeshLambertMaterial({
    color: 0xffffff,
    emissive: 0x8796a8,
    flatShading: true,
  });
  const puffsPerCloud = 7;
  const total = WORLD.cloudCount * puffsPerCloud;
  const mesh = new THREE.InstancedMesh(puffGeo, material, total);
  const m = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const s = new THREE.Vector3();
  const p = new THREE.Vector3();
  let i = 0;
  const range = WORLD.size * 0.9;
  for (let c = 0; c < WORLD.cloudCount; c++) {
    const cx = (rand() - 0.5) * range * 2;
    const cz = (rand() - 0.5) * range * 2;
    // Keep the airspace above the runway clear.
    if (Math.abs(cx) < 200 && Math.abs(cz) < 900) continue;
    const cy = 420 + rand() * 520;
    const size = 40 + rand() * 70;
    for (let k = 0; k < puffsPerCloud; k++) {
      const a = (k / puffsPerCloud) * Math.PI * 2 + rand();
      const r = size * (0.2 + rand() * 0.9);
      p.set(cx + Math.cos(a) * r * 1.4, cy + rand() * size * 0.3, cz + Math.sin(a) * r);
      const ps = size * (0.45 + rand() * 0.5);
      s.set(ps * 1.3, ps * 0.7, ps);
      q.setFromAxisAngle(THREE.Object3D.DEFAULT_UP, rand() * Math.PI);
      m.compose(p, q, s);
      mesh.setMatrixAt(i++, m);
    }
  }
  mesh.count = i;
  mesh.name = 'clouds';
  return mesh;
}
