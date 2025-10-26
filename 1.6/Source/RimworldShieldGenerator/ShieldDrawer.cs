using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimworldShieldGenerator
{
    class ShieldDrawer
    {
        // --- Materials / Color ---
        private static Material borderMaterial;
        private static Material pulseMaterial;
        private static readonly MaterialPropertyBlock MPB = new MaterialPropertyBlock();

        // Player-selected color (rgb + alpha used everywhere)
        private static Color ShieldColor = new Color(0.2f, 0.45f, 0.85f, 1.0f);

        // --- Pulse mesh cache ---
        private static List<Mesh> cachedPulseMeshes; // batched meshes
        private static int cachedCellsHash;
        private static int cachedCellCount;
        private static IntVec2 cachedMin; // bounds
        private static IntVec2 cachedMax;
        private static Map cachedMap;

        // --- Tunables ---
        private const int MAX_VERTS_PER_MESH = 60000; // Unity limit safety
        private const int VERTS_PER_CELL = 4;
        private const int TRIS_PER_CELL = 6;

        // Public API from comp
        public static void SetShieldColor(Color newColor)
        {
            ShieldColor = newColor;
            borderMaterial = null;
            pulseMaterial = null;
            // keep meshes; alpha changes are applied via MPB each draw
        }

        public static void DrawShield(List<IntVec3> shieldedCells, ThingWithComps parent)
        {
            if (shieldedCells == null || shieldedCells.Count == 0 || parent?.Map == null)
                return;

            Map map = parent.Map;

            // Clamp alpha so it never fully vanishes (still honor player alpha)
            float userAlpha = Mathf.Clamp01(ShieldColor.a);
            float baseAlpha = Mathf.Clamp(userAlpha, 0.05f, 1f);

            // Materials (created once per color change)
            if (borderMaterial == null)
                borderMaterial = SolidColorMaterials.NewSolidColorMaterial(
                    new Color(ShieldColor.r, ShieldColor.g, ShieldColor.b, 0.35f),
                    ShaderDatabase.MetaOverlay);

            if (pulseMaterial == null)
                pulseMaterial = SolidColorMaterials.NewSolidColorMaterial(
                    new Color(ShieldColor.r, ShieldColor.g, ShieldColor.b, 1f),
                    ShaderDatabase.MoteGlow);

            // --- Step 1: Build bounded inside region (shield interior) ---
            HashSet<IntVec3> region;
            CellRect bounds;
            BuildInsideRegionBounded(shieldedCells, map, parent.Position, out region, out bounds);

            // --- Step 2: Rebuild pulse meshes only if footprint changed ---
            int cellsHash = HashCells(region);
            if (cachedPulseMeshes == null
                || cachedCellCount != region.Count
                || cachedCellsHash != cellsHash
                || cachedMap != map
                || cachedMin.x != bounds.minX || cachedMin.z != bounds.minZ
                || cachedMax.x != bounds.maxX || cachedMax.z != bounds.maxZ)
            {
                RebuildPulseMeshes(region, bounds, map);

                cachedCellCount = region.Count;
                cachedCellsHash = cellsHash;
                cachedMin = new IntVec2(bounds.minX, bounds.minZ);
                cachedMax = new IntVec2(bounds.maxX, bounds.maxZ);
                cachedMap = map;
            }

            // --- Step 3: Draw pulse (batched meshes) with pulsing alpha ---
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 0.9f);
            float finalAlpha = baseAlpha * 0.25f * pulse; // soft interior
            Color pulseColor = new Color(ShieldColor.r, ShieldColor.g, ShieldColor.b, finalAlpha);
            MPB.SetColor(ShaderPropertyIDs.Color, pulseColor);

            Vector3 pos = new Vector3(0f, AltitudeLayer.MoteOverhead.AltitudeFor() + 0.001f, 0f);
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

            if (cachedPulseMeshes != null)
            {
                for (int i = 0; i < cachedPulseMeshes.Count; i++)
                {
                    Graphics.DrawMesh(cachedPulseMeshes[i], matrix, pulseMaterial, 0, null, 0, MPB);
                }
            }

            // --- Step 4: Draw border (only where region meets true outside) ---
            DrawBorder(region, bounds, map);
        }

        // ------------------------------------------------------------
        // Inside region (bounded flood) + border extraction
        // ------------------------------------------------------------
        private static void BuildInsideRegionBounded(List<IntVec3> shieldedCells, Map map, IntVec3 seed, out HashSet<IntVec3> region, out CellRect bounds)
        {
            // Tight bounds of the shield line shape
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < shieldedCells.Count; i++)
            {
                IntVec3 c = shieldedCells[i];
                if (c.x < minX) minX = c.x;
                if (c.z < minZ) minZ = c.z;
                if (c.x > maxX) maxX = c.x;
                if (c.z > maxZ) maxZ = c.z;
            }

            // expand by 1 to be safe
            minX = Math.Max(0, minX - 1);
            minZ = Math.Max(0, minZ - 1);
            maxX = Math.Min(map.Size.x - 1, maxX + 1);
            maxZ = Math.Min(map.Size.z - 1, maxZ + 1);
            bounds = CellRect.FromLimits(minX, minZ, maxX, maxZ);

            // --- copy to a local for inner helper use ---
            CellRect localBounds = bounds;

            // Mask of shield edge cells inside bounds
            bool[,] shieldMask = new bool[localBounds.Width, localBounds.Height];
            for (int i = 0; i < shieldedCells.Count; i++)
            {
                var c = shieldedCells[i];
                int lx = c.x - localBounds.minX;
                int lz = c.z - localBounds.minZ;
                if (lx >= 0 && lz >= 0 && lx < localBounds.Width && lz < localBounds.Height)
                    shieldMask[lx, lz] = true;
            }

            // Flood from perimeter of bounds to find OUTSIDE (not crossing shield line)
            bool[,] outside = new bool[localBounds.Width, localBounds.Height];
            Queue<IntVec2> q = new Queue<IntVec2>();

            // enqueue perimeter cells
            for (int x = 0; x < localBounds.Width; x++)
            {
                TryEnqueueOutside(x, 0);
                TryEnqueueOutside(x, localBounds.Height - 1);
            }
            for (int z = 0; z < localBounds.Height; z++)
            {
                TryEnqueueOutside(0, z);
                TryEnqueueOutside(localBounds.Width - 1, z);
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                // 4-neighbours
                TryEnqueueOutside(p.x + 1, p.z);
                TryEnqueueOutside(p.x - 1, p.z);
                TryEnqueueOutside(p.x, p.z + 1);
                TryEnqueueOutside(p.x, p.z - 1);
            }

            // Anything not outside and not a shield edge is INSIDE region
            region = new HashSet<IntVec3>();
            for (int z = 0; z < localBounds.Height; z++)
            {
                for (int x = 0; x < localBounds.Width; x++)
                {
                    if (!outside[x, z]) // interior or shield
                    {
                        region.Add(new IntVec3(localBounds.minX + x, 0, localBounds.minZ + z));
                    }
                }
            }

            // Local helper
            void TryEnqueueOutside(int lx, int lz)
            {
                if (lx < 0 || lz < 0 || lx >= localBounds.Width || lz >= localBounds.Height)
                    return;
                if (outside[lx, lz]) return;
                if (shieldMask[lx, lz]) return; // cannot pass through shield line
                outside[lx, lz] = true;
                q.Enqueue(new IntVec2(lx, lz));
            }
        }


        private static void DrawBorder(HashSet<IntVec3> region, CellRect bounds, Map map)
        {
            // Find edges where a region cell has a neighbor that is OUTSIDE bounds or not in region
            List<EdgeSegment> horizEdges = new List<EdgeSegment>(region.Count / 2);
            List<EdgeSegment> vertEdges = new List<EdgeSegment>(region.Count / 2);

            foreach (var cell in region)
            {
                // up/down (z)
                var north = cell + IntVec3.North;
                var south = cell + IntVec3.South;
                if (!InRegion(region, north)) horizEdges.Add(new EdgeSegment(cell, IntVec3.North));
                if (!InRegion(region, south)) horizEdges.Add(new EdgeSegment(cell, IntVec3.South));

                // left/right (x)
                var east = cell + IntVec3.East;
                var west = cell + IntVec3.West;
                if (!InRegion(region, east)) vertEdges.Add(new EdgeSegment(cell, IntVec3.East));
                if (!InRegion(region, west)) vertEdges.Add(new EdgeSegment(cell, IntVec3.West));
            }

            // Merge & draw
            MergeAndDrawEdges(horizEdges, true);
            MergeAndDrawEdges(vertEdges, false);
        }

        private static bool InRegion(HashSet<IntVec3> region, IntVec3 c) => region.Contains(c);

        // ------------------------------------------------------------
        // Pulse mesh building (batched)
        // ------------------------------------------------------------
        private static void RebuildPulseMeshes(HashSet<IntVec3> region, CellRect bounds, Map map)
        {
            if (region == null || region.Count == 0)
            {
                cachedPulseMeshes = null;
                return;
            }

            // How many cells can we fit safely per mesh
            int maxCellsPerMesh = Math.Max(1, (MAX_VERTS_PER_MESH / VERTS_PER_CELL) - 8);

            // Pack cells into batches deterministically (scan bounds)
            List<IntVec3> ordered = new List<IntVec3>(region.Count);
            for (int z = bounds.minZ; z <= bounds.maxZ; z++)
            {
                for (int x = bounds.minX; x <= bounds.maxX; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (region.Contains(c)) ordered.Add(c);
                }
            }

            var meshes = new List<Mesh>(Mathf.CeilToInt(ordered.Count / (float)maxCellsPerMesh));

            int idx = 0;
            while (idx < ordered.Count)
            {
                int batchCount = Math.Min(maxCellsPerMesh, ordered.Count - idx);

                var verts = new List<Vector3>(batchCount * VERTS_PER_CELL);
                var tris = new List<int>(batchCount * TRIS_PER_CELL);
                var uvs = new List<Vector2>(batchCount * VERTS_PER_CELL);

                int vi = 0;
                for (int i = 0; i < batchCount; i++)
                {
                    var cell = ordered[idx++];
                    float x = cell.x;
                    float z = cell.z;

                    verts.Add(new Vector3(x, 0f, z));
                    verts.Add(new Vector3(x + 1f, 0f, z));
                    verts.Add(new Vector3(x + 1f, 0f, z + 1f));
                    verts.Add(new Vector3(x, 0f, z + 1f));

                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(1f, 1f));
                    uvs.Add(new Vector2(0f, 1f));

                    tris.Add(vi + 0);
                    tris.Add(vi + 2);
                    tris.Add(vi + 1);
                    tris.Add(vi + 0);
                    tris.Add(vi + 3);
                    tris.Add(vi + 2);

                    vi += 4;
                }

                var mesh = new Mesh();
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.SetUVs(0, uvs);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                meshes.Add(mesh);
            }

            cachedPulseMeshes = meshes;
        }

        // ------------------------------------------------------------
        // Edge merging + drawing
        // ------------------------------------------------------------
        private struct EdgeSegment
        {
            public IntVec3 Cell;
            public IntVec3 Dir;
            public EdgeSegment(IntVec3 c, IntVec3 d) { Cell = c; Dir = d; }
            public override int GetHashCode() => Cell.GetHashCode() ^ Dir.GetHashCode();
            public override bool Equals(object obj) => obj is EdgeSegment o && Cell.Equals(o.Cell) && Dir.Equals(o.Dir);
        }

        private static void MergeAndDrawEdges(List<EdgeSegment> edges, bool horizontal)
        {
            if (edges == null || edges.Count == 0)
                return;

            // Fast lookup
            HashSet<EdgeSegment> set = new HashSet<EdgeSegment>(edges);
            HashSet<EdgeSegment> used = new HashSet<EdgeSegment>();
            IntVec3 step = horizontal ? IntVec3.East : IntVec3.North;

            foreach (var e in edges)
            {
                if (used.Contains(e)) continue;

                IntVec3 start = e.Cell;
                IntVec3 end = e.Cell;

                // extend forward
                for (; ; )
                {
                    var next = end + step;
                    var nextEdge = new EdgeSegment(next, e.Dir);
                    if (!set.Contains(nextEdge)) break;
                    end = next;
                    used.Add(nextEdge);
                }
                // extend backward
                for (; ; )
                {
                    var prev = start - step;
                    var prevEdge = new EdgeSegment(prev, e.Dir);
                    if (!set.Contains(prevEdge)) break;
                    start = prev;
                    used.Add(prevEdge);
                }

                used.Add(e);
                DrawShieldLine(start, end, horizontal, e.Dir);
            }
        }

        private static void DrawShieldLine(IntVec3 start, IntVec3 end, bool horizontal, IntVec3 dir)
        {
            Vector3 mid = (start.ToVector3Shifted() + end.ToVector3Shifted()) / 2f;
            float length = (end - start).LengthHorizontal + 1f;
            float thickness = 0.12f;

            Vector3 outward = new Vector3(dir.x, 0f, dir.z) * 0.5f;
            Vector3 pos = mid + outward + new Vector3(0f, 0.05f, 0f);

            Vector3 scale = horizontal
                ? new Vector3(length, 1f, thickness)
                : new Vector3(thickness, 1f, length);

            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, borderMaterial, 0);
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private static int HashCells(HashSet<IntVec3> cells)
        {
            unchecked
            {
                int h = 17;
                foreach (var c in cells)
                {
                    h = h * 31 + c.x;
                    h = h * 31 + c.z;
                }
                return h;
            }
        }
    }
}
