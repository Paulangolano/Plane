import { FlightModel, GEAR_HEIGHT } from './flightModel.js';
import { createJet, COCKPIT } from './jetModel.js';
import { CockpitHUD } from './jetHud.js';
import { FLIGHT } from '../../config.js';

// The arcade jet: forgiving controls, afterburner, the original Skyreach experience.

const TAKEOFF_KMH = Math.round(FLIGHT.takeoffSpeed * 3.6);

export function createJetAircraft() {
  const model = new FlightModel();
  const visual = createJet();
  const hud = new CockpitHUD(visual.hudAnchor);

  return {
    id: 'jet',
    name: 'Arcade Jet',
    model,
    group: visual.group,
    visual,
    audioProfile: 'jet',
    gearHeight: GEAR_HEIGHT,
    startMessage: ['Skyreach VR', `Grab the throttle with your left hand and push it forward. Pull the stick back at ${TAKEOFF_KMH} km/h.`],
    readyMessage: ['Ready for takeoff', `Throttle up, then pull back at ${TAKEOFF_KMH} km/h.`],
    layout: {
      stick: { hand: 'right', grip: COCKPIT.stickGrip, range: 0.09 },
      throttle: { hand: 'left', grip: COCKPIT.throttleGrip, range: 0.2 },
      triggers: { right: 'boost', left: 'brake' },
      buttons: {
        right: { 4: 'restart', 5: 'cycleComfort' },
        left: { 3: 'recenter', 4: 'cycleComfort', 5: 'toggleMute' },
      },
      pokeTargets: [],
      grabTargets: [],
    },
    keyMap: {},

    attach() {},
    reset() {},
    setCockpitView() {},
    handleAction() {
      return false;
    },
    /** The jet flies exactly what the pilot inputs. */
    flightInput(dt, input) {
      return input;
    },
    /** Message for a touchdown event. */
    landedMessage({ offRunway }, finished) {
      if (finished) return ['Touchdown!', offRunway ? 'Off-runway, but we will take it.' : 'Perfect landing. Press A to fly again.', 6, '#7fe08a'];
      return [offRunway ? 'Rough landing' : 'Landed', 'Throttle up to take off again.', 3];
    },
    update(dt, ctx) {
      visual.setControls(model.pitch, model.roll, model.throttle, model.boosting, ctx.time);
      visual.stickHighlight.visible = ctx.input.stickNear && !ctx.input.stickGrabbed;
      visual.throttleHighlight.visible = ctx.input.throttleNear && !ctx.input.throttleGrabbed;
      hud.update(dt, hudState(model, ctx));
    },
    dispose() {},
  };
}

function hudState(f, ctx) {
  const fwd = f.forward.clone();
  let ringBearing = null;
  let ringDistance = 0;
  const ring = ctx.course.current;
  if (ring && !ctx.course.finished) {
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
    brake: ctx.input.brake,
    bank: f.bank,
    pitchAngle: f.pitchAngle,
    heading: Math.atan2(fwd.x, -fwd.z),
    ringIndex: ctx.course.index,
    elapsed: ctx.course.elapsed,
    ringBearing,
    ringDistance,
  };
}

