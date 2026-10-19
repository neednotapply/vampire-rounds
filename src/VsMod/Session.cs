using System;
using HarmonyLib;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.Framework;
using UnityEngine;
using CharacterController = Il2CppVampireSurvivors.Objects.Characters.CharacterController;

namespace VampireRounds.Vs
{
    /// <summary>
    /// A Vampire Rounds run (sheets/systems.json "session"): any run where a main player is Orange or Blue. Every frame it
    /// tells the hidden ROUNDS the camera, each seat's position and input, and the enemies; it takes back the hits, the
    /// effects and ROUNDS's health, which VS's health bar mirrors. VS's own hits and heals go to ROUNDS instead
    /// (h_player_damage, h_player_heal); a ROUNDS death is a VS death, and a VS revive is a ROUNDS revive.
    /// </summary>
    public static class Session
    {
        public static bool InRun { get; private set; }
        public static readonly CharacterController[] SeatChar = new CharacterController[ArenaLink.Seats];
        private static readonly int[] life = new int[ArenaLink.Seats];
        private static readonly bool[] diedInRounds = new bool[ArenaLink.Seats];
        private static IntPtr lastGm;
        private static int run;
        public static Camera Cam { get; private set; }
        public static Vector2 CamCenter { get; private set; }
        public static float CamHalfH { get; private set; } = 5f;
        public static float CamAspect { get; private set; } = 16f / 9f;

        public static void Tick()
        {
            Link.Beat();
            Content.RefreshPortraits();
            Content.MenuTick();
            var L = Link.L;
            if (L == null) return;
            var gm = GM.Core;
            FindSeats(gm);
            if (InRun && gm.Pointer != lastGm)
            {
                lastGm = gm.Pointer;
                run++;
                L.I(ArenaLink.C_Run, run);
                for (int i = 0; i < life.Length; i++) { life[i]++; diedInRounds[i] = false; }
                Enemies.Reset();
                Picks.Reset();
                Mod.Log("session: Vampire Rounds run " + run + " starts (" + Describe() + ")");
                TestMode.Mark("run-started");
            }
            L.I(ArenaLink.C_Mode, InRun ? 1 : 0);
            if (!InRun) { L.I(ArenaLink.C_TargetCount, 0); return; }
            bool paused = gm.IsPaused || Il2Cpp.PauseSystem.Paused || !gm.IsGameRunning || Picks.Open;
            L.I(ArenaLink.C_Paused, paused ? 1 : 0);
            WriteCamera(L);
            try { L.F(ArenaLink.C_Volume, Mathf.Clamp01(gm.PlayerOptions.Config.SoundsVolume)); }
            catch { L.F(ArenaLink.C_Volume, 0.8f); }
            for (int s = 0; s < ArenaLink.Seats; s++) WriteSeat(L, s, paused);
            Enemies.WriteTargets(L);
            Enemies.ApplyHits(L);
            Enemies.ApplyFx(L);
            MirrorHealth(L);
            Picks.Tick(L);
            if (Link.ArenaUp && L.I(ArenaLink.ResultOff + ArenaLink.R_Run) == run) TestMode.Mark("engine-ready");
        }

        private static string Describe()
        {
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < SeatChar.Length; s++) if (SeatChar[s] != null) sb.Append((sb.Length > 0 ? ", " : "") + (s == 0 ? "Orange" : "Blue") + " = VS player " + SeatChar[s]._PlayerIndex);
            return sb.ToString();
        }

        /// <summary>Main players who are Orange or Blue take their seat; a second player on the same character takes the free one.</summary>
        private static void FindSeats(GameManager gm)
        {
            for (int s = 0; s < SeatChar.Length; s++) SeatChar[s] = null;
            InRun = false;
            if (gm == null || gm.Stage == null) return;
            var mains = gm.MainPlayers;
            if (mains == null) return;
            for (int i = 0; i < mains.Count; i++)
            {
                var c = mains[i];
                if (c == null || c.IsFollower || !Content.IsOurs(c)) continue;
                int seat = Content.SeatOf(c.CharacterType);
                if (SeatChar[seat] != null) seat = 1 - seat;
                if (seat < 0 || seat >= SeatChar.Length || SeatChar[seat] != null) continue;
                SeatChar[seat] = c;
                InRun = true;
            }
        }

        private static void WriteCamera(ArenaLink L)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Cam = cam;
            Vector3 p = cam.transform.position;
            CamCenter = new Vector2(p.x, p.y);
            CamHalfH = Mathf.Max(0.1f, cam.orthographicSize);
            CamAspect = Mathf.Clamp(cam.aspect, 0.5f, 4f);
            L.F(ArenaLink.C_CamX, p.x);
            L.F(ArenaLink.C_CamY, p.y);
            L.F(ArenaLink.C_CamOrtho, CamHalfH);
            L.F(ArenaLink.C_Aspect, CamAspect);
            L.F(ArenaLink.C_Scale, CamHalfH / Tuning.roundsHalfView);
            int fw, fh;
            if (CamAspect >= (float)ArenaLink.MaxW / ArenaLink.MaxH) { fw = ArenaLink.MaxW; fh = Mathf.RoundToInt(ArenaLink.MaxW / CamAspect); }
            else { fh = ArenaLink.MaxH; fw = Mathf.RoundToInt(ArenaLink.MaxH * CamAspect); }
            L.I(ArenaLink.C_FrameW, fw);
            L.I(ArenaLink.C_FrameH, fh);
        }

        private static void WriteSeat(ArenaLink L, int s, bool paused)
        {
            int o = ArenaLink.Slot(s);
            var c = SeatChar[s];
            L.I(o + ArenaLink.S_Present, c != null ? 1 : 0);
            if (c == null) return;
            Vector3 p = c.CachedTransform.position;
            L.F(o + ArenaLink.S_PosX, p.x);
            L.F(o + ArenaLink.S_PosY, p.y);
            Vector2 move = c.CurrentDirection;
            L.F(o + ArenaLink.S_MoveX, paused ? 0f : move.x);
            L.F(o + ArenaLink.S_MoveY, paused ? 0f : move.y);
            Vector2 aim; bool fire;
            SeatInput.Aim(c, new Vector2(p.x, p.y), out aim, out fire);
            L.F(o + ArenaLink.S_AimX, aim.x);
            L.F(o + ArenaLink.S_AimY, aim.y);
            L.I(o + ArenaLink.S_ShootHeld, fire && !paused && !c.IsDead ? 1 : 0);
            if (!paused && SeatInput.Block(c)) L.I(o + ArenaLink.S_BlockPresses, L.I(o + ArenaLink.S_BlockPresses) + 1);
            L.I(o + ArenaLink.S_Life, life[s]);
            int o2 = ArenaLink.Slot2(s);
            Vector2 cursor;
            bool mine = SeatInput.Cursor(c, out cursor);
            L.I(o2 + ArenaLink.U_CursorOn, mine ? 1 : 0);
            L.F(o2 + ArenaLink.U_CursorX, cursor.x);
            L.F(o2 + ArenaLink.U_CursorY, cursor.y);
            L.I(o2 + ArenaLink.U_DownHeld, move.y < -0.5f ? 1 : 0);
        }

        /// <summary>ROUNDS's health is the real one: VS's bar shows it as a share of VS's own max health. A ROUNDS death ends
        /// the VS life (VS's revivals or game over follow).</summary>
        public static void MirrorHealth(ArenaLink L)
        {
            if (!Link.ArenaUp) return;
            for (int s = 0; s < SeatChar.Length; s++)
            {
                var c = SeatChar[s];
                if (c == null) continue;
                int q = ArenaLink.RSlot(s);
                if (L.I(q + ArenaLink.Q_Present) == 0 || L.I(q + ArenaLink.Q_Life) != life[s]) continue;
                float hp = L.F(q + ArenaLink.Q_Hp), max = Mathf.Max(1f, L.F(q + ArenaLink.Q_MaxHp));
                bool dead = L.I(q + ArenaLink.Q_Dead) != 0 || hp <= 0f;
                if (c.IsDead) continue;
                float vsMax = Mathf.Max(1f, c.MaxHp());
                if (dead)
                {
                    if (diedInRounds[s]) continue;
                    diedInRounds[s] = true;
                    Mod.Log("session: " + (s == 0 ? "Orange" : "Blue") + " died in ROUNDS; VS's death follows");
                    passThrough = true;
                    try { c.CurrentHp = 0.01f; c.TakeDamage(vsMax * 10f); }
                    finally { passThrough = false; }
                    continue;
                }
                c.CurrentHp = Mathf.Clamp(vsMax * hp / max, 0.01f, vsMax);
            }
        }

        private static bool passThrough;
        private static int dmgCalls;
        private static float dmgSum, dmgSince;

        // ------------------------------------------------------------------ hooks

        [HarmonyPatch(typeof(CharacterController), nameof(CharacterController.TakeDamage))]
        public static class DamagePatch
        {
            public static void Prefix(CharacterController __instance, out float __state) => __state = BeforeDamage(__instance);
            public static void Postfix(CharacterController __instance, float __state) => AfterDamage(__instance, __state);
        }

        /// <summary>
        /// VS lands its enemies' hits its own way (TakeDamage runs as in VS, with its own pacing); the health a hit takes
        /// from Orange or Blue is given back at once and sent to ROUNDS's health instead (blocked while ROUNDS's block is
        /// up). VS's bar then follows ROUNDS's health (MirrorHealth), and VS may only end the life when ROUNDS's player
        /// is dead (DiePatch).
        /// </summary>
        public static float BeforeDamage(CharacterController c)
        {
            if (passThrough || !InRun || !Content.IsOurs(c) || !Link.ArenaUp || Array.IndexOf(SeatChar, c) < 0) return float.NaN;
            float hp = c.CurrentHp;
            // Measured from full health, so a hit shows whole even when the bar (ROUNDS's health) is nearly empty.
            measuredFrom = Mathf.Max(1f, c.MaxHp());
            c.CurrentHp = measuredFrom;
            return hp;
        }

        private static float measuredFrom;

        public static void AfterDamage(CharacterController c, float hpBefore)
        {
            if (float.IsNaN(hpBefore)) return;
            int s = Array.IndexOf(SeatChar, c);
            float taken = measuredFrom - c.CurrentHp;
            c.CurrentHp = hpBefore;
            if (s < 0 || taken < Tuning.hitNoise || c.IsDead || Picks.Open) return;
            Link.L.PostEvent(ArenaLink.EvDamage, s, taken, 0f, 1f, 0f, life[s]);
            if (DevConsole.On)
            {
                dmgCalls++; dmgSum += taken;
                if (Time.unscaledTime - dmgSince > 2f) { Mod.Log("dev: VS landed " + dmgCalls + " hits, " + dmgSum.ToString("0.0") + " damage in " + (Time.unscaledTime - dmgSince).ToString("0.0") + " s"); dmgCalls = 0; dmgSum = 0f; dmgSince = Time.unscaledTime; }
            }
        }

        [HarmonyPatch(typeof(CharacterController), nameof(CharacterController.Die))]
        public static class DiePatch
        {
            public static bool Prefix(CharacterController __instance) => MayDie(__instance);
        }

        /// <summary>While ROUNDS plays Orange or Blue, only ROUNDS's death ends their VS life.</summary>
        public static bool MayDie(CharacterController c)
        {
            if (passThrough || !InRun || !Content.IsOurs(c) || !Link.ArenaUp || Array.IndexOf(SeatChar, c) < 0) return true;
            if (c.CurrentHp <= 0f) c.CurrentHp = 0.01f;
            return false;
        }

        [HarmonyPatch(typeof(CharacterController), nameof(CharacterController.RecoverHp))]
        public static class HealPatch
        {
            public static bool Prefix(CharacterController __instance, float value) => PlayerHealed(__instance, value);
        }

        public static bool PlayerHealed(CharacterController c, float value)
        {
            if (!InRun || !Content.IsOurs(c) || !Link.ArenaUp || value <= 0f) return true;
            int s = Array.IndexOf(SeatChar, c);
            if (s < 0) return true;
            Link.L.PostEvent(ArenaLink.EvHeal, s, value, 0f, 0f, 0f, life[s]);
            return false;
        }

        [HarmonyPatch(typeof(CharacterController), nameof(CharacterController.Revive))]
        public static class RevivePatch
        {
            public static void Postfix(CharacterController __instance) => Revived(__instance);
        }

        /// <summary>A VS revival (Tiragisu, a partner's revive) is a new life: ROUNDS revives its player at full health.</summary>
        public static void Revived(CharacterController c)
        {
            int s = Array.IndexOf(SeatChar, c);
            if (s < 0) return;
            life[s]++;
            diedInRounds[s] = false;
            Mod.Log("session: " + (s == 0 ? "Orange" : "Blue") + " revived (life " + life[s] + ")");
        }

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.AddStartingWeaponsForAllCharacters))]
        public static class StartingGunPatch
        {
            public static void Postfix(GameManager __instance) => RemoveStartingGun(__instance);
        }

        /// <summary>The ROUNDS gun's VS placeholder slot leaves Orange and Blue before the run starts: ROUNDS's gun fires instead.</summary>
        public static void RemoveStartingGun(GameManager gm)
        {
            try
            {
                var mains = gm.MainPlayers;
                for (int i = 0; mains != null && i < mains.Count; i++)
                {
                    var c = mains[i];
                    if (c == null || !Content.IsOurs(c)) continue;
                    gm.RemoveWeaponFromPlayer(Content.GunSlot, c);
                    Mod.Log("session: " + Content.Rows[c.CharacterType].VsName + "'s VS weapon slot cleared (ROUNDS's gun fires instead)");
                }
            }
            catch (Exception e) { Mod.Once("session: starting gun", e); }
        }
    }
}
