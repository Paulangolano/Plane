import { createJetAircraft } from './jet/jetAircraft.js';
import { createSR22Aircraft } from './sr22/sr22Aircraft.js';

// The aircraft you can pick on the start screen.
export const AIRCRAFT = {
  jet: { name: 'Arcade Jet', create: () => createJetAircraft() },
  sr22: { name: 'SR22-class (realistic)', create: (world, settings) => createSR22Aircraft(world, settings) },
};

export function createAircraft(id, world, settings) {
  return (AIRCRAFT[id] ?? AIRCRAFT.jet).create(world, settings);
}
