// Central tuning values for Skyreach VR.
// The Unity port mirrors these numbers in SkyreachConfig.cs, so keep both in sync.

export const WORLD = {
  seed: 1337,
  size: 6000, // metres covered by the playable ocean
  seaLevel: 0,
  fogNear: 600,
  fogFar: 4200,
  islandCount: 9,
  terrainResolution: 2, // vertex spacing multiplier (higher = fewer triangles)
  cloudCount: 70,
};

export const FLIGHT = {
  maxSpeed: 120, // m/s at full throttle, level flight
  boostSpeed: 170, // m/s with afterburner
  minFlySpeed: 38, // below this the jet starts to stall
  takeoffSpeed: 45,
  accel: 0.2, // how quickly airspeed chases its target (per second)
  pitchRate: 1.35, // rad/s at full stick
  rollRate: 2.6,
  yawRate: 0.55,
  controlSmoothing: 7, // higher = snappier stick response
  gravityClimbPenalty: 9.81, // climbing bleeds speed, diving adds it
  stallSinkRate: 14,
  groundFriction: 0.35,
  brakeFriction: 1.6,
  safeTouchdownVSpeed: 7, // m/s descent rate that still counts as a landing
  safeTouchdownRoll: 0.45, // radians
  boostDuration: 4,
  boostRecharge: 0.35, // boost seconds regained per second
};

export const COURSE = {
  ringCount: 12,
  ringRadius: 22,
  ringTube: 1.6,
  ringBonusSeconds: 3,
};

export const COMFORT = {
  vignetteDefault: 0.6, // 0 = off, 1 = strongest
  snapTurnDegrees: 0,
};

export const SPAWN = {
  // Start lined up on the main runway, engines idle.
  runwayLength: 900,
  runwayWidth: 40,
  runwayHeight: 6,
};
