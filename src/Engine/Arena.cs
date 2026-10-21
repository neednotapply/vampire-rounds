using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VampireRounds
{
    /// <summary>
    /// The real ROUNDS game running Orange and Blue for Vampire Survivors (sheets/systems.json "arena"). A map made here
    /// starts ROUNDS's own sandbox (GM_Test), so the players are ROUNDS's players: its gun, bullets, block, health and every
    /// card effect, mod cards included. VS moves them (each is held where its VS character stands), VS's enemies are
    /// mirrored in as trigger circles, the screen edges are walls, and ROUNDS sends back bullet hits, health and its picture
    /// through <see cref="ArenaLink"/>. Adapted from Roundhead's arena (MIT, the same creator).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class Arena : MonoBehaviour
    {
        public static Arena I;
        public ArenaLink link;

        /// <summary>The arena sits this far below ROUNDS's menu and card-pick area (ROUNDS units).</summary>
        public const float OffsetY = -1000f;
        /// <summary>Each seat's own floor under its feet (only its legs and body see it), screen-edge walls (0), enemies (15).</summary>
        private const int FloorLayer0 = 6, FloorLayer1 = 7;
        public const int SolidLayer = 0, BulletFloorLayer = 17, TargetLayer = 15;
        public const int Seats = ArenaLink.Seats;

        private enum Phase { Idle, MapStarting, Ready }
        private Phase phase = Phase.Idle;
        private GameObject mapGo;
        private readonly Player[] players = new Player[Seats];
        private readonly bool[] creating = new bool[Seats];
        private readonly int[] seenBlock = new int[Seats], seenAccept = new int[Seats], seenNav = new int[Seats], blockCount = new int[Seats];
        private readonly Vector2[] lastMouse = { new Vector2(-1, -1), new Vector2(-1, -1) };
        private readonly GameObject[] floors = new GameObject[Seats];
        private readonly GameObject[] walls = new GameObject[4];
        private int eventRead;
        private int runSeen = -1;
        private int pickSeen;
        private bool picking;
        private int pickSlot = -1;
        private float scale = 0.5f;
        private int mode;
        private float lastControlBeat = -1f;
        private int lastBeatValue = -1;
        private float nextPidCheck;
        private bool paused;
        private readonly int[] life = new int[Seats];
        private readonly int[] shots = new int[Seats];
        private readonly HashSet<string> stepErrors = new HashSet<string>();
        public bool Paused => paused;
        /// <summary>True while a VS hit is being applied to a ROUNDS player (it has no ROUNDS attacker).</summary>
        public static bool VsHit;
        /// <summary>True while a VS heal (Floor Chicken, a partner's) is being applied: already in VS's measure (HealScale).</summary>
        public static bool VsHeal;

        private static readonly AccessTools.FieldRef<PlayerVelocity, Vector2> VelRef = AccessTools.FieldRefAccess<PlayerVelocity, Vector2>("velocity");
        private static readonly AccessTools.FieldRef<PlayerVelocity, bool> SimRef = AccessTools.FieldRefAccess<PlayerVelocity, bool>("simulated");
        private static readonly AccessTools.FieldRef<PlayerVelocity, bool> KinematicRef = AccessTools.FieldRefAccess<PlayerVelocity, bool>("isKinematic");

        /// <summary>One mirrored VS enemy: a trigger circle with a <see cref="VsTarget"/>, reused across enemies (pooled).</summary>
        private sealed class Mirror
        {
            public GameObject go;
            public VsTarget target;
            public CircleCollider2D col;
            public int id;
            public float r = -1f;
        }
        private readonly Dictionary<int, Mirror> mirrors = new Dictionary<int, Mirror>();
        private readonly Stack<Mirror> spare = new Stack<Mirror>();
        private readonly HashSet<int> seenIds = new HashSet<int>();
        private readonly List<int> gone = new List<int>();

        public static void Create(GameObject host)
        {
            var link = ArenaLink.Open();
            if (link == null) { EnginePlugin.L.LogWarning("arena: could not open shared memory: " + ArenaLink.LastOpenNote); return; }
            I = host.AddComponent<Arena>();
            I.link = link;
            // A ROUNDS started (or started again) while VS runs only acts on what VS sends from now on.
            I.eventRead = link.I(ArenaLink.C_EventWrite);
            I.pickSeen = link.I(ArenaLink.C_Pick + ArenaLink.P_Serial);
            link.I(ArenaLink.ResultOff + ArenaLink.R_Magic, ArenaLink.Magic);
            link.I(ArenaLink.ResultOff + ArenaLink.R_Pid, System.Diagnostics.Process.GetCurrentProcess().Id);
            // The status table's sequence lock starts even: a ROUNDS that died while writing it (WriteEffects) left it odd.
            int sf = link.I(ArenaLink.ResultOff + ArenaLink.R_StatusFrame);
            if ((sf & 1) != 0) link.I(ArenaLink.ResultOff + ArenaLink.R_StatusFrame, sf + 1);
            host.AddComponent<ArenaRenderer>();
            host.AddComponent<ArenaViewCam>();
            var harmony = new Harmony("vampirerounds.arena");
            foreach (var t in typeof(Arena).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0 && !t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Any(m => m.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)) continue;
                if (t == typeof(SteamQuiet.SteamQuietPatch)) continue; // applied at start (SteamQuiet.Apply)
                try { harmony.CreateClassProcessor(t, true).Patch(); }
                catch (Exception e) { EnginePlugin.L.LogWarning("arena: patches in " + t.Name + " not applied: " + e.Message); }
            }
            // Patches on card-mod classes (found by name at runtime, so the engine needs no mod at build time).
            try { ArenaModHooks.Apply(harmony); } catch (Exception e) { EnginePlugin.L.LogWarning("arena: mod hooks: " + e.Message); }
            try { AxisBullets.Apply(harmony); } catch (Exception e) { EnginePlugin.L.LogWarning("arena: axis bullets: " + e.Message); }
            EnginePlugin.L.LogInfo("arena: shared memory " + ArenaLink.LastOpenNote);
        }

        public Vector2 ToRounds(float vx, float vy) => new Vector2(vx / scale, vy / scale + OffsetY);
        public Vector2 ToVs(Vector2 r) => new Vector2(r.x * scale, (r.y - OffsetY) * scale);
        public int Mode => mode;
        public bool Picking => picking;
        public bool WorldReady => phase == Phase.Ready;
        /// <summary>VS units per ROUNDS unit.</summary>
        public float Scale => scale;

        public int SlotOf(Player p)
        {
            for (int i = 0; i < Seats; i++) if (players[i] != null && players[i] == p) return i;
            return -1;
        }

        public int SlotOf(CharacterData d) => d == null ? -1 : SlotOf(d.player);

        public int SlotOfPlayerId(int playerId)
        {
            for (int i = 0; i < Seats; i++) if (players[i] != null && players[i].playerID == playerId) return i;
            return -1;
        }

        private bool Present(int seat) => link.I(ArenaLink.Slot(seat) + ArenaLink.S_Present) != 0;

        // ------------------------------------------------------------------ main loop

        private void Update()
        {
            // Heartbeat first: VS calls ROUNDS gone when this beat stands still, so nothing below may stop it.
            link.I(ArenaLink.ResultOff + ArenaLink.R_Magic, ArenaLink.Magic);
            link.Inc(ArenaLink.ResultOff + ArenaLink.R_Beat);
            if (Time.unscaledTime > nextCardCount) { nextCardCount = Time.unscaledTime + 2f; cardCount = Cards.All().Count; }
            link.I(ArenaLink.ResultOff + ArenaLink.R_CardCount, cardCount);
            int beat = link.I(ArenaLink.C_Beat);
            if (beat != lastBeatValue) { lastBeatValue = beat; lastControlBeat = Time.unscaledTime; }
            bool vsAlive = link.I(ArenaLink.C_Magic) == ArenaLink.Magic && Time.unscaledTime - lastControlBeat < Tuning.engineStaleSeconds;
            // Only VS's process going away ends this hidden ROUNDS. A VS that stops beating for a while (alt-tab, a long load)
            // comes back, and ROUNDS's players must still be here when it does.
            if (EnginePlugin.Hidden && Time.unscaledTime > nextPidCheck)
            {
                nextPidCheck = Time.unscaledTime + 2f;
                EnginePlugin.CheckVs(link);
            }

            if (vsAlive) mode = link.I(ArenaLink.C_Mode);
            paused = !vsAlive || link.I(ArenaLink.C_Paused) != 0;
            float s = link.F(ArenaLink.C_Scale);
            if (s > 0.0001f) scale = s;
            Step("world", () =>
            {
                if (phase != Phase.Idle && mapGo == null) LoseWorld();
                // The sandbox starts as soon as VS is there, so the first run finds the players ready.
                if (vsAlive && phase == Phase.Idle) StartWorld();
                if (phase == Phase.MapStarting && GameManager.instance != null && GameManager.instance.isPlaying && MapManager.instance.currentMap != null) phase = Phase.Ready;
            });

            if (phase == Phase.Ready)
            {
                Step("players", SpawnPlayers);
                Step("run", CheckRun);
                Step("targets", SyncTargets);
                Step("walls", PlaceWalls);
                Step("events", ReadEvents);
                Step("hits", LandWaitingHits);
                Step("lives", CheckLives);
                Step("status", () => { float dt = TimeHandler.deltaTime; foreach (var st in status.Values) st.Tick(dt); });
                Step("park", ParkOrPlay);
                Step("hold", HoldPlayers);
                Step("pick", CheckPick);
                Step("portraits", () => Portraits.Tick(players));
                Step("decks", () => CardIcons.Tick(players, link, this));
                Step("log", LogState);
                if (DevShots) Step("dev bullets", DevBullets);
            }
            Step("result", WriteResult);
            Step("sound", () => Sound(vsAlive));
        }

        private float nextMusicMute, nextCardCount;
        private int cardCount;

        /// <summary>
        /// ROUNDS's own sounds (shots, hits, blocks, card effects, the pick screen) play from this hidden ROUNDS at VS's
        /// sound-effects volume (C_Volume), silent in VS's menus (mode 0), paused with VS. ROUNDS's music stays off.
        /// </summary>
        private void Sound(bool vsAlive)
        {
            float v = vsAlive && mode != 0 ? Mathf.Clamp01(link.F(ArenaLink.C_Volume)) : 0f;
            if ((v > 0f) != (AudioListener.volume > 0f)) EnginePlugin.L.LogInfo("arena: ROUNDS sound " + (v > 0f ? "on at " + v.ToString("0.00") : "off") + " (mode " + mode + ")");
            if (Mathf.Abs(AudioListener.volume - v) > 0.001f) AudioListener.volume = v;
            bool hush = paused && !picking;
            if (AudioListener.pause != hush) AudioListener.pause = hush;
            if (Time.unscaledTime < nextMusicMute || SoundImplementation.SoundVolumeManager.Instance == null) return;
            nextMusicMute = Time.unscaledTime + 1f;
            SoundImplementation.SoundVolumeManager.Instance.SetAudioMixerVolumes(1f, 0f, 1f);
        }

        /// <summary>Both ROUNDS players are made at once, Orange first, so their ROUNDS player ids are 0 and 1 (their colours).</summary>
        private void SpawnPlayers()
        {
            for (int i = 0; i < Seats; i++)
                if (players[i] == null && !creating[i])
                {
                    if (i > 0 && (players[i - 1] == null || creating[i - 1])) break;
                    StartCoroutine(MakePlayer(i));
                    break;
                }
        }

        /// <summary>A new VS run: fresh decks (ROUNDS's own reset), no enemies, no statuses.</summary>
        private void CheckRun()
        {
            int run = link.I(ArenaLink.C_Run);
            if (run == runSeen) return;
            bool first = runSeen < 0;
            runSeen = run;
            ClearTargets();
            for (int i = 0; i < Seats; i++) { pendingHit[i] = 0f; hopTime[i] = 0f; }
            for (int i = 0; i < Seats; i++)
                if (players[i] != null && !creating[i] && (!first || players[i].data.currentCards.Count > 0))
                {
                    ResetCards(players[i]);
                    try { players[i].data.healthHandler.Revive(true); } catch (Exception e) { EnginePlugin.L.LogWarning("arena: revive at run start: " + e.Message); }
                }
            CardIcons.Reset();
            link.I(ArenaLink.ResultOff + ArenaLink.R_Run, run);
            EnginePlugin.L.LogInfo("arena: run " + run + " starts: plain guns, no enemies");
            if (!first) DevStartCards();
        }

        /// <summary>Developer aid (VAMPIREROUNDS_DEV_CARDS="Explosive Bullet,Bouncy", never set for players): every seat starts
        /// each VS run with these cards, to try a card at once instead of waiting for it in a pick. Applied the way ROUNDS's
        /// offline pick does (ApplyCardStats.OFFLINE_Pick on a copy of the card).</summary>
        private void DevStartCards()
        {
            string names = Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_CARDS");
            if (string.IsNullOrEmpty(names) || CardChoice.instance == null) return;
            // "random:8" adds eight cards drawn from every card ROUNDS has (mods' included), different each run.
            var wanted = new List<string>();
            foreach (string raw in names.Split(','))
            {
                string w = raw.Trim();
                int n;
                if (w.StartsWith("random:", StringComparison.OrdinalIgnoreCase) && int.TryParse(w.Substring(7), out n))
                {
                    var pool = CardChoice.instance.cards.Where(c => c != null && !wanted.Contains(c.cardName)).OrderBy(c => UnityEngine.Random.value).Take(n);
                    foreach (var c in pool) wanted.Add(c.cardName);
                }
                else if (w.Length > 0) wanted.Add(w);
            }
            EnginePlugin.L.LogInfo("arena: dev cards this run: " + string.Join(", ", wanted.ToArray()));
            foreach (string want in wanted)
            {
                var card = CardChoice.instance.cards.FirstOrDefault(c => c != null && string.Equals(c.cardName, want, StringComparison.OrdinalIgnoreCase));
                if (card == null) { EnginePlugin.L.LogWarning("arena: dev card '" + want + "' not found"); continue; }
                for (int i = 0; i < Seats; i++)
                {
                    var p = players[i];
                    if (p == null || creating[i]) continue;
                    var go = Instantiate(card.gameObject, new Vector3(0f, OffsetY - 800f, 0f), Quaternion.identity);
                    try
                    {
                        go.GetComponent<CardInfo>().sourceCard = card;
                        var apply = go.GetComponent<ApplyCardStats>();
                        Traverse.Create(apply).Method("Start").GetValue();
                        apply.OFFLINE_Pick(new[] { p });
                        EnginePlugin.L.LogInfo("arena: dev card " + card.cardName + " for seat " + i);
                    }
                    catch (Exception e) { EnginePlugin.L.LogWarning("arena: dev card " + want + ": " + e.Message); }
                    finally { Destroy(go); }
                }
            }
        }

        /// <summary>One arena step: a failure is logged once per message and the frame goes on, so results keep flowing.</summary>
        private void Step(string name, Action step)
        {
            try { step(); }
            catch (Exception e) { if (stepErrors.Add(name + ": " + e.Message)) EnginePlugin.L.LogError("arena " + name + " failed: " + e); }
        }

        /// <summary>ROUNDS unloaded its scene: the sandbox, the players and the mirrors are gone, so build them again.</summary>
        private void LoseWorld()
        {
            EnginePlugin.L.LogWarning("arena: the ROUNDS sandbox was unloaded; starting it again");
            StopAllCoroutines();
            if (picking) link.I(ArenaLink.ResultOff + ArenaLink.R_PickDone, pickSeen); // stopped coroutines skip their finally
            phase = Phase.Idle;
            mirrors.Clear();
            spare.Clear();
            status.Clear();
            picking = false;
            pickSlot = -1;
            for (int i = 0; i < Seats; i++) { players[i] = null; creating[i] = false; floors[i] = null; }
            for (int i = 0; i < walls.Length; i++) walls[i] = null;
        }

        private void LateUpdate()
        {
            // After every card effect of this frame: summed DoT / continuous hits, coalesced effect events, the status table.
            if (phase == Phase.Ready) Step("effects", WriteEffects);
        }

        private void FixedUpdate()
        {
            if (phase != Phase.Ready) return;
            Step("hold", HoldPlayers);
        }

        private static float Radius(Player p)
        {
            var c = p.GetComponent<CircleCollider2D>();
            return c != null ? c.radius * p.transform.localScale.x : 0.7f;
        }

        // ------------------------------------------------------------------ world and players

        private void StartWorld()
        {
            phase = Phase.MapStarting;
            // A Map appearing while ROUNDS is not playing starts ROUNDS's own sandbox (Map.Awake -> GM_Test), offline.
            var scene = SceneManager.CreateScene("Vampire Rounds Arena");
            mapGo = new GameObject("Vampire Rounds Stage");
            mapGo.SetActive(false);
            SceneManager.MoveGameObjectToScene(mapGo, scene);
            mapGo.transform.position = Vector3.zero;
            var map = mapGo.AddComponent<Map>();
            map.wasSpawned = true;
            var sp = new GameObject("Spawn");
            sp.transform.SetParent(mapGo.transform, false);
            sp.transform.position = new Vector3(0f, OffsetY + 5f, 0f);
            sp.AddComponent<SpawnPoint>();
            mapGo.SetActive(true);
            EnginePlugin.L.LogInfo("arena: ROUNDS sandbox map created");
        }

        private IEnumerator MakePlayer(int slot)
        {
            creating[slot] = true;
            try
            {
                foreach (object step in MakePlayerSteps(slot)) yield return step;
            }
            finally { creating[slot] = false; }
        }

        private IEnumerable MakePlayerSteps(int slot)
        {
            int before = PlayerAssigner.instance.players.Count;
            yield return StartCoroutine(PlayerAssigner.instance.CreatePlayer(null, false));
            // CreatePlayer gives up silently (max players, waiting for Photon): never wait forever.
            float giveUp = Time.unscaledTime + 5f;
            while (PlayerAssigner.instance.players.Count <= before)
            {
                if (Time.unscaledTime > giveUp) { EnginePlugin.L.LogWarning("arena: ROUNDS did not make player " + (slot + 1) + "; trying again"); yield break; }
                yield return null;
            }
            var data = PlayerAssigner.instance.players[PlayerAssigner.instance.players.Count - 1];
            Traverse.Create(PlayerAssigner.instance).Method("SetPlayersCanJoin", false).GetValue();
            var p = data.player;
            players[slot] = p;
            StartHealth.Apply(p.data.stats);
            yield return null;
            try { p.GetFaceOffline(); } catch (Exception e) { EnginePlugin.L.LogWarning("arena face: " + e.Message); }
            // Each player stands on its own floor (so its legs step as VS walks it); bullets never see those floors.
            int own = slot == 0 ? FloorLayer0 : FloorLayer1;
            var pc = p.GetComponent<PlayerCollision>();
            pc.mask = (pc.mask | (1 << own)) & ~(1 << BulletFloorLayer);
            foreach (var l in p.GetComponentsInChildren<LegRaycasters>(true))
            {
                l.mask = (l.mask | (1 << own)) & ~(1 << BulletFloorLayer);
                var legs = Traverse.Create(l).Field("legs").GetValue<IkLeg[]>();
                if (legs != null) foreach (var leg in legs) if (leg != null) leg.mask = (leg.mask | (1 << own)) & ~(1 << BulletFloorLayer);
            }
            foreach (var leg in p.GetComponentsInChildren<IkLeg>(true)) leg.mask = (leg.mask | (1 << own)) & ~(1 << BulletFloorLayer);
            foreach (var w in p.GetComponentsInChildren<WallRayCaster>(true)) w.mask = w.mask & ~(1 << SolidLayer);
            var gm = Traverse.Create(data).Field("groundMask");
            gm.SetValue((LayerMask)((((LayerMask)gm.GetValue()).value | (1 << own)) & ~(1 << BulletFloorLayer)));
            data.block.BlockAction = (Action<BlockTrigger.BlockTriggerType>)Delegate.Combine(data.block.BlockAction, new Action<BlockTrigger.BlockTriggerType>(t => { if (t != BlockTrigger.BlockTriggerType.Empower) blockCount[slot]++; }));
            var floor = new GameObject("floor " + (slot == 0 ? "orange" : "blue"));
            floor.layer = own;
            floor.transform.SetParent(mapGo.transform, false);
            var box = floor.AddComponent<BoxCollider2D>();
            box.size = new Vector2(400f, 1f);
            floors[slot] = floor;
            // The other seat's floor never touches this player.
            for (int o = 0; o < Seats; o++)
                if (o != slot && floors[o] != null)
                    foreach (var oc in floors[o].GetComponents<Collider2D>())
                        foreach (var c in p.GetComponentsInChildren<Collider2D>(true)) Physics2D.IgnoreCollision(oc, c, true);
            for (int o = 0; o < Seats; o++)
                if (o != slot && players[o] != null)
                    foreach (var c in players[o].GetComponentsInChildren<Collider2D>(true)) Physics2D.IgnoreCollision(box, c, true);
            life[slot] = -1; // a fresh body has done no VS life yet: it is revived at the first one VS sends
            EnginePlugin.L.LogInfo("arena: ROUNDS player " + p.playerID + " is seat " + slot + " (" + (slot == 0 ? "Orange" : "Blue") + ")");
        }

        /// <summary>Where a seat stood when it was parked (no VS player on it): it comes back where VS puts it.</summary>
        private void ParkOrPlay()
        {
            for (int i = 0; i < Seats; i++)
            {
                var p = players[i];
                if (p == null || creating[i]) continue;
                if (!Present(i) || mode != 1)
                {
                    // No VS player on this seat: kept far under the arena, still and out of every picture.
                    if (p.data.isPlaying) { p.data.isPlaying = false; SimRef(p.data.playerVel) = false; }
                    KinematicRef(p.data.playerVel) = true;
                    VelRef(p.data.playerVel) = Vector2.zero;
                    p.transform.position = new Vector3(i * 20f, OffsetY - 400f, 0f);
                    continue;
                }
                bool play = !picking;
                if (play == p.data.isPlaying) continue;
                p.data.isPlaying = play;
                SimRef(p.data.playerVel) = play;
            }
        }

        /// <summary>
        /// Every seat VS plays is held where its VS character stands, before ROUNDS's own scripts run in Update and
        /// FixedUpdate (this component runs first). PlayerVelocity.isKinematic zeroes the velocity each step and skips
        /// ROUNDS's gravity and movement, so jumps, recoil, knockback and block dashes move nothing: VS moves the player.
        /// Its own floor follows under its feet, so ROUNDS's legs plant and step as it walks. A ROUNDS jump is a hop seen from
        /// above (<see cref="StartHop"/>): the body rises over its spot and comes down, its legs hanging off the floor.
        /// </summary>
        private void HoldPlayers()
        {
            for (int i = 0; i < Seats; i++)
            {
                var p = players[i];
                if (p == null || creating[i] || !Present(i) || mode != 1) continue;
                int s = ArenaLink.Slot(i);
                KinematicRef(p.data.playerVel) = true;
                VelRef(p.data.playerVel) = Vector2.zero;
                Vector2 r = ToRounds(link.F(s + ArenaLink.S_PosX), link.F(s + ArenaLink.S_PosY));
                float up = HopLift(i);
                p.transform.position = new Vector3(r.x, r.y + up, 0f);
                if (up > 0f)
                {
                    p.data.sinceGrounded = Time.time - hopStart[i];
                    p.data.isGrounded = false;
                }
                else
                {
                    p.data.sinceGrounded = 0f;
                    p.data.isGrounded = true;
                }
                if (floors[i] != null)
                    floors[i].transform.position = new Vector3(r.x, r.y - Tuning.legGroundDrop * Mathf.Abs(p.transform.localScale.x) - 0.5f, 0f);
            }
        }

        // ------------------------------------------------------------------ hops (ROUNDS's jumps seen from above)

        private readonly float[] hopStart = new float[Seats], hopTime = new float[Seats], hopHeight = new float[Seats];
        private readonly int[] hops = new int[Seats], dmgDodged = new int[Seats];

        /// <summary>
        /// A ROUNDS jump of an arena player (PlayerJump.Jump: Pogo every 4 s, Super Jump, a jump card's own jump) becomes a hop:
        /// seen from above nothing goes up, so the guy rises over his spot and lands again, and while he is in the air VS's
        /// enemies pass under him (their hits do not land, <see cref="Hopping"/>). multiplier is ROUNDS's jump strength
        /// (Pogo 0.5, a charged Super Jump up to 3) times the jump stat cards change; a stronger jump goes higher and longer.
        /// </summary>
        public void StartHop(Player p, float multiplier)
        {
            int i = SlotOf(p);
            if (i < 0 || mode != 1 || paused) return;
            float m = Mathf.Clamp(multiplier * Mathf.Max(0.1f, p.data.stats.jump), 0.3f, 4f);
            hopStart[i] = Time.time;
            hopTime[i] = Tuning.hopSeconds * Mathf.Sqrt(m);
            hopHeight[i] = Tuning.hopHeight * m * Mathf.Abs(p.transform.localScale.x);
            hops[i]++;
        }

        /// <summary>True while the seat is in the air from a hop.</summary>
        public bool Hopping(int i) => i >= 0 && i < Seats && hopTime[i] > 0f && Time.time - hopStart[i] < hopTime[i];

        /// <summary>How high the seat is in its hop right now (a parabola over the hop's time), 0 on the ground.</summary>
        private float HopLift(int i)
        {
            if (!Hopping(i)) return 0f;
            float u = (Time.time - hopStart[i]) / hopTime[i];
            return hopHeight[i] * 4f * u * (1f - u);
        }

        // ------------------------------------------------------------------ input (GeneralInput prefix)

        public void FeedInput(GeneralInput g, int slot)
        {
            g.ResetInput();
            var data = players[slot].data;
            int s = ArenaLink.Slot(slot);
            int blocks = link.I(s + ArenaLink.S_BlockPresses);
            bool blockPressed = blocks != seenBlock[slot];
            seenBlock[slot] = blocks;
            if (GameManager.lockInput || g.stunnedInput || !data.isPlaying || mode != 1 || paused) { wasShooting[slot] = false; return; }
            Vector2 move = new Vector2(link.F(s + ArenaLink.S_MoveX), link.F(s + ArenaLink.S_MoveY));
            // Side view: only the x of VS's walk turns the body and moves the legs.
            g.direction = Mathf.Abs(move.x) > 0.3f ? new Vector3(Mathf.Sign(move.x), 0f, 0f) : Vector3.zero;
            if (g.direction != Vector3.zero) g.latestPressedDirection = g.direction;
            Vector3 aim = new Vector3(link.F(s + ArenaLink.S_AimX), link.F(s + ArenaLink.S_AimY), 0f);
            if (aim.sqrMagnitude > 0.0001f) aim.Normalize();
            g.aimDirection = aim;
            if (g.aimDirection != Vector3.zero) g.lastAimDirection = g.aimDirection;
            g.jumpIsPressed = false;
            g.jumpWasPressed = false;
            if (!g.silencedInput)
            {
                g.shootIsPressed = link.I(s + ArenaLink.S_ShootHeld) != 0;
                // VS weapons fire by themselves: the ROUNDS gun fires whenever it is ready while VS says so (full auto at
                // ROUNDS's attack speed, with ROUNDS's ammo and reload).
                g.shootWasPressed = g.shootIsPressed;
                g.shootWasReleased = !g.shootIsPressed && wasShooting[slot];
                g.shieldWasPressed = blockPressed;
            }
            wasShooting[slot] = g.shootIsPressed;
        }

        private readonly bool[] wasShooting = new bool[Seats];

        public void CountShot(Player p)
        {
            int slot = SlotOf(p);
            if (slot >= 0) shots[slot]++;
            if (slot >= 0 && DevShots && shots[slot] % 10 == 1) LogShot(p, slot);
        }

        private static readonly bool DevShots = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV"));

        private float nextPickDiag;
        private float nextBulletDiag;

        /// <summary>Developer runs: the bullets in flight every 2 s (where they go and what rides on them).</summary>
        private void DevBullets()
        {
            if (Time.unscaledTime < nextBulletDiag || mode != 1) return;
            nextBulletDiag = Time.unscaledTime + 2f;
            var sb = new System.Text.StringBuilder("arena: dev bullets");
            int n = 0;
            foreach (var m in FindObjectsOfType<MoveTransform>())
            {
                if (m == null || m.transform.position.y > OffsetY * 0.5f) continue;
                if (n++ >= 8) break;
                Vector2 v = m.velocity;
                sb.Append(" | " + m.transform.root.name + " at " + ToVs(m.transform.position).ToString("0.0") + " v " + v.normalized.ToString("0.00") + " x" + v.magnitude.ToString("0") + " grav " + m.gravity.ToString("0.0") + " sim " + m.simulateGravity);
                var names = new HashSet<string>();
                foreach (var c in m.transform.root.GetComponentsInChildren<MonoBehaviour>(true)) if (c != null) names.Add(c.GetType().Name);
                names.ExceptWith(new[] { "MoveTransform", "ProjectileHit", "RayCastTrail", "PhotonView", "SyncProjectile", "SpawnedAttack", "ProjectileInit", "RemoveAfterSeconds", "ProjectileCollision", "DynamicParticleSet", "ChildRPC", "TrailRenderer", "ProjectileHitEmpty", "SetTeamColor", "CanvasScaler", "GraphicRaycaster" });
                sb.Append(" {" + string.Join(",", names.ToArray()) + "}");
            }
            if (n > 0) EnginePlugin.L.LogInfo(sb.ToString());
            var lb = new System.Text.StringBuilder("arena: dev lines");
            int ln = 0;
            foreach (var lr in FindObjectsOfType<LineRenderer>())
            {
                if (lr == null || !lr.enabled || !lr.gameObject.activeInHierarchy || lr.positionCount < 2) continue;
                Vector3 a = lr.GetPosition(0), b = lr.GetPosition(lr.positionCount - 1);
                if (!lr.useWorldSpace) { a = lr.transform.TransformPoint(a); b = lr.transform.TransformPoint(b); }
                if (a.y > OffsetY * 0.5f && b.y > OffsetY * 0.5f) continue;
                if (ln++ >= 8) break;
                lb.Append(" | " + lr.transform.root.name + "/" + lr.name + " " + ToVs(a).ToString("0.0") + " -> " + ToVs(b).ToString("0.0") + " n" + lr.positionCount + " w" + lr.widthMultiplier.ToString("0.00") + " " + ColorUtility.ToHtmlStringRGBA(lr.startColor));
            }
            foreach (var tr in FindObjectsOfType<TrailRenderer>())
            {
                if (tr == null || !tr.enabled || !tr.gameObject.activeInHierarchy || tr.positionCount < 2) continue;
                Vector3 a = tr.GetPosition(0), b = tr.GetPosition(tr.positionCount - 1);
                if (a.y > OffsetY * 0.5f && b.y > OffsetY * 0.5f) continue;
                if (ln++ >= 12) break;
                lb.Append(" | trail " + tr.transform.root.name + "/" + tr.name + " " + ToVs(a).ToString("0.0") + " -> " + ToVs(b).ToString("0.0") + " n" + tr.positionCount + " t" + tr.time.ToString("0.00") + " w" + tr.widthMultiplier.ToString("0.00"));
            }
            if (ln > 0) EnginePlugin.L.LogInfo(lb.ToString());
        }

        /// <summary>Developer runs: how each card of a pick is drawn (its canvas turn, front and back, curve animations).</summary>
        private static void DiagPick(List<GameObject> cards)
        {
            if (cards == null) return;
            var sb = new System.Text.StringBuilder("arena: dev pick");
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null) { sb.Append(" | " + i + " gone"); continue; }
                var vis = c.GetComponentInChildren<CardVisuals>();
                var canvas = vis != null ? vis.transform.Find("Canvas") : null;
                var front = canvas != null ? canvas.Find("Front") : null;
                var back = canvas != null ? canvas.Find("Back") : null;
                sb.Append(" | " + i + " " + (c.GetComponent<CardInfo>() != null ? c.GetComponent<CardInfo>().cardName : c.name) + (vis != null && vis.isSelected ? " [sel]" : ""));
                if (canvas != null) sb.Append(" canvas " + canvas.localEulerAngles.ToString("0") + " world " + canvas.eulerAngles.ToString("0") + " scale " + canvas.lossyScale.x.ToString("0.00"));
                if (front != null) { var g = front.GetComponent<CanvasGroup>(); sb.Append(" front " + (front.gameObject.activeSelf ? "on" : "off") + (g != null ? " a" + g.alpha.ToString("0.00") : "")); }
                if (back != null) { var g = back.GetComponent<CanvasGroup>(); sb.Append(" back " + (back.gameObject.activeSelf ? "on" : "off") + (g != null ? " a" + g.alpha.ToString("0.00") : "") + " rot " + back.localEulerAngles.ToString("0")); }
                foreach (var ca in c.GetComponentsInChildren<CurveAnimation>(true))
                    sb.Append(" {" + ca.name + " " + ca.currentState + (ca.useTimeScale ? "" : " unscaled") + " rot " + ca.transform.localEulerAngles.ToString("0") + "}");
            }
            sb.Append(" | timeScale " + Time.timeScale + " dt " + TimeHandler.deltaTime.ToString("0.000"));
            EnginePlugin.L.LogInfo(sb.ToString());
        }

        /// <summary>Developer runs: where a shot leaves from, against the player and the camera (VS units).</summary>
        private void LogShot(Player p, int slot)
        {
            try
            {
                var gun = p.data.weaponHandler.gun;
                Vector2 body = ToVs(p.transform.position), muzzle = ToVs(gun.shootPosition != null ? gun.shootPosition.position : gun.transform.position);
                Vector2 cam = new Vector2(link.F(ArenaLink.C_CamX), link.F(ArenaLink.C_CamY));
                Vector2 held = new Vector2(link.F(ArenaLink.Slot(slot) + ArenaLink.S_PosX), link.F(ArenaLink.Slot(slot) + ArenaLink.S_PosY));
                Vector2 aim = new Vector2(link.F(ArenaLink.Slot(slot) + ArenaLink.S_AimX), link.F(ArenaLink.Slot(slot) + ArenaLink.S_AimY));
                EnginePlugin.L.LogInfo("arena: dev shot " + shots[slot] + " seat " + slot + " body " + body.ToString("F2") + " vs " + held.ToString("F2") + " muzzle " + muzzle.ToString("F2")
                    + " aim " + aim.ToString("F2") + " gun points " + ((Vector2)gun.shootPosition.forward).ToString("F2") + " camera " + cam.ToString("F2") + " half height " + link.F(ArenaLink.C_CamOrtho).ToString("F2"));
            }
            catch (Exception e) { LogOnce("dev shot", e); }
        }

        // ------------------------------------------------------------------ VS's enemies and the screen mirrored in ROUNDS

        private void ClearTargets()
        {
            foreach (var m in mirrors.Values) Park(m);
            mirrors.Clear();
            status.Clear();
            agg.Clear();
        }

        private void Park(Mirror m)
        {
            if (m.go == null) return;
            m.target.StopAllCoroutines();
            m.go.SetActive(false);
            spare.Push(m);
        }

        private Mirror NewMirror()
        {
            while (spare.Count > 0)
            {
                var m = spare.Pop();
                if (m.go != null) return m;
            }
            var go = new GameObject("vs enemy");
            go.layer = TargetLayer;
            go.transform.SetParent(mapGo.transform, false);
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            var t = go.AddComponent<VsTarget>();
            t.Col = col;
            // RayHitPoison looks for a DamageOverTime on what it hit; the effect patches turn it into enemy poison.
            go.AddComponent<DamageOverTime>();
            go.SetActive(false);
            return new Mirror { go = go, target = t, col = col };
        }

        private int syncs;
        private readonly List<int> ended = new List<int>();

        /// <summary>VS's enemy table (nearest to the camera first, at most MaxTargets) into ROUNDS trigger circles.</summary>
        private void SyncTargets()
        {
            if (mode != 1) { if (mirrors.Count > 0) ClearTargets(); return; }
            int n = Mathf.Clamp(link.I(ArenaLink.C_TargetCount), 0, ArenaLink.MaxTargets);
            seenIds.Clear();
            syncs++;
            for (int i = 0; i < n; i++)
            {
                int e = ArenaLink.Target(i);
                int id = link.I(e + ArenaLink.T_Id);
                if (id == 0 || !seenIds.Add(id)) continue;
                Mirror m;
                if (!mirrors.TryGetValue(id, out m))
                {
                    m = NewMirror();
                    m.id = id;
                    m.r = -1f;
                    m.target.id = id;
                    m.target.group = id;
                    mirrors[id] = m;
                    m.go.SetActive(true);
                }
                Vector2 c = ToRounds(link.F(e + ArenaLink.T_X), link.F(e + ArenaLink.T_Y));
                m.go.transform.position = c;
                float r = Mathf.Max(0.05f, link.F(e + ArenaLink.T_R) / scale);
                if (Mathf.Abs(r - m.r) > 0.001f) { m.r = r; m.col.radius = r; }
                var st = Status(id);
                if (!st.alive) st.EndLife();
                st.alive = true;
                st.seen = syncs;
                st.flagsIn = link.I(e + ArenaLink.T_Flags);
                st.hpLeft = Mathf.Max(0f, link.F(e + ArenaLink.T_Hp)) / Tuning.damageScale;
                if ((st.flagsIn & ArenaLink.TfNoStatus) != 0) st.ClearTransient();
            }
            if (mirrors.Count != seenIds.Count)
            {
                gone.Clear();
                foreach (var id in mirrors.Keys) if (!seenIds.Contains(id)) gone.Add(id);
                foreach (var id in gone) { Park(mirrors[id]); mirrors.Remove(id); }
            }
            // An enemy no longer in the table (it died, or was put back in VS's pool) ends its life: its statuses go and its
            // damage-over-time ticks stop. Two syncs in a row: the table has no lock, so one torn read can miss an enemy.
            foreach (var s in status.Values)
                if (s.alive && s.seen < syncs - 1) { s.EndLife(); ended.Add(s.group); }
            if (ended.Count > 0)
            {
                foreach (var g in ended) status.Remove(g);
                ended.Clear();
            }
        }

        /// <summary>Walls just outside VS's camera view: ROUNDS's bullets end there, and Bouncy bullets bounce back in.</summary>
        private void PlaceWalls()
        {
            if (mode != 1) return;
            float ortho = link.F(ArenaLink.C_CamOrtho), aspect = link.F(ArenaLink.C_Aspect);
            if (ortho <= 0f || aspect <= 0.1f) return;
            Vector2 c = ToRounds(link.F(ArenaLink.C_CamX), link.F(ArenaLink.C_CamY));
            float hh = ortho / scale + Tuning.wallMargin, hw = ortho * aspect / scale + Tuning.wallMargin, t = Tuning.wallThickness;
            for (int i = 0; i < 4; i++)
            {
                if (walls[i] == null)
                {
                    walls[i] = new GameObject("screen edge " + i);
                    walls[i].layer = SolidLayer;
                    walls[i].transform.SetParent(mapGo.transform, false);
                    walls[i].AddComponent<BoxCollider2D>();
                    foreach (var p in players) if (p != null) foreach (var pc in p.GetComponentsInChildren<Collider2D>(true)) Physics2D.IgnoreCollision(walls[i].GetComponent<Collider2D>(), pc, true);
                }
                var b = walls[i].GetComponent<BoxCollider2D>();
                // left, right: tall; bottom, top: wide. Longer than ArenaViewCam.MinBoundSpan, so it reads them as the bounds.
                bool side = i < 2;
                float sign = i % 2 == 0 ? -1f : 1f;
                walls[i].transform.position = side ? new Vector3(c.x + sign * (hw + t / 2f), c.y, 0f) : new Vector3(c.x, c.y + sign * (hh + t / 2f), 0f);
                b.size = side ? new Vector2(t, Mathf.Max(100f, 2f * hh + 2f * t)) : new Vector2(Mathf.Max(100f, 2f * hw + 2f * t), t);
            }
        }

        // ------------------------------------------------------------------ enemy statuses (ROUNDS rules, sent to VS)

        private readonly Dictionary<int, TargetStatus> status = new Dictionary<int, TargetStatus>();

        public TargetStatus Status(int g)
        {
            TargetStatus s;
            if (!status.TryGetValue(g, out s)) status[g] = s = new TargetStatus { group = g };
            return s;
        }

        /// <summary>PlayerImmunity.IsImune for an enemy, returning whether the hit lands: a named source still on cooldown blocks
        /// it unless its damage beats the stored one, which then takes over with a fresh window.</summary>
        public bool Immunity(int g, string name, float secs, float dmg)
        {
            var s = Status(g);
            TargetStatus.Immunity im;
            if (s.immune.TryGetValue(name, out im) && im.time > 0f && dmg <= im.dmg) return false;
            if (im == null) s.immune[name] = im = new TargetStatus.Immunity();
            im.time = secs;
            im.dmg = dmg;
            return true;
        }

        private readonly HashSet<string> onceErrors = new HashSet<string>();
        public void LogOnce(string what, Exception e)
        {
            if (onceErrors.Add(what + e.Message)) EnginePlugin.L.LogWarning("arena " + what + ": " + e);
        }

        public static int Rgba(Color c) => ArenaLink.Rgba(c.r, c.g, c.b, c.a);

        private struct AggKey
        {
            public int id, slot, kind;
            public override int GetHashCode() => (id * 397) ^ (slot * 31) ^ kind;
            public override bool Equals(object o) => o is AggKey && ((AggKey)o).id == id && ((AggKey)o).slot == slot && ((AggKey)o).kind == kind;
        }
        private struct AggHit { public float dmg; public Vector2 pos; public int rgba; }
        private readonly Dictionary<AggKey, AggHit> agg = new Dictionary<AggKey, AggHit>();

        public void PostFx(int group, int type, float a, float b = 0f, float c = 0f, float d = 0f, int slot = -1) => link.PostFx(group, type, a, b, c, d, slot);

        private static readonly System.Reflection.MethodInfo MuData = AccessTools.Method(AccessTools.TypeByName("ModdingUtils.Extensions.CharacterStatModifiersExtension"), "GetAdditionalData");

        /// <summary>ModdingUtils' gravity-on-damage (Flip, Grounded) only runs on a HealthHandler: read it from the attacker.</summary>
        public void GravityOnHit(VsTarget t, Player by)
        {
            if (MuData == null || by == null) return;
            try
            {
                var tr = Traverse.Create(MuData.Invoke(null, new object[] { by.data.stats }));
                float dur = tr.Field("gravityDurationOnDoDamage").GetValue<float>(), mul = tr.Field("gravityMultiplierOnDoDamage").GetValue<float>();
                if (dur > 0f && mul != 1f) t.AddGravity(mul, dur);
            }
            catch (Exception e) { LogOnce("gravity", e); }
        }

        /// <summary>Once per frame: summed damage-over-time / continuous hits, coalesced effect events, the status table.</summary>
        private void WriteEffects()
        {
            foreach (var kv in agg) link.PostHit(kv.Key.id, kv.Value.dmg, kv.Value.pos.x, kv.Value.pos.y, kv.Key.slot, kv.Key.kind, kv.Value.rgba);
            agg.Clear();
            foreach (var s in status.Values)
            {
                if (s.fxSlow > 0f) link.PostFx(s.group, ArenaLink.FxSlow, s.fxSlow, s.fxFast ? 1f : 0f);
                if (s.fxStun > 0f) link.PostFx(s.group, ArenaLink.FxStun, s.fxStun);
                if (s.fxSilence > 0f) link.PostFx(s.group, (s.flags & ArenaLink.StConfused) != 0 ? ArenaLink.FxConfuse : ArenaLink.FxSilence, s.fxSilence);
                if (s.dv != Vector2.zero) link.PostFx(s.group, ArenaLink.FxForce, s.dv.x, s.dv.y, s.flying, s.cont ? 1f : 0f);
                s.fxSlow = s.fxStun = s.fxSilence = s.flying = 0f;
                s.fxFast = s.cont = false;
                s.dv = Vector2.zero;
            }
            // The table under its sequence lock (R_StatusFrame): odd while the rows and count are written, even once they
            // are all in. The finally writes the count of rows done and makes it even again even when a row throws.
            int r = ArenaLink.ResultOff, n = 0, fo = r + ArenaLink.R_StatusFrame;
            int f = link.I(fo) & ~1;
            link.I(fo, f + 1);
            System.Threading.Thread.MemoryBarrier();
            try
            {
                foreach (var s in status.Values)
                {
                    if (!s.Any) continue;
                    if (n >= ArenaLink.MaxStatus) break;
                    int e = r + ArenaLink.R_Status + n++ * ArenaLink.StatusSize;
                    link.I(e + ArenaLink.T_Group, s.group);
                    link.F(e + ArenaLink.T_Slow, s.Slow);
                    link.F(e + ArenaLink.T_Stun, s.stun);
                    link.F(e + ArenaLink.T_Silence, s.silence);
                    link.F(e + ArenaLink.T_Dot, s.dot);
                    link.I(e + ArenaLink.T_DotRgba, s.dotRgba);
                    link.I(e + ArenaLink.T_SFlags, s.flags);
                    link.F(e + ArenaLink.T_Aux, s.aux);
                }
            }
            finally
            {
                link.I(r + ArenaLink.R_StatusCount, n);
                System.Threading.Thread.MemoryBarrier();
                link.I(fo, f + 2);
            }
        }

        private int hitsPosted;

        public void TargetHit(int id, float damage, Vector2 roundsPos, Player by, int kind = ArenaLink.HitBullet, Color c = default(Color))
        {
            hitsPosted++;
            Vector2 p = ToVs(roundsPos);
            int slot = by != null ? SlotOf(by) : -1;
            int rgba = Rgba(c);
            if (kind == ArenaLink.HitDot || kind == ArenaLink.HitContinuous)
            {
                var k = new AggKey { id = id, slot = slot, kind = kind };
                AggHit a;
                agg.TryGetValue(k, out a);
                a.dmg += damage;
                a.pos = p;
                a.rgba = rgba;
                agg[k] = a;
                return;
            }
            link.PostHit(id, damage, p.x, p.y, slot, kind, rgba);
        }

        // ------------------------------------------------------------------ events from VS

        private readonly float[] lastHit = new float[Seats], lastBlocked = new float[Seats], dmgTaken = new float[Seats];
        private readonly int[] dmgIn = new int[Seats], dmgBlocked = new int[Seats], dmgMerged = new int[Seats];
        /// <summary>VS damage that arrived within playerHitCooldown of the last hit ROUNDS took, per seat, and its direction:
        /// it lands as one hit when the cooldown is over (<see cref="LandHits"/>).</summary>
        private readonly float[] pendingHit = new float[Seats];
        private readonly Vector2[] pendingDir = new Vector2[Seats];
        /// <summary>Orange's and Blue's own blasts that reached them and were kept off (ArenaEffectPatches.Blast), per seat.</summary>
        public readonly int[] ownBlasts = new int[Seats];

        private void ReadEvents()
        {
            int write = link.I(ArenaLink.C_EventWrite);
            if (write - eventRead > ArenaLink.EventRing) eventRead = write - ArenaLink.EventRing;
            while (eventRead < write)
            {
                int e = ArenaLink.C_Events + (eventRead % ArenaLink.EventRing) * ArenaLink.EventSize;
                eventRead++;
                int type = link.I(e + ArenaLink.E_Type), slot = link.I(e + ArenaLink.E_Slot);
                if (slot < 0 || slot >= Seats || players[slot] == null || creating[slot]) continue;
                var p = players[slot];
                float a = link.F(e + ArenaLink.E_A), b = link.F(e + ArenaLink.E_B), c = link.F(e + ArenaLink.E_C);
                try
                {
                    switch (type)
                    {
                        case ArenaLink.EvDamage:
                        {
                            dmgIn[slot]++;
                            if (p.data.dead) break;
                            // In the air over the horde (a hop): the enemies pass under him.
                            if (Hopping(slot)) { dmgDodged[slot]++; break; }
                            // ROUNDS's block (its window after a block) stops VS hits too, with ROUNDS's own blocked-hit path.
                            if (Blocking(p))
                            {
                                dmgBlocked[slot]++;
                                pendingHit[slot] = 0f;
                                if (Time.unscaledTime - lastBlocked[slot] < Tuning.blockedHitCooldown) break;
                                lastBlocked[slot] = Time.unscaledTime;
                                p.data.healthHandler.CallTakeDamage(new Vector2(b, c).normalized * Mathf.Max(1f, a), p.transform.position);
                                break;
                            }
                            // VS paces its own hits (each enemy at its own rate), so every one counts: hits within
                            // playerHitCooldown of the last one ROUNDS took wait and land together (LandHits). 0.3.0 dropped
                            // them, about 85% of a horde's hits, and a guy stood in a horde at full health.
                            pendingHit[slot] += a;
                            Vector2 dir = new Vector2(b, c);
                            if (dir.sqrMagnitude >= 0.01f) pendingDir[slot] = dir;
                            if (Time.unscaledTime - lastHit[slot] < Tuning.playerHitCooldown) { dmgMerged[slot]++; break; }
                            LandHits(slot);
                            break;
                        }
                        case ArenaLink.EvHeal:
                            if (p.data.dead || a <= 0f) break;
                            VsHeal = true;
                            try { p.data.healthHandler.Heal(a * Tuning.healScale); }
                            finally { VsHeal = false; }
                            break;
                        case ArenaLink.EvKill:
                            if (!p.data.dead) p.data.healthHandler.DoDamage(Vector2.up * (p.data.health + 1000f), p.transform.position, Color.white, null, null, false, true, true);
                            break;
                        case ArenaLink.EvBlockedHit:
                            if (p.data.block.sinceBlock < 0.3f) p.data.healthHandler.CallTakeDamage(new Vector2(b, c).normalized * Mathf.Max(1f, a), p.transform.position);
                            break;
                    }
                }
                catch (Exception ex) { VsHit = false; EnginePlugin.L.LogWarning("arena event " + type + ": " + ex.Message); }
            }
        }

        /// <summary>ROUNDS's block, or its window just after one (Block.sinceBlock), stops VS hits.</summary>
        private static bool Blocking(Player p) => p.data.block.sinceBlock < 0.3f || p.data.block.IsBlocking();

        /// <summary>The VS damage waiting for a seat lands in ROUNDS as one hit (ROUNDS's own damage path: its flash, sound,
        /// on-damage cards), scaled by tuning contactDamageScale.</summary>
        private void LandHits(int slot)
        {
            var p = players[slot];
            float a = pendingHit[slot];
            pendingHit[slot] = 0f;
            if (p == null || a <= 0f || p.data.dead) return;
            lastHit[slot] = Time.unscaledTime;
            dmgTaken[slot] += a * Tuning.contactDamageScale;
            Vector2 dir = pendingDir[slot];
            if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
            VsHit = true;
            try { p.data.healthHandler.CallTakeDamage(dir.normalized * a * Tuning.contactDamageScale, p.transform.position); }
            finally { VsHit = false; }
        }

        /// <summary>Waiting VS damage lands once playerHitCooldown is over, even when no new hit comes; a block that went up in
        /// the meantime stops it, and a pause keeps it for later.</summary>
        private void LandWaitingHits()
        {
            if (paused) return;
            for (int i = 0; i < Seats; i++)
            {
                if (pendingHit[i] <= 0f) continue;
                var p = players[i];
                if (p == null || creating[i] || p.data.dead) { pendingHit[i] = 0f; continue; }
                if (Blocking(p)) { pendingHit[i] = 0f; dmgBlocked[i]++; continue; }
                if (Time.unscaledTime - lastHit[i] >= Tuning.playerHitCooldown) LandHits(i);
            }
        }

        /// <summary>A new VS life (run start, VS revive): ROUNDS's own full revive, once per life number, echoed in Q_Life.</summary>
        private void CheckLives()
        {
            for (int i = 0; i < Seats; i++)
            {
                var p = players[i];
                if (p == null || creating[i] || !Present(i)) continue;
                // ROUNDS dies below 0 health; a hit that leaves it at 0 or a sliver would leave it standing forever.
                if (!p.data.dead && p.data.isPlaying && p.data.health < 0.5f && life[i] >= 0)
                    p.data.healthHandler.DoDamage(Vector2.up * 1000f, p.transform.position, Color.white, null, null, false, true, true);
                int serial = link.I(ArenaLink.Slot(i) + ArenaLink.S_Life);
                if (serial == life[i]) continue;
                try { p.data.healthHandler.Revive(true); }
                catch (Exception e) { EnginePlugin.L.LogWarning("arena revive: " + e.Message); }
                pendingHit[i] = 0f;
                life[i] = serial;
                EnginePlugin.L.LogInfo("arena: seat " + i + " life " + serial + " (full health " + p.data.maxHealth.ToString("0") + ")");
            }
        }

        // ------------------------------------------------------------------ cards

        /// <summary>ROUNDS's own reset between games (PlayerManager.ResetCharacters for one player): new gun, base stats and
        /// block, no cards, empty card bar.</summary>
        private static void ResetCards(Player p)
        {
            Traverse.Create(p).Method("FullReset").GetValue();
            // FullReset leaves what ApplyCardStats also changes: jumps, gravity, regeneration, ammo regen, bullet, gun sounds.
            try
            {
                var pre = PlayerAssigner.instance.playerPrefab;
                p.data.jumps = pre.GetComponent<CharacterData>().jumps;
                p.data.currentJumps = p.data.jumps;
                p.GetComponent<Gravity>().gravityForce = pre.GetComponent<Gravity>().gravityForce;
                p.data.healthHandler.regeneration = pre.GetComponent<HealthHandler>().regeneration;
                var gun = p.data.weaponHandler.gun;
                var preHold = pre.GetComponent<Holding>();
                var preGun = preHold != null && preHold.holdable != null ? preHold.holdable.GetComponent<Gun>() : null;
                var ammo = gun.GetComponentInChildren<GunAmmo>();
                var preAmmo = preGun != null ? preGun.GetComponentInChildren<GunAmmo>() : null;
                if (ammo != null) ammo.ammoReg = preAmmo != null ? preAmmo.ammoReg : 0f;
                if (preGun != null && preGun.projectiles.Length > 0 && gun.projectiles.Length > 0) gun.projectiles[0].objectToSpawn = preGun.projectiles[0].objectToSpawn;
                var audio = p.GetComponent<PlayerAudioModifyers>();
                if (audio != null) audio.modifyers.Clear();
            }
            catch (Exception e) { EnginePlugin.L.LogWarning("arena: stat reset: " + e.Message); }
            p.data.currentCards.Clear();
            try
            {
                var bars = Traverse.Create(CardBarHandler.instance).Field("cardBars").GetValue<CardBar[]>();
                if (bars != null && p.playerID >= 0 && p.playerID < bars.Length) bars[p.playerID].ClearBar();
            }
            catch (Exception e) { EnginePlugin.L.LogWarning("arena: card bar reset: " + e.Message); }
            EnginePlugin.L.LogInfo("arena: player " + p.playerID + " reset to the plain gun");
        }

        // ------------------------------------------------------------------ ROUNDS card pick

        private bool pickCancelled;

        private void CheckPick()
        {
            int serial = link.I(ArenaLink.C_Pick + ArenaLink.P_Serial);
            if (serial == pickSeen) return;
            int slot = link.I(ArenaLink.C_Pick + ArenaLink.P_Slot);
            int count = link.I(ArenaLink.C_Pick + ArenaLink.P_Count);
            if (count <= 0)
            {
                // VS gave the pick up: a running one ends, one not started yet never starts.
                pickSeen = serial;
                if (picking) pickCancelled = true;
                else link.I(ArenaLink.ResultOff + ArenaLink.R_PickDone, serial);
                return;
            }
            if (picking) return;
            count = Mathf.Clamp(count, 1, ArenaLink.MaxPicked);
            if (slot < 0 || slot >= Seats || players[slot] == null || creating[slot]) return;
            pickSeen = serial;
            pickCancelled = false;
            StartCoroutine(RunPick(serial, slot, count));
        }

        private IEnumerator RunPick(int serial, int slot, int count)
        {
            picking = true;
            pickSlot = slot;
            try
            {
                foreach (object step in RunPickSteps(slot, count)) yield return step;
            }
            finally
            {
                // Also when ROUNDS's pick throws: VS gets the cards picked so far and the arena is not stuck picking.
                picking = false;
                pickSlot = -1;
                link.I(ArenaLink.ResultOff + ArenaLink.R_PickDone, serial);
            }
        }

        private IEnumerable RunPickSteps(int slot, int count)
        {
            link.I(ArenaLink.ResultOff + ArenaLink.R_PickedCount, 0);
            var p = players[slot];
            seenNav[slot] = link.I(ArenaLink.Slot(slot) + ArenaLink.S_NavPresses);
            seenAccept[slot] = link.I(ArenaLink.Slot(slot) + ArenaLink.S_AcceptPresses);
            EnginePlugin.L.LogInfo("arena: ROUNDS card pick for seat " + slot + " x" + count);
            for (int i = 0; i < count && !pickCancelled; i++)
            {
                CardChoiceVisuals.instance.Show(p.playerID, true);
                // ROUNDS's pick, driven here so its own failures are caught; it ends early only when nothing can be picked
                // or VS gives the pick up.
                var cc = CardChoice.instance;
                var t = Traverse.Create(cc);
                var it = cc.DoPick(1, p.playerID, PickerType.Player);
                float stuckSince = -1f;
                while (true)
                {
                    if (pickCancelled) { EnginePlugin.L.LogInfo("arena: VS gave the pick up; ending it"); AbortRoundsPick(cc); break; }
                    bool more;
                    try { more = it.MoveNext(); }
                    catch (Exception e) { EnginePlugin.L.LogError("arena: ROUNDS pick threw: " + e); AbortRoundsPick(cc); break; }
                    if (!more) break;
                    var spawned = t.Field("spawnedCards").GetValue<List<GameObject>>();
                    bool selectable = t.Field("pickrID").GetValue<int>() != -1 && !t.Field("isPlaying").GetValue<bool>() && spawned != null && spawned.Any(c => c != null);
                    if (cc.IsPicking && !selectable && t.Field("pickrID").GetValue<int>() != -1)
                    {
                        if (stuckSince < 0f) stuckSince = Time.unscaledTime;
                        else if (Time.unscaledTime - stuckSince > 8f) { EnginePlugin.L.LogError("arena: ROUNDS pick stalled; ending it"); AbortRoundsPick(cc); break; }
                    }
                    else stuckSince = -1f;
                    if (DevShots && Time.unscaledTime > nextPickDiag) { nextPickDiag = Time.unscaledTime + 1f; DiagPick(spawned); }
                    yield return it.Current;
                }
                yield return new WaitForSecondsRealtime(0.1f);
            }
            CardChoiceVisuals.instance.Hide();
            yield return new WaitForSecondsRealtime(0.35f);
        }

        private static void AbortRoundsPick(CardChoice cc)
        {
            try
            {
                cc.IsPicking = false;
                Traverse.Create(cc).Field("pickrID").SetValue(-1);
                UIHandler.instance.StopShowPicker();
            }
            catch (Exception e) { EnginePlugin.L.LogWarning("arena: pick abort: " + e.Message); }
        }

        public void RecordPicked(int playerId, CardInfo card)
        {
            int slot = SlotOfPlayerId(playerId);
            if (slot < 0 || card == null) return;
            var src = card.sourceCard != null ? card.sourceCard : card;
            int n = link.I(ArenaLink.ResultOff + ArenaLink.R_PickedCount);
            if (n >= ArenaLink.MaxPicked) return;
            link.Str(ArenaLink.ResultOff + ArenaLink.R_Picked + n * ArenaLink.PickedSize, ArenaLink.PickedSize, src.cardName ?? src.gameObject.name);
            link.I(ArenaLink.ResultOff + ArenaLink.R_PickedCount, n + 1);
            EnginePlugin.L.LogInfo("arena: seat " + slot + " picked " + src.cardName);
        }

        /// <summary>Card pick input from VS: left/right presses, the mouse over a card, accept.</summary>
        public int TakeNav(int slot)
        {
            int s = ArenaLink.Slot(slot);
            int n = link.I(s + ArenaLink.S_NavPresses);
            if (n == seenNav[slot]) return 0;
            seenNav[slot] = n;
            return link.I(s + ArenaLink.S_NavDir);
        }

        public bool TakeAccept(int slot)
        {
            int n = link.I(ArenaLink.Slot(slot) + ArenaLink.S_AcceptPresses);
            if (n == seenAccept[slot]) return false;
            seenAccept[slot] = n;
            return true;
        }

        /// <summary>The card under the mouse (pick camera space), or -1 when the mouse has not moved.</summary>
        public int MouseCard(int slot, List<GameObject> cards, Camera cam)
        {
            int s = ArenaLink.Slot(slot);
            var m = new Vector2(link.F(s + ArenaLink.S_MouseX), link.F(s + ArenaLink.S_MouseY));
            if (m.x < 0f || cam == null || (m - lastMouse[slot]).sqrMagnitude < 1e-6f) return -1;
            lastMouse[slot] = m;
            Vector3 w = cam.ViewportToWorldPoint(new Vector3(m.x, m.y, 10f));
            int best = -1;
            float bestD = 6f * 6f;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                float d = ((Vector2)cards[i].transform.position - (Vector2)w).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------ result for VS

        private float nextLog;

        private float dumpAt = -1f;

        /// <summary>Developer aid (VAMPIREROUNDS_DEV): every renderer inside the run's view, once, 10 s into the first run.</summary>
        private void DumpView()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV")) || mode != 1) return;
            if (dumpAt < 0f) { dumpAt = Time.unscaledTime + 10f; return; }
            if (dumpAt == 0f || Time.unscaledTime < dumpAt) return;
            dumpAt = 0f;
            var cam = ArenaViewCam.Cam;
            if (cam == null) return;
            var view = ArenaViewCam.View;
            var sb = new System.Text.StringBuilder("arena view dump (" + view + "):");
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if (b.max.x < view.xMin || b.min.x > view.xMax || b.max.y < view.yMin || b.min.y > view.yMax) continue;
                if (b.size.x < 2.5f && b.size.y < 2.5f) continue; // the big ones only
                string path = r.name;
                for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                sb.Append(Environment.NewLine + "  " + r.GetType().Name + " " + path + " size " + b.size.ToString("0.0") + " at " + b.center.ToString("0.0") + " scale " + r.transform.lossyScale.ToString("0.00") + " layer " + r.gameObject.layer);
            }
            // ROUNDS's UI (layer 5, drawn by the UI camera over the whole screen): everything active in it.
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.gameObject.layer != 5) continue;
                string path = r.name;
                for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                sb.Append(Environment.NewLine + "  UI renderer " + r.GetType().Name + " " + path + " size " + r.bounds.size.ToString("0.0") + " at " + r.bounds.center.ToString("0.0"));
            }
            if (CardChoiceVisuals.instance != null)
            {
                var v = CardChoiceVisuals.instance;
                sb.Append(Environment.NewLine + "  CardChoiceVisuals active " + v.gameObject.activeInHierarchy + " at " + v.transform.position.ToString("0.0") + " layer " + v.gameObject.layer);
                foreach (Transform k in v.transform) sb.Append(Environment.NewLine + "    " + k.name + (k.gameObject.activeSelf ? "" : " [off]") + " layer " + k.gameObject.layer);
            }
            EnginePlugin.L.LogInfo(sb.ToString());
        }

        private void LogState()
        {
            Step("dump", DumpView);
            if (mode == 1 && Time.unscaledTime > nextLog)
            {
                nextLog = Time.unscaledTime + 5f;
                var sb = new System.Text.StringBuilder("arena: enemies " + mirrors.Count + ", statuses " + status.Count + ", hits " + hitsPosted + ", scale " + scale.ToString("0.000"));
                for (int i = 0; i < Seats; i++)
                    if (players[i] != null && Present(i)) sb.Append(" | seat " + i + " at " + players[i].transform.position.ToString("0.0") + " hp " + players[i].data.health.ToString("0") + "/" + players[i].data.maxHealth.ToString("0") + " cards " + players[i].data.currentCards.Count + " shots " + shots[i] + " vs hits " + dmgIn[i] + " (taken " + dmgTaken[i].ToString("0") + ", blocked " + dmgBlocked[i] + ", merged " + dmgMerged[i] + ", hopped over " + dmgDodged[i] + " in " + hops[i] + " hops" + ", own blasts kept off " + ownBlasts[i] + ")"
                        + (players[i].data.isPlaying ? "" : " not-playing") + (players[i].data.playerVel != null && !SimRef(players[i].data.playerVel) ? " not-simulated" : "") + (players[i].data.healthHandler.isRespawning ? " respawning" : "") + (players[i].data.dead ? " DEAD" : ""));
                EnginePlugin.L.LogInfo(sb.ToString() + (paused ? " (paused)" : ""));
            }
        }

        private void WriteResult()
        {
            int r = ArenaLink.ResultOff;
            link.I(r + ArenaLink.R_State, phase != Phase.Ready ? ArenaLink.StateOff : picking ? ArenaLink.StatePicking : ArenaLink.StateArena);
            for (int i = 0; i < Seats; i++)
            {
                int q = ArenaLink.RSlot(i);
                try { WriteSlot(i, q); }
                catch (Exception e)
                {
                    link.I(q + ArenaLink.Q_Present, 0);
                    if (stepErrors.Add("result" + i + ": " + e.Message)) EnginePlugin.L.LogError("arena result seat " + i + " failed: " + e);
                }
            }
        }

        private void WriteSlot(int i, int q)
        {
            var p = players[i];
            bool present = p != null && !creating[i];
            link.I(q + ArenaLink.Q_Present, present ? 1 : 0);
            if (!present) return;
            var d = p.data;
            Vector2 c = ToVs(p.transform.position);
            link.F(q + ArenaLink.Q_X, c.x);
            link.F(q + ArenaLink.Q_Y, c.y);
            link.F(q + ArenaLink.Q_Hp, d.health);
            link.F(q + ArenaLink.Q_MaxHp, d.maxHealth);
            link.I(q + ArenaLink.Q_Dead, d.dead ? 1 : 0);
            link.I(q + ArenaLink.Q_Blocking, d.block.sinceBlock < 0.3f ? 1 : 0);
            link.I(q + ArenaLink.Q_Blocks, blockCount[i]);
            link.F(q + ArenaLink.Q_AimX, d.input.lastAimDirection.x);
            link.F(q + ArenaLink.Q_AimY, d.input.lastAimDirection.y);
            link.I(q + ArenaLink.Q_Cards, d.currentCards != null ? d.currentCards.Count : 0);
            link.F(q + ArenaLink.Q_MoveMul, d.stats != null ? d.stats.movementSpeed : 1f);
            link.I(q + ArenaLink.Q_Shots, shots[i]);
            link.I(q + ArenaLink.Q_Reloading, d.weaponHandler != null && d.weaponHandler.gun != null && d.weaponHandler.gun.isReloading ? 1 : 0);
            link.I(q + ArenaLink.Q_Life, life[i]); // last: everything above describes this life
        }

        private void OnDestroy()
        {
            try { link?.I(ArenaLink.ResultOff + ArenaLink.R_State, ArenaLink.StateOff); } catch { }
        }
    }
}
