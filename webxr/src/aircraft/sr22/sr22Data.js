// Performance data for the SR22-class single-engine piston aircraft, based on the published
// figures for the Cirrus SR22 G7 (IO-550-N, 310 hp). Everything is SI internally; the
// cockpit displays knots, feet and feet-per-minute like the real avionics.
// The Unity port mirrors these numbers in Sr22Data.cs.

export const KT = 0.514444; // m/s per knot
export const FT = 0.3048; // m per foot
export const FPM = FT / 60; // m/s per foot-per-minute

export const SR22 = {
  mass: 1550, // kg, typical loaded weight (max take-off 1633 kg)
  wingArea: 13.46, // m²
  wingSpan: 11.68, // m
  // Lift
  clAlpha: 5.2, // per radian
  cl0: 0.25, // lift at zero body angle of attack (wing incidence + camber)
  clMax: [1.42, 1.68, 1.94], // flaps UP / 50% / 100%  → stall 70 / 65 / 60 KCAS
  flapCl: [0, 0.3, 0.6],
  // Drag
  cd0: 0.029,
  inducedK: 0.042, // 1 / (π e AR)
  flapCd: [0, 0.012, 0.045],
  propIdleDragCd: 0.009, // windmilling prop at idle
  // Engine + prop
  maxPower: 231000, // W (310 hp)
  staticThrust: 3300, // N
  idleRpm: 700,
  maxRpm: 2700,
  fuelUsableGal: 92,
  // Handling
  pitchGain: 3.2, // how hard the elevator chases its commanded angle of attack (1/s)
  elevatorAlpha: 0.25, // rad of AoA change from full stick
  trimAlpha: [-0.06, 0.16], // trimmed AoA range (full nose-down .. full nose-up trim)
  maxRollRate: 1.0, // rad/s at cruise
  maxYawRate: 0.35, // rad/s rudder authority
  weathervane: 2.2, // directional stability
  dihedral: 0.9, // sideslip → roll
  sideForce: 0.9, // side-force coefficient per rad of sideslip
  refSpeed: 60, // m/s where control authority is "normal"
  // Ground
  gearHeight: 1.45, // m from the eye-point origin down to the wheels (plus model offset)
  rollingFriction: 0.025,
  brakeFriction: 0.45,
  rotateMinSpeed: 25, // m/s before the nose can come up
  tailStrikePitch: 0.21, // rad
  // Limits and V-speeds (knots, as on the airspeed tape)
  vso: 60,
  vs1: 70,
  vr: 72,
  vx: 85,
  vy: 108,
  vfe50: 150,
  vfe100: 110,
  vno: 177,
  vne: 205,
  vpd: 140, // max demonstrated parachute deployment speed
  structuralLimit: 240, // knots: beyond this the airframe fails
  // Landing
  safeSink: 3.0, // m/s (≈ 600 fpm) still a normal landing
  hardSink: 4.5, // m/s beyond this the gear collapses
  // Parachute (whole-airframe recovery system)
  chuteCdA: 335, // m² → about 1,700 fpm descent at this weight
  chuteInflateTime: 3, // s
  chuteMaxDeploySpeed: 165, // knots: above this the canopy fails
  // Flap settings
  flapDetents: [0, 0.5, 1],
  flapRate: 0.12, // fraction of travel per second
};

export function flapIndex(f) {
  return f < 0.25 ? 0 : f < 0.75 ? 1 : 2;
}

/** Interpolate a per-detent table ([UP, 50%, 100%]) at a flap fraction 0..1. */
function atFlaps(table, flaps) {
  const t = Math.min(Math.max(flaps, 0), 1) * 2;
  return t <= 1 ? table[0] + (table[1] - table[0]) * t : table[1] + (table[2] - table[1]) * (t - 1);
}

/** Stall angle of attack (rad) for a flap setting. */
export function stallAlpha(flaps) {
  return (atFlaps(SR22.clMax, flaps) - SR22.cl0 - atFlaps(SR22.flapCl, flaps)) / SR22.clAlpha;
}

/** Lift coefficient for a given angle of attack (rad) and flap fraction (0..1). */
export function liftCoefficient(alpha, flaps) {
  const clMax = atFlaps(SR22.clMax, flaps);
  const base = SR22.cl0 + atFlaps(SR22.flapCl, flaps);
  const aStall = (clMax - base) / SR22.clAlpha;
  const aNegStall = -0.17;
  if (alpha > aStall) {
    // Past the stall lift falls away, then behaves roughly like a flat plate.
    const over = alpha - aStall;
    if (over < 0.3) return Math.max(clMax - over * 2.6, 0.62 * clMax);
    return Math.max(Math.sin(2 * alpha), 0.3);
  }
  if (alpha < aNegStall) {
    const clMin = base + SR22.clAlpha * aNegStall;
    return Math.min(clMin + (aNegStall - alpha) * 2.6, 0.6 * clMin);
  }
  return base + SR22.clAlpha * alpha;
}

export function dragCoefficient(cl, flaps, powerLever) {
  const idleDrag = SR22.propIdleDragCd * Math.max(0, 1 - powerLever * 4);
  return SR22.cd0 + SR22.inducedK * cl * cl + atFlaps(SR22.flapCd, flaps) + idleDrag;
}

/** Air density ratio at altitude (m). */
export function densityRatio(alt) {
  return Math.exp(-Math.max(alt, 0) / 8500);
}
