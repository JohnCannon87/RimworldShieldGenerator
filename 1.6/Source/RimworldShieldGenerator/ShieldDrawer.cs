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
    class ShieldDrawer
    {
        private static Material material;
        private static Color shieldColor = new Color(0, 0.5f, 0.5f, 0.35f);

        // ---------- Drawing ----------
        public static void DrawShield(List<IntVec3> shieldedCells, ThingWithComps parent)
        {
            if (material == null)
                material = SolidColorMaterials.NewSolidColorMaterial(shieldColor, ShaderDatabase.MetaOverlay);

            if (shieldedCells.NullOrEmpty())
                return;

            Map map = parent.Map;
            HashSet<IntVec3> shieldSet = new HashSet<IntVec3>(shieldedCells);

            // --- 1️⃣ Flood fill to mark outside cells ---
            Queue<IntVec3> openQueue = new Queue<IntVec3>();
            HashSet<IntVec3> outside = new HashSet<IntVec3>();

            foreach (IntVec3 edge in map.AllCells)
            {
                if (edge.x == 0 || edge.z == 0 || edge.x == map.Size.x - 1 || edge.z == map.Size.z - 1)
                {
                    if (!shieldSet.Contains(edge))
                    {
                        openQueue.Enqueue(edge);
                        outside.Add(edge);
                    }
                }
            }

            while (openQueue.Count > 0)
            {
                IntVec3 c = openQueue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    if (!n.InBounds(map) || outside.Contains(n) || shieldSet.Contains(n))
                        continue;

                    outside.Add(n);
                    openQueue.Enqueue(n);
                }
            }

            // --- 2️⃣ Collect outer edges ---
            List<EdgeSegment> horizEdges = new List<EdgeSegment>(); // east-west edges (facing north/south)
            List<EdgeSegment> vertEdges = new List<EdgeSegment>();  // north-south edges (facing east/west)

            foreach (IntVec3 cell in shieldedCells)
            {
                if (!cell.InBounds(map))
                    continue;

                for (int i = 0; i < 4; i++)
                {
                    IntVec3 dir = GenAdj.CardinalDirections[i];
                    IntVec3 n = cell + dir;
                    if (!n.InBounds(map))
                        continue;

                    if (outside.Contains(n))
                    {
                        if (dir.z != 0)
                            horizEdges.Add(new EdgeSegment(cell, dir));
                        else
                            vertEdges.Add(new EdgeSegment(cell, dir));
                    }
                }
            }

            // --- 3️⃣ Merge and draw continuous runs ---
            MergeAndDrawEdges(horizEdges, true);
            MergeAndDrawEdges(vertEdges, false);
        }
                private struct EdgeSegment
        {
            public IntVec3 Cell;
            public IntVec3 Dir;

            public EdgeSegment(IntVec3 c, IntVec3 d)
            {
                Cell = c;
                Dir = d;
            }

            public override int GetHashCode()
            {
                return Cell.GetHashCode() ^ Dir.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (!(obj is EdgeSegment)) return false;
                EdgeSegment other = (EdgeSegment)obj;
                return Cell.Equals(other.Cell) && Dir.Equals(other.Dir);
            }
        }
        private static void MergeAndDrawEdges(List<EdgeSegment> edges, bool horizontal)
        {
            if (edges.Count == 0)
                return;

            HashSet<EdgeSegment> used = new HashSet<EdgeSegment>();
            IntVec3 step = horizontal ? IntVec3.East : IntVec3.North;

            foreach (EdgeSegment e in edges)
            {
                if (used.Contains(e))
                    continue;

                IntVec3 start = e.Cell;
                IntVec3 end = e.Cell;

                // Extend line in both directions
                while (true)
                {
                    IntVec3 next = end + step;
                    EdgeSegment nextEdge = new EdgeSegment(next, e.Dir);
                    if (!edges.Contains(nextEdge))
                        break;
                    end = next;
                    used.Add(nextEdge);
                }

                while (true)
                {
                    IntVec3 prev = start - step;
                    EdgeSegment prevEdge = new EdgeSegment(prev, e.Dir);
                    if (!edges.Contains(prevEdge))
                        break;
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
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }
    }
}
