using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.Sound;

namespace RimworldShieldGenerator
{
    public class DynamicShieldEmitterComp : ThingComp
    {
        private List<IntVec3> shieldedCells = new List<IntVec3>();
        private Building_GravEngine cachedGravship;
        private int shieldActiveTimer;
        private const int shieldDelayTicks = 2000;
        private int shieldCooldownTicksRemaining;
        private bool IsOnCooldown => shieldCooldownTicksRemaining > 0;
        private int nextShieldRefreshTick;
        private bool manualOverrideEnabled = false;
        private bool manualShieldActive = false;

        private Mesh cubeMesh;

        public static readonly SoundDef HitSoundDef = SoundDef.Named("ArcShield_Hit");
        private static readonly Vector3[] CardinalDirs3D =
        {
            new Vector3(1f, 0f, 0f),   // East
            new Vector3(-1f, 0f, 0f),  // West
            new Vector3(0f, 0f, 1f),   // North
            new Vector3(0f, 0f, -1f),  // South
        };

        private Color shieldColor = new Color(0.2f, 0.45f, 0.85f, 1f); // Default blue

        private void OpenColorPicker()
        {
            Find.WindowStack.Add(new Dialog_ShieldColorPicker(
                shieldColor,
                newColor =>
                {
                    shieldColor = newColor;
                    ShieldDrawer.SetShieldColor(newColor);
                }));
        }

        // ---------- Core Lifecycle ----------

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            Logger.Message($"[{parent}] PostSpawnSetup start. RespawningAfterLoad={respawningAfterLoad}");

            try
            {
                base.PostSpawnSetup(respawningAfterLoad);
                cachedGravship = FindContainingGravEngine();

                if (cachedGravship != null)
                {
                    Logger.Message($"[{parent}] Linked to GravEngine {cachedGravship.Label}.");
                }
                else
                {
                    Logger.Warning($"[{parent}] No GravEngine detected on spawn.");
                }

                Logger.Message("PostSpawnSetup complete.");
            }
            catch (Exception ex)
            {
                Logger.Error($"PostSpawnSetup exception: {ex}");
                throw;
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (parent.Map == null || !parent.Spawned)
                return;

            // Late gravship detection
            if (cachedGravship == null && Find.TickManager.TicksGame % 60 == 0)
            {
                var engine = FindContainingGravEngine();
                if (engine != null)
                {
                    Logger.Message($"[{parent}] Late GravEngine detection succeeded at {engine.Position}.");
                    cachedGravship = engine;
                    RefreshShieldCells();
                }
            }

            // Handle cooldown
            if (shieldCooldownTicksRemaining > 0)
            {
                shieldCooldownTicksRemaining--;
                ApplyOffPower();
                return;
            }

            if (!IsActive())
            {
                ApplyOffPower();
                return;
            }

            if (shieldActiveTimer <= shieldDelayTicks)
                shieldActiveTimer++;

            // Periodic refresh every 1200 ticks (~20 seconds)
            if (Find.TickManager.TicksGame >= nextShieldRefreshTick)
            {
                nextShieldRefreshTick = Find.TickManager.TicksGame + 1200;
                RefreshShieldCells();
            }

            ShieldProjectiles();
            ApplyIdlePower();
        }

        private void ApplyOffPower()
        {
            try
            {
                var comp = parent.GetComp<CompPowerTrader>();
                if (comp == null) return;

                comp.PowerOutput = 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyOffPower exception: {ex}");
            }
        }

        private void ApplyIdlePower()
        {
            try
            {
                var comp = parent.GetComp<CompPowerTrader>();
                if (comp == null) return;

                // In RimWorld, negative PowerOutput consumes power (draws from the net).
                comp.PowerOutput = -IdleWatts;
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyIdlePower exception: {ex}");
            }
        }

        public override void PostDraw()
        {
            try
            {
                if (parent == null)
                {
                    Logger.Warning("PostDraw: parent is null!");
                    return;
                }

                if (cubeMesh == null)
                {
                    cubeMesh = GraphicsUtil.CreateCuboidMesh();
                    Logger.Warning($"PostDraw: cubeMesh was null, regenerated.");
                    return;
                }

                base.PostDraw();

                if (IsActive())
                {
                    ShieldDrawer.DrawShield(shieldedCells, parent);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"PostDraw exception for {parent}: {ex}");
                throw;
            }
        }

        // ---------- Shield Logic ----------

        private bool IsActive()
        {
            if (IsOnCooldown)
                return false;

            if (!PowerOn)
                return false;

            if (manualOverrideEnabled)
            {
                // Manual override always wins
                return manualShieldActive;
            }

            bool active = parent.Spawned && IsThreatPresent();

            if (active)
                shieldActiveTimer = 0;
            else if (shieldActiveTimer > shieldDelayTicks)
                active = false;

            return active;
        }

        private bool PowerOn =>
            parent.GetComp<CompPowerTrader>()?.PowerOn ?? false;

        private float IdleWatts
        {
            get
            {
                // Constant idle draw from settings (defaults to 600W if not set)
                try { return RimworldShieldGeneratorMod.settings?.shieldIdleWatts ?? 600f; }
                catch { return 600f; }
            }
        }

        private bool IsThreatPresent()
        {
            try
            {
                return GenHostility.AnyHostileActiveThreatToPlayer(parent.Map, true);
            }
            catch (Exception ex)
            {
                Logger.Error($"IsThreatPresent exception: {ex}");
                return false;
            }
        }

        private void ShieldProjectiles()
        {
            if (shieldedCells.NullOrEmpty()) return;
            foreach (var cell in shieldedCells)
                BlockProjectilesAt(cell);
        }

        private void BlockProjectilesAt(IntVec3 cell)
        {
            try
            {
                if (!cell.InBounds(parent.Map)) return;

                var thingsHere = parent.Map.thingGrid.ThingsListAt(cell).ToList();

                foreach (Thing thing in thingsHere)
                {
                    if (thing is Projectile proj && !proj.Destroyed)
                    {
                        if (!WasFiredByPlayer(proj))
                        {
                            if (ConsumeShieldEnergy(proj)){
                                proj.Destroy();
                                FleckMaker.ThrowLightningGlow(proj.ExactPosition, parent.Map, 1.5f);
                                FleckMaker.ThrowSmoke(proj.ExactPosition, parent.Map, 1.5f);
                                HitSoundDef.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
                            }
                            else
                            {
                                TriggerShieldFailure();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"BlockProjectilesAt exception at {cell}: {ex}");
            }
        }

        private void TriggerShieldFailure()
        {
            if(RimworldShieldGeneratorMod.settings?.shieldCooldownTicks == 0)
            {
                return;
            }

            try
            {
                Logger.Warning($"[{parent}] Shield failure due to insufficient power!");

                // Disable immediately
                shieldCooldownTicksRemaining = RimworldShieldGeneratorMod.settings?.shieldCooldownTicks ?? 5000;
                shieldActiveTimer = 0;

                // Visual EMP burst effect
                if (!shieldedCells.NullOrEmpty())
                {
                    foreach (var cell in shieldedCells)
                    {
                        if (!cell.InBounds(parent.Map)) continue;

                        FleckMaker.ThrowExplosionInterior(cell.ToVector3Shifted(), parent.Map, FleckDefOf.MicroSparks);
                        FleckMaker.ThrowSmoke(cell.ToVector3Shifted(), parent.Map, 1.0f);
                        FleckMaker.ThrowLightningGlow(cell.ToVector3Shifted(), parent.Map, 0.8f);
                    }
                }

                float radius = 3f; // fallback
                if (!shieldedCells.NullOrEmpty())
                {
                    int minX = shieldedCells.Min(c => c.x);
                    int maxX = shieldedCells.Max(c => c.x);
                    int minZ = shieldedCells.Min(c => c.z);
                    int maxZ = shieldedCells.Max(c => c.z);

                    float width = maxX - minX + 1;
                    float height = maxZ - minZ + 1;
                    radius = Math.Max(width, height);
                    radius = Math.Max(radius, 79);
                }

                // Big effect centered on shield origin
                GenExplosion.DoExplosion(
                    parent.Position,
                    parent.Map,
                    radius,
                    DamageDefOf.EMP,
                    instigator: parent,
                    damAmount: shieldedCells.Count,
                    armorPenetration: 0,
                    explosionSound: HitSoundDef,
                    weapon: null,
                    projectile: null,
                    intendedTarget: null,
                    postExplosionSpawnThingDef: null,
                    postExplosionSpawnChance: 0f,
                    postExplosionSpawnThingCount: 0,
                    applyDamageToExplosionCellsNeighbors: false,
                    chanceToStartFire: 0f,
                    damageFalloff: false
                );

                // Optional: small battery feedback drain
                var powerComp = parent.GetComp<CompPowerTrader>();
                var net = powerComp?.PowerNet;
                if (net != null)
                {
                    foreach (var bat in net.batteryComps)
                    {
                        float feedbackLoss = bat.StoredEnergy * 0.05f; // lose 5% stored energy
                        bat.DrawPower(feedbackLoss);
                    }
                }

                Messages.Message("Shield Generator overloaded and shut down!", parent, MessageTypeDefOf.NegativeEvent);
            }
            catch (Exception ex)
            {
                Logger.Error($"TriggerShieldFailure exception: {ex}");
            }
        }


        // Draw energy from connected batteries when the shield blocks a projectile.
        private bool ConsumeShieldEnergy(Projectile proj)
        {
            try
            {
                float costWd = ComputeInterceptEnergyCostWd(proj); // in Watt-days

                var powerComp = parent.GetComp<CompPowerTrader>();
                if (powerComp == null || powerComp.PowerNet == null)
                    return false;

                float available = powerComp.PowerNet.batteryComps.Sum(b => b.StoredEnergy);
                if (available < costWd)
                    return false;

                // Drain energy
                float remaining = costWd;
                foreach (var bat in powerComp.PowerNet.batteryComps)
                {
                    float draw = Mathf.Min(bat.StoredEnergy, remaining);
                    bat.DrawPower(draw);
                    remaining -= draw;
                    if (remaining <= 0f) break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ConsumeShieldEnergy exception: {ex}");
                return false;
            }
            return true;
        }

        // Central place to decide the per-intercept energy cost (in Watt-days).
        // You can make this damage/type/defs-based later.
        private float ComputeInterceptEnergyCostWd(Projectile proj)
        {
            // Pull a base cost from settings; default to 0.12 Wd (~100W for ~17 mins).
            float baseCostWd = RimworldShieldGeneratorMod.settings?.shieldInterceptCostWd ?? 6f;
            float percentageOfDamageDrained = RimworldShieldGeneratorMod.settings?.percentageOfDamageDrained/100 ?? 0.5f;

            // Optional “smart” scaling: bump cost a bit with projectile damage.
            // DamageAmount is per hit; scale lightly so it doesn’t nuke batteries instantly.
            int dmg = 0;
            try { dmg = proj.DamageAmount; } catch { /* ignore */ }

            // Example: +0.002 Wd per point of damage (tune to taste or remove)
            float dmgFactorWd = dmg * percentageOfDamageDrained;

            return Mathf.Max(0f, baseCostWd + dmgFactorWd);
        }


        private bool WasFiredByPlayer(Projectile proj)
        {
            Thing launcher = GetInstanceField(typeof(Projectile), proj, "launcher") as Thing;
            return launcher?.Faction?.IsPlayer ?? false;
        }

        public static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field?.GetValue(instance);
        }

        // ---------- Shield Generation ----------

        private void RefreshShieldCells()
        {
            try
            {
                Logger.Message($"[{parent}] RefreshShieldCells (Gravship mode)");
                GenerateGravshipShieldCells();
                Logger.Message($"[{parent}] Refreshed shield cells ({shieldedCells?.Count ?? 0}).");
            }
            catch (Exception ex)
            {
                Logger.Error($"RefreshShieldCells exception: {ex}");
            }
        }

        private void GenerateGravshipShieldCells()
        {
            try
            {
                var engine = FindContainingGravEngine();
                if (engine == null)
                {
                    Logger.Warning($"[{parent}] No GravEngine found for shield generation. Clearing shielded cells.");
                    shieldedCells.Clear();
                    return;
                }

                var substructure = engine.ValidSubstructure;
                if (substructure == null || substructure.Count == 0)
                {
                    Logger.Warning($"[{parent}] GravEngine has no valid substructure cells.");
                    shieldedCells.Clear();
                    return;
                }

                var map = parent.Map;
                var hull = new HashSet<IntVec3>(substructure);
                var border = new HashSet<IntVec3>();

                var directions = GenAdj.CardinalDirections.Concat(GenAdj.DiagonalDirections);

                foreach (var cell in hull)
                {
                    foreach (var dir in directions)
                    {
                        var neighbor = cell + dir;
                        if (!neighbor.InBounds(map)) continue;
                        if (!hull.Contains(neighbor))
                            border.Add(neighbor);
                    }
                }

                border.RemoveWhere(c => hull.Contains(c));
                shieldedCells = border.ToList();
            }
            catch (Exception ex)
            {
                Logger.Error($"[{parent}] GenerateGravshipShieldCells exception: {ex}");
                shieldedCells.Clear();
            }
        }

        private Building_GravEngine FindContainingGravEngine()
        {
            try
            {
                if (parent.Map == null)
                {
                    Logger.Warning($"[{parent}] FindContainingGravEngine: parent.Map is null!");
                    return null;
                }

                var engines = parent.Map.listerThings.AllThings
                    .OfType<Building_GravEngine>()
                    .ToList();

                foreach (var engine in engines)
                {
                    var comp = engine.GetComp<CompSubstructureFootprint>();
                    if (comp == null) continue;

                    float radius = comp.Props.radius;
                    float dist = parent.Position.DistanceTo(engine.Position);

                    if (dist <= radius)
                        return engine;
                }

                return null;
            }
            catch (Exception e)
            {
                Logger.Error($"[{parent}] Error in FindContainingGravEngine: {e}");
                return null;
            }
        }

        // ---------- UI + Saving ----------

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 🎨 Color picker
            yield return new Command_Action
            {
                defaultLabel = "Change Shield Color",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ChangeColor", true),
                action = OpenColorPicker,
                defaultDesc = "Change the color of this shield's visual effect."
            };

            // ⚙️ Manual override toggle
            yield return new Command_Toggle
            {
                defaultLabel = "Manual Override",
                defaultDesc = "Enable or disable manual control of the shield. When enabled, automatic threat detection is ignored.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ChangeColor", true),
                isActive = () => manualOverrideEnabled,
                toggleAction = () =>
                {
                    manualOverrideEnabled = !manualOverrideEnabled;
                }
            };

            // 🛡️ Manual shield control (only visible when override is enabled)
            if (manualOverrideEnabled)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = manualShieldActive ? "Deactivate Shield" : "Activate Shield",
                    defaultDesc = manualShieldActive
                        ? "Manually turn the shield off."
                        : "Manually activate the shield, ignoring threats.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ChangeColor", true),
                    isActive = () => manualShieldActive,
                    toggleAction = () =>
                    {
                        manualShieldActive = !manualShieldActive;
                        RefreshShieldCells(); // <-- add this
                    }
                };
            }
        }


        public override void PostExposeData()
        {
            Scribe_Values.Look(ref shieldActiveTimer, "shieldActiveTimer");
            Scribe_Values.Look(ref shieldColor, "shieldColor", new Color(0.2f, 0.45f, 0.85f, 1f));
            Scribe_Values.Look(ref shieldCooldownTicksRemaining, "shieldCooldownTicksRemaining", 0);
            Scribe_Values.Look(ref manualOverrideEnabled, "manualOverrideEnabled", false);
            Scribe_Values.Look(ref manualShieldActive, "manualShieldActive", false);
        }

        public override string CompInspectStringExtra()
        {
            string status;

            if (IsOnCooldown)
            {
                float secondsLeft = shieldCooldownTicksRemaining / 60f; // convert ticks → seconds
                status = $"<color=#FF8800>COOLDOWN</color> ({secondsLeft:F1}s remaining)";
            }
            else if (!PowerOn)
            {
                status = "<color=#FF5555>Offline (No Power)</color>";
            }
            else if (!IsThreatPresent())
            {
                status = "<color=#BBBBBB>Idle</color>";
            }
            else
            {
                status = "<color=#00FFAA>Active</color>";
            }

            if (manualOverrideEnabled)
            {
                status += manualShieldActive ? " (Manual ON)" : " (Manual OFF)";
            }

            return
                $"Status: {status}\n" +
                $"Mode: Gravship\n" +
                $"Idle Draw: {IdleWatts:F0} W\n" +
                (IsOnCooldown
                    ? $"Cooldown: {shieldCooldownTicksRemaining / 60f:F1}s remaining"
                    : $"Ready to intercept projectiles.");
        }


    }
}
