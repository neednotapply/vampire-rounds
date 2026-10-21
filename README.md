# Vampire Rounds (Vampire Survivors x ROUNDS)

A new adventure in Vampire Survivors where you play ROUNDS's Orange Guy or Blue Guy.

![ROUNDS's card pick inside Vampire Survivors](media/shots/pick.png)

**On Melty:** [melty.gg/m/vampire-rounds](https://melty.gg/m/vampire-rounds) (not published yet; once it is, one click with
your own Steam copies of Vampire Survivors and ROUNDS).

## What you get

- **ROUNDS's real players.** Your own ROUNDS runs hidden in the background and draws Orange Guy and Blue Guy the way
  ROUNDS does (the face you made in ROUNDS, legs, gun, health bar) on Vampire Survivors's stage, exactly where VS puts
  them and on the layer VS draws its own hero on: enemies in front of them and the Mad Forest's treetops cover them.
  Character select shows each one standing as ROUNDS draws a player at rest, at VS's own size, with the gun's icon.
- **ROUNDS's gun instead of VS's weapons.** It fires by itself at the nearest enemy (the mouse aims it while it
  moves), with ROUNDS's ammo, reload and bullets; right mouse or Space is ROUNDS's block. ROUNDS runs the gun, so
  every card works with ROUNDS's own code: Bouncy bounces off the screen edges, Poison ticks, explosions hit crowds.
- **Every level-up is a ROUNDS card pick, and so is a treasure chest (at most one a minute).** ROUNDS's own pick
  screen (5 cards, its rarities, art and rules); the card goes into your ROUNDS player at once. Chests that come
  sooner give their gold alone.
- **ROUNDS's card bar where VS shows your items.** Each guy's ROUNDS cards fill VS's item area at the top left as
  ROUNDS's own card bar does: a square with the card's two letters on the guy's colour per card ("Co" for Comb), in the
  order you took them, ten to a row. VS's own weapon and item slots are hidden there, but VS still collects and uses its
  pickups.
- **Every card shows.** A mod card that comes with no picture (PCE ships 40 of them) gets the two letters ROUNDS's
  card bar shows for it, big, in the card's colour; MapsExtended no longer shrinks the first card of a pick away; the
  cards stand in front of the guy's face in the pick.
- **Five stages, unlocked as in VS's own adventures.** Mad Forest, Inlaid Library, Dairy Plant, Gallo Tower and
  Cappella Magna (VS's main stages that need no DLC), each VS's own (enemies, bosses, gems, gold, pickups, music).
  Surviving 10 minutes in a stage (15 in Cappella Magna) is its goal: it gives gold and opens the next stage on the
  adventure's chapter list, where you pick it when you like.
- **Vampire Survivors's night.** VS decides when an enemy's hit lands (its own pacing); the hit takes ROUNDS's health,
  and ROUNDS's death ends the VS life (VS's revivals bring ROUNDS's player back). ROUNDS's slows, stuns and silences
  land as VS's own slow, freeze and defang.
- **Harder than 0.2.0.** ROUNDS cards make a guy strong much faster than VS's items: the guys start with 70 health
  instead of ROUNDS's 100 (health cards multiply it as in ROUNDS), and need more experience per level.
- **Its own adventure, straight away.** VS opens on the Vampire Rounds adventure's menu: the adventure's art covers the
  screen while VS's warning, title and menus are passed (Back leaves the adventure). Its own progress, roster (Orange
  Guy and Blue Guy, both open from the start) and stages; no VS merchants (they sell VS weapons). Your VS story and
  characters are untouched; Orange Guy and Blue Guy only appear in it.
- **Thunderstore card mods.** The Vampire Rounds launcher (what Melty starts) browses ROUNDS mods on Thunderstore
  (card mods only, ticked by default), installs them with their dependencies into the hidden ROUNDS's own BepInEx,
  turns them on and off, updates them, and can copy an r2modman / Thunderstore Mod Manager profile or a profile code.
  Their cards show up in your picks, with their own code (the launcher is Roundhead's, MIT, the same creator).
- **A face editor.** The launcher's FACES window shows each guy's face and your eight ROUNDS faces to start from;
  EDIT opens the face editor, like ROUNDS's own: every eye, mouth and detail ROUNDS has (EYES, MOUTH, DETAIL 1,
  DETAIL 2), shown on the guy, and you drag them on the face to place them. Faces are in their own colours, in the
  launcher and in the game (ROUNDS's soft-shadow material turned them dark grey in the engine's pictures). The faces
  are Vampire Rounds's own: ROUNDS's saved faces stay as they are.

Needs ROUNDS on its **old-rounds-for-mods** Steam beta branch (ROUNDS > Properties > Betas).

## How it works

- `managed/VampireRoundsLauncher.exe` (src/Launcher, .NET Framework 4.8): what Melty starts. Thunderstore browser on the
  left, your mods on the right, PLAY starts VS (with `--vr-rounds` naming ROUNDS's folder) and waits for it. Mods go
  where the hidden ROUNDS's BepInEx is, `UserData/VampireRounds/rounds/BepInEx/plugins/<Owner-Name>`, listed in
  `UserData/VampireRounds/rounds/mods.json`; its log and download cache are in `%LOCALAPPDATA%\VampireRounds`.
  The face editor writes each guy's face (ROUNDS's PlayerFace values: a part and an offset for the eyes, mouth and two
  details; src/Shared/FaceFile.cs) to `%LOCALAPPDATA%\VampireRounds\faces.txt`, which the engine puts into ROUNDS's
  in-memory face selection before it makes its players. Its pieces come from a hidden ROUNDS run with
  `--vampirerounds-faces` (about ten seconds, redrawn whenever ROUNDS's saved faces change): ROUNDS's eight faces on each
  guy, each guy's bare body and every face part, all pictured in one frame around the face (`faces/parts`, with
  `parts.txt`), so the launcher lays a face together from them. `--draw-faces` draws them without the window.
- `Mods/VampireRounds.dll` (src/VsMod): the MelonLoader mod for VS. It adds the adventure (VS's unused
  `ADV_LIB_001` slot), its five stages (copies of the player's own stage data under stage slots no VS stage uses:
  `TEST`, `TESTONE`, `ADV_0001TEST`, `ENTRANCE`, `CASTLE`; sheets/stages.json) with their goals (VS's adventure
  progress), Orange Guy and Blue Guy (VS's own custom-character system), starts the hidden ROUNDS, opens VS on the
  adventure's menu under a cover, shows the card logos in VS's item panel (CardSlots), and runs the link every frame.
- `Plugins/VampireRounds.InteropFix.dll` (src/InteropFix): MelonLoader 0.7.3's Il2CppInterop cannot find
  `Class::GetDefaultFieldValue` on VS's Unity 6 build and VS crashes two seconds after start with any MelonLoader
  mod; this points the hook at the right function (the byte pattern was first found by the Isaac Survivors mashup).
- `UserData/VampireRounds/rounds` (src/Engine + BepInEx 5.4.23.2): loaded into ROUNDS from where Melty installs it
  with Doorstop's command-line arguments (its log: `UserData/VampireRounds/rounds/BepInEx/LogOutput.log`); the engine is
  [Roundhead](https://github.com/neednotapply/roundhead)'s ROUNDS arena (MIT, the same creator) adapted to VS: Orange Guy and Blue Guy held where VS puts them, VS's enemies
  mirrored as trigger circles, the screen edges as walls, bullets flying straight. ROUNDS still opens a window in batch
  mode; it is kept off screen as a tool window that takes no focus.
- `version.dll` in the ROUNDS folder: UnityDoorstop 4 (from BepInEx 5.4.23.2, byte-identical to Roundhead's); it does
  nothing unless ROUNDS is started with Vampire Rounds's arguments.
- The two games talk through shared memory (`Local\VampireRoundsArena1`, sheets/link.json): VS writes the camera,
  each seat's position and input, the enemies and events; ROUNDS writes health, hits, effects, card picks and its
  rendered picture (players and bullets; pick screen). Each seat's deck goes to `%LOCALAPPDATA%\VampireRounds\deck<seat>.txt`
  and each card's square of ROUNDS's card bar to `cards\chip<seat>-<key>.rgba` (the engine's CardIcons: ROUNDS's own
  square for that seat's bar with the card's letters, pictured over black and white as 0.2.0 showed the bar over VS);
  `Q_DeckSerial` tells VS when to read them again.

Nothing from either game is in this repository or the package: each player's own ROUNDS draws the characters and
runs the cards, and their own VS supplies the stage.

## Building

Windows, .NET SDK 8, Python 3 with Pillow, your own VS and ROUNDS on Steam.

1. Interop assemblies for VS: run Cpp2IL 2022.1.0-pre-release.21 (`--output-as dummydll`) on VS, then
   Il2CppInterop.CLI 1.5.1 `generate --use-opt-out-prefixing` with Unity 6000.0.62's libraries, into
   `~/vsr-work/vs-interop` (MelonLoader makes the same ones on the first start).
2. BepInEx 5.4.23.2 (`BepInEx_win_x64_5.4.23.2.zip` from its GitHub releases) unpacked into `~/vsr-work/dl/bepinex`,
   and the license texts (BepInEx, UnityDoorstop, HarmonyX, MonoMod, Mono.Cecil) in `~/vsr-work/dl/licenses`.
3. The design sheets in `sheets/` are the source of truth: `python -I tools/sheets.py gen`, then
   `python -I tools/sheets.py preflight --code` (must say clean; it checks hook targets against your decompiled games).
4. `python -I tools/package.py 0.4.0` builds all four projects (the launcher too) and writes `dist/VampireRounds-0.4.0.zip`; Melty installs
   it with the recipe in `tools/recipe.json`.
5. Developer runs: `devtools/devinstall.py on|off` (puts the staged build into your games and takes it out again),
   `devtools/run.ps1 -Auto [-Slow] [-Hero blue] [-Cards "Explosive Bullet"]` (self-playing runs, optionally starting with ROUNDS cards;
   `VAMPIREROUNDS_DEV_ALLSTAGES=1` opens every stage and `VAMPIREROUNDS_AUTOPLAY_STAGE=<stages row or slot>` plays it), `devtools/shots.sh`
   (screenshots), `devtools/cards.ps1` (every card ROUNDS has, whole and as its logo, with what it is drawn with, into
   `%LOCALAPPDATA%\VampireRounds\cards\sheet` and `cards\audit.txt`).
6. Cover art: `python -I tools/cover.py` (from PowerShell; needs Edge and the internet for its fonts) repaints
   `media/art/card.png` (Melty's game card) and `media/art/adventure_bg.png` (the adventure's backdrop in VS) from
   `tools/cover.html`; the package takes the backdrop from that PNG.

## Test mode

While `%LOCALAPPDATA%\Melty\test\vampire-rounds.json` exists, the mod plays by itself (into the adventure as Orange,
walking a circle and stepping away from the horde, taking picks) and writes `pass <check>` lines for: engine-ready,
adventure-listed, run-started, rounds-drawn, gun-fired, enemy-hit, card-picked, card-logo-shown. A run that plays by
itself never writes VS's save, so a test leaves the player's progress as it was.

## License

MIT (LICENSE.txt). Third-party in the package: BepInEx (MIT), UnityDoorstop (LGPL-2.1), HarmonyX (MIT), MonoMod (MIT),
Mono.Cecil (MIT).
