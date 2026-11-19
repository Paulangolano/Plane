import * as THREE from 'three';
import { WorldData } from '../world/worldData.js';
import { createTerrain } from '../world/terrain.js';
import { createOcean } from '../world/ocean.js';
import { createSky } from '../world/sky.js';
import { createClouds } from '../world/clouds.js';
import { RaceCourse, createRingMeshes } from '../world/rings.js';
import { createInputState, clearEdges } from '../input/inputState.js';
import { XRInput } from '../input/xrInput.js';
import { KeyboardInput } from '../input/keyboardInput.js';
import { MessagePanel } from '../ui/messages.js';
import { ComfortVignette } from '../ui/vignette.js';
import { formatTime } from '../ui/format.js';
import { GameAudio } from './audio.js';
import { COURSE } from '../config.js';
import { loadSettings, saveSettings } from './storage.js';
import { createAircraft, AIRCRAFT } from '../aircraft/registry.js';

// Main game loop: world, the active aircraft, input, race course, messages, audio.
// Aircraft-specific behaviour (physics, cockpit, controls) lives in src/aircraft/*.

export class Game {
  constructor(container) {
    this.container = container;
    this.settings = loadSettings();
    this.timer = new THREE.Timer();
    this.time = 0;
    this.state = 'ready'; // ready | flying | crashed | finished | ended
    this.viewMode = 'cockpit'; // desktop only: cockpit | chase
    this.stats = { frames: 0, fps: 0, acc: 0 };

    this.initRenderer();
    this.initScene();
    this.initPlayer();
    this.initInput();
    this.setAircraft(AIRCRAFT[this.settings.aircraft] ? this.settings.aircraft : 'jet');

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
      this.message.show(...this.aircraft.startMessage, 7);
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
    // The rig is the pilot's seat; the XR camera and controllers live inside it.
    this.rig = new THREE.Group();
    this.rig.name = 'rig';
    this.rig.add(this.camera);
    this.message = new MessagePanel(this.rig);
    this.vignette = new ComfortVignette(this.camera, this.settings.comfortLevel);
    this.audio = new GameAudio();
    this.audio.setMuted(this.settings.muted);
  }

  initInput() {
    this.input = createInputState();
    this.xrInput = new XRInput(this.renderer, this.rig, this.rig);
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

  // ------------------------------------------------------------ aircraft
  /** Swap the active aircraft ('jet' or 'sr22'). */
  setAircraft(id) {
    if (this.aircraft?.id === id) return;
    if (this.aircraft) {
      this.aircraft.dispose();
      this.scene.remove(this.aircraft.group);
    }
    const a = createAircraft(id, this.world, this.settings);
    this.aircraft = a;
    this.settings.aircraft = id;
    saveSettings(this.settings);
    this.scene.add(a.group);
    a.group.add(this.rig);
    this.message.setParent(a.group);
    this.xrInput.setLayout(a.layout, a.group);
    this.keyboard.setKeyMap(a.keyMap);
    a.attach(this.context());
    const m = a.model;
    m.on('takeoff', () => {
      if (this.state === 'ready') {
        this.state = 'flying';
        this.course.start();
        this.message.show('Airborne!', 'Fly through the glowing rings in order.', 3);
      }
    });
    m.on('landed', (e) => {
      if (this.state === 'crashed' || this.state === 'ended') return;
      this.message.show(...a.landedMessage(e, this.state === 'finished'));
    });
    m.on('crash', ({ reason }) => this.onCrash(reason));
    this.setView(this.viewMode);
    this.reset();
  }

  /** Read-only view of the game for aircraft modules. */
  context() {
    return {
      game: this,
      input: this.input,
      course: this.course,
      world: this.world,
      message: this.message,
      audio: this.audio,
      time: this.time,
      saveSettings: () => saveSettings(this.settings),
    };
  }

  // Backwards-compatible handles used by tests and the console.
  get flight() {
    return this.aircraft.model;
  }

  get jet() {
    return this.aircraft;
  }

  // ------------------------------------------------------------ game flow
  reset() {
    const a = this.aircraft;
    const spawn = new THREE.Vector3(0, this.world.runway.height + a.gearHeight, this.world.runway.halfLength - 40);
    a.model.reset(spawn, 0);
    a.reset();
    this.input.throttle = 0;
    this.course.reset();
    this.ringVisuals.refresh();
    this.state = 'ready';
    this.crashTimer = 0;
    this.explosion.reset();
    a.group.visible = true;
    this.syncAircraft();
    this.prevPos.copy(a.model.position);
    this.prevQuat.copy(a.model.quaternion);
    this.message.show(...a.readyMessage, 6);
  }

  onCrash(reason) {
    this.state = 'crashed';
    this.crashTimer = 0;
    this.course.running = false;
    this.explosion.trigger(this.flight.position);
    this.audio.crash();
    this.xrInput.pulse('left', 1, 400);
    this.xrInput.pulse('right', 1, 400);
    const how = this.aircraft.id === 'sr22' ? 'Hold Y (or Enter) to restart.' : 'Press A (or Enter) to restart.';
    this.message.show('Crashed!', `${reason} ${how}`, Infinity, '#ff7a5a');
  }

  onRing(event) {
    this.ringVisuals.refresh();
    this.xrInput.pulse('right', 0.6, 80);
    if (event.type === 'finish') {
      this.state = 'finished';
      this.audio.chime(true);
      const key = this.aircraft.id === 'jet' ? 'bestTime' : `bestTime_${this.aircraft.id}`;
      const best = this.settings[key];
      const isBest = !best || event.time < best;
      if (isBest) {
        this.settings[key] = event.time;
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
    const chase = mode === 'chase' && !this.renderer.xr.isPresenting;
    if (chase) {
      this.rig.position.set(this.aircraft?.id === 'sr22' ? 0.3 : 0, 4.5, 20);
      this.rig.rotation.set(-0.12, 0, 0);
    } else {
      this.rig.position.set(0, 0, 0);
      this.rig.rotation.set(0, 0, 0);
    }
    this.aircraft?.setCockpitView(!chase);
  }

  // ----------------------------------------------------------------- loop
  tick(timestamp) {
    this.timer.update(timestamp);
    const rawDt = this.timer.getDelta();
    const dt = Math.min(rawDt, 0.05);
    this.time += dt;
    this.updateFps(rawDt);
    const a = this.aircraft;
    const m = a.model;

    const presenting = this.renderer.xr.isPresenting;
    if (presenting && this.calibrateIn > 0) {
      this.calibrateIn -= dt;
      if (this.calibrateIn <= 0) this.calibrateSeat();
    }
    if (presenting) this.xrInput.update(dt, this.input);
    else this.keyboard.update(dt, this.input);
    this.handleButtons();

    if (this.state !== 'crashed') {
      this.prevPos.copy(m.position);
      this.prevQuat.copy(m.quaternion);
      const flightInput = a.flightInput(dt, this.input);
      m.update(dt, flightInput, (x, z) => this.world.sampleGround(x, z));
      if (this.state === 'flying' || this.state === 'finished') {
        this.course.tick(dt);
        const ev = this.course.check(this.prevPos, m.position);
        if (ev) this.onRing(ev);
      }
    } else {
      this.crashTimer += dt;
    }

    this.syncAircraft();
    if (!presenting && this.viewMode === 'cockpit') {
      this.camera.rotation.set(this.look.pitch, this.look.yaw, 0, 'YXZ');
    }

    // Visual updates
    const angSpeed = (2 * Math.acos(Math.min(1, Math.abs(this.prevQuat.dot(m.quaternion))))) / Math.max(dt, 1e-4);
    this.vignette.update(dt, this.state === 'crashed' ? 0 : angSpeed);
    const ctx = this.context();
    a.update(dt, ctx);
    this.ocean.update(this.time);
    this.sky.follow(m.position);
    this.ringVisuals.update(this.time);
    this.explosion.update(dt);
    this.message.update(dt);
    this.audio.update({
      throttle: m.throttle,
      speed: m.speed,
      boosting: m.boosting,
      crashed: this.state === 'crashed',
      profile: a.audioProfile,
      rpm: m.rpm,
    });

    clearEdges(this.input);
    this.renderer.render(this.scene, this.camera);
  }

  handleButtons() {
    const i = this.input;
    const m = this.flight;
    if (i.restart && (this.state !== 'flying' || m.onGround)) this.reset();
    else if (i.restart && this.state === 'flying') {
      // Restarting mid-air needs a second press within 2 s to avoid accidents.
      if (this.time - (this.lastRestartPress ?? -10) < 2) this.reset();
      else this.message.show('Restart?', this.aircraft.id === 'sr22' ? 'Hold again to go back to the runway.' : 'Press A again to go back to the runway.', 2);
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
    for (const action of i.actions) {
      if (this.state !== 'crashed') this.aircraft.handleAction(action, this.context());
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

  syncAircraft() {
    const g = this.aircraft.group;
    g.position.copy(this.flight.position);
    g.quaternion.copy(this.flight.quaternion);
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
