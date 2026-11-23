using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThatsLit
{
    /// <summary>
    /// Flat, allocation-free hashed 2D XZ conservative broadphase over static scene foliage colliders.
    /// Built lazily once per raid (and invalidated on additive scene loads) to skip per-move
    /// Physics.OverlapSphereNonAlloc queries when no static foliage collider can possibly intersect
    /// the 4m query sphere. When any candidate collider's conservative 3D AABB intersects the query
    /// sphere, the caller runs the exact PhysX Physics.OverlapSphereNonAlloc query so collider
    /// geometry, Terrain/player/Rigidbody semantics, ordering, and scores remain 100% bit-identical
    /// to vanilla PhysX.
    ///
    /// Contract: assumes map foliage colliders are static scene geometry that do not move, change
    /// layers into foliageLayerMask, or spawn dynamically inside an already-loaded scene without a
    /// SceneManager.sceneLoaded event. Can be disabled live via EnableFoliageSpatialGrid if a mod
    /// spawns dynamic foliage colliders mid-raid.
    /// </summary>
    internal sealed class FoliageSpatialGrid
    {
        private const float CellSize = 8.0f;
        private const float InvCellSize = 1.0f / CellSize;
        private const int MaxCellsPerCollider = 64;
        private const int MaxOversizedEntries = 64;
        private const int MaxQueryCellsPerAxis = 8;
        private const float MaxSafeCoord = 50000.0f;

        private struct BroadphaseEntry
        {
            public Collider collider;
            public Vector3 boundsMin;
            public Vector3 boundsMax;
        }

        private readonly int _layerMask;
        private readonly BroadphaseEntry[] _entries;
        private readonly int[] _visitStamps;
        private readonly long[] _bucketKeys;
        private readonly int[] _bucketStarts;
        private readonly int[] _bucketCounts;
        private readonly int _bucketMask;
        private readonly int[] _cellEntries;
        private readonly int[] _oversizedEntries;
        private readonly float _worldMinX, _worldMaxX, _worldMinZ, _worldMaxZ;
        private int _queryStamp;

        public int EntryCount => _entries.Length;
        public int CellCount { get; }

        private FoliageSpatialGrid(
            int layerMask,
            BroadphaseEntry[] entries,
            long[] bucketKeys,
            int[] bucketStarts,
            int[] bucketCounts,
            int[] cellEntries,
            int[] oversizedEntries,
            int cellCount,
            float minX, float maxX, float minZ, float maxZ)
        {
            _layerMask = layerMask;
            _entries = entries;
            _visitStamps = new int[entries.Length];
            _bucketKeys = bucketKeys;
            _bucketStarts = bucketStarts;
            _bucketCounts = bucketCounts;
            _bucketMask = bucketKeys.Length - 1;
            _cellEntries = cellEntries;
            _oversizedEntries = oversizedEntries;
            CellCount = cellCount;
            _worldMinX = minX;
            _worldMaxX = maxX;
            _worldMinZ = minZ;
            _worldMaxZ = maxZ;
        }

        internal static FoliageSpatialGrid Build(LayerMask foliageLayerMask)
        {
            int mask = foliageLayerMask.value;
            if (mask == 0)
                return null;

            Collider[] allColliders = UnityEngine.Object.FindObjectsOfType<Collider>(true);
            if (allColliders == null || allColliders.Length == 0)
                return null;

            var entriesList = new List<BroadphaseEntry>(1024);
            var cellMap = new Dictionary<long, List<int>>(2048);
            var oversizedList = new List<int>();

            float worldMinX = float.PositiveInfinity;
            float worldMaxX = float.NegativeInfinity;
            float worldMinZ = float.PositiveInfinity;
            float worldMaxZ = float.NegativeInfinity;

            for (int i = 0; i < allColliders.Length; i++)
            {
                Collider c = allColliders[i];
                if (c == null) continue;
                GameObject go = c.gameObject;
                if (!go.scene.IsValid()) continue;
                if ((mask & (1 << go.layer)) == 0) continue;

                // Player-root colliders (layer 8) are skipped by vanilla UpdateFoliageScore and cannot contribute score.
                if (go.transform.root.gameObject.layer == 8) continue;

                // If any non-player dynamic (Rigidbody) collider exists on foliageLayerMask, fall back cleanly to PhysX.
                if (c.attachedRigidbody != null)
                    return null;

                // Vanilla UpdateFoliageScore skips TerrainCollider when scoring; it cannot contribute score on its own.
                if (c is TerrainCollider) continue;

                // Compute a mathematically conservative 3D world AABB (works even if inactive, rotated, negative-scaled, or sheared).
                // If an unknown inactive collider type cannot yield conservative bounds, fall back cleanly to PhysX.
                if (!TryComputeConservativeAabb(c, out Vector3 boundsMin, out Vector3 boundsMax))
                    return null;

                int entryIdx = entriesList.Count;
                entriesList.Add(new BroadphaseEntry
                {
                    collider = c,
                    boundsMin = boundsMin,
                    boundsMax = boundsMax
                });

                if (boundsMin.x < worldMinX) worldMinX = boundsMin.x;
                if (boundsMax.x > worldMaxX) worldMaxX = boundsMax.x;
                if (boundsMin.z < worldMinZ) worldMinZ = boundsMin.z;
                if (boundsMax.z > worldMaxZ) worldMaxZ = boundsMax.z;

                // Outlier coordinate guard: route extreme-coordinate colliders to oversized list before FloorToInt.
                if (boundsMin.x < -MaxSafeCoord || boundsMax.x > MaxSafeCoord
                 || boundsMin.z < -MaxSafeCoord || boundsMax.z > MaxSafeCoord)
                {
                    oversizedList.Add(entryIdx);
                    if (oversizedList.Count > MaxOversizedEntries)
                        return null;
                    continue;
                }

                int minCx = Mathf.FloorToInt(boundsMin.x * InvCellSize);
                int maxCx = Mathf.FloorToInt(boundsMax.x * InvCellSize);
                int minCz = Mathf.FloorToInt(boundsMin.z * InvCellSize);
                int maxCz = Mathf.FloorToInt(boundsMax.z * InvCellSize);

                long spanX = (long)maxCx - (long)minCx + 1L;
                long spanZ = (long)maxCz - (long)minCz + 1L;
                if (spanX <= 0 || spanZ <= 0
                 || spanX > MaxCellsPerCollider || spanZ > MaxCellsPerCollider
                 || spanX * spanZ > MaxCellsPerCollider)
                {
                    oversizedList.Add(entryIdx);
                    if (oversizedList.Count > MaxOversizedEntries)
                        return null;
                    continue;
                }

                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cz = minCz; cz <= maxCz; cz++)
                    {
                        long key = PackCellKey(cx, cz);
                        if (!cellMap.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>(4);
                            cellMap[key] = list;
                        }
                        list.Add(entryIdx);
                    }
                }
            }

            if (entriesList.Count == 0)
                return null;

            int uniqueCells = cellMap.Count;
            int tableSize = 16;
            int minTableSize = Math.Max(16, uniqueCells * 2);
            while (tableSize < minTableSize && tableSize < (1 << 24))
                tableSize <<= 1;
            if (uniqueCells >= tableSize)
                return null;

            int totalCellRefs = 0;
            foreach (var kv in cellMap)
                totalCellRefs += kv.Value.Count;

            var bucketKeys = new long[tableSize];
            var bucketStarts = new int[tableSize];
            var bucketCounts = new int[tableSize];
            var cellEntries = new int[totalCellRefs];
            int maskBits = tableSize - 1;
            int writePos = 0;

            foreach (var kv in cellMap)
            {
                long key = kv.Key;
                List<int> list = kv.Value;
                int count = list.Count;
                int start = writePos;
                for (int j = 0; j < count; j++)
                    cellEntries[writePos++] = list[j];

                int slot = HashKey(key, maskBits);
                while (bucketCounts[slot] != 0)
                    slot = (slot + 1) & maskBits;

                bucketKeys[slot] = key;
                bucketStarts[slot] = start;
                bucketCounts[slot] = count;
            }

            return new FoliageSpatialGrid(
                mask,
                entriesList.ToArray(),
                bucketKeys,
                bucketStarts,
                bucketCounts,
                cellEntries,
                oversizedList.ToArray(),
                uniqueCells,
                worldMinX, worldMaxX, worldMinZ, worldMaxZ);
        }

        /// <summary>
        /// Computes a mathematically conservative world-space 3D AABB that encloses the collider under
        /// arbitrary rotation, negative scale, or shear, even when the collider is inactive at build time.
        /// For Sphere/Capsule, uses Gershgorin's theorem on the Gram matrix (sqrt(||A^T A||_inf) >= ||A||_2),
        /// which is a guaranteed upper bound on maximum vector stretching for any 3x3 linear map.
        /// </summary>
        private static bool TryComputeConservativeAabb(Collider c, out Vector3 boundsMin, out Vector3 boundsMax)
        {
            boundsMin = default;
            boundsMax = default;
            Transform t = c.transform;
            Matrix4x4 m = t.localToWorldMatrix;

            // If active & enabled, we also union with PhysX's own c.bounds for guaranteed conservatism.
            bool hasPhysxBounds = c.enabled && c.gameObject.activeInHierarchy;
            Bounds physxBounds = hasPhysxBounds ? c.bounds : default;

            if (c is SphereCollider sc)
            {
                Vector3 center = m.MultiplyPoint3x4(sc.center);
                float scaleBound = ComputeConservativeStretchBound(m);
                float r = Mathf.Abs(sc.radius) * scaleBound;
                if (!IsFinite(center) || float.IsNaN(r) || float.IsInfinity(r))
                    return false;
                Vector3 ext = new Vector3(r, r, r);
                boundsMin = center - ext;
                boundsMax = center + ext;
            }
            else if (c is BoxCollider bc)
            {
                Bounds b = TransformLocalBox(bc.center, new Vector3(Mathf.Abs(bc.size.x), Mathf.Abs(bc.size.y), Mathf.Abs(bc.size.z)) * 0.5f, m);
                if (!IsFinite(b.min) || !IsFinite(b.max))
                    return false;
                boundsMin = b.min;
                boundsMax = b.max;
            }
            else if (c is CapsuleCollider cc)
            {
                Vector3 center = m.MultiplyPoint3x4(cc.center);
                float scaleBound = ComputeConservativeStretchBound(m);
                float maxLocalExtent = Mathf.Max(Mathf.Abs(cc.height) * 0.5f, Mathf.Abs(cc.radius));
                float r = maxLocalExtent * scaleBound;
                if (!IsFinite(center) || float.IsNaN(r) || float.IsInfinity(r))
                    return false;
                Vector3 ext = new Vector3(r, r, r);
                boundsMin = center - ext;
                boundsMax = center + ext;
            }
            else if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                Bounds localB = mc.sharedMesh.bounds;
                Bounds b = TransformLocalBox(localB.center, localB.extents, m);
                if (!IsFinite(b.min) || !IsFinite(b.max))
                    return false;
                boundsMin = b.min;
                boundsMax = b.max;
            }
            else if (hasPhysxBounds && physxBounds.size.sqrMagnitude > 0f && IsFinite(physxBounds.min) && IsFinite(physxBounds.max))
            {
                boundsMin = physxBounds.min;
                boundsMax = physxBounds.max;
            }
            else
            {
                // Unknown collider type that is currently inactive: cannot safely bound it.
                return false;
            }

            if (hasPhysxBounds && physxBounds.size.sqrMagnitude > 0f && IsFinite(physxBounds.min) && IsFinite(physxBounds.max))
            {
                boundsMin = Vector3.Min(boundsMin, physxBounds.min);
                boundsMax = Vector3.Max(boundsMax, physxBounds.max);
            }

            // Small safety epsilon against float precision mismatch with PhysX broadphase.
            const float pad = 0.05f;
            Vector3 padVec = new Vector3(pad, pad, pad);
            boundsMin -= padVec;
            boundsMax += padVec;
            return true;
        }

        /// <summary>
        /// Returns true if any active indexed foliage collider's conservative 3D AABB intersects
        /// the sphere (center, radius), or if query parameters exceed safe bounds (fail-safe).
        /// Returning false guarantees that no indexed static foliage collider can intersect the sphere.
        /// </summary>
        internal bool HasPotentialOverlap(Vector3 center, float radius)
        {
            if (_entries.Length == 0)
                return false;

            if (!IsFinite(center) || float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f
             || center.x - radius < -MaxSafeCoord || center.x + radius > MaxSafeCoord
             || center.z - radius < -MaxSafeCoord || center.z + radius > MaxSafeCoord)
            {
                return true; // Fail-safe to PhysX on extreme/invalid coordinates
            }

            if (center.x + radius < _worldMinX || center.x - radius > _worldMaxX
             || center.z + radius < _worldMinZ || center.z - radius > _worldMaxZ)
            {
                return false;
            }

            int minCx = Mathf.FloorToInt((center.x - radius) * InvCellSize);
            int maxCx = Mathf.FloorToInt((center.x + radius) * InvCellSize);
            int minCz = Mathf.FloorToInt((center.z - radius) * InvCellSize);
            int maxCz = Mathf.FloorToInt((center.z + radius) * InvCellSize);

            long spanX = (long)maxCx - (long)minCx + 1L;
            long spanZ = (long)maxCz - (long)minCz + 1L;
            if (spanX <= 0 || spanZ <= 0 || spanX > MaxQueryCellsPerAxis || spanZ > MaxQueryCellsPerAxis)
            {
                return true; // Fail-safe to PhysX if radius covers an unexpectedly wide cell span
            }

            if (_queryStamp == int.MaxValue)
            {
                Array.Clear(_visitStamps, 0, _visitStamps.Length);
                _queryStamp = 1;
            }
            else
            {
                _queryStamp++;
            }
            int stamp = _queryStamp;
            float radiusSq = radius * radius;
            bool hitTriggers = Physics.queriesHitTriggers;

            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    long key = PackCellKey(cx, cz);
                    int slot = HashKey(key, _bucketMask);
                    while (true)
                    {
                        int count = _bucketCounts[slot];
                        if (count == 0)
                            break;
                        if (_bucketKeys[slot] == key)
                        {
                            int start = _bucketStarts[slot];
                            int end = start + count;
                            for (int i = start; i < end; i++)
                            {
                                int entryIdx = _cellEntries[i];
                                if (_visitStamps[entryIdx] == stamp)
                                    continue;
                                _visitStamps[entryIdx] = stamp;

                                if (PotentiallyMatches(ref _entries[entryIdx], center, radiusSq, hitTriggers))
                                    return true;
                            }
                            break;
                        }
                        slot = (slot + 1) & _bucketMask;
                    }
                }
            }

            for (int i = 0; i < _oversizedEntries.Length; i++)
            {
                int entryIdx = _oversizedEntries[i];
                if (_visitStamps[entryIdx] == stamp)
                    continue;
                _visitStamps[entryIdx] = stamp;

                if (PotentiallyMatches(ref _entries[entryIdx], center, radiusSq, hitTriggers))
                    return true;
            }

            return false;
        }

        private bool PotentiallyMatches(ref BroadphaseEntry e, Vector3 center, float radiusSq, bool hitTriggers)
        {
            if (SqrDistPointToAabb(center, e.boundsMin, e.boundsMax) > radiusSq)
                return false;

            Collider c = e.collider;
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                return false;
            if (!hitTriggers && c.isTrigger)
                return false;
            if ((_layerMask & (1 << c.gameObject.layer)) == 0)
                return false;

            return true;
        }

        /// <summary>
        /// Unconditional upper bound on ||A||_2 = sqrt(lambda_max(A^T A)) via Gershgorin's circle theorem
        /// on the symmetric Gram matrix G = A^T A:
        ///   lambda_max(G) <= max_i sum_j |G_ij| = ||G||_inf.
        /// Exact max-axis length when columns are orthogonal, and strictly conservative for any shear
        /// (including weak shear) with zero magic orthogonality threshold.
        /// </summary>
        private static float ComputeConservativeStretchBound(Matrix4x4 m)
        {
            Vector3 ax = m.MultiplyVector(Vector3.right);
            Vector3 ay = m.MultiplyVector(Vector3.up);
            Vector3 az = m.MultiplyVector(Vector3.forward);

            float d01 = Mathf.Abs(Vector3.Dot(ax, ay));
            float d02 = Mathf.Abs(Vector3.Dot(ax, az));
            float d12 = Mathf.Abs(Vector3.Dot(ay, az));

            float row0 = ax.sqrMagnitude + d01 + d02;
            float row1 = ay.sqrMagnitude + d01 + d12;
            float row2 = az.sqrMagnitude + d02 + d12;

            return Mathf.Sqrt(Mathf.Max(row0, Mathf.Max(row1, row2))) * 1.0001f;
        }

        private static Bounds TransformLocalBox(Vector3 localCenter, Vector3 localHalfExtents, Matrix4x4 m)
        {
            Vector3 c = m.MultiplyPoint3x4(localCenter);
            Vector3 ax = m.MultiplyVector(new Vector3(localHalfExtents.x, 0f, 0f));
            Vector3 ay = m.MultiplyVector(new Vector3(0f, localHalfExtents.y, 0f));
            Vector3 az = m.MultiplyVector(new Vector3(0f, 0f, localHalfExtents.z));
            Vector3 worldExt = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, worldExt * 2f);
        }

        private static float SqrDistPointToAabb(Vector3 p, Vector3 min, Vector3 max)
        {
            float dx = p.x < min.x ? min.x - p.x : (p.x > max.x ? p.x - max.x : 0f);
            float dy = p.y < min.y ? min.y - p.y : (p.y > max.y ? p.y - max.y : 0f);
            float dz = p.z < min.z ? min.z - p.z : (p.z > max.z ? p.z - max.z : 0f);
            return dx * dx + dy * dy + dz * dz;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        private static long PackCellKey(int cx, int cz) => ((long)cx << 32) | (uint)cz;

        private static int HashKey(long key, int mask)
        {
            ulong h = (ulong)key;
            h ^= h >> 33;
            h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33;
            return (int)(h & (ulong)mask);
        }
    }
}
