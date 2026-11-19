import * as THREE from 'three';

// Large readable messages ("Ring 3/12", "Crashed!") shown on a panel
// fixed to the cockpit, about 2.5 m in front of the pilot.

export class MessagePanel {
  constructor(parent) {
    this.canvas = document.createElement('canvas');
    this.canvas.width = 1024;
    this.canvas.height = 448;
    this.ctx = this.canvas.getContext('2d');
    this.texture = new THREE.CanvasTexture(this.canvas);
    this.texture.colorSpace = THREE.SRGBColorSpace;
    this.material = new THREE.MeshBasicMaterial({
      map: this.texture,
      transparent: true,
      depthTest: false,
      depthWrite: false,
      toneMapped: false,
    });
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(1.6, 0.7), this.material);
    this.mesh.position.set(0, 0.2, -2.6);
    this.mesh.renderOrder = 10;
    this.mesh.name = 'messages';
    this.mesh.visible = false;
    parent.add(this.mesh);
    this.timer = 0;
    this.current = null;
  }

  /**
   * @param {string} title
   * @param {string} [subtitle]
   * @param {number} [seconds] how long to show it; Infinity keeps it until replaced
   * @param {string} [colour]
   */
  show(title, subtitle = '', seconds = 2.5, colour = '#ffffff') {
    const c = this.ctx;
    c.clearRect(0, 0, 1024, 448);
    c.fillStyle = 'rgba(8, 16, 28, 0.72)';
    c.beginPath();
    c.roundRect(24, 24, 976, 400, 40);
    c.fill();
    c.textAlign = 'center';
    c.fillStyle = colour;
    c.font = 'bold 100px system-ui, sans-serif';
    c.fillText(title, 512, subtitle ? 150 : 260);
    if (subtitle) {
      c.fillStyle = '#cfe3f5';
      c.font = '42px system-ui, sans-serif';
      wrap(c, subtitle, 512, 232, 900, 54);
    }
    this.texture.needsUpdate = true;
    this.mesh.visible = true;
    this.material.opacity = 1;
    this.timer = seconds;
    this.current = title;
  }

  /** Move the panel to another aircraft. */
  setParent(parent) {
    parent.add(this.mesh);
  }

  hide() {
    this.mesh.visible = false;
    this.current = null;
  }

  update(dt) {
    if (!this.mesh.visible || this.timer === Infinity) return;
    this.timer -= dt;
    if (this.timer < 0.5) this.material.opacity = Math.max(0, this.timer / 0.5);
    if (this.timer <= 0) this.hide();
  }
}

function wrap(c, text, x, y, maxWidth, lineHeight) {
  const words = text.split(' ');
  let line = '';
  for (const w of words) {
    const test = line ? `${line} ${w}` : w;
    if (c.measureText(test).width > maxWidth && line) {
      c.fillText(line, x, y);
      line = w;
      y += lineHeight;
    } else line = test;
  }
  c.fillText(line, x, y);
}
