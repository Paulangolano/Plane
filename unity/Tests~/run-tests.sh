#!/usr/bin/env bash
# Checks the Unity C# code without the Unity editor (needs the .NET 8 SDK and Node.js).
#  1. Compiles all runtime scripts against Unity's reference assemblies.
#  2. Runs the flight/world/course logic tests, including a parity check that the Unity
#     world is identical to the WebXR one, and an autopilot that flies the whole course.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
webxr="$here/../../webxr"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "== Compile check"
dotnet build "$here/CompileCheck/CompileCheck.csproj" -nologo -v q

echo "== Exporting WebXR world for the parity check"
cat > "$webxr/.world-export.mjs" <<'JS'
import { WorldData } from './src/world/worldData.js';
const w = new WorldData();
const heights = [];
for (let i = 0; i < 400; i++) {
  const x = ((i * 7919) % 8000) - 4000;
  const z = ((i * 104729) % 8000) - 4000;
  heights.push([x, z, w.heightAt(x, z)]);
}
console.log(JSON.stringify({
  islands: w.islands.map((i) => [i.x, i.z, i.radius, i.peak]),
  heights,
  rings: w.rings.map((r) => [r.position.x, r.position.y, r.position.z, r.normal.x, r.normal.y, r.normal.z]),
}));
JS
(cd "$webxr" && node .world-export.mjs > "$tmp/world.json"; rm -f .world-export.mjs)

echo "== Logic tests"
dotnet run --project "$here/LogicTests/LogicTests.csproj" -v q -- "$tmp/world.json"
