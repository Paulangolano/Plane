import * as THREE from 'three';
import { COURSE } from '../config.js';

// Ring course: crossing detection (pure logic, unit tested) + the ring visuals.

export class RaceCourse {
  constructor(rings) {
    this.rings = rings.map((r) => ({
      center: new THREE.Vector3(r.position.x, r.position.y, r.position.z),
      normal: new THREE.Vector3(r.normal.x, r.normal.y, r.normal.z),
    }));
    this.reset();
  }

  reset() {
    this.index = 0;
    this.elapsed = 0;
    this.running = false;
    this.finished = false;
    this.missedPenalty = 0;
  }

  get current() {
    return this.rings[this.index];
  }

  start() {
    if (!this.running && !this.finished) this.running = true;
  }

  tick(dt) {
    if (this.running) this.elapsed += dt;
  }

  /**
   * Check whether the segment prev->cur flew through the active ring.
   * @returns {null | {type:'ring', index:number} | {type:'finish', time:number}}
   */
  check(prev, cur) {
    if (this.finished) return null;
    const ring = this.current;
    const d0 = _tmp.subVectors(prev, ring.center).dot(ring.normal);
    const d1 = _tmp.subVectors(cur, ring.center).dot(ring.normal);
    if (!(d0 < 0 && d1 >= 0)) return null;
    const t = d0 / (d0 - d1);
    const hit = _tmp.lerpVectors(prev, cur, t);
    if (hit.distanceTo(ring.center) > COURSE.ringRadius) return null;
    const passed = this.index;
    this.index++;
    if (this.index >= this.rings.length) {
      this.finished = true;
      this.running = false;
      return { type: 'finish', index: passed, time: this.elapsed };
    }
    return { type: 'ring', index: passed };
  }
}

const _tmp = new THREE.Vector3();

export function createRingMeshes(course) {
  const group = new THREE.Group();
  group.name = 'rings';
  const geo = new THREE.TorusGeometry(COURSE.ringRadius, COURSE.ringTube, 10, 40);
  const activeMat = new THREE.MeshBasicMaterial({ color: 0xffb21a });
  const nextMat = new THREE.MeshLambertMaterial({ color: 0xffffff, emissive: 0x335577 });
  const doneMat = new THREE.MeshLambertMaterial({ color: 0x48e070, emissive: 0x0d3a18 });
  // Inner glow disc on the active ring makes it readable from far away.
  const glowMat = new THREE.MeshBasicMaterial({
    color: 0xffd36b,
    transparent: true,
    opacity: 0.18,
    depthWrite: false,
    side: THREE.DoubleSide,
  });
  const glow = new THREE.Mesh(new THREE.CircleGeometry(COURSE.ringRadius - COURSE.ringTube, 32), glowMat);

  const meshes = course.rings.map((ring, i) => {
    const mesh = new THREE.Mesh(geo, nextMat);
    mesh.position.copy(ring.center);
    // Torus lies in the XY plane facing +Z; orient it to face the ring normal.
    mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), ring.normal);
    mesh.name = `ring-${i}`;
    group.add(mesh);
    return mesh;
  });

  // Number labels so players can see the order.
  meshes.forEach((mesh, i) => {
    const label = makeLabel(String(i + 1));
    label.position.set(0, COURSE.ringRadius + 7, 0);
    mesh.add(label);
  });

  function refresh() {
    meshes.forEach((mesh, i) => {
      if (i < course.index) {
        mesh.material = doneMat;
        mesh.visible = i >= course.index - 1; // keep only the last passed ring
      } else if (i === course.index) {
        mesh.material = activeMat;
        mesh.visible = true;
        mesh.add(glow);
      } else {
        mesh.material = nextMat;
        mesh.visible = i <= course.index + 3; // only show a few ahead to reduce clutter
      }
    });
    if (course.finished) glow.removeFromParent();
  }
  refresh();

  return {
    group,
    refresh,
    update(time) {
      const active = meshes[course.index];
      if (active && !course.finished) {
        const pulse = 1 + Math.sin(time * 4) * 0.04;
        active.scale.setScalar(pulse);
        glowMat.opacity = 0.14 + Math.sin(time * 4) * 0.06;
      }
    },
  };
}

function makeLabel(text) {
  const canvas = document.createElement('canvas');
  canvas.width = 128;
  canvas.height = 128;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = 'rgba(10,20,35,0.75)';
  ctx.beginPath();
  ctx.arc(64, 64, 58, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = '#ffffff';
  ctx.font = 'bold 72px system-ui, sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, 64, 68);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthWrite: false }));
  sprite.scale.set(9, 9, 1);
  return sprite;
}
