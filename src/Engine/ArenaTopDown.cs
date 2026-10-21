using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VampireRounds
{
    /// <summary>
    /// ROUNDS's jumps seen from above (sheets/hooks.json r_hop): VS shows the arena from the top, where up is only north. A
    /// ROUNDS jump of Orange or Blue (Pogo every 4 s, Super Jump, any card that jumps them) is a hop over the horde instead
    /// (<see cref="Arena.StartHop"/>): the guy rises over his spot and lands, and no VS hit lands while he is in the air.
    /// </summary>
    public static class HopPatch
    {
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerJump), "Jump")]
        public static void Hopped(PlayerJump __instance, float multiplier)
        {
            var a = Arena.I;
            if (a == null || !a.WorldReady || __instance == null) return;
            try
            {
                var data = __instance.GetComponent<CharacterData>();
                // Jump sets sinceJump to 0 when it jumps; ROUNDS turned this one down (no jump left).
                if (data == null || data.player == null || data.sinceJump > 0f) return;
                a.StartHop(data.player, multiplier);
            }
            catch (Exception e) { a.LogOnce("hop", e); }
        }
    }

    /// <summary>
    /// Card-mod bullets made for ROUNDS's side view, seen from above (sheets/hooks.json r_axis_bullets). CR's Shooting Star,
    /// Comet, Meteor and Cloud speed a bullet up or slow it down along one world axis every 0.1 s (ShootingStarMono: y x1.025,
    /// CometMono: x x1.1, MeteorMono: y x1.5 while falling, CloudMono: x x0.9 and y x0.85), for bullets that rise and fall.
    /// From above that only bends them north, south, east or west (Shooting Star sent the shots to the top of the screen), so
    /// each such change is undone and the bullet changes speed along its own path instead, every 0.1 s like the card:
    /// Shooting Star x1.025, Comet x1.1, Meteor x1.1 (from above a bullet never falls, so it gets Comet's pace), Cloud
    /// x0.875 (it slows and lingers, without drifting south). Careen only stops a falling bullet from falling (CareenMono
    /// zeroes a small downward speed), which from above flattened every shot aimed a little south: it does nothing here.
    /// Asteroid (both axes alike), Pulse and Satellite keep the path already and stay as they are. Found by name: skipped
    /// when CR is not installed.
    /// </summary>
    public static class AxisBullets
    {
        private sealed class Rule { public FieldInfo move; public float perTick; }

        private static readonly Dictionary<Type, Rule> rules = new Dictionary<Type, Rule>();
        private static readonly Dictionary<int, float> nextTick = new Dictionary<int, float>();
        /// <summary>The CR scripts' own updateDelay.</summary>
        private const float Tick = 0.1f;

        public static void Apply(Harmony harmony)
        {
            Wrap(harmony, "CR.MonoBehaviors.ShootingStarMono", 1.025f);
            Wrap(harmony, "CR.MonoBehaviors.CometMono", 1.1f);
            Wrap(harmony, "CR.MonoBehaviors.MeteorMono", 1.1f);
            Wrap(harmony, "CR.MonoBehaviors.CloudMono", 0.875f);
            Wrap(harmony, "CR.MonoBehaviors.CareenMono", 1f);
            if (rules.Count > 0) EnginePlugin.L.LogInfo("arena: " + rules.Count + " card-mod bullet scripts keep their bullets' paths seen from above");
        }

        private static void Wrap(Harmony harmony, string typeName, float perTick)
        {
            var t = FindType(typeName);
            if (t == null) return;
            try
            {
                var update = AccessTools.DeclaredMethod(t, "Update");
                var move = AccessTools.Field(t, "move");
                if (update == null || move == null || move.FieldType != typeof(MoveTransform)) { EnginePlugin.L.LogWarning("arena: " + typeName + " has no Update or move; left as it is"); return; }
                rules[t] = new Rule { move = move, perTick = perTick };
                harmony.Patch(update, prefix: new HarmonyMethod(typeof(AxisBullets), nameof(Before)), postfix: new HarmonyMethod(typeof(AxisBullets), nameof(After)));
            }
            catch (Exception e) { EnginePlugin.L.LogWarning("arena: " + typeName + " not wrapped: " + e.Message); }
        }

        /// <summary>A loaded type by full name, or null without any log (the mod is simply not installed).</summary>
        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        private static MoveTransform Move(MonoBehaviour b, out Rule r)
        {
            r = null;
            if (b == null || !rules.TryGetValue(b.GetType(), out r)) return null;
            return r.move.GetValue(b) as MoveTransform;
        }

        /// <summary>The bullet's velocity before the card's script runs (NaN: not an arena bullet, or not started yet).</summary>
        public static void Before(MonoBehaviour __instance, out Vector3 __state)
        {
            __state = new Vector3(float.NaN, 0f, 0f);
            var a = Arena.I;
            if (a == null || !a.WorldReady) return;
            Rule r;
            var move = Move(__instance, out r);
            // Only bullets in the arena, below ROUNDS's own screen (as ArenaPatches.BulletsFlyStraight).
            if (move == null || move.transform.position.y > Arena.OffsetY * 0.5f) return;
            __state = move.velocity;
        }

        public static void After(MonoBehaviour __instance, Vector3 __state)
        {
            if (float.IsNaN(__state.x)) return;
            try
            {
                Rule r;
                var move = Move(__instance, out r);
                if (move == null) return;
                Vector3 v = __state;
                int id = __instance.GetInstanceID();
                float now = Time.time, next;
                if (nextTick.Count > 2048) nextTick.Clear();
                if (!nextTick.TryGetValue(id, out next)) nextTick[id] = now + Tick;
                else if (now >= next) { nextTick[id] = now + Tick; v *= r.perTick; }
                v.z = 0f;
                move.velocity = v;
            }
            catch (Exception e) { if (Arena.I != null) Arena.I.LogOnce("axis bullets", e); }
        }
    }
}
