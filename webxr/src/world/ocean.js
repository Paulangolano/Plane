import * as THREE from 'three';
import { WORLD } from '../config.js';

// Opaque animated ocean. Turquoise lagoons are baked per vertex from the terrain
// depth, so there is no costly transparency on Quest.

const vertexShader = /* glsl */ `
  #include <common>
  #include <fog_pars_vertex>
  uniform float uTime;
  attribute float aShallow;
  varying float vShallow;
  varying vec3 vWorldPos;
  varying vec3 vNormal;
  void main() {
    vec3 p = position;
    float w1 = sin(p.x * 0.021 + uTime * 0.9) * cos(p.z * 0.017 + uTime * 0.7);
    float w2 = sin((p.x + p.z) * 0.043 + uTime * 1.6);
    p.y += (w1 * 1.1 + w2 * 0.35) * (1.0 - aShallow * 0.7);
    float dx = cos(p.x * 0.021 + uTime * 0.9) * 0.021 * 1.1 + cos((p.x + p.z) * 0.043 + uTime * 1.6) * 0.043 * 0.35;
    float dz = -sin(p.z * 0.017 + uTime * 0.7) * 0.017 * 1.1 + cos((p.x + p.z) * 0.043 + uTime * 1.6) * 0.043 * 0.35;
    vNormal = normalize(vec3(-dx * 2.0, 1.0, -dz * 2.0));
    vShallow = aShallow;
    vec4 world = modelMatrix * vec4(p, 1.0);
    vWorldPos = world.xyz;
    vec4 mvPosition = viewMatrix * world;
    gl_Position = projectionMatrix * mvPosition;
    #include <fog_vertex>
  }
`;

const fragmentShader = /* glsl */ `
  #include <common>
  #include <fog_pars_fragment>
  uniform vec3 uDeep;
  uniform vec3 uShallow;
  uniform vec3 uSky;
  uniform vec3 uSunDir;
  uniform float uTime;
  varying float vShallow;
  varying vec3 vWorldPos;
  varying vec3 vNormal;
  void main() {
    vec3 viewDir = normalize(cameraPosition - vWorldPos);
    vec3 n = normalize(vNormal);
    float fresnel = pow(1.0 - max(dot(viewDir, n), 0.0), 4.0);
    vec3 base = mix(uDeep, uShallow, vShallow);
    // Foam where the water meets the beach.
    float foam = smoothstep(0.82, 1.0, vShallow) * (0.6 + 0.4 * sin(uTime * 2.0 + vWorldPos.x * 0.15 + vWorldPos.z * 0.11));
    vec3 col = mix(base, uSky, fresnel * 0.65);
    col = mix(col, vec3(1.0), foam * 0.45);
    vec3 h = normalize(uSunDir + viewDir);
    float spec = pow(max(dot(n, h), 0.0), 180.0);
    col += vec3(1.0, 0.95, 0.85) * spec * 1.4;
    gl_FragColor = vec4(col, 1.0);
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
    #include <fog_fragment>
  }
`;

export function createOcean(world, sunDir) {
  const size = WORLD.size * 2.2;
  const segments = 150;
  const geo = new THREE.PlaneGeometry(size, size, segments, segments);
  geo.rotateX(-Math.PI / 2);
  const pos = geo.attributes.position;
  const shallow = new Float32Array(pos.count);
  for (let i = 0; i < pos.count; i++) {
    const h = world.heightAt(pos.getX(i), pos.getZ(i));
    // 0 = deep ocean, 1 = right at the shoreline.
    shallow[i] = THREE.MathUtils.clamp(1 + h / 30, 0, 1);
  }
  geo.setAttribute('aShallow', new THREE.BufferAttribute(shallow, 1));

  const material = new THREE.ShaderMaterial({
    vertexShader,
    fragmentShader,
    fog: true,
    uniforms: THREE.UniformsUtils.merge([
      THREE.UniformsLib.fog,
      {
        uTime: { value: 0 },
        uDeep: { value: new THREE.Color(0x0b4f7c) },
        uShallow: { value: new THREE.Color(0x35c9c0) },
        uSky: { value: new THREE.Color(0xa9d6f5) },
        uSunDir: { value: sunDir.clone() },
      },
    ]),
  });
  const mesh = new THREE.Mesh(geo, material);
  mesh.name = 'ocean';
  mesh.position.y = WORLD.seaLevel;
  mesh.frustumCulled = false;

  // A huge flat skirt beyond the detailed ocean so the horizon never shows an edge.
  const skirt = new THREE.Mesh(
    new THREE.RingGeometry(size * 0.49, 60000, 48, 1),
    new THREE.MeshBasicMaterial({ color: 0x0b4f7c, fog: true }),
  );
  skirt.rotation.x = -Math.PI / 2;
  skirt.position.y = -0.5;
  mesh.add(skirt);

  return {
    mesh,
    update(time) {
      material.uniforms.uTime.value = time;
    },
  };
}
