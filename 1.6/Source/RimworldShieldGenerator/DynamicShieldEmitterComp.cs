using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.Sound;
using System.Reflection;

namespace RimworldShieldGenerator
{
    public class DynamicShieldEmitterComp : ThingComp
    {
        private enum ShieldMode { Area, Gravship }

        private ShieldMode currentMode = ShieldMode.Area;
        private List<IntVec3> shieldedCells = new List<IntVec3>();
        private Area selectedArea;
        private string selectedAreaLabel;
        private Building_GravEngine cachedGravship;
        private Color shieldColor = new Color(0, 0.5f, 0.5f, 0.35f);

        private int shieldActiveTimer;
        private const int shieldDelayTicks = 2000;

        private Mesh cubeMesh;
        private Material material;

        private bool dirtyMesh = true;
        private List<Mesh> meshes = new List<Mesh>();
        private readonly float shieldThickness = 0.5f;

        public static readonly SoundDef HitSoundDef = SoundDef.Named("WallShield_Hit");

        // ---------- Core Lifecycle ----------

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            Logger.Message($"[{parent}] PostSpawnSetup start. RespawningAfterLoad={respawningAfterLoad}");

            try
            {
                base.PostSpawnSetup(respawningAfterLoad);

                if (respawningAfterLoad && !selectedAreaLabel.NullOrEmpty())
                {
                    selectedArea = parent.Map.areaManager.GetLabeled(selectedAreaLabel);
                    Logger.Message($"Restored selected area: {selectedAreaLabel}");
                }

                cachedGravship = FindContainingGravEngine();
                if (cachedGravship != null)
                {
                    currentMode = ShieldMode.Gravship;
                    Logger.Message($"[{parent}] Auto-detected gravship {cachedGravship.Label}. Using Gravship mode.");
                }
                else
                {
                    Logger.Message($"[{parent}] No gravship detected on spawn.");
                }

                RefreshShieldCells();
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

            // 🔹 Delayed gravship detection
            if (currentMode == ShieldMode.Gravship && cachedGravship == null && Find.TickManager.TicksGame % 60 == 0)
            {
                var engine = FindContainingGravEngine();
                if (engine != null)
                {
                    Logger.Message($"[{parent}] Late GravEngine detection succeeded at {engine.Position}.");
                    RefreshShieldCells();
                }
            }

            if (!IsActive())
            {
                SetPowerLevel(0);
                return;
            }

            shieldActiveTimer++;
            ShieldProjectiles();
            SetPowerLevel(PowerUsage);
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

                if (cubeMesh == null || material == null)
                {
                    cubeMesh = GraphicsUtil.CreateCuboidMesh();
                    material = SolidColorMaterials.NewSolidColorMaterial(shieldColor, ShaderDatabase.MetaOverlay);

                    Logger.Warning($"PostDraw: cubeMesh or material were null (cubeMesh={cubeMesh != null}, material={material != null})");
                    return;
                }

                base.PostDraw();

                if (IsActive())
                {
                    Logger.Message($"[{parent}] Drawing shield ({shieldedCells.Count} cells).");
                    DrawShield();
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
            bool active = parent.Spawned && PowerOn && IsThreatPresent();

            if (active)
                shieldActiveTimer = 0;
            else if (shieldActiveTimer > shieldDelayTicks)
                active = false;

            return active;
        }

        private bool PowerOn =>
            parent.GetComp<CompPowerTrader>()?.PowerOn ?? false;

        private float PowerUsage
        {
            get
            {
                int cells = shieldedCells != null ? shieldedCells.Count : 0;
                int perCell = 0;
                try { perCell = RimworldShieldGeneratorMod.settings?.shieldPowerPerCell ?? 0; }
                catch (Exception ex)
                {
                    Logger.Warning($"PowerUsage: settings read failed: {ex}");
                }
                return cells * perCell;
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

        private void SetPowerLevel(float watts)
        {
            try
            {
                var comp = parent.GetComp<CompPowerTrader>();
                if (comp != null) comp.PowerOutput = watts;
            }
            catch (Exception ex)
            {
                Logger.Error($"SetPowerLevel exception: {ex}");
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
                            Logger.Message($"Intercepted projectile {proj.Label} at {cell}");
                            proj.Destroy();
                            FleckMaker.ThrowLightningGlow(proj.ExactPosition, parent.Map, 1.5f);
                            FleckMaker.ThrowSmoke(proj.ExactPosition, parent.Map, 1.5f);
                            HitSoundDef.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"BlockProjectilesAt exception at {cell}: {ex}");
            }
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
                switch (currentMode)
                {
                    case ShieldMode.Gravship:
                        Logger.Message($"[{parent}] RefreshShieldCells -> Gravship");
                        GenerateGravshipShieldCells();
                        break;

                    case ShieldMode.Area:
                        Logger.Message($"[{parent}] RefreshShieldCells -> Area");
                        GenerateAreaShieldCells();
                        break;
                }

                Logger.Message($"[{parent}] Refreshed shield cells ({shieldedCells?.Count ?? 0}) in {currentMode} mode.");
            }
            catch (Exception ex)
            {
                Logger.Error($"RefreshShieldCells exception: {ex}");
            }
        }

        private void GenerateAreaShieldCells()
        {
            if (selectedArea == null)
            {
                Logger.Warning("No area selected for shield generation.");
                shieldedCells.Clear();
                return;
            }

            dirtyMesh = true;
            shieldedCells = selectedArea.ActiveCells.ToList();
            Logger.Message($"GenerateAreaShieldCells -> {shieldedCells.Count} cells");
        }

        private void GenerateGravshipShieldCells()
        {
            Logger.Message($"[{parent}] GenerateGravshipShieldCells called.");

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

                // Use both cardinal + diagonal directions for smoother outline
                var directions = GenAdj.CardinalDirections.Concat(GenAdj.DiagonalDirections);

                // Collect edge-adjacent cells
                foreach (var cell in hull)
                {
                    foreach (var dir in directions)
                    {
                        var neighbor = cell + dir;
                        if (!neighbor.InBounds(map)) continue;

                        // Add only cells that are outside the hull
                        if (!hull.Contains(neighbor))
                            border.Add(neighbor);
                    }
                }

                // Slightly expand outward for shield thickness (1 cell out from the hull)
                var expanded = new HashSet<IntVec3>(border);
                foreach (var cell in border)
                {
                    foreach (var dir in directions)
                    {
                        var neighbor = cell + dir;
                        if (neighbor.InBounds(map) && !hull.Contains(neighbor))
                            expanded.Add(neighbor);
                    }
                }

                // Filter out any cells that overlap the substructure or are inside it
                expanded.RemoveWhere(c => hull.Contains(c));

                shieldedCells = expanded.ToList();
                Logger.Message($"[{parent}] Generated hull-following outer shield with {shieldedCells.Count} cells.");
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

                // Get all grav engines on the map
                var engines = parent.Map.listerThings.AllThings
                    .OfType<Building_GravEngine>()
                    .ToList();

                if (engines.Count == 0)
                {
                    Logger.Message($"[{parent}] No GravEngines found on this map.");
                    return null;
                }

                // Find the one whose substructure radius includes our position
                foreach (var engine in engines)
                {
                    var comp = engine.GetComp<CompSubstructureFootprint>();
                    if (comp == null)
                        continue;

                    float radius = comp.Props.radius;
                    float dist = parent.Position.DistanceTo(engine.Position);

                    if (dist <= radius)
                    {
                        Logger.Message($"[{parent}] Found GravEngine at {engine.Position} (radius {radius:F1}) - within {dist:F1} cells.");
                        return engine;
                    }
                }

                Logger.Message($"[{parent}] No GravEngine found within any substructure radius.");
                return null;
            }
            catch (Exception e)
            {
                Logger.Error($"[{parent}] Error in FindContainingGravEngine: {e}");
                return null;
            }
        }



        // ---------- Drawing ----------

        private void DrawShield()
        {
            if (cubeMesh == null)
                cubeMesh = GraphicsUtil.CreateCuboidMesh();

            if (material == null)
                material = SolidColorMaterials.NewSolidColorMaterial(shieldColor, ShaderDatabase.MetaOverlay);

            if (shieldedCells.NullOrEmpty())
                return;

            foreach (var cell in shieldedCells)
                DrawShieldSegment(cell);
        }

        private void DrawShieldSegment(IntVec3 cell)
        {
            try
            {
                if (cubeMesh == null || material == null)
                {
                    Logger.Warning($"DrawShieldSegment: cubeMesh or material is null! (cubeMesh={cubeMesh != null}, material={material != null})");
                    return;
                }

                Vector3 center = cell.ToVector3Shifted();
                Vector3 scale = new Vector3(shieldThickness, 1f, shieldThickness);
                Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.identity, scale);
                Graphics.DrawMesh(cubeMesh, matrix, material, 0);
            }
            catch (Exception ex)
            {
                Logger.Error($"DrawShieldSegment exception: {ex}");
                throw;
            }
        }

        // ---------- UI + Saving ----------

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new Command_Action
            {
                defaultLabel = "Toggle Shield Mode",
                icon = ContentFinder<Texture2D>.Get("UI/EnableManualMode"),
                action = () => ToggleMode(),
                activateSound = SoundDef.Named("Click")
            };

            if (currentMode == ShieldMode.Area)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Select Shield Area",
                    icon = ContentFinder<Texture2D>.Get("UI/SelectRegion"),
                    action = () => SelectArea()
                };
            }
        }

        private void ToggleMode()
        {
            currentMode = currentMode == ShieldMode.Area ? ShieldMode.Gravship : ShieldMode.Area;
            Logger.Message($"[{parent}] Toggled mode to {currentMode}");

            if (currentMode == ShieldMode.Gravship)
            {
                cachedGravship = FindContainingGravEngine();
                if (cachedGravship != null)
                    Logger.Message($"[{parent}] Gravship detected on mode swap: {cachedGravship.Label}");
                else
                    Logger.Warning($"[{parent}] No gravship found after switching to Gravship mode!");
            }

            RefreshShieldCells();
        }

        private void SelectArea()
        {
            AreaUtility.MakeAllowedAreaListFloatMenu(a =>
            {
                selectedArea = a;
                selectedAreaLabel = a.Label;
                Logger.Message($"Area selected: {selectedAreaLabel}");
                RefreshShieldCells();
            }, addNullAreaOption: false, addManageOption: true, parent.Map);
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref selectedAreaLabel, "selectedAreaLabel");
            Scribe_Values.Look(ref shieldActiveTimer, "shieldActiveTimer");
            Scribe_Values.Look(ref currentMode, "currentMode");
        }

        public override string CompInspectStringExtra()
        {
            return $"Mode: {currentMode}\n" +
                   $"Cells: {shieldedCells.Count}\n" +
                   $"Power: {Mathf.Abs(PowerUsage):F0} W";
        }
    }
}
