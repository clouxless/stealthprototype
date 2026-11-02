# Asset rules

The final game will not use any AI-generated assets. To stop placeholders from
ever being mistaken for final work, every placeholder must be **obviously** a
placeholder and **easy to find**.

## Rules

1. **No generated art, models, textures or audio.** Code (including AI-assisted
   code) may build placeholder geometry out of Unity's built-in primitives and
   draw simple procedural patterns (checkers, grid lines). It must never produce
   or import art meant to look final.
2. **Placeholders look like placeholders.**
   - Environment: grey checker with magenta grid lines (`PH_Grid`).
   - Characters: tinted grey/white checker capsules with a box "nose" showing facing (`PH_Checker`).
   - Effects: flat unlit lines (`PH_FX_*`), created at runtime.
   - UI: Unity's default IMGUI text, always labelled `[PH]`.
   - Audio: none, or beeps generated in code. No audio files.
3. **Placeholders are easy to find.**
   - Every placeholder asset lives under `Assets/_Placeholder/`.
   - Every placeholder asset and scene object name starts with `PH_`.
   - Final assets never go in `Assets/_Placeholder/` and never start with `PH_`.
4. **Builds check for them.**
   - `Stealth Prototype > Audit Placeholders` lists every placeholder used by the
     scenes in Build Settings and the open scene.
   - A **release** (non-development) build fails if any build scene references
     `Assets/_Placeholder/` or contains objects named `PH_*`. Development builds
     are allowed through with a warning.

## Replacing a placeholder

When a final, human-made asset arrives, put it outside `Assets/_Placeholder/`,
swap the reference, and delete the placeholder. Run the audit afterwards.
