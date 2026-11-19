// The single shape every input source writes into. The game only reads this.

export function createInputState() {
  return {
    pitch: 0, // +1 = nose up (stick pulled back)
    roll: 0, // +1 = roll right
    yaw: 0, // +1 = yaw right
    throttle: 0, // 0..1, persistent between frames
    boost: false,
    brake: false,
    // One-frame "pressed" events
    restart: false,
    cycleComfort: false,
    toggleMute: false,
    toggleView: false,
    recenter: false,
    // Grab feedback for visuals
    stickGrabbed: false,
    throttleGrabbed: false,
    stickNear: false,
    throttleNear: false,
  };
}

export function clearEdges(state) {
  state.restart = false;
  state.cycleComfort = false;
  state.toggleMute = false;
  state.toggleView = false;
  state.recenter = false;
}

export function deadzone(v, dz = 0.12) {
  if (Math.abs(v) < dz) return 0;
  return Math.sign(v) * ((Math.abs(v) - dz) / (1 - dz));
}
