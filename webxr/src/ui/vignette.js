import * as THREE from 'three';

// Comfort vignette: gently darkens your peripheral vision during fast turns,
// which greatly reduces motion sickness in VR. Attached to the head (camera).

export const COMFORT_LEVELS = [
  { name: 'Off', strength: 0 },
  { name: 'Light', strength: 0.35 },
  { name: 'Medium', strength: 0.6 },
  { name: 'Strong', strength: 0.9 },
];

export class ComfortVignette {
  constructor(camera, levelIndex = 2) {
    this.levelIndex = levelIndex;
    this.amount = 0;
    this.material = new THREE.ShaderMaterial({
      transparent: true,
      depthTest: false,
      depthWrite: false,
      uniforms: { uAmount: { value: 0 } },
      vertexShader: /* glsl */ `
        varying vec2 vUv;
        void main() {
          vUv = uv;
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }
      `,
      fragmentShader: /* glsl */ `
        uniform float uAmount;
        varying vec2 vUv;
        void main() {
          float d = length(vUv - 0.5) * 2.0;
          float inner = mix(1.2, 0.45, uAmount);
          float a = smoothstep(inner, inner + 0.35, d) * uAmount * 1.4;
          gl_FragColor = vec4(0.0, 0.0, 0.0, clamp(a, 0.0, 0.95));
        }
      `,
    });
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), this.material);
    this.mesh.position.z = -0.12;
    this.mesh.scale.setScalar(0.42);
    this.mesh.renderOrder = 1000;
    this.mesh.frustumCulled = false;
    this.mesh.name = 'vignette';
    camera.add(this.mesh);
  }

  get level() {
    return COMFORT_LEVELS[this.levelIndex];
  }

  cycle() {
    this.levelIndex = (this.levelIndex + 1) % COMFORT_LEVELS.length;
    return this.level;
  }

  /** @param {number} angularSpeed rad/s of the aircraft */
  update(dt, angularSpeed) {
    const target = THREE.MathUtils.clamp((angularSpeed - 0.35) / 1.6, 0, 1) * this.level.strength;
    this.amount += (target - this.amount) * Math.min(1, dt * 5);
    this.material.uniforms.uAmount.value = this.amount;
    this.mesh.visible = this.amount > 0.01;
  }
}
