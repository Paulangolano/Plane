// The single shape every input source writes into. The game and aircraft only read this.

export function createInputState() {
  return {
    pitch: 0, // +1 = nose up (stick pulled back)
    roll: 0, // +1 = roll right
    yaw: 0, // +1 = yaw right
    throttle: 0, // 0..1, persistent between frames
    boost: false,
    brake: false,
    brakeLeft: 0, // differential toe brakes 0..1
    brakeRight: 0,
    trimRate: 0, // trim hat: +1 = nose-up trim
    rollTrimRate: 0,
    // One-frame events: named actions (e.g. 'flapsDown', 'apToggle') plus the classic booleans
    actions: new Set(),
    restart: false,
    cycleComfort: false,
    toggleMute: false,
    toggleView: false,
    recenter: false,
    // Grab / touch feedback for cockpit visuals
    stickGrabbed: false,
    throttleGrabbed: false,
    stickNear: false,
    throttleNear: false,
    capsPull: 0, // metres the parachute handle has been pulled
    hold: null, // { id, progress 0..1 } while a hold-to-activate control is being held
    touching: new Set(), // ids of cockpit buttons a fingertip is touching
  };
}

const CLASSIC = ['restart', 'cycleComfort', 'toggleMute', 'toggleView', 'recenter'];

/** Raise a one-frame action. Classic actions also set their boolean flag. */
export function fire(state, action) {
  state.actions.add(action);
  if (CLASSIC.includes(action)) state[action] = true;
}

export function clearEdges(state) {
  state.actions.clear();
  for (const k of CLASSIC) state[k] = false;
}

export function deadzone(v, dz = 0.12) {
  if (Math.abs(v) < dz) return 0;
  return Math.sign(v) * ((Math.abs(v) - dz) / (1 - dz));
}
