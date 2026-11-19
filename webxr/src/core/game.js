import * as THREE from 'three';
import { FlightModel, GEAR_HEIGHT } from '../aircraft/flightModel.js';
import { createJet } from '../aircraft/jet.js';
import { WorldData } from '../world/worldData.js';
import { createTerrain } from '../world/terrain.js';
import { createOcean } from '../world/ocean.js';
import { createSky } from '../world/sky.js';
import { createClouds } from '../world/clouds.js';
import { RaceCourse, createRingMeshes } from '../world/rings.js';
import { createInputState, clearEdges } from '../input/inputState.js';
import { XRInput } from '../input/xrInput.js';
import { KeyboardInput } from '../input/keyboardInput.js';
import { CockpitHUD, formatTime } from '../ui/hud.js';
import { MessagePanel } from '../ui/messages.js';
import { ComfortVignette } from '../ui/vignette.js';
import { GameAudio } from './audio.js';
import { FLIGHT, COURSE } from '../config.js';
import { loadSettings, saveSettings } from './storage.js';

const TAKEOFF_KMH = Math.round(FLIGHT.takeoffSpeed * 3.6);

export class Game {
  constructor(container) {
    this.container = container;
    this.settings = loadSettings();
    this.timer = new THREE.Timer();
    this.time = 0;
    this.state = 'ready'; // ready | flying | crashed | finished
    this.viewMode = 'cockpit'; // desktop only: cockpit | chase
    this.stats = { frames: 0, fps: 0, acc: 0 };

    this.initRenderer();
    this.initScene();
    this.initPlayer();
    this.initInput();
    this.reset();

    window.addEventListener('resize', () => this.onResize());
    this.renderer.setAnimationLoop((t) => this.tick(t));
  }

  // ---------------------------------------------------------------- setup
  initRenderer() {
    const r = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
    r.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    r.setSize(window.innerWidth, window.innerHeight);
    r.outputColorSpace = THREE.SRGBColorSpace;
    r.toneMapping = THREE.ACESFilmicToneMapping;
    r.toneMappingExposure = 1.15;
    r.xr.enabled = true;
    r.xr.setReferenceSpaceType('local'); // seated: origin = your head when you entered VR
    r.xr.setFoveation(1); // fixed foveated rendering = big performance win on Quest
    r.domElement.id = 'game-canvas';
    this.container.appendChild(r.domElement);
    this.renderer = r;

    r.xr.addEventListener('sessionstart', () => {
      this.audio.start();
      this.message.show('Skyreach VR', `Grab the throttle with your left hand and push it forward. Pull the stick back at ${TAKEOFF_KMH} km/h.`, 7);
      this.camera.position.set(0, 0, 0);
      this.camera.rotation.set(0, 0, 0);
      this.setView('cockpit');
      this.calibrateIn = 0.6; // seconds: wait for stable head tracking, then seat the player
    });
    r.xr.addEventListener('sessionend', () => {
      this.rig.position.set(0, 0, 0);
      this.rig.rotation.set(0, 0, 0);
      this.camera.position.set(0, 0, 0);
      this.camera.rotation.set(0, 0, 0);
    });
  }

  initScene() {
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(72, window.innerWidth / window.innerHeight, 0.05, 60000);
    this.world = new WorldData();
    this.sky = createSky(this.scene);
    this.scene.fog.near = 600;
    this.terrain = createTerrain(this.world);
    this.scene.add(this.terrain);
    this.ocean = createOcean(this.world, this.sky.sunDir);
    this.scene.add(this.ocean.mesh);
    this.scene.add(createClouds());
    this.course = new RaceCourse(this.world.rings);
    this.ringVisuals = createRingMeshes(this.course);
    this.scene.add(this.ringVisuals.group);
    this.explosion = createExplosion();
    this.scene.add(this.explosion.mesh);
  }

  initPlayer() {
    this.flight = new FlightModel();
    this.jet = createJet();
    this.scene.add(this.jet.group);
    // The rig is the pilot's seat; the XR camera and controllers live inside it.
    this.rig = new THREE.Group();
    this.rig.name = 'rig';
    this.jet.group.add(this.rig);
    this.rig.add(this.camera);
    this.hud = new CockpitHUD(this.jet.hudAnchor);
    this.message = new MessagePanel(this.jet.group);
    this.vignette = new ComfortVignette(this.camera, this.settings.comfortLevel);
    this.audio = new GameAudio();
    this.audio.setMuted(this.settings.muted);

    this.flight.on('takeoff', () => {
      if (this.state === 'ready') {
        this.state = 'flying';
        this.course.start();
        this.message.show('Airborne!', 'Fly through the glowing rings in order.', 3);
      }
    });
    this.flight.on('landed', ({ offRunway }) => {
      if (this.state === 'finished') {
        this.message.show('Touchdown!', offRunway ? 'Off-runway, but we will take it.' : 'Perfect landing. Press A to fly again.', 6, '#7fe08a');
      } else {
        this.message.show(offRunway ? 'Rough landing' : 'Landed', 'Throttle up to take off again.', 3);
      }
    });
    this.flight.on('crash', ({ reason }) => this.onCrash(reason));
  }

  initInput() {
    this.input = createInputState();
    this.xrInput = new XRInput(this.renderer, this.rig, this.jet.group);
    this.keyboard = new KeyboardInput();
    this.prevPos = new THREE.Vector3();
    this.prevQuat = new THREE.Quaternion();

    // Desktop mouse-look (drag) for looking around the cockpit.
    this.look = { yaw: 0, pitch: -0.14, dragging: false };
    const el = this.renderer.domElement;
    el.addEventListener('pointerdown', (e) => {
      this.look.dragging = true;
      this.look.x = e.clientX;
      this.look.y = e.clientY;
      this.audio.start();
    });
    window.addEventListener('pointerup', () => (this.look.dragging = false));
    window.addEventListener('pointermove', (e) => {
      if (!this.look.dragging || this.renderer.xr.isPresenting) return;
      this.look.yaw -= (e.clientX - this.look.x) * 0.004;
      this.look.pitch -= (e.clientY - this.look.y) * 0.004;
      this.look.pitch = THREE.MathUtils.clamp(this.look.pitch, -1.2, 1.2);
      this.look.yaw = THREE.MathUtils.clamp(this.look.yaw, -2.2, 2.2);
      this.look.x = e.clientX;
      this.look.y = e.clientY;
    });
  }

  // ------------------------------------------------------------ game flow
  reset() {
    const spawn = new THREE.Vector3(0, this.world.runway.height + GEAR_HEIGHT, this.world.runway.halfLength - 40);
    this.flight.reset(spawn, 0);
    this.input.throttle = 0;
    this.course.reset();
    this.ringVisuals.refresh();
    this.state = 'ready';
    this.crashTimer = 0;
    this.explosion.reset();
    this.jet.group.visible = true;
    this.syncJet();
    this.prevPos.copy(this.flight.position);
    this.prevQuat.copy(this.flight.quaternion);
    if (this.message) {
      this.message.show('Ready for takeoff', `Throttle up, then pull back at ${TAKEOFF_KMH} km/h.`, 6);
    }
  }

  onCrash(reason) {
    this.state = 'crashed';
    this.crashTimer = 0;
    this.course.running = false;
    this.explosion.trigger(this.flight.position);
    this.audio.crash();
    this.xrInput.pulse('left', 1, 400);
    this.xrInput.pulse('right', 1, 400);
    this.message.show('Crashed!', `${reason} Press A (or Enter) to restart.`, Infinity, '#ff7a5a');
  }

  onRing(event) {
    this.ringVisuals.refresh();
    this.xrInput.pulse('right', 0.6, 80);
    if (event.type === 'finish') {
      this.state = 'finished';
      this.audio.chime(true);
      const best = this.settings.bestTime;
      const isBest = !best || event.time < best;
      if (isBest) {
        this.settings.bestTime = event.time;
        saveSettings(this.settings);
      }
      this.message.show(
        isBest ? 'New record!' : 'Course complete!',
        `Time ${formatTime(event.time)}${!isBest ? `  (best ${formatTime(best)})` : ''}. Now land on the runway!`,
        8,
        '#ffd23a',
      );
    } else {
      this.audio.chime(false);
      this.message.show(`Ring ${event.index + 1} / ${COURSE.ringCount}`, '', 1.5, '#ffd23a');
    }
  }

  setView(mode) {
    this.viewMode = mode;
    if (mode === 'chase' && !this.renderer.xr.isPresenting) {
      this.rig.position.set(0, 4.5, 20);
      this.rig.rotation.set(-0.12, 0, 0);
    } else {
      this.rig.position.set(0, 0, 0);
      this.rig.rotation.set(0, 0, 0);
    }
  }

  // ----------------------------------------------------------------- loop
  tick(timestamp) {
    this.timer.update(timestamp);
    const dt = Math.min(this.timer.getDelta(), 0.05);
    this.time += dt;
    this.updateFps(dt);

    const presenting = this.renderer.xr.isPresenting;
    if (presenting && this.calibrateIn > 0) {
      this.calibrateIn -= dt;
      if (this.calibrateIn <= 0) this.calibrateSeat();
    }
    if (presenting) this.xrInput.update(dt, this.input);
    else this.keyboard.update(dt, this.input);
    this.handleButtons();

    if (this.state !== 'crashed') {
      this.prevPos.copy(this.flight.position);
      this.prevQuat.copy(this.flight.quaternion);
      this.flight.update(dt, this.input, (x, z) => this.world.sampleGround(x, z));
      if (this.state === 'flying' || this.state === 'finished') {
        this.course.tick(dt);
        const ev = this.course.check(this.prevPos, this.flight.position);
        if (ev) this.onRing(ev);
      }
    } else {
      this.crashTimer += dt;
    }

    this.syncJet();
    if (!presenting && this.viewMode === 'cockpit') {
      this.camera.rotation.set(this.look.pitch, this.look.yaw, 0, 'YXZ');
    }

    // Visual updates
    const angSpeed = (2 * Math.acos(Math.min(1, Math.abs(this.prevQuat.dot(this.flight.quaternion))))) / Math.max(dt, 1e-4);
    this.vignette.update(dt, this.state === 'crashed' ? 0 : angSpeed);
    this.jet.setControls(this.flight.pitch, this.flight.roll, this.flight.throttle, this.flight.boosting, this.time);
    this.jet.stickHighlight.visible = this.input.stickNear && !this.input.stickGrabbed;
    this.jet.throttleHighlight.visible = this.input.throttleNear && !this.input.throttleGrabbed;
    this.ocean.update(this.time);
    this.sky.follow(this.flight.position);
    this.ringVisuals.update(this.time);
    this.explosion.update(dt);
    this.message.update(dt);
    this.hud.update(dt, this.hudState());
    this.audio.update({
      throttle: this.flight.throttle,
      speed: this.flight.speed,
      boosting: this.flight.boosting,
      crashed: this.state === 'crashed',
    });

    clearEdges(this.input);
    this.renderer.render(this.scene, this.camera);
  }

  handleButtons() {
    const i = this.input;
    if (i.restart && (this.state !== 'flying' || this.flight.onGround)) this.reset();
    else if (i.restart && this.state === 'flying') {
      // Restarting mid-air needs a second press within 2 s to avoid accidents.
      if (this.time - (this.lastRestartPress ?? -10) < 2) this.reset();
      else this.message.show('Restart?', 'Press A again to go back to the runway.', 2);
      this.lastRestartPress = this.time;
    }
    if (i.cycleComfort) {
      const level = this.vignette.cycle();
      this.settings.comfortLevel = this.vignette.levelIndex;
      saveSettings(this.settings);
      this.message.show('Comfort vignette', level.name, 1.5);
    }
    if (i.toggleMute) {
      this.settings.muted = !this.settings.muted;
      this.audio.setMuted(this.settings.muted);
      saveSettings(this.settings);
      this.message.show(this.settings.muted ? 'Sound off' : 'Sound on', '', 1.2);
    }
    if (i.recenter && this.renderer.xr.isPresenting) {
      this.calibrateSeat();
      this.message.show('Seat re-centred', '', 1.2);
    }
    if (i.toggleView && !this.renderer.xr.isPresenting) {
      this.setView(this.viewMode === 'cockpit' ? 'chase' : 'cockpit');
    }
  }

  /**
   * Move the rig so the player's real head sits at the pilot's eye point, facing the nose.
   * Works whether the headset's origin is at eye height (seated) or on the floor (standing).
   */
  calibrateSeat() {
    const head = this.camera.position; // head pose relative to the rig (XR reference space)
    const e = new THREE.Euler().setFromQuaternion(this.camera.quaternion, 'YXZ');
    this.rig.rotation.set(0, -e.y, 0);
    this.rig.position.copy(head).applyAxisAngle(THREE.Object3D.DEFAULT_UP, -e.y).negate();
  }

  syncJet() {
    this.jet.group.position.copy(this.flight.position);
    this.jet.group.quaternion.copy(this.flight.quaternion);
  }

  hudState() {
    const f = this.flight;
    const fwd = f.forward.clone();
    let ringBearing = null;
    let ringDistance = 0;
    const ring = this.course.current;
    if (ring && !this.course.finished) {
      const local = ring.center.clone().sub(f.position);
      ringDistance = local.length();
      local.applyQuaternion(f.quaternion.clone().invert());
      ringBearing = Math.atan2(local.x, -local.z);
    }
    return {
      speed: f.speed,
      altitude: f.altitude,
      verticalSpeed: f.velocity.y,
      throttle: f.throttle,
      boostFuel: f.boostFuel,
      boosting: f.boosting,
      stalling: f.stalling,
      onGround: f.onGround,
      brake: this.input.brake,
      bank: f.bank,
      pitchAngle: f.pitchAngle,
      heading: Math.atan2(fwd.x, -fwd.z),
      ringIndex: this.course.index,
      elapsed: this.course.elapsed,
      ringBearing,
      ringDistance,
    };
  }

  updateFps(dt) {
    const s = this.stats;
    s.frames++;
    s.acc += dt;
    if (s.acc >= 1) {
      s.fps = s.frames / s.acc;
      s.frames = 0;
      s.acc = 0;
    }
  }

  onResize() {
    if (this.renderer.xr.isPresenting) return;
    this.camera.aspect = window.innerWidth / window.innerHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(window.innerWidth, window.innerHeight);
  }
}

function createExplosion() {
  const mat = new THREE.MeshBasicMaterial({ color: 0xffb040, transparent: true, depthWrite: false });
  const mesh = new THREE.Mesh(new THREE.IcosahedronGeometry(1, 2), mat);
  mesh.visible = false;
  mesh.name = 'explosion';
  let t = 0;
  return {
    mesh,
    trigger(pos) {
      mesh.position.copy(pos);
      mesh.visible = true;
      t = 0;
    },
    reset() {
      mesh.visible = false;
    },
    update(dt) {
      if (!mesh.visible) return;
      t += dt;
      mesh.scale.setScalar(2 + t * 30);
      mat.opacity = Math.max(0, 0.9 - t * 0.6);
      mat.color.setHSL(0.08 - t * 0.03, 1, 0.6 - t * 0.2);
      if (t > 1.6) mesh.visible = false;
    },
  };
}
