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
                float a = L.F(f + ArenaLink.FX_A), b = L.F(f + ArenaLink.FX_B);
                if (L.I(f + ArenaLink.FX_Serial) != fxRead + 1) break;
                fxRead++;
                EnemyController e;
                if (!byId.TryGetValue(target, out e) || e == null || e.IsDead) continue;
                try { Fx(e, type, a, b); }
                catch (Exception ex) { Mod.Once("enemies: effect " + type, ex); }
            }
        }

        private static void Fx(EnemyController e, int type, float a, float b)
        {
            bool boss = e.IsBoss;
            switch (type)
            {
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
