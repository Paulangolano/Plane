# Architecture

Both versions are split the same way: **pure logic** (no rendering, unit-testable) at the bottom, then **builders** that turn that logic into meshes, then a **game loop** that wires input → physics → visuals.

```
            ┌────────────── Game loop (game.js / GameManager.cs) ──────────────┐
 input ───▶ │ InputState ─▶ FlightModel.update ─▶ RaceCourse.check ─▶ HUD/audio │ ──▶ render
            └─────────────────────────────────────────────────────────────────────┘
                 ▲                      ▲                  ▲
          XR controllers /        WorldData.sampleGround   WorldData.rings
          keyboard                (terrain height, runway) (course layout)
```

## Aircraft

Each aircraft lives in its own folder under `webxr/src/aircraft/` and exposes the same small interface to the game (`aircraft/registry.js` lists them):

- `model`: the physics (`update(dt, input, sampleGround)`, position/quaternion, `takeoff`/`landed`/`crash` events)
- `group`: its 3D model, with the pilot's eye at the origin
- `layout`: how Quest controllers map to it. This covers which hand holds the stick/yoke and which holds the throttle/power lever, trigger and button actions, a trim hat, fingertip-pressable panel buttons (`pokeTargets`) and pullable handles (`grabTargets`)
- `keyMap`: keyboard actions
- `flightInput(dt, input)`: turns pilot input into control input (the SR22 inserts the autopilot and Safe Return here)
- `handleAction(action)`: flaps, autopilot buttons, parachute, and so on
- `update(dt, ctx)`: cockpit animation and instruments

The SR22's logic is plain JavaScript with no rendering (`sr22Physics.js`, `autopilot.js`, `safeReturn.js`), so it is unit-tested headlessly against the real aircraft's numbers.

## Module map

| Concern | WebXR (`webxr/src/`) | Unity (`unity/Assets/Skyreach/Scripts/`) |
|---|---|---|
| Tuning values | `config.js` | `Core/SkyreachConfig.cs` |
| Seeded RNG + Perlin noise | `core/noise.js` | `Core/SkyreachNoise.cs` (bit-exact port) |
| Islands, terrain height, ring layout | `world/worldData.js` | `World/WorldData.cs` |
| Flight physics (jet) | `aircraft/jet/flightModel.js` | `Aircraft/FlightModel.cs` |
| Ring crossing + timer | `world/rings.js` (`RaceCourse`) | `World/RaceCourse.cs` |
| Terrain/runway/trees/ocean/clouds | `world/terrain.js`, `ocean.js`, `clouds.js`, `sky.js` | `World/WorldBuilder.cs` + `Shaders/` |
| Jet + cockpit | `aircraft/jet/jetModel.js` | `Aircraft/JetBuilder.cs` |
| Quest controllers | `input/xrInput.js` | `Input/XRRig.cs` |
| Keyboard | `input/keyboardInput.js` | `Input/KeyboardInput.cs` |
| Dashboard HUD | `aircraft/jet/jetHud.js` (canvas texture) | `UI/CockpitHUD.cs` (world-space uGUI) |
| Messages / vignette | `ui/messages.js`, `ui/vignette.js` | `UI/MessagePanel.cs`, `UI/ComfortVignette.cs` |
| Audio | `core/audio.js` (Web Audio) | `Core/GameAudio.cs` (`OnAudioFilterRead`) |
| Main loop | `core/game.js` | `Core/GameManager.cs` |

## Coordinate systems

Three.js is right-handed with the nose pointing **−Z**. Unity is left-handed with the nose pointing **+Z**. `WorldData.cs` runs the same maths as the JS version in "web" coordinates and mirrors Z at its public boundary (`unity z = −web z`). The result is the same archipelago, with takeoff toward +Z in Unity. `FlightModel.cs` is written natively in Unity's frame. The comments note where rotation signs differ (for example, a positive rotation about X pitches the nose *down* in Unity).

## Key design decisions

- **Seated cockpit, world moves around you.** The XR rig is a child of the jet, so the cockpit is a fixed frame of reference. Together with the comfort vignette and smoothed controls, this is what keeps flight comfortable in VR.
- **Automatic seat calibration.** Headsets report head position relative to different origins (eye level or floor). Shortly after entering VR, the rig is moved so your real head sits at the pilot's eye point, facing the nose.
- **Grab maths in cockpit space.** Hand motion is converted into the jet's local frame before becoming stick or throttle deflection, so "pull back" always means toward your chest, whichever way you were facing.
- **Each ring faces the way you arrive at it**, so you never meet one edge-on. Ring 1 points straight off the runway and the last ring is lined up for landing. A test checks every ring is enterable, and an autopilot flies the full course.
- **Quest performance:** merged and instanced meshes, an opaque ocean with lagoon colour baked per vertex, no real-time shadows, fixed foveation, and a HUD that redraws at 15 Hz.
