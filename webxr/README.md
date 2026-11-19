# Skyreach VR: WebXR version

Plays in the **Meta Quest Browser** with no install. Built with [Three.js](https://threejs.org) and [Vite](https://vite.dev).

## Requirements

- Node.js 20 or newer (22 recommended)
- A Meta Quest 2/3/Pro for VR. Any desktop browser works for keyboard testing.

## Commands

```bash
npm install
```

```bash
npm run dev
```
Starts a local dev server for desktop testing at http://localhost:5173.

```bash
npm run dev:quest
```
Same, but over **HTTPS on your network**, so the Quest can open it. Use the `Network:` address it prints, and accept the self-signed certificate (*Advanced → Proceed*).

```bash
npm run build
```
Production build into `dist/`. Upload that folder to any HTTPS static host.

```bash
npm test
```
Runs all tests (unit, then a build, then browser tests). See [`../docs/TESTING.md`](../docs/TESTING.md).

## Testing VR without a headset

Open the game with `?emulate=quest2` on the end of the address (for example `http://localhost:5173/?emulate=quest2`). This loads Meta's [IWER](https://github.com/meta-quest/immersive-web-emulation-runtime) emulator so **Enter VR** works in a desktop browser. The automated VR tests use this too. `window.__skyreach` exposes the running game in the console for debugging.

## Performance on Quest 2

- About 200k triangles and around 65 draw calls per eye. Islands, trees and clouds are merged or instanced.
- Fixed foveated rendering, no shadows, no transparency on the ocean (lagoon colours are baked per vertex).
- The HUD canvas redraws at 15 Hz, not every frame.

## Tuning

Speeds, turn rates, ring count, island count and so on are in [`src/config.js`](src/config.js). If you change them, mirror the change in `unity/Assets/Skyreach/Scripts/Core/SkyreachConfig.cs` so both versions stay identical.
