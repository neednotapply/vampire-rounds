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
