import * as THREE from 'three';
import { WORLD } from '../config.js';

// Builds one low-poly mesh per island, vertex-coloured by height and slope,
// plus the runway with its markings. Everything is merged per island to keep draw calls low.

const SAND = new THREE.Color(0xe8d49a);
const WET_SAND = new THREE.Color(0xc7b07a);
const GRASS = new THREE.Color(0x5f9e3a);
const FOREST = new THREE.Color(0x3c7a2c);
const ROCK = new THREE.Color(0x8a8378);
const SNOW = new THREE.Color(0xf4f6f8);
const SEABED = new THREE.Color(0xd9c690);

function terrainColour(h, slope, n, out) {
  if (h < -1) out.copy(SEABED);
  else if (h < 2.5) out.copy(WET_SAND).lerp(SAND, Math.max(0, (h + 1) / 3.5));
  else if (h < 7) out.copy(SAND).lerp(GRASS, (h - 2.5) / 4.5);
  else if (h < 160) out.copy(GRASS).lerp(FOREST, Math.min(1, 0.5 + n * 0.8));
  else if (h < 260) out.copy(FOREST).lerp(ROCK, (h - 160) / 100);
  else out.copy(ROCK).lerp(SNOW, Math.min(1, (h - 260) / 60));
  if (slope > 0.55 && h > 8) out.lerp(ROCK, Math.min(1, (slope - 0.55) * 2.5));
  return out;
}

export function createTerrain(world) {
  const group = new THREE.Group();
  group.name = 'terrain';
  const material = new THREE.MeshLambertMaterial({ vertexColors: true, flatShading: true });

  for (const isl of world.islands) {
    const extentX = isl.radius * 1.3;
    const extentZ = (isl.radius * 1.3) / isl.stretch;
    const spacing = (isl.home ? 18 : 22) * WORLD.terrainResolution * 0.5;
    const segX = Math.ceil((extentX * 2) / spacing);
    const segZ = Math.ceil((extentZ * 2) / spacing);
    const geo = new THREE.PlaneGeometry(extentX * 2, extentZ * 2, segX, segZ);
    geo.rotateX(-Math.PI / 2);
    const pos = geo.attributes.position;
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i) + isl.x;
      const z = pos.getZ(i) + isl.z;
      pos.setXYZ(i, x, world.heightAt(x, z), z);
    }
    // Non-indexed so each triangle can have a crisp flat colour.
    const flat = geo.toNonIndexed();
    geo.dispose();
    flat.computeVertexNormals();
    const fp = flat.attributes.position;
    const fn = flat.attributes.normal;
    const colors = new Float32Array(fp.count * 3);
    const c = new THREE.Color();
    for (let i = 0; i < fp.count; i += 3) {
      // Colour per triangle using its centroid.
      const cx = (fp.getX(i) + fp.getX(i + 1) + fp.getX(i + 2)) / 3;
      const cy = (fp.getY(i) + fp.getY(i + 1) + fp.getY(i + 2)) / 3;
      const cz = (fp.getZ(i) + fp.getZ(i + 1) + fp.getZ(i + 2)) / 3;
      const slope = 1 - fn.getY(i);
      const n = world.noise.noise(cx * 0.02, cz * 0.02) * 0.5 + 0.5;
      terrainColour(cy, slope, n, c);
      // Tiny per-triangle jitter breaks up banding.
      const j = 1 + (world.noise.noise(cx * 0.37, cz * 0.37) * 0.06);
      for (let k = 0; k < 3; k++) {
        colors[(i + k) * 3] = c.r * j;
        colors[(i + k) * 3 + 1] = c.g * j;
        colors[(i + k) * 3 + 2] = c.b * j;
      }
    }
    flat.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    const mesh = new THREE.Mesh(flat, material);
    mesh.name = isl.home ? 'home-island' : 'island';
    mesh.matrixAutoUpdate = false;
    group.add(mesh);
  }

  group.add(createRunway(world));
  group.add(createTrees(world));
  return group;
}

function createRunway(world) {
  const { halfWidth, halfLength, height } = world.runway;
  const canvas = document.createElement('canvas');
  canvas.width = 128;
  canvas.height = 1024;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#3b3d40';
  ctx.fillRect(0, 0, 128, 1024);
  ctx.fillStyle = '#f2f2f2';
  // Edge lines
  ctx.fillRect(4, 0, 3, 1024);
  ctx.fillRect(121, 0, 3, 1024);
  // Centre dashes
  for (let y = 40; y < 984; y += 40) ctx.fillRect(62, y, 4, 22);
  // Threshold piano keys at both ends
  for (let i = 0; i < 8; i++) {
    ctx.fillRect(12 + i * 13, 6, 7, 26);
    ctx.fillRect(12 + i * 13, 992, 7, 26);
  }
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  const geo = new THREE.PlaneGeometry(halfWidth * 2, halfLength * 2);
  geo.rotateX(-Math.PI / 2);
  const mesh = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({ map: tex }));
  mesh.position.y = height + 0.08;
  mesh.name = 'runway';

  // Approach lights so the runway is easy to find from the air.
  const lights = new THREE.Group();
  const lightGeo = new THREE.BoxGeometry(1.2, 0.6, 1.2);
  const red = new THREE.MeshBasicMaterial({ color: 0xff3b2f });
  const green = new THREE.MeshBasicMaterial({ color: 0x3dff6a });
  const white = new THREE.MeshBasicMaterial({ color: 0xffffff });
  const edges = new THREE.InstancedMesh(lightGeo, white, Math.ceil((halfLength * 2) / 60) * 2);
  const m = new THREE.Matrix4();
  let idx = 0;
  for (let z = -halfLength; z <= halfLength; z += 60) {
    m.makeTranslation(-halfWidth - 2, height + 0.3, z);
    edges.setMatrixAt(idx++, m);
    m.makeTranslation(halfWidth + 2, height + 0.3, z);
    edges.setMatrixAt(idx++, m);
  }
  edges.count = idx;
  lights.add(edges);
  for (let x = -halfWidth; x <= halfWidth; x += 5) {
    const g = new THREE.Mesh(lightGeo, green);
    g.position.set(x, height + 0.3, halfLength + 3);
    lights.add(g);
    const r = new THREE.Mesh(lightGeo, red);
    r.position.set(x, height + 0.3, -halfLength - 3);
    lights.add(r);
  }
  const group = new THREE.Group();
  group.add(mesh, lights);
  group.add(createTower(world));
  return group;
}

function createTower(world) {
  const tower = new THREE.Group();
  const mat = new THREE.MeshLambertMaterial({ color: 0xe9e4da });
  const glass = new THREE.MeshLambertMaterial({ color: 0x2b5f7a, emissive: 0x0b2230 });
  const base = new THREE.Mesh(new THREE.CylinderGeometry(4, 5, 26, 8), mat);
  base.position.y = 13;
  const cab = new THREE.Mesh(new THREE.CylinderGeometry(7, 5.5, 6, 8), glass);
  cab.position.y = 29;
  const roof = new THREE.Mesh(new THREE.CylinderGeometry(7.6, 7.6, 1, 8), mat);
  roof.position.y = 32.5;
  const hangar = new THREE.Mesh(new THREE.BoxGeometry(40, 14, 30), new THREE.MeshLambertMaterial({ color: 0xa9b3bd }));
  hangar.position.set(-30, 7, 60);
  tower.add(base, cab, roof, hangar);
  const x = -world.runway.halfWidth - 70;
  tower.position.set(x, world.heightAt(x, 0) - 0.5, 0);
  return tower;
}

function createTrees(world) {
  // Simple low-poly pines, instanced in one draw call.
  const trunk = new THREE.CylinderGeometry(0.6, 0.8, 4, 5);
  trunk.translate(0, 2, 0);
  const crown = new THREE.ConeGeometry(4, 11, 6);
  crown.translate(0, 9, 0);
  const merged = mergeColoured([
    [trunk, new THREE.Color(0x6b4a2b)],
    [crown, new THREE.Color(0x2f6b2a)],
  ]);
  const material = new THREE.MeshLambertMaterial({ vertexColors: true, flatShading: true });
  const max = 1800;
  const trees = new THREE.InstancedMesh(merged, material, max);
  const m = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const s = new THREE.Vector3();
  const p = new THREE.Vector3();
  let count = 0;
  let seed = 4242;
  const rand = () => {
    seed = (seed * 16807) % 2147483647;
    return seed / 2147483647;
  };
  for (const isl of world.islands) {
    const attempts = Math.floor(isl.radius * isl.radius * 0.0012);
    for (let i = 0; i < attempts && count < max; i++) {
      const a = rand() * Math.PI * 2;
      const r = Math.sqrt(rand()) * isl.radius;
      const x = isl.x + Math.cos(a) * r;
      const z = isl.z + (Math.sin(a) * r) / isl.stretch;
      const h = world.heightAt(x, z);
      if (h < 9 || h > 170) continue;
      if (world.runwayDistance(x, z) < 90) continue;
      // Skip steep spots
      const slope = Math.abs(world.heightAt(x + 4, z) - h) + Math.abs(world.heightAt(x, z + 4) - h);
      if (slope > 5) continue;
      if (world.noise.noise(x * 0.006, z * 0.006) < -0.05) continue; // clumps of forest
      const scale = 0.7 + rand() * 0.8;
      q.setFromAxisAngle(THREE.Object3D.DEFAULT_UP, rand() * Math.PI);
      s.set(scale, scale * (0.8 + rand() * 0.5), scale);
      p.set(x, h - 0.5, z);
      m.compose(p, q, s);
      trees.setMatrixAt(count++, m);
    }
  }
  trees.count = count;
  trees.name = 'trees';
  return trees;
}

/** Merge geometries into one, baking a flat colour into each. */
export function mergeColoured(parts) {
  const positions = [];
  const normals = [];
  const colors = [];
  for (const [g, col] of parts) {
    const geo = g.index ? g.toNonIndexed() : g;
    geo.computeVertexNormals();
    const p = geo.attributes.position;
    const n = geo.attributes.normal;
    for (let i = 0; i < p.count; i++) {
      positions.push(p.getX(i), p.getY(i), p.getZ(i));
      normals.push(n.getX(i), n.getY(i), n.getZ(i));
      colors.push(col.r, col.g, col.b);
    }
  }
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  out.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
  out.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  return out;
}
