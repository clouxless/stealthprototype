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
6. Run **Stealth Prototype > Build Graybox Test Scene**, then press **Play**.
7. Commit the template's files (`Assets/`, `Packages/`, `ProjectSettings/`, and all
   `.meta` files) so the project opens the same everywhere.

If point lights don't cast shadows, open the URP asset in `Assets/Settings` (the PC one)
and turn on **Lighting > Additional Lights > Cast Shadows**.

## Controls

| Action | Keyboard + mouse | Gamepad |
| --- | --- | --- |
| Move | WASD | Left stick |
| Sneak (slow, silent) | Hold Shift | Hold LB |
| Dash (i-frames, a little noisy) | Space | A |
| Melee | LMB | X |
| Context: takedown (hold), clamber, climb | E | Y |
| Aim / fire | Hold RMB, LMB to fire | Hold LT, RT to fire |
| First-person look (can't move, fire along view) | V | R3 |
| Toggle vision cones / noise rings / help | F1 / F2 / F3 | |
| Restart | R | |

## How the stealth works

- **Light is the read on vision.** Every guard carries a flashlight whose cone matches
  their vision cone. If the beam is on you, you're in danger. The beam's colour shows the
  guard's state: warm white for patrol, amber for suspicious or searching, red for alert.
- **Exposure.** Lights with a `StealthLight` component add to your exposure, and they're
  blocked by geometry. In shadow you turn into a dark silhouette and guards can only see
  you up close. The HUD shows HIDDEN or EXPOSED.
- **Verticality.** Guards check line of sight in 3D against your feet, chest and head. They
  rarely look up (20 degrees by default), so the overhead beam and the perch are safe
  until you're far enough away to be in their sightline. Low crates hide your legs but
  not your head.
- **Scale.** Characters are placeholder mannequins at human proportions (about 1.75 m,
  0.3 m collider radius), so level metrics match the final art.
- **Hearing.** Running, dashing, melee and gunfire make noise (F2 shows the rings).
  Sneaking is silent. Noise passes through walls for now.
- **Takedowns.** Get close behind a guard who isn't alert and a `Takedown` prompt
  appears. Hold E through the choke (2.5 s). You're locked in place and visible the
  whole time, and the struggle makes a little noise. Let go early and the guard breaks
  free and raises the alarm.
- **Climbing.** Face a ledge and a `Clamber` (up to 1.6 m) or `Climb` (up to 4.2 m)
  prompt appears. Press E. The catwalk's base wall, the perch tower, crates and the
  platform can all be climbed. Guards can't climb, so high ground is an escape route.
- **States.** *Patrol* turns into *Suspicious* (stop, turn, go look) and then *Alert*
  (radio nearby guards, chase, shoot after a visible wind-up). When they lose you they
  switch to *Search* around your last known position, then go back to *Patrol*.

## Code map

| Path | What |
| --- | --- |
| `Scripts/Player/PlayerMotor.cs` | Movement, sneak, dash, footstep noise |
| `Scripts/Player/PlayerCombat.cs` | Melee, takedowns, aiming, gun |
| `Scripts/Camera/CameraRig.cs` | Top-down follow, first-person look, shake |
| `Scripts/Stealth/StealthLight.cs`, `LightExposure.cs` | Light-based visibility |
| `Scripts/AI/GuardVision.cs` | Cone, 3D line of sight, light factor |
| `Scripts/AI/GuardBrain.cs` | State machine, hearing, radio, shooting |
| `Scripts/AI/VisionConeMesh.cs` | Floor fan showing vision (debug-ish) |
| `Editor/GrayboxBuilder.cs` | Builds the test scene from code |
| `Editor/PlaceholderAudit.cs` | Placeholder audit and release-build block |
