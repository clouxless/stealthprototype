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
6. Run **Stealth Prototype > Build Graybox Test Scene** (or **Build Dock Sandbox Scene**, or
   **Build Tutorial Gatehouse Scene**),
   then press **Play**.
7. Commit the template's files (`Assets/`, `Packages/`, `ProjectSettings/`, and all
   `.meta` files) so the project opens the same everywhere.

If everything renders **solid magenta**, no URP asset is active (this happens when the
project wasn't made from the Universal 3D template). Both scene builders fix this
automatically, and **Stealth Prototype > Set Up URP** does it on its own.

If point lights don't cast shadows, open the URP asset in `Assets/Settings` (the PC one)
and turn on **Lighting > Additional Lights > Cast Shadows**.

## Controls

| Action | Keyboard + mouse | Gamepad |
| --- | --- | --- |
| Move | WASD | Left stick |
| Dash (i-frames, a little noisy) | Space | A |
| Melee | LMB | X |
| Context: takedown (hold), clamber, climb, hang off an edge | E | Y |
| Cover: cling to a tall wall (hold), slide along it | Hold Shift | Hold LB |
| Knock (in cover, a vent or a grate): hold to see its reach, release to knock | Hold LMB, release | Hold X, release |
| Focus (hold; you can't move) | Q | L3 |
| Crawl (toggle) | C | B |
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
  a short visible wind-up (0.3 s) and always hit unless you break line of sight first. Once the
  "!" freeze ends, an alerted guard who can see you within 20 m fires about every 0.8 s, so
  being seen in the open is deadly fast.
- **Short hunts.** When a guard loses you, a marker shows where they'll search: your last
  known position, then one step further along your heading. Break line of sight or lure
  them with noise and the hunt ends quickly. The lasting cost is the room: after the
  first alert, guards stay wary (they spot you faster) and the red alarm floods stay on.
- **Focus.** Hold Q to listen, also while clinging to a wall. Over about 3 seconds the world fades to pitch black, leaving
  only you and sounds drawn as ripples. Once it is fully black, the camera pulls out. Hold longer for more: loud
  nearby sounds, then every sound (from 0.75 s), then which guard made it and their state
  (from 3 s). Ripples are slow, soft rings with fainter rings trailing them, like water, and
  each source makes at most one every 1.2 s. As focus deepens the ripples shrink, from the
  sound's full radius down to a fifth of it after 9 s of holding (slower than the camera's
  pull-out, which ends at 5 s), so it gets easier to pinpoint who
  made them (`FocusMode.ripple*` to tune). Ripples from sounds
  off screen drift in across the screen edge. Only sound shows. Critical sounds (gunshots,
  shouts) always draw a ring, focus or not.
- **Walking is the sneaking.** There's one walking speed and no sneak button. Walking is
  silent, and a guard never notices you outside his vision cone, so walking up behind one
  for a takedown always works. Guards find you by sight, or by noise you choose to make:
  dashing, fighting, knocking, shooting.
- **Crawling is the extra-sneaky option.** Press C to toggle it. It's slow (1 m/s) and does
  exactly three things, and nothing else:
  1. **One tier darker.** Guards see you as if your light tier were one darker: a Lit floor
     counts as Dim, and a Dim floor counts as Dark. Dark stays Dark. Your character's tint and
     the HUD show the tier guards get.
  2. **Low profile.** Low cover (crates, ledges, waist-high walls) hides you completely, and
     when part of you is hidden you're spotted more slowly.
  3. **Small spaces.** You fit under tables and through vents and floor grates, and you can't
     stand up where there's no head room.

  Crawling does not change hearing (walking is already silent) or how wide a guard's cone is.
- **Height layers.** Floor, platform (from 0.75 m) and catwalk (from 2.75 m). Guards see
  their own layer and one below; they only look up while searching. Vision cones are drawn
  on the surfaces they cover, so you can see whether a cone reaches the catwalk. Geometry
  above you fades out, and characters behind walls show as silhouettes.
- **Cover.** Hold Shift (LB) next to a tall wall, or walk into one while holding it, to press
  your back to it. Let go to step off. The stick slides you along the wall (left and right stay
  as they are on screen top-down, even when the camera swings). Near the end of the wall, in the
  last metre and short of the very edge, the camera swings low to look along the wall and round
  the corner, MGS1-style (`CameraRig.coverSwing`: 1 is the full swing, 0 stays top-down). Cover
  is for seeing and luring, not hiding: guards still see you there.
- **Knocking (luring).** In cover, hold LMB (X) to show the knock's reach on the floor: a 16 m
  circle with any sealed-off spaces cut out. Release to knock; let go of Shift first to cancel.
  Every guard who hears it stops and turns to it with a "?" for a second (`hearNoticeTime`),
  then walks to it by the shortest path, does the same look-around and goes back. A second
  knock while he's investigating gets the same pause before he heads to the new spot. In a vent or a floor grate the same hold-and-release works without Shift (moving
  cancels it).
- **How noise travels.** Every noise is a straight-line circle of its radius. A guard inside
  the circle hears it if there's any open way round from the noise to him, however long. Only a
  fully sealed space doesn't hear it. Open doorways and open air (the catwalk and the floor
  below it) connect spaces; closed doors, vents and grates don't. Vents and grates are one-way:
  outside noise never gets in or travels through, but a knock inside a vent comes out at both of
  its openings (full range from each), and a knock in a floor grate is heard in the space above.
  The noise rings (F2) and the knock preview stop at walls that seal the noise in. Focus follows
  the same rule, so it only shows sounds from the space you're in (and none from outside while
  you're in a vent or grate). See `SoundGrid` and `SoundDuct`.
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
- No guard patrols back and forth in a straight line (no sudden 180-degree turns): every
  route is a loop, and dead ends like the perch are rounded off.
- Two tables to crawl under. No closet yet (it was optional).

## Dock sandbox

**Stealth Prototype > Build Dock Sandbox Scene** builds `PH_DockSandbox`, a test level for
the player and guard AI, laid out from Cloux's design on a 2 m grid (50 x 28 m):

- **Start:** the dark south-west corner, boxed in by a solid block. Two ways out: the ladder
  or the floor grate right in front of you.
- **West catwalk:** ladders (face one, press E) at both ends lead to a catwalk 3.2 m up along
  the west wall, running south to north over the pit. Guards can't climb, and its edges can be
  hung from.
- **Floor grates (Arkham-style):** two crawlspaces under the floor, one from the start under
  the start block, one down the pit-side lane under the north block. Stand on a grate and press
  E to drop in, push the stick to crawl along it, and press E again to climb out wherever there's
  room to stand. Inside you're silent, and guards walk over you without seeing you.
- **Bottomless pit:** a hole in the west floor. Fall in and you're downed.
- **Yard:** solid blocks, floor to ceiling, that block sight, with a north, middle and south lane between them. Two of the middle ones are roofed rooms (below).
  Downward lamps light the lane crossings and leave the runs between them dark.
- **Guards:** all loops, with no sudden about-faces. A north guard holds a post in the
  north-west pocket, then walks one big loop: east along the north lane, back west along the
  middle lane, a look over the pit, and up the west lane home. An east guard loops the top-right
  block past the elevator, and a south guard loops the west-bottom block along the pit edge.
  A post guard stands in the lane between the two south-east blocks, facing north.
- **Rooms for testing noise.** Two of the middle blocks are now roofed rooms, each with a guard
  inside. The **sealed room** (the west one) has a closed door: knock on its walls from outside
  and its guard never hears it. A crawl vent runs through its west wall from the lane: knock
  inside the vent and the noise comes out at both ends, so the guard inside comes to the vent's
  inner mouth. The **open room** (the east one) has an open doorway on its south side: knock on
  its north wall from the north lane, and its guard hears it through the doorway and walks out
  and round to you.
- **Alarm lights:** two floodlights, one on the catwalk over the pit and one on the south lane,
  stay dark until the first alert, then stay on for good.
- **Exit:** the lit elevator at the east end. Reaching it shows your time, alerts and
  takedowns, then restarts.

The floor plan is a text map at the top of `Editor/DockSandboxBuilder.cs`. Edit it and run
the menu item again to try a variation.

## Tutorial: guide wall to the gatehouse

**Stealth Prototype > Build Tutorial Gatehouse Scene** builds `PH_TutorialGatehouse`, the
first stretch of the tutorial from map A (`design/tutorial-gatehouse/versions/map-A-labeled` in
the project files). It's geometry and lights only for now: **no guards yet**.

- **Coordinates are the map's:** x is east, z is north (upriver), in metres. Heights follow
  the real lowest lock head, measured from the guide wall top: ledge +6.4 m, bridge and pier
  deck +16 m, all in flights of 3.2 m.
- **Guide wall:** 3 m wide, 120 m long, railed, with buttresses under its river side and four
  mooring dolphins carrying on downstream. You start at its downstream tip.
- **Lower stairs:** a scissor stair (two flights) on the front face of the lower block, east of
  where the wall lands. Step east off the end of the wall onto it. It tops out on the **ledge**
  (22 x 7 m) next to a small hut.
- **Upper stairs:** three flights against the upper pier face, side by side in three 1.4 m lanes,
  with 2.8 m of open ledge in front. It comes out on the bridge through a gap in the south parapet.
- **How the stairs are built:** each flight is 8 m long and rises 3.2 m (22 degrees), with a
  1.5 m landing at each end to turn round on. Every flight is solid down to the floor below it (so
  there's no gap to fall through), and no flight runs over another (so the camera always sees you).
- **Bridge:** a 13 m road from x -40 to 66, over chamber 1 (centre x -17), the pier, and the
  east bank. Lampposts on both edges. The west end is two Humvees with their headlights down the
  road; the east end is a wall. Both ends are walled off past the climb limit.
- **Pier deck:** the gatehouse (15 x 13 m) against the chamber edge, a small box between it and
  the bridge, open deck to the east and an outbuilding further north.
- **Goal:** the gatehouse door, on its south face.
- **Rules here:** one hit point (one shot kills) and a silent dash. Falling into the water
  restarts the level.

The layout lives in `Editor/TutorialGatehouseBuilder.cs`, one method per area.

## Look and testing toggles

- **Black and white, except guard state:** a global URP volume
  (`Assets/Game/Rendering/BlackAndWhite.asset`) greys out every hue except red through amber, so
  the only colour in the world is suspicious (amber) and alert (red) guards: their cones,
  bodies, the last-known marker and their shots. Keep everything else neutral or it'll show
  through. The debug HUD keeps its colours.
- **Flashlights are drawn crisp and flat**, in the same draped style as the vision cones: bright
  where the beam makes you Lit, faint where it makes you Dim, nothing in its shadows. Tinted
  amber or red with the guard's state.
- **Long sight:** guards see a Lit player dead ahead (within 12 degrees) out to 26 m, shown as a
  faint spike in front of each vision cone. Off-centre, the normal 13 m cone applies.
- **Gunfire gives you away:** your muzzle flash counts as Lit for 1.5 s, and guards who hear a
  shot or get hit go alert at the shot and look up for 6 s, so shooting from the catwalk gets
  you shot back.
- **X-ray (F4):** shows guards through walls. Off by default. You always see yourself.

## Code map

| Path | What |
| --- | --- |
| `Scripts/Player/PlayerMotor.cs` | Movement, dash, crawling |
| `Scripts/Player/PlayerContextActions.cs` | E: takedown, clamber, climb, ledge hang |
| `Scripts/Player/PlayerCover.cs` | Cover cling, corner view, knock (also in vents and grates) |
| `Scripts/Player/NoisePreview.cs` | The knock's reach drawn on the floor before you knock |
| `Scripts/Core/NoiseSystem.cs` | Hearing: circle plus same-space rule, feed for Focus |
| `Scripts/Core/SoundGrid.cs` | Which spaces sound can reach (air cells flooded into spaces) |
| `Scripts/Core/SoundDuct.cs` | Vents and grates: one-way for noise |
| `Scripts/Core/NoiseShape.cs`, `NoiseRingFX.cs` | Noise rings and preview, cut at sealing walls |
| `Scripts/Player/FocusMode.cs` | Focus (sound ripples) |
| `Scripts/Player/PlayerCombat.cs` | Melee, aiming, gun |
| `Scripts/Camera/CameraRig.cs` | Top-down follow, cover corner view, first-person look, shake |
| `Scripts/Stealth/StealthLight.cs`, `LightExposure.cs` | Light tiers from the floor under the player |
| `Scripts/Stealth/HeightLayers.cs` | Named height layers |
| `Scripts/Level/AlarmLight.cs` | Lights that switch on for good after the first alert |
| `Scripts/AI/GuardVision.cs` | Cone, line of sight, tier sight distances, height layers |
| `Scripts/AI/GuardBrain.cs` | State machine, tells, hearing, radio, search, shooting |
| `Scripts/AI/LastKnownMarker.cs` | Marker where hunting guards will search |
| `Scripts/AI/VisionConeMesh.cs` | Vision drawn on the surfaces it covers |
| `Scripts/Level/SandboxGoal.cs`, `FallHazard.cs` | Exit trigger with run stats, fall hazard |
| `Scripts/Level/GrateRun.cs`, `Scripts/Player/PlayerGrateCrawl.cs` | Floor grates: enter, crawl, exit |
| `Editor/GrayboxBuilder.cs` | Builds the test scene from code |
| `Editor/DockSandboxBuilder.cs` | Builds the dock sandbox from a text map |
| `Editor/TutorialGatehouseBuilder.cs` | Builds the tutorial's first stretch (map A) |
| `Editor/PlaceholderAudit.cs` | Placeholder audit and release-build block |
