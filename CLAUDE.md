# Notes for Claude sessions

- Unity 6 LTS project, URP, Input System, AI Navigation. 3D, top-down camera with a first-person look mode.
- Scope right now: player controller and guard AI only. This is not a roguelite; Hades is a game-feel reference only.
- Stealth references: MGS1, Of Guards and Thieves, Batman Arkham City, Spider-Man Shattered Dimensions (Noir). Verticality and light-as-vision-cue matter.
- **Read `ASSETS.md` before adding any asset.** Never generate art/audio. Placeholders go in `Assets/_Placeholder/`, named `PH_*`.
- Code lives in `Assets/Game/Scripts` (namespace `StealthProto`) and `Assets/Game/Editor`.
- There is no Unity editor in the cloud container, so code cannot be compiled there. Say so when handing over changes.
