# Skyreach VR: Unity version (native Quest app)

A Unity 6 port of the WebXR game with the same physics, world, course and both aircraft: the **Arcade Jet** and the realistic **SR22-class** (side yoke, power lever, flaps, trim, toe brakes, glass cockpit, autopilot, parachute and Safe Return emergency autoland). It runs as a native Quest app through **OpenXR**, using the **Built-in Render Pipeline** with custom single-pass-stereo shaders.

Everything (islands, ocean, jet, cockpit, HUD, sounds) is generated in code at startup. There are no models, textures or audio files to import, and the scene contains one GameObject.

## 1. Install the tools (one time)

1. Install [Unity Hub](https://unity.com/download).
2. In Unity Hub → **Installs → Install Editor**, choose **Unity 6 LTS** (6000.0 or newer). Tick **Android Build Support**, including **OpenJDK** and **Android SDK & NDK Tools**.
3. On your Quest, enable **Developer Mode**: create or join a developer organization at <https://developer.oculus.com/manage/organizations/create/>, then in the Meta Horizon phone app go to *Devices → your headset → Headset settings → Developer mode → On*.

## 2. Open the project

1. Unity Hub → **Projects → Add → Add project from disk** → select this `unity/` folder.
2. Open it with Unity 6. The first import takes a few minutes. Unity creates the `Packages/` and `ProjectSettings/` folders itself.

## 3. Set up for Quest with the **Skyreach** menu

Run these in order from the **Skyreach** menu at the top of the Unity window:

| Menu item | What it does |
|---|---|
| **1. Install Quest Packages** | Adds *XR Plug-in Management* and *OpenXR*. If Unity asks to enable the new Input System and restart, click **Yes**. |
| **2. Configure for Meta Quest** | Switches to Android; sets IL2CPP + ARM64, Vulkan, ASTC, Linear colour, min API 32, 4× MSAA; enables OpenXR with the *Meta Quest* feature and *Oculus Touch* controller profile, single-pass instanced. |
| **3. Create Game Scene** | Creates `Assets/Skyreach/Scenes/Skyreach.unity` and adds it to the build. |

Press **Play** to fly in the editor with the keyboard: W/S pitch, A/D roll, Shift/Ctrl throttle, V chase camera, right-mouse-drag to look around. Press **1** for the Arcade Jet or **2** for the SR22.

SR22 keys: `[` / `]` flaps, T/G trim, B brakes, `,` / `.` left/right brake, P parking brake, Z autopilot, H HDG, K ALT, U VS, L LVL, `-` / `=` heading bug, PgUp/PgDn altitude, hold 9 for the parachute, hold 0 for Safe Return, O assists.

**Choosing the aircraft in VR:** while parked on the runway, a small **AIRCRAFT** panel floats ahead and to the right. Touch **ARCADE JET** or **SR22** with a fingertip. The choice is remembered.

> If step 2 reports that OpenXR could not be configured automatically, do it by hand: **Edit → Project Settings → XR Plug-in Management → Android tab**, tick **OpenXR** and **Meta Quest feature group**. Then under **OpenXR → Interaction Profiles**, add **Oculus Touch Controller Profile**. Run step 2 again afterwards.

## 4. Put it on your Quest

**Option A, one click:** plug the Quest in by USB-C, put the headset on and allow USB debugging, then choose **Skyreach → 5. Build And Run on Quest (USB)**.

**Option B, APK file:** choose **Skyreach → 4. Build APK**. That creates `Builds/SkyreachVR.apk`. Install it with [SideQuest](https://sidequestvr.com) (drag the APK onto it) or with `adb install -r Builds/SkyreachVR.apk`.

On the headset, the app is in **Library → Unknown Sources → Skyreach VR**. Launch it while seated. Your seat calibrates automatically after a moment, and you can click the left thumbstick to re-centre at any time.

## Project layout

```
Assets/Skyreach/
├── Scripts/                     (assembly: Skyreach.Runtime)
│   ├── Core/        GameManager (main loop), config, seeded noise, synth audio, settings
│   ├── Aircraft/    Aircraft (shared interface), JetAircraft, FlightModel + JetBuilder (arcade jet)
│   │   └── Sr22/    Sr22Data, Sr22Physics, Sr22Autopilot, Sr22SafeReturn (pure C#, tested),
│   │                Sr22Builder (cockpit + exterior), Sr22Avionics (PFD + MFD), Sr22Aircraft (glue)
│   ├── World/       WorldData (islands/terrain/course), WorldBuilder, RaceCourse, RingVisuals
│   ├── Input/       XRRig (head + Touch controllers via OpenXR), ControlLayout, KeyboardInput, InputState
│   ├── UI/          CockpitHUD, MessagePanel, ComfortVignette, UIFactory
│   └── Rendering/   MeshFactory (procedural meshes), SkyreachMaterials
├── Shaders/         Lit, Ocean, Sky, Unlit, Vignette (all single-pass instanced stereo)
├── Editor/          Skyreach menu (setup + build); Editor/XR auto-configures OpenXR
├── Tests/Editor/    Unity Test Runner edit-mode tests
└── Scenes/          created by "Create Game Scene"
Tests~/              checks that run without Unity (ignored by Unity's importer)
```

## Tests

- **In Unity:** *Window → General → Test Runner → EditMode → Run All*. These test the jet flight model, ring rules, terrain clearance, an autopilot that flies the full course, SR22 stall speeds, take-off, the parachute, and a Safe Return landing.
- **Without Unity** (needs .NET 8 SDK and Node.js): `./Tests~/run-tests.sh`. This compiles every runtime script against Unity's engine reference assemblies, then runs 38 logic tests. One test checks that the Unity world is identical to the WebXR one. The SR22 tests check stall speeds, take-off, climb, the parachute, the autopilot, and Safe Return landing on the runway from four starting points.

## Notes and limits

- The code was compiled and its game logic tested outside Unity. The full Unity editor and a Quest build could not be run in the environment where it was written, so the first import is the first real run. If anything in the *Skyreach* menu fails, the Console message says which manual step to take.
- To switch to URP later you would need URP versions of the five shaders. The C# code doesn't depend on the render pipeline.
- Tuning values live in `Scripts/Core/SkyreachConfig.cs`, mirrored from `webxr/src/config.js`.
