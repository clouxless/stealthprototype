# Stealth prototype

Top-down 3D stealth-action prototype in Unity. Current scope: **player controller** and
**guard AI**, tested in a vertical graybox room.

References: MGS1 and Of Guards and Thieves (guard AI, vision, alert states), Batman Arkham
City and Spider-Man Shattered Dimensions Noir (verticality, light as the read on enemy
vision), Hades (game feel only).

> **Asset rules:** read [ASSETS.md](ASSETS.md). Every placeholder lives in
> `Assets/_Placeholder/`, is named `PH_*`, and blocks release builds.

## Setup (first time)

The repo holds only our code and docs. Unity generates the project settings, so:

1. Install **Unity Hub** and the latest **Unity 6 LTS** editor.
2. In Unity Hub, create a new project from the **Universal 3D** template. Call it
   `stealthprototype` and put it anywhere *outside* any existing clone.
3. In a terminal, inside that new project folder:
   ```sh
   git init
   git remote add origin https://github.com/clouxless/stealthprototype.git
   git fetch origin
   git checkout -b main --track origin/main
   ```
   The repo's files land next to the template's files, without conflicts.
4. Open the project in Unity. If it asks to enable the new Input System backend, say yes
   and let it restart.
5. Check **Window > Package Manager** has **Input System** and **AI Navigation**
   installed. Install either one if it's missing.
6. Run **Stealth Prototype > Build Graybox Test Scene** (or **Build Dock Sandbox Scene**),
   then press **Play**.
7. Commit the template's files (`Assets/`, `Packages/`, `ProjectSettings/`, and all
   `.meta` files) so the project opens the same everywhere.

If everything renders **solid magenta**, URP is installed but no URP asset is active (this
happens when the project wasn't made from the Universal 3D template). Create one with
**Assets > Create > Rendering > URP Asset (with Universal Renderer)**, then drag it into
**Project Settings > Graphics > Default Render Pipeline** and into every level under
**Project Settings > Quality > Render Pipeline Asset**. Rebuild the scene afterwards.

If point lights don't cast shadows, open the URP asset in `Assets/Settings` (the PC one)
and turn on **Lighting > Additional Lights > Cast Shadows**.

## Controls

| Action | Keyboard + mouse | Gamepad |
| --- | --- | --- |
| Move | WASD | Left stick |
| Sneak (slow, silent) | Hold Shift | Hold LB |
| Dash (i-frames, a little noisy) | Space | A |
| Melee | LMB | X |
| Context: takedown (hold), clamber, climb, hang off an edge | E | Y |
| Cover: push into a tall wall; knock while in cover | Move into wall; LMB | Left stick; X |
| Focus (hold; you can't move) | Q | L3 |
| Prone (crawl) | C | B |
| Aim / fire | Hold RMB, LMB to fire | Hold LT, RT to fire |
| First-person look (can't move, fire along view) | V | R3 |
| Zoom camera | Mouse wheel | D-pad up / down |
| Toggle vision cones / noise rings / help | F1 / F2 / F3 | |
| Restart | R | |

While hanging: the stick shimmies along the edge, E climbs up, Space or C lets go.

## How the stealth works

- **Light in three tiers.** Only the floor under your feet counts: Dark, Dim or Lit. Each
  tier has one fixed guard sight distance everywhere (Dark 2.5 m, Dim 6.5 m, Lit 13 m).
  Lamp pools have a crisp Lit core and a Dim ring. A flashlight counts only if its beam
  lights the floor you stand on. Your character's tint shows the tier; there's no meter.
- **Guards don't roll dice.** Patrols, pauses, head sweeps and searches are the same every
  time. A "?" shows before a guard turns suspicious and a "!" before an alert. Shots have
  a visible wind-up and always hit unless you break line of sight first.
- **Short hunts.** When a guard loses you, a marker shows where they'll search: your last
  known position, then one step further along your heading. Break line of sight or lure
  them with noise and the hunt ends quickly. The lasting cost is the room: after the
  first alert, guards stay wary (they spot you faster) and the red alarm floods stay on.
- **Focus.** Hold Q to listen. The screen dims and sounds show as ripples. Hold longer for
  more: loud nearby sounds, then every sound with edge arrows for off-screen ones, then
  which guard made it and their state. Only sound shows. Critical sounds (gunshots,
  shouts) always draw a ring, focus or not.
- **Crawling.** Prone is slow and the quietest way to move. Lying right behind a low crate hides you
  and you fit under tables and through the vent in the inner wall. You can't stand up
  where there's no head room.
- **Height layers.** Floor, platform (from 0.75 m) and catwalk (from 2.75 m). Guards see
  their own layer and one below; they only look up while searching. Vision cones are drawn
  on the surfaces they cover, so you can see whether a cone reaches the catwalk. Geometry
  above you fades out, and characters behind walls show as silhouettes.
- **Cover.** Push into a tall wall to press against it, then slide along it. At the end,
  keep pushing to peek: the view slides round the corner. LMB knocks on the wall, a noise
  that draws guards to it. Cover is for seeing, not hiding: guards still see you there.
- **Ledge hang.** At a drop (the catwalk edge, the beam), walk to the edge and press E to
  hang. Shimmy along, climb back up with E, or let go. Your grip lasts 6 s. While hanging,
  the floor below you sets your light tier (switchable to the ledge on
  `PlayerContextActions.hangLight`).
- **Takedowns.** Get close behind a guard who isn't alert and a `Takedown` prompt
  appears. Hold E through the choke (2.5 s). You're locked in place and visible the
  whole time, and the struggle makes a little noise. Let go early and the guard breaks
  free and raises the alarm.
- **Climbing.** Face a ledge and a `Clamber` (up to 1.6 m) or `Climb` (up to 4.2 m)
  prompt appears. Press E (also from cover). Guards can't climb, so high ground is an
  escape route.
- **Scale.** Characters are placeholder mannequins at human proportions (about 1.75 m,
  0.3 m collider radius), so level metrics match the final art.

## The graybox room

- Four guards with overlapping coverage: floor, west, catwalk and platform. Taking one out
  usually happens inside another's sightline.
- Darkness is scarce: wide lamp pools with narrow dark lanes between them. You start in
  the dark south-west corner.
- The perch has a counter: a lamp lights its east half, and every lap the catwalk guard
  walks out along the beam to check it.
- A crawl-only vent through the inner wall comes out in a dark lane behind the west
  guard's route, a better angle than walking round the wall past the floor guard.
- Two tables to crawl under. No closet yet (it was optional).

## Dock sandbox

**Stealth Prototype > Build Dock Sandbox Scene** builds `PH_DockSandbox`, a test level for
the player and guard AI, laid out from Cloux's design on a 2 m grid (50 x 28 m):

- **Start:** the dark south-west corner, boxed in by a solid block. Two ways out: the ladder
  or the floor grate.
- **West catwalk:** ladders (face one, press E) at both ends lead to a catwalk 3.2 m up along
  the west wall, running south to north over the pit. Guards can't climb, and its edges can be
  hung from.
- **Floor grates:** two crawlways under the floor, one from the start under the start block,
  one down the pit-side lane under the north block. Walk into the open hatch at either end,
  go prone (C) and crawl; guards walk over the grate and can't see through it. Clamber out of
  the far hatch.
- **Bottomless pit:** a hole in the west floor. Fall in and you're downed.
- **Yard:** six solid blocks, floor to ceiling, that block sight, with a north, middle and south lane between them.
  Downward lamps light the lane crossings and leave the runs between them dark.
- **Guards:** a north guard that circles the north-west floor and loops the middle-top block, an
  east guard that loops the east block past the elevator, and a south guard that loops the
  west-bottom block along the pit edge, plus a post guard standing in the lane between the two
  south-east blocks, facing north.
- **Alarm lights:** two floodlights, one on the catwalk over the pit and one on the south lane,
  stay dark until the first alert, then stay on for good.
- **Exit:** the lit elevator at the east end. Reaching it shows your time, alerts and
  takedowns, then restarts.

The floor plan is a text map at the top of `Editor/DockSandboxBuilder.cs`. Edit it and run
the menu item again to try a variation.

## Code map

| Path | What |
| --- | --- |
| `Scripts/Player/PlayerMotor.cs` | Movement, sneak, dash, prone, footstep noise |
| `Scripts/Player/PlayerContextActions.cs` | E: takedown, clamber, climb, ledge hang |
| `Scripts/Player/PlayerCover.cs` | Cover cling, peek, knock |
| `Scripts/Player/FocusMode.cs` | Focus (sound ripples) |
| `Scripts/Player/PlayerCombat.cs` | Melee, aiming, gun |
| `Scripts/Camera/CameraRig.cs` | Top-down follow, first-person look, shake |
| `Scripts/Stealth/StealthLight.cs`, `LightExposure.cs` | Light tiers from the floor under the player |
| `Scripts/Stealth/HeightLayers.cs` | Named height layers |
| `Scripts/Level/AlarmLight.cs` | Lights that switch on for good after the first alert |
| `Scripts/AI/GuardVision.cs` | Cone, line of sight, tier sight distances, height layers |
| `Scripts/AI/GuardBrain.cs` | State machine, tells, hearing, radio, search, shooting |
| `Scripts/AI/LastKnownMarker.cs` | Marker where hunting guards will search |
| `Scripts/AI/VisionConeMesh.cs` | Vision drawn on the surfaces it covers |
| `Scripts/Level/SandboxGoal.cs`, `FallHazard.cs` | Exit trigger with run stats, fall hazard |
| `Editor/GrayboxBuilder.cs` | Builds the test scene from code |
| `Editor/DockSandboxBuilder.cs` | Builds the dock sandbox from a text map |
| `Editor/PlaceholderAudit.cs` | Placeholder audit and release-build block |
