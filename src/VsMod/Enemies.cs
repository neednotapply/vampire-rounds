using System;
using System.Collections.Generic;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.Framework;
using Il2CppVampireSurvivors.Objects.Characters;
using UnityEngine;

namespace VampireRounds.Vs
{
    /// <summary>
    /// VS's enemies in the hidden ROUNDS (sheets/systems.json "enemies", sheets/effects.json): every frame the live enemies
    /// near the camera go into the target table (nearest first), each with an id for its life (VS pools enemies, so a
    /// reused one gets a new id). ROUNDS's hits come back as VS's own GetDamaged (damage numbers, knockback, death, gems);
    /// its slows, stuns and silences as VS's own SlowEnemy, Freeze and DoDefang.
    /// </summary>
    public static class Enemies
    {
        private struct Seen { public EnemyController e; public int id; public float d; }
        private static Dictionary<IntPtr, int> ids = new Dictionary<IntPtr, int>(), idsNext = new Dictionary<IntPtr, int>();
        private static readonly Dictionary<int, EnemyController> byId = new Dictionary<int, EnemyController>();
        private static readonly List<Seen> seen = new List<Seen>();
        private static int nextId = 1, hitRead, fxRead;
        public static int Count { get; private set; }
        public static int HitsApplied, HitsMissed;

        public static void Reset()
        {
            ids.Clear();
            byId.Clear();
            drift.Clear();
            drifting.Clear();
            var L = Link.L;
            if (L == null) return;
            hitRead = L.I(ArenaLink.ResultOff + ArenaLink.R_HitWrite);
            fxRead = L.I(ArenaLink.ResultOff + ArenaLink.R_FxWrite);
        }

        public static void WriteTargets(ArenaLink L)
        {
            var stage = GM.Core.Stage;
            var list = stage != null ? stage.SpawnedEnemies : null;
            seen.Clear();
            idsNext.Clear();
            byId.Clear();
            if (list != null)
            {
                Vector2 c = Session.CamCenter;
                float hw = Session.CamHalfH * Session.CamAspect * 1.25f, hh = Session.CamHalfH * 1.25f;
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    if (e == null || e.IsDead) continue;
                    var t = e._cachedTransform;
                    if (t == null || !e.gameObject.activeInHierarchy) continue;
                    Vector3 p = t.position;
                    float dx = p.x - c.x, dy = p.y - c.y;
                    if (Mathf.Abs(dx) > hw || Mathf.Abs(dy) > hh) continue;
                    IntPtr key = e.Pointer;
                    int id;
                    if (!ids.TryGetValue(key, out id)) id = nextId++;
                    idsNext[key] = id;
                    byId[id] = e;
                    seen.Add(new Seen { e = e, id = id, d = dx * dx + dy * dy });
                }
            }
            var swap = ids; ids = idsNext; idsNext = swap;
            if (seen.Count > ArenaLink.MaxTargets) seen.Sort((a, b) => a.d.CompareTo(b.d));
            int n = Math.Min(seen.Count, ArenaLink.MaxTargets);
            for (int i = 0; i < n; i++)
            {
                var s = seen[i];
                int o = ArenaLink.Target(i);
                Vector3 p = s.e._cachedTransform.position;
                L.I(o + ArenaLink.T_Id, s.id);
                L.F(o + ArenaLink.T_X, p.x);
                L.F(o + ArenaLink.T_Y, p.y);
                L.F(o + ArenaLink.T_R, Radius(s.e));
                L.I(o + ArenaLink.T_Flags, s.e.IsBoss ? ArenaLink.TfBoss : ArenaLink.TfLight);
                L.F(o + ArenaLink.T_Hp, s.e.Hp);
            }
            L.I(ArenaLink.C_TargetCount, n);
            Count = n;
        }

        private static float Radius(EnemyController e)
        {
            try
            {
                var b = e.body;
                if (b != null && b.WorldRadius > 0.001f) return b.WorldRadius;
            }
            catch { }
            var r = e.EnemyRenderer;
            return r != null ? Mathf.Max(0.05f, Mathf.Min(r.bounds.extents.x, r.bounds.extents.y)) : 0.12f;
        }

        /// <summary>Which way is away from the enemies within range (each pushes harder the closer it is); zero when none.</summary>
        public static Vector2 Away(Vector2 from, float range)
        {
            Vector2 sum = Vector2.zero;
            float r2 = range * range;
            foreach (var s in seen)
            {
                if (s.e == null || s.e.IsDead) continue;
                Vector3 p = s.e._cachedTransform.position;
                Vector2 d = from - new Vector2(p.x, p.y);
                float m = d.sqrMagnitude;
                if (m > r2 || m < 1e-6f) continue;
                sum += d / m;
            }
            return sum;
        }

        public static EnemyController Nearest(Vector2 from, float range)
        {
            EnemyController best = null;
            float bd = range * range;
            foreach (var s in seen)
            {
                if (s.e == null || s.e.IsDead) continue;
                Vector3 p = s.e._cachedTransform.position;
                float d = (p.x - from.x) * (p.x - from.x) + (p.y - from.y) * (p.y - from.y);
                if (d < bd) { bd = d; best = s.e; }
            }
            return best;
        }

        // ------------------------------------------------------------------ ROUNDS -> VS

        public static void ApplyHits(ArenaLink L)
        {
            int write = L.I(ArenaLink.ResultOff + ArenaLink.R_HitWrite);
            if (write < hitRead) hitRead = write; // ROUNDS started again
            if (write - hitRead > ArenaLink.HitRing) hitRead = write - ArenaLink.HitRing;
            while (hitRead < write)
            {
                int h = ArenaLink.ResultOff + ArenaLink.R_Hits + (hitRead % ArenaLink.HitRing) * ArenaLink.HitSize;
                if (L.I(h + ArenaLink.H_Serial) != hitRead + 1) break; // still being written: next frame
                int target = L.I(h + ArenaLink.H_Target), seat = L.I(h + ArenaLink.H_Slot), kind = L.I(h + ArenaLink.H_Kind);
                float dmg = L.F(h + ArenaLink.H_Damage);
                if (L.I(h + ArenaLink.H_Serial) != hitRead + 1) break;
                hitRead++;
                EnemyController e;
                if (!byId.TryGetValue(target, out e) || e == null || e.IsDead) { HitsMissed++; continue; }
                HitsApplied++;
                try { Hit(e, dmg, seat, kind); }
                catch (Exception ex) { Mod.Once("enemies: hit", ex); }
            }
        }

        private static void Hit(EnemyController e, float roundsDamage, int seat, int kind)
        {
            float might = 1f;
            var c = seat >= 0 && seat < Session.SeatChar.Length ? Session.SeatChar[seat] : null;
            if (c != null) { try { might = Mathf.Max(0.1f, c.PPower()); } catch { } }
            float dmg = roundsDamage * Tuning.damageScale * might;
            if (dmg <= 0f) return;
            bool over = kind == ArenaLink.HitDot || kind == ArenaLink.HitContinuous || kind == ArenaLink.HitAura;
            e.GetDamaged(dmg, over ? HitVfxType.None : (kind == ArenaLink.HitArea ? HitVfxType.Explo : HitVfxType.Default), over ? 0f : 1f, WeaponType.VOID, !over);
            TestMode.Mark("enemy-hit");
        }

        private static readonly Dictionary<IntPtr, Vector2> drift = new Dictionary<IntPtr, Vector2>();
        private static readonly Dictionary<IntPtr, EnemyController> drifting = new Dictionary<IntPtr, EnemyController>();
        private static readonly List<IntPtr> driftDone = new List<IntPtr>();

        /// <summary>Enemies sliding from a lasting push move with their built-up velocity each frame (VS units/s), which
        /// fades once the push stops (tuning enemyDriftFade per second); paused with VS.</summary>
        public static void Drift(bool paused)
        {
            if (drift.Count == 0 || paused) return;
            float dt = Time.deltaTime, keep = Mathf.Exp(-Tuning.enemyDriftFade * dt);
            driftDone.Clear();
            foreach (var kv in drifting)
            {
                var e = kv.Value;
                Vector2 v = drift[kv.Key];
                if (e == null || e.IsDead || v.sqrMagnitude < 0.0025f) { driftDone.Add(kv.Key); continue; }
                try
                {
                    var t = e._cachedTransform;
                    t.position = t.position + new Vector3(v.x * dt, v.y * dt, 0f);
                }
                catch { driftDone.Add(kv.Key); continue; }
                drift[kv.Key] = v * keep;
            }
            foreach (var k in driftDone) { drift.Remove(k); drifting.Remove(k); }
        }

        public static void ApplyFx(ArenaLink L)
        {
            int write = L.I(ArenaLink.ResultOff + ArenaLink.R_FxWrite);
            if (write < fxRead) fxRead = write;
            if (write - fxRead > ArenaLink.FxRing) fxRead = write - ArenaLink.FxRing;
            while (fxRead < write)
            {
                int f = ArenaLink.ResultOff + ArenaLink.R_Fx + (fxRead % ArenaLink.FxRing) * ArenaLink.FxSize;
                if (L.I(f + ArenaLink.FX_Serial) != fxRead + 1) break;
                int target = L.I(f + ArenaLink.FX_Target), type = L.I(f + ArenaLink.FX_Type);
                float a = L.F(f + ArenaLink.FX_A), b = L.F(f + ArenaLink.FX_B), c = L.F(f + ArenaLink.FX_C), d = L.F(f + ArenaLink.FX_D);
                if (L.I(f + ArenaLink.FX_Serial) != fxRead + 1) break;
                fxRead++;
                EnemyController e;
                if (!byId.TryGetValue(target, out e) || e == null || e.IsDead) continue;
                try { Fx(e, type, a, b, c, d); }
                catch (Exception ex) { Mod.Once("enemies: effect " + type, ex); }
            }
        }

        private static void Fx(EnemyController e, int type, float a, float b, float c, float d)
        {
            bool boss = e.IsBoss;
            switch (type)
            {
                case ArenaLink.FxGravity:
                {
                    // Seen from above nothing falls: more gravity (Grounded, Crush) pins the enemy to the ground, so it walks
                    // slower; less or upside-down gravity (Toss, Flip) lifts it off its feet, helpless for a moment.
                    float secs = Mathf.Max(0.1f, b);
                    if (a > 1f) e.SlowEnemy(secs * 1000f, 1f, Mathf.Clamp(1f - 1f / a, 0.2f, 0.6f) * (boss ? Tuning.bossSlowScale : Tuning.enemySlowScale));
                    else if (a < 1f) e.Freeze(Mathf.Min(secs, boss ? Tuning.bossStunCap : Tuning.enemyStunCap) * 1000f, 1f, 255u);
                    break;
                }
                case ArenaLink.FxPull:
                {
                    // Implode's pull toward the blast (a, b: the point in VS units): most of the way in, bosses stay put.
                    if (boss) break;
                    var t = e._cachedTransform;
                    Vector2 to = new Vector2(a, b) - new Vector2(t.position.x, t.position.y);
                    Vector2 step = Vector2.ClampMagnitude(to * Tuning.enemyPullShare, Tuning.enemyPushMax);
                    t.position = t.position + new Vector3(step.x, step.y, 0f);
                    break;
                }
                case ArenaLink.FxSlow:
                {
                    float amount = Mathf.Clamp(a * (boss ? Tuning.bossSlowScale : Tuning.enemySlowScale), 0f, 0.9f);
                    // ROUNDS's slow decays at 0.3 per second: it lasts about amount / 0.3 s.
                    if (amount > 0.01f) e.SlowEnemy(Mathf.Max(0.3f, amount / 0.3f) * 1000f, 1f, amount);
                    break;
                }
                case ArenaLink.FxStun:
                    e.Freeze(Mathf.Min(a, boss ? Tuning.bossStunCap : Tuning.enemyStunCap) * 1000f, 1f, 255u);
                    break;
                case ArenaLink.FxSilence:
                case ArenaLink.FxConfuse:
                    e.DoDefang(a * 1000f, 4521864u, false);
                    break;
                case ArenaLink.FxGold:
                    e.SlowEnemy(60000f, 1f, 0.5f);
                    break;
                case ArenaLink.FxForce:
                {
                    if (boss) break;
                    if (d > 0.5f)
                    {
                        // A push that keeps going (a Thruster stuck to the enemy, ROUNDS's Force every physics step): its
                        // velocity builds up while it burns and the enemy slides with it (Drift), as a thruster rockets a
                        // ROUNDS player across the map. One frame's share as a nudge moved it about nothing (0.3.0).
                        Vector2 v;
                        drift.TryGetValue(e.Pointer, out v);
                        drift[e.Pointer] = Vector2.ClampMagnitude(v + new Vector2(a, b), Tuning.enemyDriftMax);
                        drifting[e.Pointer] = e;
                        break;
                    }
                    Vector2 dv = new Vector2(a, b) * Tuning.enemyPushScale;
                    if (dv.magnitude > Tuning.enemyPushMax) dv = dv.normalized * Tuning.enemyPushMax;
                    var t = e._cachedTransform;
                    t.position = t.position + new Vector3(dv.x, dv.y, 0f);
                    break;
                }
            }
        }
    }
}
