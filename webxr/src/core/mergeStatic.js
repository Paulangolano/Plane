import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

// Draw calls are the main GPU cost on Quest. This merges a group's static child meshes
// into one mesh per material (moving parts live in sub-groups and are left alone).

/**
 * @param {THREE.Object3D} parent
 * @param {{skip?: Set<THREE.Object3D>}} [opts] meshes to leave untouched (e.g. ones that get hidden)
 */
export function mergeStatic(parent, { skip = new Set() } = {}) {
  parent.updateMatrixWorld(true);
  const toLocal = new THREE.Matrix4().copy(parent.matrixWorld).invert();
  const groups = new Map();
  for (const child of [...parent.children]) {
    if (!child.isMesh || skip.has(child) || child.material.transparent || child.material.map) continue;
    const m = new THREE.Matrix4().multiplyMatrices(toLocal, child.matrixWorld);
    let g = child.geometry.index ? child.geometry.toNonIndexed() : child.geometry.clone();
    for (const name of Object.keys(g.attributes)) if (name !== 'position' && name !== 'normal') g.deleteAttribute(name);
    if (!g.attributes.normal) g.computeVertexNormals();
    g.applyMatrix4(m);
    if (!groups.has(child.material)) groups.set(child.material, []);
    groups.get(child.material).push(g);
    parent.remove(child);
  }
  for (const [material, geos] of groups) {
    const merged = new THREE.Mesh(mergeGeometries(geos), material);
    merged.name = `merged-${material.uuid.slice(0, 6)}`;
    merged.matrixAutoUpdate = false;
    parent.add(merged);
  }
  return groups.size;
}
