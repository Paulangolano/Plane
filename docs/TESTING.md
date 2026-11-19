# Testing

## WebXR (`cd webxr && npm test`)

**Unit tests** (`tests/unit/`, Node's built-in runner, about 1 s):
- Flight model: parked jet stays still, takeoff only when you pull back, roll/pitch directions, stall, crash in the sea, a gentle approach lands, afterburner drain and recharge.
- World: deterministic generation, flat runway with smooth shoulders, every island above the sea, rings clear the terrain, ring 1 and the last ring face along the runway, and no ring is met edge-on.
- **Autopilot:** flies the real course from a standing start using only player inputs: takeoff, all 12 rings in order, no crash (lap ≈ 100 s).

**Browser tests** (`tests/e2e/`, Playwright + software WebGL, a few minutes):
- Desktop: start screen, Quest 2 performance budget (draw calls and triangles), keyboard takeoff, chase camera, no console errors.
- Course: a real autopilot takeoff through ring 1, rings counted in order, finishing the course saves a best time, crash then restart.
- **VR on an emulated Quest 2** (Meta IWER): Enter VR, stereo rendering, seat calibration, both controllers tracked, thumbstick throttle, grabbing the throttle and stick with the grip buttons, rolling with the grabbed stick, afterburner trigger, comfort vignette button, A-twice restart, re-centre button.

If Playwright isn't installed globally, add it to the project first:

```bash
npm i --no-save playwright
```

```bash
npx playwright install chromium
```

## Unity

- **Inside Unity:** *Window → General → Test Runner → EditMode → Run All* (`Assets/Skyreach/Tests/Editor/`).
- **Without Unity:** `unity/Tests~/run-tests.sh` (needs .NET 8 SDK + Node.js).
  1. Compiles every runtime script against Unity's engine reference assemblies (NuGet `UnityEngine.Modules`), with the Input System code path enabled.
  2. Runs 25 logic tests on the real C# `FlightModel`, `WorldData` and `RaceCourse`, including a **parity check** that islands, 400 terrain heights and all ring positions match the WebXR version, and the full-course autopilot.

## Not covered by automated tests

- How it feels on real hardware: frame rate on an actual Quest 2, comfort, and whether the controls feel natural. Try it and adjust `config.js` / `SkyreachConfig.cs` to taste.
- The Unity editor menu scripts and the Quest build itself (they need the Unity editor and Android toolchain).
