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

## 0.3.0 (2026-10-20, creator: card display, straight into the menu, more levels, card logos in the item slots, the
## shrinking card, test with their Thunderstore mods, balance)
- Design agreed: all of VS's main stages that need no DLC (Mad Forest, Inlaid Library, Dairy Plant, Gallo Tower, Cappella
  Magna), unlocked as in VS's adventures (a stage's goal opens the next on the chapter list; nothing moves the player
  there). Goals: survive 10 minutes (15 in Cappella Magna). Balance "for starters": less health and harder levels.
- Creator's mods (installed by the launcher on 2026-10-20 into VS's UserData/VampireRounds/rounds/BepInEx/plugins):
  PCE 2.7.11, XAngelMoonX-CR 2.7.0 and 28 dependencies (MapsExtended 1.4.2, Classes Manager Reborn, RarityLib, ...);
  225 cards. Their play log (2026-10-20 19:03-20:04, copied to ~/vsr-work/logs/creator-2026-10-20): 26 cards in about
  14 minutes of play (10 from chests, 6 of them within 3 minutes), 1236 hp, health always full.
- Shrinking card: MapsExtended's CardVisuals.ChangeSelected postfix multiplies ScaleShake.targetScale by
  MainCam.orthographicSize / 20 on every call, and ChangeSelected returns early (no reset) when the state is unchanged;
  our DoPlayerSelect re-sent card 0's state every 0.2 s (ROUNDS's network resync): card 0 shrank away. Now only a changed
  card is told, and the size it gets is ROUNDS's own (1.15 / 0.9). Same fix as Roundhead 0.1.4.
- "Cards that do not display": devtools/cards.ps1 (engine --vampirerounds-cards) pictured all 225 cards: every card with
  art renders right (UI/Default, UI/Procedural and Sprites/Default only; no art layer outside ROUNDS's main camera mask).
  41 cards have cardArt == null (40 PCE: Comb, Last Stand, Super Ball, Fireworks, ...; 1 CMR): ROUNDS itself shows them
  with an empty art space. CardLook.Emblem gives them ROUNDS's card-bar letters ("Co"), big, in the card colour
  (TextMeshProUGUI with the title's font, 4x its size), dimmed with the card when not chosen. The pick's big face (top
  hat) was drawn over the cards: card canvases now sort at 500 on MostFront (ROUNDS: 10).
- Card logos: CardIcons (engine) pictures a card's art square (0.8 of the face's width around Canvas/Front/Background/Art)
  from ROUNDS's own AddCardVisual at (30000, 30000) on layer 29, 0.5 s after it starts (real time; VS may have ROUNDS
  paused), 96 px, into %LOCALAPPDATA%\VampireRounds\cards\<key>.rgba; deck<seat>.txt + Q_DeckSerial (rseat 60).
  CardSlots (VS) puts copies of VS's WeaponSlot prefab (GameEquipmentPanel._WeaponPrefab, its Icon stretched to the
  frame, level pips and Blocked off) in a GridLayoutGroup beside WeaponsPanel (ignoreLayout), 5 per row at 1.25x VS's
  36-unit slot; WeaponsPanel and AccessoryPanel get CanvasGroup alpha 0. ROUNDS's own letter card bar is hidden
  (CanvasGroup alpha 0 on CardViz). VS's panel: EquipmentPanel(Clone) [HorizontalLayoutGroup] > PlayerIcon (off),
  Container [VerticalLayoutGroup] > WeaponsPanel / AccessoryPanel [GridLayoutGroup], slots 36x36 every 42 units.
- Straight into the menu: Boot.Gui covers the screen (OnGUI, over VS's canvases) with the adventure backdrop and title
  from the mod's first frame until the adventure's menu is up (18 s on this PC, most of it VS's own loading); it lifts
  after 12 s without a boot step or 60 s in all. Boot steps every 0.1 s instead of 0.5 s.
- Stages: slots TEST, TESTONE, ADV_0001TEST, ENTRANCE, CASTLE (no VS or DLC data uses them). Goal types: TEST001_ACH001
  and MS001_ACH019 (no adventure uses them), POES_ACH012-014 (A Garlic Paradise's; each adventure keeps its progress in
  its own PlayerOptionsData, AdventureManager.CurrentAdventureSaveData.AdventureProgress, so they never mix). VS looks
  goal texts up by type (progressLang/{TYPE}description): AchievementData.GetLocalizedDescription(AdventureAchievementType)
  is patched in our adventure only. A 0.2.0 save that met the Mad Forest goal (it opened nothing then) gets the Library
  opened at InitAdventure (VS never checks a met goal again); seen on the creator's save.
- Balance: start health 70 (StartHealth: postfix on CharacterStatModifiers.ResetStats and at player creation; ROUNDS
  sizes players by health, so they start ~6% smaller), growth 1.5 -> 0.8, a chest is a card pick at most once per 60 s
  of VS's run clock (GameManager.SurvivedSeconds; the others give their gold alone, with a note).
- Heals: the 0.2.0 Harmony prefix on CharacterController.RecoverHp(float, bool, bool, ItemType?, WeaponType?) threw in
  Il2CppInterop's trampoline (Buffer.Memmove NRE, 669 times in the creator's log): heals were lost. VS's own RecoverHp now
  runs; MirrorHealth sees VS's bar rise between frames and sends the difference to ROUNDS.
- Test and developer autoplay runs never write VS's save (prefix on PlayerOptions.Save); autoplay steps away from the
  horde (Enemies.Away). Dev switches: VAMPIREROUNDS_DEV_ALLSTAGES, VAMPIREROUNDS_AUTOPLAY_STAGE, VAMPIREROUNDS_DEV_GOALMINUTES.
- The creator's VS save was backed up to ~/vsr-work/save-backup before the dev runs (Steam userdata 64298853/1794680).
- Creator after seeing the art logos (2026-10-20): "the weapon icons should look like the letters, not the art; we're
  basically replacing the Vampire Survivors item UI with the Rounds one". The item area now shows ROUNDS's card bar:
  CardIcons.DrawChip copies the bar's template square (Bar1's hidden first child "Card": ProceduralImage frame 767676,
  "CarrdOrange" fill FFB32309, TMP text A6A6A6) with the letters ROUNDS gave the card, renders it over black and over
  white and keeps ArenaRenderer.UiFrame's coverage (the fill is a 3.5 % wash: one picture was see-through, as the
  creator saw). The live square was not copied: it fades in after a pick (copied then: empty). 10 squares to a row at
  VS's slot size (45 units), gap 3. The art logos (Picture/SaveLogo) stay for the developer card sheet only.
- Art logos at a run's start showed the card's back: a card made by AddCardVisual while ROUNDS's time stood still kept
  its Canvas turned (CurveAnimation "In" from (0,180,0), startRotation is that turned pose) and its face's CanvasGroup
  at 0. Fixed for the sheet by showing the face (CanvasGroup 1, Canvas/Back off) and running animators on; forcing the
  CurveAnimation start pose made it worse (it is the turned pose).
- 0.3.0 published 2026-10-21 (release ca43b46b, Melty test 67578fc5 passed 3/3, footage reviewed); went into Melty's
  safety review.

## 0.4.0 (2026-10-21, creator: "text art" cards, effects that don't fit top-down, near-infinite health, Blue's squares,
## the test player should block, avoid walls and try many cards)
- "Text art" (Fr, Th): PCE's own code returns no art for 42 of its cards (GetCardArt() => null: Friendly Bullets, Thank You
  Sir, Comb, Super Ball, ...) and CMR's Mastering the Trade has none either; ROUNDS shows them blank, 0.3.0's emblem is
  ours. Every other card's art loads (PCE's asset bundle works: Shuffle, Murder, Pac-Bullets have theirs).
- Pick cards dealt face down: ROUNDS's own reveal (Canvas CurveAnimation starts Out at (0,180,0); ChangeSelected(true)
  plays it In the first time the cursor reaches a card). Seen in the dev pick log; not a bug.
- Health: VS lands ~15 hits/s on a guy in a horde of 100 at 5 min; 0.3.0 let one through per 0.4 s and dropped the rest
  (907 of 1073 in the creator's log), and Beetle (CR: +5 regen below max) out-healed what was left. Now hits within
  playerHitCooldown are summed into the next one (Arena.LandHits/LandWaitingHits; "merged" in the status line), every
  ROUNDS heal on Orange/Blue is x roundsHealScale 0.15 (HealScale prefix on HealthHandler.Heal; VS's own heals are
  x healScale 0.5 and flagged Arena.VsHeal), and lifesteal/on-damage rewards count only what the enemy had left
  (VsTarget.Hit, TargetStatus.hpLeft from VS's T_Hp / damageScale): a Flamethrower+lifesteal hit healed 8250 before.
- Shots "hitting the ceiling": MapsExtended transpiles ScreenEdgeBounce.Update to measure ROUNDS's map (71 x 40 at the
  origin) instead of the camera; every bouncing bullet in the arena (1000 below) was "under the map", bounced up from
  (35.6, -20) and its trail ran off the top of the screen. ArenaTargetPatches.EdgeBounceOnVSView (first-priority prefix)
  runs ROUNDS's rule against ArenaViewCam.View and skips the transpiled method for arena bullets.
- Bounces: bullets now bounce off VS enemies as off walls (the 0.1.x rule ending them on an enemy is gone), so Bouncy,
  Riccochet, Mayhem, Super Ball and Trickster chain through a horde; Target Bounce re-aims at the next enemy, never the one
  it bounced off (VsTarget.Closest skipGroup).
- Jumps: PlayerJump.Jump on Orange/Blue starts a hop (Arena.StartHop, HopPatch): the body rises over its spot and lands
  (tuning hopSeconds 0.6 x sqrt(strength), hopHeight 2.2 x strength), and VS hits do not land meanwhile ("hopped over").
  Pogo hops every 4 s, Super Jump (walk down to charge) hops high. Jetpack/Old Jetpack/extra jumps need ROUNDS's jump
  button, which VS has none of: still unused (asked the creator).
- Card-mod bullets that change one world axis (CR ShootingStarMono y x1.025, CometMono x x1.1, MeteorMono y x1.5 while
  falling, CloudMono x0.9/y0.85, CareenMono zeroing small downward speed = "ground avoidance") bent shots north/south or
  flattened them; AxisBullets undoes each change and scales the speed along the path instead (Careen: nothing).
- Gravity and pulls reach VS: FxGravity (Grounded, Crush: slowed; Flip, Toss: lifted = frozen, capped), FxPull (Implode:
  most of the way to the blast). Thruster's lasting push builds a slide (Enemies.Drift, enemyDriftMax 5, fade 2.5/s);
  measured force 73695 with two Thrusters.
- Blue's card-bar squares: ROUNDS's Bar2 template fill is CardBlue 96DCFF05 (pale wash) vs Orange's CarrdOrange FFB32309;
  over VS it came out grey. CardIcons.StrongFill raises the fill's saturation/alpha to Orange's in its own hue.
- Test player: blocks when an enemy is within 0.9 VS units (new check block-used, in the recipe), slides along a wall when
  it has not moved for 0.5 s, and steps 0-4 cards right before taking one. Developers: VAMPIREROUNDS_DEV_CARDS takes
  "random:N" (N cards from every card, mods' included, logged per run); dev logs of picks, bullets, lines and heals.
- Player report (Melty, 0.3.0, another player's PC): "boot: open the Vampire Rounds adventure failed: NullReferenceException
  at SelectAdventuresPage.ConfirmAdventure" and "Cannot generate navigation with no Adventures listed". VS's
  SelectAdventuresPage.Populate asks HasCollectedItem(RELIC_ATLAS) before each base-game adventure (seen with a dev probe:
  DevAdventureProbe, dev-only, VAMPIREROUNDS_DEV_NO_ATLAS hides the Atlas); ADV_LIB_001 is one of those slots, so a save
  without the Atlas listed nothing. Content.EnsureListed (Populate postfix) adds our card when it is missing; verified with
  the Atlas hidden: card added, boot reached the adventure's menu, the run started and fired.
- 0.4.0 published 2026-10-21 (release 7bacbbce, Melty test 592f225d passed 3/3, footage reviewed); in Melty's safety
  review for 2+ hours (0.3.0 took ~30 min), publish requested again at 22:4x UTC.
- Player report (load:errors, 1 player, 0.3.0) re-checked against 0.4.0 in the reporting player's exact state: dev
  switches VAMPIREROUNDS_DEV_NO_ATLAS + VAMPIREROUNDS_DEV_NO_DLC (IsOwned false for every other adventure) +
  VAMPIREROUNDS_DEV_FRESH (HasSeenAdventuresIntroTutorial false, in memory; the run plays by itself so VS never saves):
  VS listed 0 adventures, EnsureListed added ours (1 card), boot reached the adventure's menu (19.8 s), autoplay started
  a run that fired and hit. No new release needed; the fix record (report_bug_fix) carries this evidence.

## 0.4.1 (2026-10-24, players' Melty: "load errors" on 0.4.0, "freezes ~1:23 into a Play" on 0.4.0)
- Both 0.4.0 reports are one player's very first start (the 0.3.0 report under load:errors was the Atlas problem 0.4.0
  fixed): MelonLoader 0.7.3 builds VS's Il2Cpp assemblies (Cpp2IL + Il2CppInterop, ~90 s here) on VS's main thread inside
  its il2cpp_runtime_invoke hook, so VS's window takes no messages: Windows marks it Not Responding (seen here with a
  window watcher: HUNG for up to ~40 s at a time) and the player closed it after 83 s, mid-build ("Creating field
  accessors..."). Our mod and ROUNDS had not even started; MelonLoader 0.7.3's MelonStartScreen.LoadAndRun is a stub
  that just calls the build.
- "Load errors": InteropFix's AccessTools.TypeByName walked every loaded type; before the build Il2CppInterop.Runtime's
  types that need Il2Cppmscorlib cannot load, and HarmonyX logged the ReflectionTypeLoadException warning. Now the hook
  class is asked of Il2CppInterop.Runtime itself (Plugin.HookType). A .NET 6 experiment (Il2CppInterop.Runtime from ML
  0.7.3, Il2Cppmscorlib from ~/vsr-work/vs-interop) confirmed: TypeByName loads Il2Cppmscorlib, the direct lookup and the
  FindTargetMethod patch do not.
- Found while testing: TypeByName loading Il2Cppmscorlib also LOCKS it, and a rebuild (after any VS or MelonLoader update)
  starts by deleting the old assemblies: "UnauthorizedAccessException ... Il2Cppmscorlib.dll is denied" in
  OldFiles_Cleanup, "Failed to start MelonLoader", the game crashes (e0434352) and the half-deleted Il2CppAssemblies
  stays broken at every start (the hash is not saved). 0.4.0's plugin did this to every player at the next VS update;
  Isaac Survivors's VS6InteropFix (installed on this PC) still does. A loaded assembly's file can be renamed, not deleted.
- FirstStart (InteropFix): patches the generator module as it loads (AppDomain.AssemblyLoad; MelonLoader.dll is
  [PatchShield], Il2CppAssemblyGenerator.dll is not): Core.Run runs on a worker thread while the main thread pumps the
  window's messages after 2 s (MsgWaitForMultipleObjectsEx + PeekMessageW/DispatchMessageW, WM_QUIT kept for VS's loop),
  so the close button MelonLoader greys out for the build now holds; OldFiles_Cleanup / OldFiles_LAM prefixes delete each
  old file themselves or rename one in use to "<name>.vr-old", deleted at the next start. Dev switches
  VAMPIREROUNDS_DEV_NOPUMP / VAMPIREROUNDS_DEV_NOSETASIDE give the old behaviour.
- Launcher: LoaderWatch reads MelonLoader/Latest.log after Play (this start's log: first line's time) and on "Assembly
  Generation Needed!" shows FirstStartNotice (topmost, no focus, not in Melty test runs) with the step until "Loading
  Mods..."; launcher.log keeps how long it took. DevLaunch: %LOCALAPPDATA%\VampireRounds\dev-launch.txt, one line per
  coming VS start ("regenerate" = --melonloader.agfregenerate, VAMPIREROUNDS_*=value), so Melty test runs can be first
  starts.
- Melty test 544e58bc (first 0.4.1 build, run 1 = "regenerate VAMPIREROUNDS_DEV_NOPUMP=1"): reproduced the freeze (window
  HUNG, Melty closed it at 1:43) and exposed the lock above (that run's failed rebuild left this PC's Il2CppAssemblies
  half deleted). Release 0850bfef (pump + set aside); Melty test b8736526 passed 3/3, run 1 = "regenerate": build 85.9 s,
  window answered throughout (longest gap 198 ms, never HUNG), Il2Cppmscorlib.dll set aside, "Assembly Generation
  Successful!" (this PC repaired), mod loaded and played; runs 2-3 ordinary starts.
- That run's footage showed the answering window blank white for the build (VS draws nothing before its first frame):
  FirstStartNote paints the note into it (GDI, from the pump's WM_PAINT; redrawn each second without erasing; rendered
  to bitmaps first to check the layout), and "Done: starting Vampire Rounds..." once the build ends (MelonLoader then
  loads the mods for ~10 s without drawing). Release ebb3ecf8 (note, without "Done"): Melty test 6a8292b3 run 1 showed the
  note with its clock for the whole 108 s build; one 6.5 s stretch without answers fell in "Writing assemblies" (likely a
  blocking full GC of the generator's heap, which stops every managed thread): the pump now runs under
  GCLatencyMode.SustainedLowLatency and logs the longest gap's full collections and slowest message.
- Final 0.4.1 = release fb358543 (also: the launcher shows its own note only when the plugin's "VS's window shows what is
  happening" line has not come 6 s after "Assembly Generation Needed!"; checked offline with a made-up log both ways).
  Melty test 9c836692 run 1 ("regenerate"): build 113.4 s; longest gap 12.1 s at 108 s with 2 full collections and the
  slowest message 1 ms, so it IS the GC (memory pressure on this PC; SustainedLowLatency did not stop them), window HUNG
  for ~4.6 s of it; launcher log: "The game's window shows what is happening meanwhile." Earlier builds' longest gaps:
  198 ms, 6.5 s. If it matters later: pump from native code (a CLR STA wait, or a hidden modal dialog's loop) so a GC
  cannot stop it. After the build MelonLoader's own mod loading and Il2CppInterop setup keep the main thread ~4-8 s.
- Test 9c836692 passed 3/3 (footage reviewed, graduates). Publish requested 2026-10-24 (release fb358543, Melty's safety
  review); report_bug_fix recorded load:errors and crash:hung:vampiresurvivors.exe against it. This PC's MelonLoader
  assemblies are whole again (the first 0.4.1 test's failed rebuild was repaired by the next test's rebuild).
- 0.4.1 live 2026-10-24 18:46 UTC (safety review ~30 min); player_logs shows both issues resolved.
