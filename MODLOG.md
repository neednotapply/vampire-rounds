# Vampire Rounds — mod log

## Design (agreed 2026-10-19)
- Host: Vampire Survivors (Steam 1794680, build 25984009, Unity 6000.0.62f1 IL2CPP, MelonLoader v0.7.3 from Melty).
- Companion: ROUNDS (Steam 1557740, old-rounds-for-mods branch, Unity 2018.4.34f1 Mono, build 6707853).
- A "Vampire Rounds" adventure in VS's Adventures menu. Orange and Blue (ROUNDS players) are the characters;
  solo or two players on one PC (VS local co-op). ROUNDS's gun replaces VS weapons; every VS level-up opens
  ROUNDS's real pick screen; VS stages, enemies, bosses, chests, gold, merchant stay.
- First version: one adventure, opening stage Mad Forest.

## Paths
- VS: D:\SteamLibrary\steamapps\common\Vampire Survivors
- ROUNDS: D:\SteamLibrary\steamapps\common\ROUNDS (has Roundhead's version.dll doorstop 4 + an r2modman winhttp.dll doorstop 3)
- Work (outside the project): ~/vsr-work
  - roundhead/ (clone of the creator's Roundhead repo, MIT) — CardEngine to adapt
  - vs-dummy/ (Cpp2IL dummy dlls), vs-interop/ (Il2CppInterop 1.5.1 opt-out prefixing; names match ML 0.7.3)
  - vs-decomp/runtime (ilspy of the dummy VampireSurvivors.Runtime: signatures only)
  - vs-data/ (VS TextAssets read for reference only, never shipped)
  - ref-isaac/ (Isaac Survivors decompiled, reference for VS workshop-character registration)

## VS facts
- Adventures: DataManager.AllAdventures (Dictionary<AdventureType, AdventureData>), data TextAsset "AdventureData"
  (keys ADV_*), stage sets in "StageSet" (STAGESET_* -> list of StageType names; base stages allowed, e.g. LIBRARY).
- AdventureType enum has 10 values (0..9). StageSetType enum.
- Custom characters: VS's own workshop system (WorkshopCharacterBootstrapper.LoadCustomCharacters,
  RuntimeCharacters.Custom.Allocate/TryGetCharacterType, DataManager.RegisterWorkshopCharacter,
  WorkshopCharacterPrefabBuilder) — Isaac Survivors uses this.
- VS ships Coherence networking (online co-op), not our concern.

## Route (decided)
- VS side: MelonLoader 0.7.3 mod (Melty installs ML). Built against Il2CppInterop 1.5.1 assemblies generated offline
  (Cpp2IL pre-release.21 + Il2CppInterop.CLI 1.5.1, --use-opt-out-prefixing, Unity libs 6000.0.62) in ~/vsr-work/vs-interop.
- ROUNDS side: Roundhead's arena engine (BepInEx 5.4.23.2 plugin) in a hidden ROUNDS (-batchmode), started by the VS mod
  with Doorstop 4 args (version.dll proxy in ROUNDS folder = BepInEx's winhttp.dll, byte-identical to Roundhead's).
  Profile copied to %LOCALAPPDATA%\VampireRounds\rounds-profile. Two Doorstop 4 proxies never double-load
  (the first sets DOORSTOP_DISABLE).
- Link: shared memory Local\VampireRoundsArena1 (sheets/link.json).

## VS hooks (research, see sheets/hooks.json)
- Adventure slot AdventureType.ADV_LIB_001 (unused), StageSetType.STAGE_DATA_ADV_BMN002 (unused), stage slot TEST,
  progress type TEST001_ACH001, progressKey VampireRoundsMelty (no CompleteAdv_ achievement).
- Inject in DataManager.LoadBaseJObjects postfix (_allAdventuresJson etc.); roster fixed in LoadDataFromJson postfix.
- Characters via WorkshopCharacterBootstrapper.LoadCustomCharacters postfix (Isaac pattern); weapon slot ___PLCHLD200.
- Level-up: prefix GameManager.SwapToLevelUpScreen -> our pick; complete via LevelUpCompletedSignal / OnLevelUpCompleted.
- Chest: prefix GameManager.SwapToTreasureScreen. Player damage: prefix CharacterController.GetDamaged(float).
- Enemies: GM.Core.Stage.SpawnedEnemies; EnemyController.GetDamaged(value, HitVfxType, kb, WeaponType, hasKb).
- Camera.main renders into a RenderTexture: mouse via viewport. PPU 100.

## Dev install
- devtools/devinstall.py on|off (records changes in ~/vsr-work/devinstall.json; sets other mashups' dlls aside).

## Melty
- Draft: modId 6533a92b-5a46-4750-b637-1932e00826c4, slug vampire-rounds (created 2026-10-19). License MIT, remix allowed (creator: "mix as you see fit").
- 0.1.0 (release 48ab2295): Melty test e084ca6e passed 3/3 runs of 5 min (all 7 own checks each, footage reviewed), graduates.
  Earlier tests: 1 black footage (ROUNDS window in front, cancelled), 2 "BepInEx loaded" not seen (BepInEx was copied to a
  profile; now runs in place from UserData/VampireRounds/rounds).
- Listing pictures: media/shots run9 (cover), pick, charsel.
- Install recipe: tools/recipe.json (fileName gets the version). Melty drops runtimeData under {game}.
- Test c0e9bba1 of 0.1.0 (creator asked for a Melty test, not publishing): run 1 FAIL in review - Explosive Bullet killed
  Orange 18 s after the pick (ROUNDS explosions hurt their owner; the horde touches the player and the gun aims at the
  closest enemy). 0.1.1 (release 3961f600): ArenaEffectPatches.Blast blocks every non-heal blast on Orange/Blue, own or
  partner's. Dev aid: VAMPIREROUNDS_DEV_CARDS / run.ps1 -Cards gives cards at each run start.
  Verified locally with -Cards "Explosive Bullet": 12 own blasts kept off in ~1 min, hp only lost to VS hits (the
  engine's status line now counts "own blasts kept off"). Final 0.1.1 = release 3fe05e92; Melty test 434716f8 passed 3/3 (footage reviewed), graduates. Still a draft: creator chose not to publish yet (2026-10-19).

## Learned while testing (2026-10-19)
- VS 1.16.111 (Unity 6000.0.62) + MelonLoader 0.7.3 crashes in coreclr ~2 s after start with ANY mod unless Il2CppInterop's
  Class_GetFieldDefaultValue_Hook.FindTargetMethod is given the right address: src/InteropFix (pattern from Isaac Survivors's
  VS6InteropFix; GetDefaultFieldValue at GameAssembly+... found once).
- VS's own contact damage: GetDamaged is called ~100x/s per surrounded player; VS paces real hits inside TakeDamage. Hooking
  TakeDamage as prefix+postfix (measure from full HP, restore) keeps VS's pacing; block Die while ROUNDS's player lives.
- ROUNDS dies only below 0 health; Vector2 damage under ~1e-5 counts as zero: a player at exactly 0 never dies -> engine
  kills it below 0.5.
- VS draws after-image trails (SpriteTrail on the character, ghosts under "SpriteTrails") and a HealthBar: hide both while
  ROUNDS draws the player.
- Adventure merchants (Stage.SpawnAdventureMerchants, SpawnCustomMerchants, SpawnMerchant, TilingTileset.SpawnAdventureMerchant)
  sell VS weapons: blocked in our adventure.
- Adventure menus: another adventure's animated backdrop only gets sprites for its own adventure (white otherwise):
  ResetBackgroundToMainGame + hide CustomBackgroundHolder children. Adventure card thumbnail: GetBackgroundForAdventureType(POES).
- Adventure titles: SpriteManager.Sprites (global by name), not only SpriteTextureReference.
- Character select tiles look up "charsel" in the character's own texture sheet (one sheet per hero).
- Level label: MainGamePage.AssignLevel after a pick (the level-up page normally does it).
- ROUNDS in -batchmode still opens a full-screen window that takes focus (and Melty records it, black): hide by EnumWindows
  per pid (Mono's MainWindowHandle finds nothing), move to -32000 as WS_EX_TOOLWINDOW|NOACTIVATE, give VS focus back.
- Melty launches VS through Steam (launch kind "game"): no command-line args arrive; ROUNDS is found via Steam libraries.
- VS start-of-run arcana pick comes from the player's main save (RELIC); autoplay skips it.

## Art (2026-10-19)
- Cover art painted in code by tools/cover.html (canvas: ROUNDS's faceted balls as jittered flat-shaded icospheres, VS's
  blood moon, castle and gold title; fonts Cinzel + Lilita One from Google Fonts), rendered by tools/cover.py with headless
  Edge (--dump-dom of the canvases' data URLs). Edge only starts from PowerShell/cmd: the sandboxed bash exits it at once.
- media/art/card.png = Melty game card (1920x1080, Orange as the Count, Blue leaping across the moon, bat balls).
- media/art/adventure_bg.png = the adventure's menu backdrop: 640x360 pixel art (VS's own backdrops are ~that) shown 3x;
  package.py makes adventure_bg.rgba from it (tools/cover.py --raw). Layout fits VS's menus: panel covers x 29..71% from
  y 10%, VS shows 16:10 (x 5..95%); the Count stands in the right strip, the moon's crown in the top band.
- VS's adventure backdrop is a UI prefab: AdventureItemUI.SetData instantiates it in the card's _BackgroundContainer;
  on confirm SelectAdventuresPage moves that copy behind the menus (SetParent + CanvasGroup fade). Ours: RectTransform
  stretched to its parent + CanvasGroup + RectMask2D, child Image with AspectRatioFitter EnvelopeParent.
- Seen in game (dev run, 0.1.2 build): the adventure card shows it. Entering the adventure from the list never calls
  SetBackgroundForAdventure, and the card's copy does not end up behind the menus (VS's own backdrop prefab there is
  "ANIMATED BACKGROUND/Art"); the holder is UI/Canvas - App/Safe Area/View - MenuBackground/CustomBackgroundHolder, beside
  Background, LeftVamp, MiddleVamp, RightVamp, the title and AdventureSubtitle. So Content.MenuTick now puts a copy in the
  holder twice a second in the adventure's menus (Content.MenuBackdrop). That fix is built but not yet seen in game.

## Mods, boot, names, look (2026-10-19, creator's requests after testing 0.1.1)
- Thunderstore card mods like Roundhead: src/Launcher is Roundhead's launcher (Thunderstore.cs, ProfileManager.cs verbatim
  apart from names; "Card mods only" ticked by default). The recipe now launches `{managed}/VampireRoundsLauncher.exe
  --vs {game} --rounds {game:custom-rounds} --managed {managed}` (kind exe, mapping managed/ -> {managed}); PLAY starts
  VampireSurvivors.exe directly (SteamAppId 1794680, `--vr-rounds <ROUNDS>`, VAMPIREROUNDS_ROUNDS) and waits for it;
  Melty test runs (vampire-rounds.json) skip the window and start VS windowed 1280x720.
- Mods go into the BepInEx the hidden ROUNDS already runs in place (VS's UserData/VampireRounds/rounds/BepInEx/plugins/
  <Owner-Name>, mods.json beside BepInEx), not a LOCALAPPDATA profile (the 0.1.0 profile copy lost "BepInEx loaded").
  Verified: --install Pykess-PCE put PCE + 11 dependencies there; the engine loaded 12 plugins, 122 cards, 14 mod hooks
  (PCE, ModdingUtils), a PCE card (Super Ball) came up in a pick. Test mods removed again from the dev install.
- Boot (src/VsMod/Boot.cs): once per VS start, WarningPage.Complete -> LandingScreenPage.MoveToNextView ->
  MainMenuPage.ShowAdventuresView -> SelectAdventuresPage.SelectAdventure(ours) + ConfirmAdventure; ~6 s to the
  adventure's menu. AutoPlay waits for Boot.Done.
- Names: Orange Guy / Blue Guy, no surname (characters surname "none" -> "", I2 texts "---" = I2's empty text).
- VS sorting: everything on sorting layer Default; VS's hero stays at order 0 and enemies/props get order (heroY - y)*100,
  so lower on screen draws in front; the Mad Forest's treetops are tilemap Overlay1 at order 1, trunks Obstacle -2100,
  pickups/chests ~1990, camera transparency sort Default. The ROUNDS layer was at 32000 (over everything); it now takes
  the hidden character renderer's sorting layer and order (0), like Roundhead on Cuphead's Player layer.
- Character select sizes pictures by their pixels, ignoring pixelsPerUnit: 3.2 UI units a pixel (tile 190 units, info
  panel frame 4x). VS's Whip icon is 16 px. gun_icon is now drawn at 16 px (tools/art.py); portraits are rendered at
  240 px and loaded at 40 px (128 units).
- Portraits: a posed copy of ROUNDS's player prefab (Roundhead's Puppet approach: only look scripts on, own floor on the
  IK legs' layer, aim right, gun at HandPos, layer 30 only), 90 frames to settle, then a probe render finds the drawn
  area and a second render frames it square. The live players are parked between runs (legs hanging, gun up), which
  made the old portraits look odd. The off hand (white ball, FollowInactiveHand) is ROUNDS's own look.

## Faces (2026-10-20, creator: "I want to be able to edit faces"; chose the launcher route)
- ROUNDS's faces: CharacterCreatorHandler.playerFaces[10] presets (PlayerPrefs eyeID-<n>, mouthID-<n>, detailID-<n>,
  detail2ID-<n> + offsets; registry HKCU\Software\Landfall Games\ROUNDS, names with _h<hash>), selectedPlayerFaces[4] from
  SelectedFace<player>. ROUNDS's ONLINE menu shows only 8 (the editable ones): right-click one (gamepad: Y) opens its
  CharacterCreator (EYES / MOUTHS / DETAIL1 / DETAIL2, DONE = SetFacePreset + SelectFace(0)). On this PC both
  SelectedFace0 and SelectedFace1 were 0, hence two identical sunglasses faces.
- Launcher FACES window (src/Launcher/FacesForm.cs, Faces.cs): 8 tiles per guy, click = choice in
  %LOCALAPPDATA%/VampireRounds/faces.txt ("orange=5", "blue=6"); default = ROUNDS's own SelectedFace0/1. "Edit faces in
  ROUNDS" starts Rounds.exe plainly (SteamAppId, no Doorstop args, so our engine stays out) and waits for it to close.
- Engine Faces.ApplyChoice (after cards are ready, before Arena.Create): selectedPlayerFaces[seat] = GetFacePreset(choice),
  in memory only (no SelectFace, so ROUNDS's PlayerPrefs are untouched); the arena's players (GetFaceOffline), the pick
  screen (CardChoiceVisuals) and the portraits read it. Verified: face 6 (top hat) on Orange Guy in a run, face 7 (horns)
  in Blue Guy's portrait.
- Pictures: the launcher runs ROUNDS hidden with --vampirerounds-engine --vampirerounds-faces (no VS, no link); the
  engine waits for the face handler, item loader and skin bank, poses each seat (Portraits.Pose), hides the gun, clears
  the face parts and equips each preset (ROUNDS's Equip skips a missing part and would leave the last one), renders a
  1.8-unit square around the head at 128 px into faces/seat<s>_face<n>.png, writes done.txt and exits (~10 s with
  12 card mods). The launcher keeps the window off screen like EngineRunner, stores a signature of ROUNDS's face values
  and redraws when they change; PLAY and Edit stop a drawing run first.

## Face editor in the launcher, face colours (2026-10-20, creator: faces were monochrome; wanted the editor in the launcher)
- Colours: on a ROUNDS player the face parts use material SFUnshadowed (shader Sprites/SFSoftShadow), which only shows
  colour under ROUNDS's soft-shadow light pass; the engine's cameras have none, so parts came out near-black (the star,
  sweat drop, bandana, horns lost their colour). FaceLook swaps that material for Unity's Sprites/Default on every equip
  (Harmony postfix on CharacterCreatorItemEquipper.Equip, hooks e_face_colours) and on the portrait pose. Verified: blue
  sweat drop in a VS run. (A first try matched "SF*" on the shader name; the shader is Sprites/SFSoftShadow.)
- The "Edit faces in ROUNDS" button (opening ROUNDS itself) is gone: the creator wanted the editor in the launcher.
- Parts export (--vampirerounds-faces): after the eight preset pictures per seat, the pose's face is cleared and the bare
  body rendered (faces/parts/body<seat>.png), then every CharacterCreatorItemLoader part (85 eyes, 58 mouths, 54
  accessories here, accessory 0 is ROUNDS's invisible "none") is equipped alone at offset 0, put on layer 31 and rendered
  in the same frame (3 player scales around Face, the equipper's itemParent, 512 px). parts.txt: "frame F size N
  pxPerOffset P clamp 4.5", "part <kind> <i> <sortingOrder> <defX> <defY> <name>", "preset <i> <face>", "selected <p> <n>".
  pxPerOffset = size/frame x |Face.lossyScale.x| x scaleM (28.7 px at 512). Whole run ~10 s, 2 MB.
- Launcher: FaceParts lays a face together (body, then parts by sortingOrder, each moved by offset x pxPerOffset, y up);
  checked against the engine's own pictures. FacesForm: each guy's face + EDIT, the eight ROUNDS faces to start from.
  FaceEditorForm: EYES / MOUTH / DETAIL 1 / DETAIL 2 grids (each part on the guy's head), drag on the face (topmost part
  with paint under the mouse), a new part starts at its own place (as ROUNDS's SpawnItem), offsets kept within 4.5 of
  the centre (ROUNDS's CharacterCreatorDragging clamp). faces.txt is now "orange=<eyes id x y>  <mouth ...>  <detail ...>
  <detail2 ...>" (src/Shared/FaceFile.cs, also built into the launcher); the engine makes a PlayerFace from it.
- Verified by driving the windows (posted clicks/drags): mouth 6 chosen, sunglasses dragged, Save wrote faces.txt, the
  next VS run's Orange Guy and his portrait wore it. Test faces removed afterwards.
- Roundhead (C:\Users\mrnic\Documents\GitHub\Roundhead\CupRounds) has an unfinished, uncommitted in-game face picker
  (src/CardEngine/FacePicker.cs, ROUNDS's own strip streamed into Cuphead's title menu); its launcher is the same as ours,
  so this editor can go there as a PR.
- 0.2.0 (2026-10-20): commit a502351 pushed to github.com/neednotapply/vampire-rounds main; Melty draft release a4d528fa
  (upload 3104643e, one click: yes, launch kind exe). Not tested on Melty yet: publishing needs a passing test of 0.2.0.
