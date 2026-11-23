# That's Lit - Logical AI Vision (Unofficial SPT 4.1.x Port)

> **Unofficial Community Port Notice**
>
> This is an **unofficial community port** of **That's Lit** by **[No3371 / BA](https://github.com/No3371/SPT_ThatsLit)** updated for **SPT 4.1.x (EFT 0.16.9.5.40743)**, **SAIN 4.5.x**, and optional **Fika 2.4.x / Headless 1.5.x**.
>
> - All credit for the original concept, design, and codebase belongs to **No3371 / BA**.
> - This port is distributed under the original repository's **MIT License** (`Copyright (c) 2023 BA` — see [`LICENSE`](./LICENSE)).
> - I reached out to the original author regarding an SPT 4.1 port, and while waiting with no reply yet, I am sharing this port so the community can enjoy That's Lit on SPT 4.1.x right away. If the original author releases an official SPT 4.1 update or requests this port to be taken down/archived, I will gladly comply immediately.
> - **Do not report bugs from this port to No3371 or Solarint (SAIN).** Please report issues on this fork's issue tracker instead.

---

## Supported Versions

- **SPT**: `4.1.x` (built & tested on SPT `4.1.6` / EFT `0.16.9.5.40743`)
- **SAIN**: `4.5.x` (optional, recommended)
- **Fika / Headless (Optional)**: Fika `2.4.x` + Fika Headless `1.5.x` via the included `ThatsLit.Sync` addon

---

## What's New in This SPT 4.1.x Port

### SPT 4.1 / EFT `0.16.9.5.40743` & SAIN 4.5.x Compatibility
- Updated vision, sighting, and aiming patches for EFT `0.16.9.5.40743` (`SeenCoefPatch` polarity fix + double precision, `PartVisionThresholdPatch`, `ExtraVisibleDistancePatch`, `EncounteringPatch`, `BlindFirePatch`).
- Updated terrain/grass sampling for EFT 0.16.9 GPU Instancer (`GPUInstancerDetailCell`) and safe `TarkovApplication.Exist` lookup.
- Added compatibility for **SAIN 4.5.x** (including `_nextAimingDelay` handling and preventing double vision-distance additions).
  - *Note*: `Interrupt SAIN No Bush ESP` (`SAINNoBushOverride`) is intentionally disabled on SAIN 4.5.x because `SAINNoBushESP` no longer exists in SAIN 4.5.
- Disabled upstream 3.10 `UpdateNotifier` check to prevent false update prompts.

### Optional Fika 2.4.x & Headless 1.5.x Support (`ThatsLit.Sync`)
- Updated `ThatsLit.Sync` for Fika `2.4.x` (`Fika.Core.Networking.LiteNetLib`, `FikaPlayer`, sender verification).
- Syncs both **brightness** and **18-bucket directional grass/prone concealment** (`TerrainScorePacket`) from clients to the host/headless so bots react accurately to every player in Co-op and Headless raids.
- Headless (`-batchmode`) skips GPU camera rendering automatically while keeping local foliage checks and synced brightness/terrain scores active for bots.

### Performance & Stability Improvements (Zero Stealth Fidelity Loss)
- **6-Angle Camera Cache & Smart Rate Cap (`Throttle Camera`)**:
  - Caches all 6 brightness camera angles so skipped frames reuse real measurements instead of zero-luminance frames.
  - Dynamically caps camera updates while moving or reacting to light/pose/shot/shadow changes (`Camera Max Rate (Hz)`, default `18 Hz`) and slows down when stationary (`Camera Idle Rate (Hz)`, default `6 Hz` in darkness / `1 Hz` in bright daylight).
  - Default is `false` to match original behavior; **enabling `Throttle Camera` in the F12 menu is strongly recommended** for a noticeable CPU/FPS boost in night, indoor, and heavy maps without losing stealth accuracy.
- **Static Foliage 2D Spatial Broadphase (`Foliage Spatial Grid`, default `true`)**:
  - Builds a lightweight (~1–2 MB) hashed 2D spatial index of static map foliage colliders at raid start (auto-invalidated on additive scene loads).
  - Skips `Physics.OverlapSphereNonAlloc` in O(1) whenever no static foliage collider can intersect the 4 m sphere around the player, and falls back to exact PhysX on contact so results remain 100% identical to vanilla.
- **Memory & CPU Cleanup**:
  - Persistent `NativeArray` readback buffers with strict ownership & disposal (eliminates per-frame allocations and native leaks).
  - O(1) GPUI cell lookup, pre-classified `DetailKind` enum, memoized foliage regex, cached `RaycastIgnoreGlass`, and thread-safe logging/compat loading.

---

## Installation

### Single-Player (Solo SPT 4.1.x)
1. Download the release archive (`ThatsLit_1.4100.0_SPT4.1.zip`).
2. Extract the **`BepInEx`** folder directly into your SPT root directory (where `EscapeFromTarkov.exe` is located) using 7-Zip or Bandizip.
   - Resulting path: `BepInEx\plugins\ThatsLit\ThatsLit.Core.dll` (along with the `.thatslitcompat.json` files).
3. *(Recommended for better FPS)* Press **F12** in-game -> **That's Lit** -> **2. Darkness / Brightness** -> enable **`Throttle Camera`**.

### Fika Co-op / Local Headless (Optional)
1. Install `ThatsLit.Core` as above on **both** the client(s) and the host / Headless client.
2. Also copy `Optional_Fika_Sync\BepInEx\plugins\ThatsLit\ThatsLit.Sync.dll` into `BepInEx\plugins\ThatsLit\` on **both** the client(s) and the host / Headless client.
   - *(Solo players without Fika do **not** need `ThatsLit.Sync.dll`.)*

---

## Original Mod Overview (by No3371)

*(The sections below are preserved and lightly updated from the original [No3371/SPT_ThatsLit](https://github.com/No3371/SPT_ThatsLit) README.)*

This is a mod for Single Player Tarkov for tuning bot vision logic to make bots mimic realistic human perception capabilities.

[\[Showcase\]](https://streamable.com/rbvo7q) [\[Showcase\]](https://streamable.com/n889vs) [\[Showcase\]](https://streamable.com/1z135g)
[\[Showcase\]](https://streamable.com/eco0yr) [\[Showcase\]](https://streamable.com/unrili)
[\[Showcase\]](https://imgur.com/a/ORTSSWO) [\[Showcase\]](https://imgur.com/a/ia7FYdS) [\[Showcase\]](https://streamable.com/lhdbbs)
[\[Showcase\]](https://streamable.com/1as7m4) [\[Showcase\]](https://streamable.com/00td1z) [\[Showcase\]](https://streamable.com/810ese)

Let's define ***unfair***: When your bullet hits or whizzes past a bot, it knows your exact position. They can see you perfectly fine in the dark as long as you are within its vision range. They don't care how dense the foliage and grasses around you are.

Yet we struggle to locate bots with our human eyes.

This is why That's Lit exists: **in SPT, we no longer play against human players — bots need to make human-like mistakes just like real players do.**

That's Lit lets AIs overlook you when you are blended in the shadow, or spot you faster when you are lit up by lighting. It also allows you to traverse fields through grasses and bushes without always getting engaged from afar. Additionally, randomness is applied to AI vision checks, so bots are no longer machines that 100% spot you in range or 0% out of range.

By default, 2 meters are displayed at the top left corner for you to learn the mechanic, and they can be disabled in the F12 menu:
- The first is the **brightness meter**, showing how dark (left) and how bright (right) you are.
- The second is the **factor meter** (squared score), showing the rough effectiveness of your brightness against AIs.

The mod is developed with SAIN and Realism in mind. It works fine without them, though the default experience may feel easier depending on your setup.

**It's very important to have a loadout mod that properly gives bots NVGs or Flashlights in night raids to keep the game balanced.**

---

## Explanation

In short: the mod modifies *SeenCoef*, which determines how long an AI needs to keep you in vision to visually confirm you.

Key factors include:
- **Lighting & Weather**: Time of day, cloudiness, shadows, indoor/outdoor state, winter snow, flashlights, gun flashes, and thunder all affect how visible you are.
- **Active Devices**: Active flashlights and lasers (including IR against NVG bots) heavily expose you.
- **NVGs & Thermals**: Bots with NVGs are largely unaffected by darkness (except very dark corners); bots with Thermals spot players faster unless countered by stims like SJ9.
- **Foliage & Bush Ratting**: Nearby bushes between you and distant bots conceal you (and close-range bush concealment is possible depending on pose and foliage type, though never 100% safe).
- **Grasses (Terrain Details)**: Surrounding grasses within ~3.5 m toward the bot act as visual cover, especially when prone.
- **Elevation, Indoor/Outdoor & Movement**: Bots outside can occasionally overlook players inside buildings; sprinting players are spotted faster while sprinting bots notice off-axis targets slower.
- **Caution System & Memory**: Bots have randomized caution levels; mechanics are nerfed if a bot has recently seen you in the same spot — keep moving!
- **Bosses**: Unaffected by default (optional in F12; bush ratting remains disabled against bosses for balance).
- **Additional Patches**: Delayed bot aim right out of sprint inertia, chance to cancel instant back-of-head spotting on surprise attacks, vision distance compensation for bright/lit/NVG/thermal cases (balancing SAIN's time/weather scaling), and blind-fire scatter.

---

## Configuration

All settings are available through the **F12** BepInEx Configuration Manager menu. If That's Lit is not listed there, verify it is installed in `BepInEx\plugins\ThatsLit\`.

### Maps
All maps are supported. The Brightness module is automatically disabled on Day Factory and The Lab because lighting is bright everywhere and camera rendering would waste FPS.

### Custom Gear Compatibility (`*.thatslitcompat.json`)
Custom lights, lasers, NVGs, and thermal scopes/goggles can be registered through any `*.thatslitcompat.json` file placed anywhere inside `BepInEx\plugins\`. See `default.thatslitcompat.json`, `empty.thatslitcompat.json`, and `That's Lit Compat Guide.md` included in the release.

---

## Recommended Mods & SAIN Notes

- **SAIN (4.5.x)**:
  - Consider bumping your SAIN preset up by 1 tier since That's Lit slows down bot visual confirmation when you use darkness/cover properly.
  - Check SAIN's **Vision Speed** settings for Movement, Elevation, Peripheral Vision, and Pose to avoid double-stacking modifiers if bots feel too blind.
  - Headshot Protection is less necessary due to per-body-part randomness.
- **Loadout / Overhaul Mods** (e.g., Realism, ALP, etc.): Recommended so night-raid bots properly carry NVGs and flashlights.

---

## Performance

- Only the **Brightness** module has a noticeable FPS cost because it uses an extra Unity camera to observe the player.
- With **`Throttle Camera` enabled** in this SPT 4.1 port (F12 -> `2. Darkness / Brightness`), camera rendering is dynamically capped and cached across all 6 angles (18 Hz active / 6 Hz dark idle / 1 Hz bright idle), cutting camera CPU overhead dramatically while keeping stealth behavior accurate.
- If you don't care about lighting/darkness mechanics at all, you can disable the Brightness module completely while keeping foliage, grass, and other modules active.

---

## Building from Source

```powershell
# Build ThatsLit.Core
dotnet build ThatsLit.Core\ThatsLit.Core.csproj -c Release -p:EFTPath="C:\Path\To\SPT"

# Build optional ThatsLit.Sync (requires Fika.Core.dll in $(EFTPath)\BepInEx\plugins\Fika\)
dotnet build ThatsLit.Sync\ThatsLit.Sync.csproj -c Release -p:EFTPath="C:\Path\To\SPT"
```

---

## Credits & License

- **Original Author**: [No3371 / BA](https://github.com/No3371/SPT_ThatsLit)
- **Original Inspiration / Credits**: Solarint (SAIN), DrakiaXYZ (VersionChecker)
- **License**: [MIT License](./LICENSE) (`Copyright (c) 2023 BA`)
