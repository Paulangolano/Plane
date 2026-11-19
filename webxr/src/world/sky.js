import * as THREE from 'three';

// Gradient sky dome with a soft sun, plus the scene lights and fog that match it.

export const SKY_COLORS = {
  zenith: new THREE.Color(0x2f7fd8),
  horizon: new THREE.Color(0xbfe3f7),
  sun: new THREE.Color(0xfff2d0),
};

export function createSky(scene) {
  const sunDir = new THREE.Vector3(0.45, 0.55, -0.7).normalize();

  const material = new THREE.ShaderMaterial({
    side: THREE.BackSide,
    depthWrite: false,
    uniforms: {
      uZenith: { value: SKY_COLORS.zenith },
      uHorizon: { value: SKY_COLORS.horizon },
      uSun: { value: SKY_COLORS.sun },
      uSunDir: { value: sunDir },
    },
    vertexShader: /* glsl */ `
      varying vec3 vDir;
      void main() {
        vDir = normalize(position);
        vec4 p = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        gl_Position = p.xyww; // always at the far plane
      }
    `,
    fragmentShader: /* glsl */ `
      uniform vec3 uZenith;
      uniform vec3 uHorizon;
      uniform vec3 uSun;
      uniform vec3 uSunDir;
      varying vec3 vDir;
      void main() {
        vec3 d = normalize(vDir);
        float t = pow(clamp(d.y, 0.0, 1.0), 0.45);
        vec3 col = mix(uHorizon, uZenith, t);
        float s = max(dot(d, uSunDir), 0.0);
        col += uSun * (pow(s, 900.0) * 3.0 + pow(s, 12.0) * 0.25);
        if (d.y < 0.0) col = uHorizon;
        gl_FragColor = vec4(col, 1.0);
        #include <colorspace_fragment>
      }
    `,
  });
  const dome = new THREE.Mesh(new THREE.SphereGeometry(1, 32, 16), material);
  dome.scale.setScalar(50000);
  dome.frustumCulled = false;
  dome.renderOrder = -1;
  dome.name = 'sky';
  scene.add(dome);

  scene.fog = new THREE.Fog(SKY_COLORS.horizon.clone(), 600, 4200);
  scene.background = SKY_COLORS.horizon.clone();

  const hemi = new THREE.HemisphereLight(0xcfe8ff, 0x5a6b4a, 1.15);
  scene.add(hemi);
  const sun = new THREE.DirectionalLight(0xfff1dc, 2.1);
  sun.position.copy(sunDir).multiplyScalar(1000);
  scene.add(sun);

  return {
    dome,
    sunDir,
    /** Keep the dome centred on the player. */
    follow(position) {
      dome.position.copy(position);
    },
  };
}
