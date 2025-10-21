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
        private static Material borderMaterial;
        private static Material pulseMaterial;

        // 🔵 Unified color — player-selectable
        private static Color ShieldColor = new Color(0.2f, 0.45f, 0.85f, 1.0f);

        private static Mesh cachedShieldMesh;
        private static int cachedCellCount;
        private static int lastMapHash;

        // Exposed setter for customization
        public static void SetShieldColor(Color newColor)
        {
            ShieldColor = newColor;
            borderMaterial = null;
            pulseMaterial = null;
        }

        public static void DrawShield(List<IntVec3> shieldedCells, ThingWithComps parent)
        {
            if (shieldedCells.NullOrEmpty()) return;

            // Clamp alpha range a little so we never fully vanish
            float baseAlpha = Mathf.Clamp(ShieldColor.a, 0.05f, 1f);

            // --- (Re)build materials if needed ---
            if (borderMaterial == null)
                borderMaterial = SolidColorMaterials.NewSolidColorMaterial(
                    new Color(ShieldColor.r, ShieldColor.g, ShieldColor.b, 0.35f),
                    ShaderDatabase.MetaOverlay
                );

            if (pulseMaterial == null)
                pulseMaterial = SolidColorMaterials.NewSolidColorMaterial(
                    new Color(ShieldColor.r, ShieldColor.g, ShieldColor.b, 1.0f * baseAlpha),
                    ShaderDatabase.MoteGlow
                );

            Map map = parent.Map;
            HashSet<IntVec3> shieldSet = new HashSet<IntVec3>(shieldedCells);

            // --- outside fill ---
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

            // --- collect border edges ---
            List<EdgeSegment> horizEdges = new List<EdgeSegment>();
            List<EdgeSegment> vertEdges = new List<EdgeSegment>();
            foreach (IntVec3 cell in shieldedCells)
            {
                if (!cell.InBounds(map)) continue;
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 dir = GenAdj.CardinalDirections[i];
                    IntVec3 n = cell + dir;
                    if (!n.InBounds(map)) continue;
                    if (outside.Contains(n))
                    {
                        if (dir.z != 0) horizEdges.Add(new EdgeSegment(cell, dir));
                        else vertEdges.Add(new EdgeSegment(cell, dir));
                    }
                }
            }

            // --- Draw ---
            DrawUnifiedPulse(shieldedCells, map, parent, baseAlpha);
            MergeAndDrawEdges(horizEdges, true);
            MergeAndDrawEdges(vertEdges, false);
        }

        private static void DrawUnifiedPulse(List<IntVec3> shieldedCells, Map map, ThingWithComps parent, float alpha)
        {
            // Reuse same inside fill logic
            HashSet<IntVec3> shieldSet = new HashSet<IntVec3>(shieldedCells);
            HashSet<IntVec3> inside = new HashSet<IntVec3>();

            Queue<IntVec3> open = new Queue<IntVec3>();
            IntVec3 origin = parent.Position;
            if (origin.InBounds(map) && !shieldSet.Contains(origin))
                open.Enqueue(origin);

            while (open.Count > 0)
            {
                IntVec3 c = open.Dequeue();
                if (!c.InBounds(map) || inside.Contains(c) || shieldSet.Contains(c))
                    continue;

                inside.Add(c);

                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    if (n.InBounds(map) && !shieldSet.Contains(n) && !inside.Contains(n))
                        open.Enqueue(n);
                }
            }

            inside.UnionWith(shieldedCells);
            var allCells = inside.ToList();

            if (cachedShieldMesh == null || cachedCellCount != allCells.Count || lastMapHash != map.GetHashCode())
            {
                cachedShieldMesh = new Mesh();
                cachedCellCount = allCells.Count;
                lastMapHash = map.GetHashCode();

                List<Vector3> verts = new List<Vector3>();
                List<int> tris = new List<int>();
                List<Vector2> uvs = new List<Vector2>();
                int vertIndex = 0;

                foreach (IntVec3 cell in allCells)
                {
                    float x = cell.x;
                    float z = cell.z;
                    verts.Add(new Vector3(x, 0f, z));
                    verts.Add(new Vector3(x + 1f, 0f, z));
                    verts.Add(new Vector3(x + 1f, 0f, z + 1f));
                    verts.Add(new Vector3(x, 0f, z + 1f));
                    uvs.AddRange(new[] {
                new Vector2(0,0), new Vector2(1,0),
                new Vector2(1,1), new Vector2(0,1)
            });
                    tris.Add(vertIndex);
                    tris.Add(vertIndex + 2);
                    tris.Add(vertIndex + 1);
                    tris.Add(vertIndex);
                    tris.Add(vertIndex + 3);
                    tris.Add(vertIndex + 2);
                    vertIndex += 4;
                }

                cachedShieldMesh.SetVertices(verts);
                cachedShieldMesh.SetTriangles(tris, 0);
                cachedShieldMesh.SetUVs(0, uvs);
                cachedShieldMesh.RecalculateNormals();
                cachedShieldMesh.RecalculateBounds();
            }

            // --- Alpha-aware pulse ---
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 0.9f);
            float finalAlpha = alpha * 0.25f * pulse; // softer interior fade
            Color color = new Color(ShieldColor.r, ShieldColor.g, ShieldColor.b, finalAlpha);

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor(ShaderPropertyIDs.Color, color);

            Vector3 pos = new Vector3(0f, AltitudeLayer.MoteOverhead.AltitudeFor() + 0.001f, 0f);
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
            Graphics.DrawMesh(cachedShieldMesh, matrix, pulseMaterial, 0, null, 0, mpb);
        }

        private static void DrawUnifiedPulse(List<IntVec3> shieldedCells, Map map, ThingWithComps parent)
        {
            HashSet<IntVec3> shieldSet = new HashSet<IntVec3>(shieldedCells);
            HashSet<IntVec3> inside = new HashSet<IntVec3>();

            Queue<IntVec3> open = new Queue<IntVec3>();
            IntVec3 origin = parent.Position;
            if (origin.InBounds(map) && !shieldSet.Contains(origin))
                open.Enqueue(origin);

            while (open.Count > 0)
            {
                IntVec3 c = open.Dequeue();
                if (!c.InBounds(map) || inside.Contains(c) || shieldSet.Contains(c))
                    continue;

                inside.Add(c);

                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    if (n.InBounds(map) && !shieldSet.Contains(n) && !inside.Contains(n))
                        open.Enqueue(n);
                }
            }

            inside.UnionWith(shieldedCells);
            var allCells = inside.ToList();

            if (cachedShieldMesh == null || cachedCellCount != allCells.Count || lastMapHash != map.GetHashCode())
            {
                cachedShieldMesh = new Mesh();
                cachedCellCount = allCells.Count;
                lastMapHash = map.GetHashCode();

                List<Vector3> verts = new List<Vector3>();
                List<int> tris = new List<int>();
                List<Vector2> uvs = new List<Vector2>();

                int vertIndex = 0;
                foreach (IntVec3 cell in allCells)
                {
                    float x = cell.x;
                    float z = cell.z;

                    verts.Add(new Vector3(x, 0f, z));
                    verts.Add(new Vector3(x + 1f, 0f, z));
                    verts.Add(new Vector3(x + 1f, 0f, z + 1f));
                    verts.Add(new Vector3(x, 0f, z + 1f));

                    uvs.Add(new Vector2(0, 0));
                    uvs.Add(new Vector2(1, 0));
                    uvs.Add(new Vector2(1, 1));
                    uvs.Add(new Vector2(0, 1));

                    tris.Add(vertIndex);
                    tris.Add(vertIndex + 2);
                    tris.Add(vertIndex + 1);
                    tris.Add(vertIndex);
                    tris.Add(vertIndex + 3);
                    tris.Add(vertIndex + 2);

                    vertIndex += 4;
                }

                cachedShieldMesh.SetVertices(verts);
                cachedShieldMesh.SetTriangles(tris, 0);
                cachedShieldMesh.SetUVs(0, uvs);
                cachedShieldMesh.RecalculateNormals();
                cachedShieldMesh.RecalculateBounds();
            }

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 0.9f);
            Color color = new Color(ShieldColor.r, ShieldColor.g, ShieldColor.b, 0.15f * pulse);

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor(ShaderPropertyIDs.Color, color);

            Vector3 pos = new Vector3(0f, AltitudeLayer.MoteOverhead.AltitudeFor() + 0.001f, 0f);
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

            Graphics.DrawMesh(cachedShieldMesh, matrix, pulseMaterial, 0, null, 0, mpb);
        }

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
            if (edges.Count == 0) return;
            HashSet<EdgeSegment> used = new HashSet<EdgeSegment>();
            IntVec3 step = horizontal ? IntVec3.East : IntVec3.North;

            foreach (EdgeSegment e in edges)
            {
                if (used.Contains(e)) continue;
                IntVec3 start = e.Cell;
                IntVec3 end = e.Cell;

                while (true)
                {
                    IntVec3 next = end + step;
                    EdgeSegment nextEdge = new EdgeSegment(next, e.Dir);
                    if (!edges.Contains(nextEdge)) break;
                    end = next;
                    used.Add(nextEdge);
                }

                while (true)
                {
                    IntVec3 prev = start - step;
                    EdgeSegment prevEdge = new EdgeSegment(prev, e.Dir);
                    if (!edges.Contains(prevEdge)) break;
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
    }
}
