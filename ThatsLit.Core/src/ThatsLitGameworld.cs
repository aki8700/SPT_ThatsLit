using Comfort.Common;
using EFT;
using ThatsLit.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

using BaseCellClass = GPUInstancer.GPUInstancerCell;
using CellClass = GPUInstancer.GPUInstancerDetailCell;
using SpatialPartitionClass = GPUInstancer.GPUInstancerSpatialPartitioningData<GPUInstancer.GPUInstancerCell>;
using System.Reflection;
using GPUInstancer;
using HarmonyLib;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EFT.Weather;

namespace ThatsLit
{
    public class ThatsLitGameworld : MonoBehaviour
    {
        private void Update()
        {
            if (!Singleton<GameWorld>.Instantiated) return;
            if (!ThatsLitPlayer.CanLoad()) return;
            for (int i = 0; i < GameWorld.AllAlivePlayersList.Count; i++)
            {
                Player player = GameWorld.AllAlivePlayersList[i];
                if (player.IsAI) continue;
                if (!AllThatsLitPlayers.ContainsKey(player))
                {
                    TrySetupPlayer(player);
                }
            }
        }

        internal void TrySetupPlayer (Player player)
        {
            if (ThatsLitAPI.ShouldSetupPlayer != null && ThatsLitAPI.ShouldSetupPlayer.Invoke(player) == false)
            {
                return;
            }
            var tlp = player.gameObject.AddComponent<ThatsLitPlayer>();
            AllThatsLitPlayers.Add(player, tlp);
            tlp.Player = player;
            ThatsLitAPI.OnBeforePlayerSetup?.Invoke(player);
            ThatsLitAPI.OnBeforePlayerSetupDirect?.Invoke(tlp);
            if (ThatsLitPlugin.DebugProxy.Value)
                ThatsLitAPI.ToggleBrightnessProxyDirect(tlp, true);
            tlp.Setup(this);
            if (player == GameWorld.MainPlayer) MainThatsLitPlayer = tlp;
        }
        private void OnDestroy()
        {
            try
            {
            
                foreach (var p in AllThatsLitPlayers.ToArray())
                {
                    ComponentHelpers.DestroyComponent(p.Value);
                }
            }
            catch
            {
                Logger.LogError("Dispose Component Error");
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            _foliageSpatialGrid = null;
            _foliageGridAttempted = false;
            _foliageInstanceNames.Clear();
            _foliageNameCache.Clear();
            detailPrototypeCache.Clear();
            lastTerrainCollider = null;
            lastColliderTerrain = null;
            terrainDetailManagers.Clear();
            terrainSpatialPartitions.Clear();

            if (Singleton<ThatsLitGameworld>.Instance == this)
                Singleton<ThatsLitGameworld>.Instance = null;
            ThatsLitAPI.OnGameWorldDestroyed?.Invoke();
        }
        public GameWorld GameWorld => Singleton<GameWorld>.Instance;
        public ThatsLitPlayer MainThatsLitPlayer { get; private set; }
        public Dictionary<IPlayer, ThatsLitPlayer> AllThatsLitPlayers { get; private set; }
        public ScoreCalculator ScoreCalculator { get; internal set; }
        public RaidSettings activeRaidSettings;
        /// <summary>Resolved location id (null in hideout / unknown). Prefer this over activeRaidSettings.LocationId.</summary>
        public string LocationId { get; private set; }
        public bool IsWinter { get; private set; }
        internal bool foliageUnavailable, terrainDetailsUnavailable;
        internal Dictionary<(string, string), DoubleIdThrottler> doubleIdThrottlers;
        internal Dictionary<string, SingleIdThrottler> singleIdThrottlers;

        internal struct DoubleIdThrottler
        {
        }
        internal struct SingleIdThrottler
        {
            internal float lastAddedDangerPoint;
            internal float lastSideLook, lastForceLook;
        }

        void Awake ()
        {
            foliageLayerMask = 1 << LayerMask.NameToLayer("Foliage") | 1 << LayerMask.NameToLayer("Grass") | 1 << LayerMask.NameToLayer("PlayerSpiritAura");
            singleIdThrottlers = new();
            doubleIdThrottlers = new();

            Singleton<ThatsLitGameworld>.Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // EFT 40743: no "No session!" throw. Headless/Fika may not expose a full client session here.
            TarkovApplication app = null;
            try { TarkovApplication.Exist(out app); } catch (Exception e) { Logger.LogWarning($"TarkovApplication lookup failed: {e.Message}"); }
            try { IsWinter = app?.Session?.Season == ESeason.Winter; } catch { IsWinter = false; }
            activeRaidSettings = app?._raidSettings;
            var gameWorld = Singleton<GameWorld>.Instance;
            bool isHideout = gameWorld is HideoutGameWorld;
            LocationId = isHideout ? null : (!string.IsNullOrEmpty(gameWorld?.LocationId) ? gameWorld.LocationId : activeRaidSettings?.LocationId);
            if (string.IsNullOrEmpty(LocationId)) LocationId = null;

            switch (LocationId)
            {
                case "factory4_night":
                case "factory4_day":
                case "laboratory":
                case null:
                    foliageUnavailable = true;
                    terrainDetailsUnavailable = true;
                    break;
                default:
                    foliageUnavailable = false;
                    terrainDetailsUnavailable = false;
                    break;
            }

            AllThatsLitPlayers = new Dictionary<IPlayer, ThatsLitPlayer>();

            switch (LocationId)
            {
                case "Lighthouse":
                    if (ThatsLitPlugin.EnableLighthouse.Value) ScoreCalculator = new LighthouseScoreCalculator();
                    break;
                case "Woods":
                    if (ThatsLitPlugin.EnableWoods.Value) ScoreCalculator = new WoodsScoreCalculator();
                    break;
                case "factory4_night":
                    if (ThatsLitPlugin.EnableFactoryNight.Value) ScoreCalculator = new NightFactoryScoreCalculator();
                    break;
                case "factory4_day":
                    ScoreCalculator = null;
                    break;
                case "bigmap": // Customs
                    if (ThatsLitPlugin.EnableCustoms.Value) ScoreCalculator = new CustomsScoreCalculator();
                    break;
                case "RezervBase": // Reserve
                    if (ThatsLitPlugin.EnableReserve.Value) ScoreCalculator = new ReserveScoreCalculator();
                    break;
                case "Interchange":
                    if (ThatsLitPlugin.EnableInterchange.Value) ScoreCalculator = new InterchangeScoreCalculator();
                    break;
                case "TarkovStreets":
                    if (ThatsLitPlugin.EnableStreets.Value) ScoreCalculator = new StreetsScoreCalculator();
                    break;
                case "Sandbox":
                case "Sandbox_high":
                    if (ThatsLitPlugin.EnableGroundZero.Value) ScoreCalculator = new GroundZeroScoreCalculator();
                    break;
                case "Shoreline":
                    if (ThatsLitPlugin.EnableShoreline.Value) ScoreCalculator = new ShorelineScoreCalculator();
                    break;
                case "laboratory":
                    // if (ThatsLitPlugin.EnableLab.Value) ScoreCalculator = new LabScoreCalculator();
                    break;
                case null:
                    if (ThatsLitPlugin.EnableHideout.Value) ScoreCalculator = new HideoutScoreCalculator();
                    break;
                default:
                    break;
            }

            ThatsLitAPI.OnGameWorldSetup?.Invoke(this);
        }


        internal void GetWeatherStats(out float fog, out float rain, out float cloud)
        {
            if (WeatherController.Instance?.WeatherCurve == null)
            {
                fog = rain = cloud = 0;
                return;
            }

            fog = WeatherController.Instance.WeatherCurve.Fog;
            rain = WeatherController.Instance.WeatherCurve.Rain;
            cloud = WeatherController.Instance.WeatherCurve.Cloudiness;
        }
        private LayerMask foliageLayerMask;// PlayerSpiritAura is Visceral Bodies compat
        private FoliageSpatialGrid _foliageSpatialGrid;
        private bool _foliageGridAttempted;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Additive map subscenes can add static foliage colliders after initial scan; invalidate so next query rebuilds.
            _foliageSpatialGrid = null;
            _foliageGridAttempted = false;
        }

        // Same pattern/replacement as the former per-call static Regex.Replace; RegexOptions.Compiled does not change matching semantics.
        static readonly Regex foliageNameRegex = new Regex(@"(.+?)\s?(\(\d+\))?", RegexOptions.Compiled);
        // Per-world memo: the normalization is a pure function of the raw GameObject name (finite set per map).
        readonly Dictionary<string, string> _foliageNameCache = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<int, string> _foliageInstanceNames = new Dictionary<int, string>();
        string GetNormalizedFoliageName(string raw)
        {
            if (raw == null) return foliageNameRegex.Replace(raw, "$1"); // Preserve original behavior (ArgumentNullException); fnames are pre-filtered non-blank
            if (!_foliageNameCache.TryGetValue(raw, out var normalized))
            {
                normalized = foliageNameRegex.Replace(raw, "$1");
                _foliageNameCache[raw] = normalized;
            }
            return normalized;
        }

        string ResolveFoliageObjectName(GameObject foliageObject)
        {
            int foliageId = foliageObject.GetInstanceID();
            if (!_foliageInstanceNames.TryGetValue(foliageId, out var fname))
            {
                string raw = foliageObject.name;
                fname = string.IsNullOrWhiteSpace(raw) ? raw : GetNormalizedFoliageName(raw);
                _foliageInstanceNames[foliageId] = fname;
            }
            return fname;
        }

        private void EnsureFoliageSpatialGrid()
        {
            if (_foliageGridAttempted || foliageUnavailable || !ThatsLitPlugin.EnableFoliageSpatialGrid.Value)
                return;
            _foliageGridAttempted = true;
            try
            {
                var sw = Stopwatch.StartNew();
                _foliageSpatialGrid = FoliageSpatialGrid.Build(foliageLayerMask);
                sw.Stop();
                if (_foliageSpatialGrid != null)
                {
                    Logger.LogInfo($"Built FoliageSpatialGrid broadphase for {LocationId ?? "unknown"}: {_foliageSpatialGrid.EntryCount} static colliders in {_foliageSpatialGrid.CellCount} cells ({sw.ElapsedMilliseconds} ms).");
                }
            }
            catch (Exception ex)
            {
                _foliageSpatialGrid = null;
                Logger.LogWarning($"Failed to build FoliageSpatialGrid; falling back to Physics.OverlapSphereNonAlloc: {ex.Message}");
            }
        }

        internal void UpdateFoliageScore(Vector3 bodyPos, PlayerFoliageProfile player)
        {
            if (player == null) return;

            if (Time.time < player.LastCheckedTime + 0.45f)
                return;
            // Skip if basically standing still
            if ((bodyPos - player.LastCheckedPos).sqrMagnitude < 0.0025f)
                return;

            player.FoliageScore = 0;
            player.LastCheckedTime = Time.time;
            player.LastCheckedPos = bodyPos;

            Array.Clear(player.Foliage, 0, player.Foliage.Length);
            Array.Clear(player.CastedFoliageColliders, 0, player.CastedFoliageColliders.Length);

            if (foliageUnavailable) return;

            bool useSpatialGrid = ThatsLitPlugin.EnableFoliageSpatialGrid.Value;
            if (useSpatialGrid && !_foliageGridAttempted)
            {
                EnsureFoliageSpatialGrid();
            }

            // O(1) conservative broadphase: if no static foliage collider's world AABB can intersect the 4m sphere,
            // skip Physics.OverlapSphereNonAlloc completely (FoliageScore = 0, FoliageCount = 0).
            // When any candidate is nearby, run the exact PhysX query so geometry, ordering, and scores match 100%.
            FoliageSpatialGrid grid = useSpatialGrid ? _foliageSpatialGrid : null;
            if (grid != null && !grid.HasPotentialOverlap(bodyPos, 4f))
            {
                player.IsFoliageSorted = true;
                player.FoliageCount = 0;
                return;
            }

            int castedCount = Physics.OverlapSphereNonAlloc(bodyPos, 4f, player.CastedFoliageColliders, foliageLayerMask);
            int validCount = 0;

            for (int i = 0; i < castedCount; i++)
            {
                Collider casted = player.CastedFoliageColliders[i];
                if (casted.gameObject.transform.root.gameObject.layer == 8) continue; // Somehow sometimes player spines are tagged PlayerSpiritAura, VB or vanilla?
                if (casted is TerrainCollider) continue; // Somehow sometimes terrains can be casted
                Vector3 bodyToFoliage = casted.transform.position - bodyPos;

                float dis = bodyToFoliage.magnitude;
                if (dis < 0.25f) player.FoliageScore += 1f;
                else if (dis < 0.35f) player.FoliageScore += 0.9f;
                else if (dis < 0.5f) player.FoliageScore += 0.8f;
                else if (dis < 0.6f) player.FoliageScore += 0.7f;
                else if (dis < 0.7f) player.FoliageScore += 0.5f;
                else if (dis < 1f) player.FoliageScore += 0.3f;
                else if (dis < 2f) player.FoliageScore += 0.2f;
                else player.FoliageScore += 0.1f;

                var foliageObject = casted.transform.parent != null ? casted.transform.parent.gameObject : casted.gameObject;
                var fname = ResolveFoliageObjectName(foliageObject);
                if (string.IsNullOrWhiteSpace(fname)) continue;

                if (ThatsLitPlugin.FoliageSamples.Value == 1)
                {
                    if (player.Foliage.Length > 0 && (player.Foliage[0] == default || dis < player.Foliage[0].dis))
                        player.Foliage[0] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
                    validCount = player.Foliage.Length > 0 ? 1 : 0;
                    continue;
                }
                if (validCount < player.Foliage.Length)
                {
                    player.Foliage[validCount] = new FoliageInfo(fname, new Vector3(bodyToFoliage.x, bodyToFoliage.z), dis);
                    validCount++;
                }
            }

            for (int j = 0; j < validCount; j++)
            {
                var f = player.Foliage[j];
                f.dis = f.dir.magnitude; // Use horizontal distance to replace casted 3D distance
                player.Foliage[j] = f;
            }
            player.IsFoliageSorted = false;
            if (player.Foliage.Length <= 1 || validCount <= 1)
            {
                player.IsFoliageSorted = true;
            }

            switch (castedCount)
            {
                case 1:
                    player.FoliageScore /= 3.3f;
                    break;
                case 2:
                    player.FoliageScore /= 2.8f;
                    break;
                case 3:
                    player.FoliageScore /= 2.3f;
                    break;
                case 4:
                    player.FoliageScore /= 1.8f;
                    break;
                case 5:
                case 6:
                    player.FoliageScore /= 1.2f;
                    break;
                case 11:
                case 12:
                case 13:
                    player.FoliageScore /= 1.15f;
                    break;
                case 14:
                case 15:
                case 16:
                    player.FoliageScore /= 1.25f;
                    break;
            }

            player.FoliageCount = validCount;
        }

        internal int MaxDetailTypes { get; set; }

        /// <summary>
        /// The 18 <see cref="PlayerTerrainDetailsProfile.detailScoreCache"/> buckets that <see cref="CalculateDetailScore"/> can resolve to
        /// (bucket 0 is the unscaled/scaled center used only by <see cref="CalculateCenterDetailScore"/>, bucket 10 is unused).
        /// This order is the wire order of That's Lit Sync's TerrainScorePacket.
        /// </summary>
        internal static readonly int[] ActiveTerrainBuckets = { 5, 15, 1, 2, 3, 4, 6, 7, 8, 9, 11, 12, 13, 14, 16, 17, 18, 19 };
        internal const int ActiveTerrainBucketCount = 18;
        /// <summary>Remote (proxy) bucket scores older than this read as zero concealment (fail-open).</summary>
        internal const float TerrainProxyMaxAge = 3.5f;

        // Per-bucket 5x5 cell index sequences and scaling. Built once from the exact same index formulas that the
        // former yield-return iterator helpers used, so the visiting order (and thus float summation order) is unchanged.
        static readonly int[][] bucketCells = BuildBucketCells();
        static readonly float[] bucketScaling = BuildBucketScaling();

        static int[][] BuildBucketCells()
        {
            var t = new int[20][];
            t[5] = Index3x3In5x5(0, 0);   // IterateDetailIndex3x3 (no direction / looking down from afar)
            t[15] = Index3x3In5x5(0, 0);  // IterateDetailIndex3x3 (looking down, < 10m, scaled 4/9)
            // Very close (< 10m) and looking slightly down (-20 ~ -10 deg)
            t[10 + 8] = IndexCrossShape3x3In5x5Center(horizontal: true, N: true); // IterateDetailIndex2x3InversedTN
            t[10 + 9] = Index2x2In5x5(2, 1);                                       // IterateDetailIndex2x2NE
            t[10 + 6] = IndexCrossShape3x3In5x5Center(vertical: true, W: true);    // IterateDetailIndex2x3InversedTE (sic: original uses W)
            t[10 + 3] = Index2x2In5x5(2, 2);                                       // IterateDetailIndex2x2SE
            t[10 + 2] = IndexCrossShape3x3In5x5Center(horizontal: true, S: true);  // IterateDetailIndex2x3InversedTS
            t[10 + 1] = Index2x2In5x5(1, 2);                                       // IterateDetailIndex2x2SW
            t[10 + 4] = IndexCrossShape3x3In5x5Center(vertical: true, W: true);    // IterateDetailIndex2x3InversedTW
            t[10 + 7] = Index2x2In5x5(1, 1);                                       // IterateDetailIndex2x2NW
            // Regular
            t[8] = Index3x3In5x5(0, 1);   // N
            t[9] = Index3x3In5x5(1, 1);   // NE
            t[6] = Index3x3In5x5(1, 0);   // E
            t[3] = Index3x3In5x5(1, -1);  // SE
            t[2] = Index3x3In5x5(0, -1);  // S
            t[1] = Index3x3In5x5(-1, -1); // SW
            t[4] = Index3x3In5x5(-1, 0);  // W
            t[7] = Index3x3In5x5(-1, 1);  // NW
            return t;
        }

        static float[] BuildBucketScaling()
        {
            var s = new float[20];
            for (int i = 0; i < s.Length; i++) s[i] = 1f;
            s[15] = 4f/9f;
            for (int i = 11; i <= 19; i++)
                if (i != 15) s[i] = 9f/4f;
            return s;
        }

        /// xOffset and yOffset decides the center of the 3x3
        static int[] Index3x3In5x5(int xOffset, int yOffset)
        {
            return new int[]
            {
                5 * (1 + yOffset) + 1 + xOffset,
                5 * (1 + yOffset) + 2 + xOffset,
                5 * (1 + yOffset) + 3 + xOffset,

                5 * (2 + yOffset) + 1 + xOffset,
                5 * (2 + yOffset) + 2 + xOffset,
                5 * (2 + yOffset) + 3 + xOffset,

                5 * (3 + yOffset) + 1 + xOffset,
                5 * (3 + yOffset) + 2 + xOffset,
                5 * (3 + yOffset) + 3 + xOffset,
            };
        }

        /// xOffset and yOffset decides the LT corner of the 2x2
        static int[] Index2x2In5x5(int xOffset, int yOffset)
        {
            if (xOffset > 3 || yOffset > 3) throw new Exception("[That's Lit] Terrain detail grid access out of Bound");
            return new int[]
            {
                5 * yOffset + xOffset,
                5 * yOffset + xOffset + 1,

                5 * (yOffset + 1) + xOffset,
                5 * (yOffset + 1) + xOffset + 1,
            };
        }

        static int[] IndexCrossShape3x3In5x5Center(bool horizontal = false, bool vertical = false, bool N = false, bool E = false, bool S = false, bool W = false)
        {
            var list = new List<int>(5);
            // 5 * 1 + 1;
            if (vertical || N) list.Add(5 * 1 + 2);
            // 5 * 1 + 3;

            if (horizontal || W) list.Add(5 * 2 + 1);
            if (horizontal || vertical) list.Add(5 * 2 + 2);
            if (horizontal || E) list.Add(5 * 2 + 3);

            // 5 * 3 + 1;
            if (vertical || S) list.Add(5 * 3 + 2);
            // 5 * 3 + 3;
            return list.ToArray();
        }

        /// <summary>
        /// Resolve the detailScoreCache bucket for the specified enemy dir, dis, vertical vision angle.
        /// Identical branching to the original CalculateDetailScore. Returns -1 only for NaN input in the close range
        /// (the original then iterated a null enumerable and threw NullReferenceException).
        /// </summary>
        static int ResolveDetailBucket(Vector3 enemyDirection, float dis, float verticalAxisAngle)
        {
            if (enemyDirection == Vector3.zero) // This should never happens for actual enemies
                return 5;
            if (verticalAxisAngle < -20f) // Looking down and ignore distance and direction
                return dis >= 10 ? 5 : 15; // 15: scaled down mid 3x3
            if (dis < 10f && verticalAxisAngle < -10f) // Very close and not looking down
            {
                var dirFlat = new Vector2(enemyDirection.x, enemyDirection.z).normalized;
                var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
                if (angle >= -22.5f && angle <= 22.5f) return 10 + 8;
                else if (angle >= 22.5f && angle <= 67.5f) return 10 + 9;
                else if (angle >= 67.5f && angle <= 112.5f) return 10 + 6;
                else if (angle >= 112.5f && angle <= 157.5f) return 10 + 3;
                else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f) return 10 + 2;
                else if (angle >= -157.5f && angle <= -112.5f) return 10 + 1;
                else if (angle >= -112.5f && angle <= -67.5f) return 10 + 4;
                else if (angle >= -67.5f && angle <= -22.5f) return 10 + 7;
                return -1;
            }
            else
            {
                var dirFlat = new Vector2(enemyDirection.x, enemyDirection.z).normalized;
                var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
                if (angle >= -22.5f && angle <= 22.5f) return 8;
                else if (angle >= 22.5f && angle <= 67.5f) return 9;
                else if (angle >= 67.5f && angle <= 112.5f) return 6;
                else if (angle >= 112.5f && angle <= 157.5f) return 3;
                else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f) return 2;
                else if (angle >= -157.5f && angle <= -112.5f) return 1;
                else if (angle >= -112.5f && angle <= -67.5f) return 4;
                else if (angle >= -67.5f && angle <= -22.5f) return 7;
                else throw new Exception($"[That's Lit] Invalid angle to enemy: {angle}");
            }
        }

        /// <summary>
        /// Calculate new or retrieve cached score for the specified enemy dir, dis, vertical vision angle
        /// </summary>
        public TerrainDetailScore CalculateDetailScore(PlayerTerrainDetailsProfile player, Vector3 enemyDirection, float dis, float verticalAxisAngle)
        {
            if (player == null) return default;

            // Remote scores (That's Lit Sync): no local detail maps, only the received buckets. Fail-open when stale.
            if (player.IsProxy)
            {
                int proxyDir = ResolveDetailBucket(enemyDirection, dis, verticalAxisAngle);
                if (proxyDir < 0 || Time.time - player.lastProxyUpdateTime > TerrainProxyMaxAge) return default;
                var proxyCache = player.detailScoreCache[proxyDir];
                return proxyCache.cached ? proxyCache : default;
            }

            if (player.Details5x5 == null) return default; // Could be resizing?

            int dir = ResolveDetailBucket(enemyDirection, dis, verticalAxisAngle);
            if (dir < 0) return default;
            return CalculateDetailBucket(player, dir);
        }

        /// <summary>
        /// Score of one bucket from the local 5x5 detail sample (cached in detailScoreCache until the next sample).
        /// </summary>
        TerrainDetailScore CalculateDetailBucket(PlayerTerrainDetailsProfile player, int dir)
        {
            if (player?.Details5x5 == null || player.DetailTypeStride <= 0) return default;
            TerrainDetailScore cache = player.detailScoreCache[dir];
            if (cache.cached) return cache;

            int[] cells = bucketCells[dir];
            float scaling = bucketScaling[dir];
            for (int c = 0; c < cells.Length; c++)
            {
                int pos = cells[c];
                for (int i = 0; i < player.DetailTypeStride; i++)
                {
                    var info = player.Details5x5[pos * player.DetailTypeStride + i];
                    if (!info.casted) continue;
                    Utility.CalculateDetailScore(info.kind, info.count, out var s1, out var s2);
                    Utility.LogUnknownDetail(info.kind, info.name, info.count);
                    s1 *= scaling;
                    s2 *= scaling;
                    cache.prone += s1;
                    cache.regular += s2;
                }
            }

            cache.cached = true;
            player.detailScoreCache[dir] = cache;
            return cache;
        }

        /// <summary>
        /// Sample all <see cref="ActiveTerrainBuckets"/> of a locally sampled (non-proxy) profile, in wire order.
        /// Buckets that cannot be computed yet (no 5x5 sample) are reported as 0.
        /// </summary>
        internal bool SampleAllTerrainBuckets(PlayerTerrainDetailsProfile player, float[] outProne, float[] outRegular)
        {
            if (player == null || player.IsProxy || !player.HasValidSample
             || outProne == null || outRegular == null
             || outProne.Length < ActiveTerrainBucketCount || outRegular.Length < ActiveTerrainBucketCount)
                return false;

            for (int b = 0; b < ActiveTerrainBucketCount; b++)
            {
                TerrainDetailScore score = player.Details5x5 == null ? default : CalculateDetailBucket(player, ActiveTerrainBuckets[b]);
                outProne[b] = score.prone;
                outRegular[b] = score.regular;
            }
            return true;
        }

        /// <summary>
        /// Store remotely sampled bucket scores (wire order) into a proxy profile.
        /// </summary>
        internal static bool SetProxyTerrainBuckets(PlayerTerrainDetailsProfile player, float[] prone, float[] regular)
        {
            if (player == null || !player.IsProxy
             || prone == null || regular == null
             || prone.Length < ActiveTerrainBucketCount || regular.Length < ActiveTerrainBucketCount)
                return false;

            for (int b = 0; b < ActiveTerrainBucketCount; b++)
            {
                float p = prone[b], r = regular[b];
                if (float.IsNaN(p) || float.IsInfinity(p) || p < 0f) p = 0f;
                if (float.IsNaN(r) || float.IsInfinity(r) || r < 0f) r = 0f;
                player.detailScoreCache[ActiveTerrainBuckets[b]] = new TerrainDetailScore(true, p, r);
            }
            player.lastProxyUpdateTime = Time.time;
            return true;
        }

        public TerrainDetailScore CalculateCenterDetailScore(PlayerTerrainDetailsProfile player, bool unscaled = false)
        {
            TerrainDetailScore cache = default;
            float scaling = unscaled? 1f : 9f;
            if (player == null || player.DetailTypeStride <= 0) return cache;
            if (TryGetCache(0, out cache))  return cache;

            if (player?.Details5x5 == null) return cache; // Could be resizing?

            for (int i = 0; i < player.DetailTypeStride; i++)
            {
                var info = player.Details5x5[(2*5+2) * player.DetailTypeStride + i];
                if (!info.casted) continue;
                Utility.CalculateDetailScore(info.kind, info.count, out var s1, out var s2);
                    Utility.LogUnknownDetail(info.kind, info.name, info.count);
                s1 *= scaling;
                s2 *= scaling;
                cache.prone += s1;
                cache.regular += s2;
            }

            cache.cached = true;
            player.detailScoreCache[0] = cache;
            return cache;

            bool TryGetCache(int index, out TerrainDetailScore cache)
            {
                cache = player.detailScoreCache[index];
                return cache.cached;
            }
        }

        static readonly FieldInfo _spDataField = AccessTools.Field(typeof(GPUInstancerDetailManager), "spData");
        static readonly FieldInfo _initializingInstancesField = AccessTools.Field(typeof(GPUInstancerTerrainManager), "initalizingInstances");

        readonly Dictionary<Terrain, GPUInstancerDetailManager> terrainDetailManagers = new Dictionary<Terrain, GPUInstancerDetailManager>();
        readonly Dictionary<Terrain, SpatialPartitionClass> terrainSpatialPartitions = new Dictionary<Terrain, SpatialPartitionClass>();
        Collider lastTerrainCollider;
        Terrain lastColliderTerrain;
        readonly Dictionary<GPUInstancerDetailManager, (string[] names, DetailKind[] kinds)> detailPrototypeCache = new Dictionary<GPUInstancerDetailManager, (string[] names, DetailKind[] kinds)>();
        (string[] names, DetailKind[] kinds) GetDetailPrototypes(GPUInstancerDetailManager manager)
        {
            if (!detailPrototypeCache.TryGetValue(manager, out var cached) || cached.names.Length != manager.prototypeList.Count)
            {
                cached = (new string[manager.prototypeList.Count], new DetailKind[manager.prototypeList.Count]);
                for (int i = 0; i < cached.names.Length; i++)
                {
                    cached.names[i] = manager.prototypeList[i] != null ? manager.prototypeList[i].name : null;
                    cached.kinds[i] = Utility.ClassifyDetailName(cached.names[i]);
                }
                detailPrototypeCache[manager] = cached;
            }
            return cached;
        }

        static bool HasActiveDetailManager()
        {
            var list = GPUInstancerDetailManager.activeManagerList;
            if (list == null || list.Count == 0)
                return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is GPUInstancerDetailManager dm && dm != null && dm.enabled)
                    return true;
            }
            return false;
        }

        bool TryGetTerrainDetailPartition(Terrain terrain, out GPUInstancerDetailManager manager, out SpatialPartitionClass spData)
        {
            manager = null;
            spData = null;
            if (!terrain || terrain.terrainData == null || terrain.terrainData.size.x <= 0f || terrain.terrainData.size.z <= 0f)
                return false;

            if (!terrainDetailManagers.TryGetValue(terrain, out manager) || !manager)
            {
                manager = terrain.GetComponent<GPUInstancerTerrainProxy>()?.detailManager;
                if (!manager)
                {
                    terrainDetailManagers.Remove(terrain);
                    terrainSpatialPartitions.Remove(terrain);
                    return false;
                }
                terrainDetailManagers[terrain] = manager;
            }

            if ((_initializingInstancesField != null && (bool)_initializingInstancesField.GetValue(manager)) || !manager.enabled || !manager.isInitialized)
            {
                terrainSpatialPartitions.Remove(terrain);
                detailPrototypeCache.Remove(manager);
                return false;
            }

            if (!terrainSpatialPartitions.TryGetValue(terrain, out spData) || spData == null)
            {
                if (_initializingInstancesField != null && (bool)_initializingInstancesField.GetValue(manager))
                    return false;

                spData = _spDataField?.GetValue(manager) as SpatialPartitionClass;
                if (spData == null || spData.cellRowAndCollumnCountPerTerrain <= 0)
                {
                    terrainSpatialPartitions.Remove(terrain);
                    return false;
                }
                terrainSpatialPartitions[terrain] = spData;
            }

            return true;
        }

        static int SampleDetailFromPartition(SpatialPartitionClass spData, int resolution, int layer, int px, int py, ref int cachedCellX, ref int cachedCellY, ref CellClass cachedCell)
        {
            if (spData == null || resolution <= 0 || px < 0 || px >= resolution || py < 0 || py >= resolution)
                return 0;

            int cellCount = spData.cellRowAndCollumnCountPerTerrain;
            if (cellCount <= 0)
                return 0;

            int resolutionPerCell = resolution / cellCount;
            if (resolutionPerCell <= 0)
                return 0;

            int cellX = Mathf.Min(px / resolutionPerCell, cellCount - 1);
            int cellY = Mathf.Min(py / resolutionPerCell, cellCount - 1);
            if (cellX < 0 || cellX >= cellCount || cellY < 0 || cellY >= cellCount)
                return 0;

            CellClass cell;
            if (cellX == cachedCellX && cellY == cachedCellY)
            {
                cell = cachedCell;
            }
            else
            {
                cachedCellX = cellX;
                cachedCellY = cellY;
                cachedCell = spData.GetCell(BaseCellClass.CalculateHash(cellX, 0, cellY), out BaseCellClass abstractCell)
                    ? abstractCell as CellClass
                    : null;
                cell = cachedCell;
            }

            var detailMapData = cell?.detailMapData;
            if (detailMapData == null || (uint)layer >= (uint)detailMapData.Count)
                return 0;

            int[] layerData = detailMapData[layer];
            if (layerData == null)
                return 0;

            int inCellX = px - cellX * resolutionPerCell;
            int inCellY = py - cellY * resolutionPerCell;
            int idx = inCellX + inCellY * resolutionPerCell;
            if ((uint)idx >= (uint)layerData.Length)
                return 0;

            return layerData[idx];
        }

        int SampleNeighborDetail(Terrain neighbor, int layer, int posX, int posY, int currentResolution)
        {
            if (!neighbor || !TryGetTerrainDetailPartition(neighbor, out var neighborMgr, out var neighborSpData))
                return 0;

            var protoList = neighborMgr.prototypeList;
            if (protoList == null || (uint)layer >= (uint)protoList.Count)
                return 0;

            var proto = protoList[layer] as GPUInstancerDetailPrototype;
            if (proto == null)
                return 0;

            int neighborRes = proto.detailResolution;
            if (neighborRes <= 0)
                return 0;

            int nx = posX < 0 ? neighborRes + posX : (posX >= currentResolution ? posX - currentResolution : (neighborRes == currentResolution ? posX : (int)((long)posX * neighborRes / currentResolution)));
            int ny = posY < 0 ? neighborRes + posY : (posY >= currentResolution ? posY - currentResolution : (neighborRes == currentResolution ? posY : (int)((long)posY * neighborRes / currentResolution)));

            int dummyCellX = -1, dummyCellY = -1;
            CellClass dummyCell = null;
            return SampleDetailFromPartition(neighborSpData, neighborRes, layer, nx, ny, ref dummyCellX, ref dummyCellY, ref dummyCell);
        }

        internal void CheckTerrainDetails(Vector3 position, PlayerTerrainDetailsProfile player)
        {
            // Proxy profiles only hold remotely sampled buckets: never re-sample (or clear) them locally,
            // and never let them flip terrainDetailsUnavailable on headless (which has no GPUI detail managers).
            if (player != null && player.IsProxy)
                return;

            if (!terrainDetailsUnavailable && (Application.isBatchMode || !HasActiveDetailManager()))
            {
                terrainDetailsUnavailable = true;
                Logger.LogInfo($"Active detail managers not found, disabling detail check...");
                return;
            }

            if (player == null || terrainDetailsUnavailable)
                return;

            if (player.HasValidSample && (position - player.LastCheckedPos).sqrMagnitude < 0.01f)
                return;

            if (Time.time < player.NextInitRetryTime)
                return;

            var ray = new Ray(position, Vector3.down);
            if (!Physics.Raycast(ray, out var hit, 100, LayersMaskController.TerrainMask))
            {
                Array.Clear(player.detailScoreCache, 0, player.detailScoreCache.Length);
                if (player.Details5x5 != null)
                    Array.Clear(player.Details5x5, 0, player.Details5x5.Length);
                player.RecentDetailCount3x3 = 0;
                player.RecentDetailCount5x5 = 0;
                player.LastCheckedTime = Time.time;
                player.LastCheckedPos = position;
                player.HasValidSample = true;
                return;
            }

            if (lastTerrainCollider != hit.collider || !lastColliderTerrain)
            {
                lastTerrainCollider = hit.collider;
                lastColliderTerrain = hit.collider != null ? hit.collider.GetComponent<Terrain>() : null;
            }
            var terrain = lastColliderTerrain;

            // While the terrain detail manager / spatial partitioning data is still initializing,
            // keep HasValidSample=false and do NOT lock LastCheckedPos so a stationary player retries
            // without broadcasting fake all-zero terrain scores. Rate-limit retries to 0.5s.
            if (!terrain || !TryGetTerrainDetailPartition(terrain, out var manager, out var spData))
            {
                player.NextInitRetryTime = Time.time + 0.5f;
                return;
            }

            Array.Clear(player.detailScoreCache, 0, player.detailScoreCache.Length);
            if (player.Details5x5 != null)
                Array.Clear(player.Details5x5, 0, player.Details5x5.Length);
            player.RecentDetailCount3x3 = 0;
            player.RecentDetailCount5x5 = 0;
            player.LastCheckedTime = Time.time;
            player.LastCheckedPos = position;
            player.HasValidSample = true;

            Vector3 hitRelativePos = hit.point - (terrain.transform.position + terrain.terrainData.bounds.min);
            var currentLocationOnTerrainmap = new Vector2(hitRelativePos.x / terrain.terrainData.size.x, hitRelativePos.z / terrain.terrainData.size.z);

            if (player.Details5x5 == null) // Initialize
            {
                foreach (var mgr in GPUInstancerDetailManager.activeManagerList)
                {
                    if (mgr?.prototypeList != null && MaxDetailTypes < mgr.prototypeList.Count)
                    {
                        MaxDetailTypes = mgr.prototypeList.Count + 2;
                    }
                }
                player.Details5x5 = new CastedDetailInfo[MaxDetailTypes * 5 * 5];
                player.DetailTypeStride = MaxDetailTypes;
                Logger.LogInfo($"Set MaxDetailTypes to {MaxDetailTypes}");
            }

            if (manager.prototypeList == null)
                return;

            if (MaxDetailTypes < manager.prototypeList.Count)
            {
                MaxDetailTypes = manager.prototypeList.Count + 2;
                player.Details5x5 = new CastedDetailInfo[MaxDetailTypes * 5 * 5];
                player.DetailTypeStride = MaxDetailTypes;
            }
            else if (player.Details5x5.Length < MaxDetailTypes * 5 * 5)
            {
                player.Details5x5 = new CastedDetailInfo[MaxDetailTypes * 5 * 5];
                player.DetailTypeStride = MaxDetailTypes;
            }

            var prototypes = GetDetailPrototypes(manager);
            int cachedCellX = -1, cachedCellY = -1;
            CellClass cachedCell = null;

            for (int d = 0; d < manager.prototypeList.Count; d++)
            {
                GPUInstancerDetailPrototype gPUInstancerDetailPrototype = manager.prototypeList[d] as GPUInstancerDetailPrototype;
                if (gPUInstancerDetailPrototype == null) continue;
                var resolution = gPUInstancerDetailPrototype.detailResolution;
                if (resolution <= 0) continue;
                // Object.name allocates a new managed string per call: fetch it once per prototype instead of once per cell.
                string prototypeName = prototypes.names[d];
                Vector2Int resolutionPos = new Vector2Int((int)(currentLocationOnTerrainmap.x * resolution), (int)(currentLocationOnTerrainmap.y * resolution));
                // EFT.UI.ConsoleScreen.Log($"JOB: Calculating score for detail#{d} at detail pos ({resolutionPos.x},{resolutionPos.y})" );
                for (int x = 0; x < 5; x++)
                    for (int y = 0; y < 5; y++)
                    {
                        var posX = resolutionPos.x - 2 + x;
                        var posY = resolutionPos.y - 2 + y;
                        int count = 0;

                        if (posX >= 0 && posX < resolution && posY >= 0 && posY < resolution)
                        {
                            count = SampleDetailFromPartition(spData, resolution, d, posX, posY, ref cachedCellX, ref cachedCellY, ref cachedCell);
                        }
                        else if (posX < 0 && posY >= 0 && posY < resolution)
                        {
                            count = SampleNeighborDetail(terrain.leftNeighbor, d, posX, posY, resolution);
                        }
                        else if (posX >= resolution && posY >= 0 && posY < resolution)
                        {
                            count = SampleNeighborDetail(terrain.rightNeighbor, d, posX, posY, resolution);
                        }
                        else if (posY >= resolution && posX >= 0 && posX < resolution)
                        {
                            count = SampleNeighborDetail(terrain.topNeighbor, d, posX, posY, resolution);
                        }
                        else if (posY < 0 && posX >= 0 && posX < resolution)
                        {
                            count = SampleNeighborDetail(terrain.bottomNeighbor, d, posX, posY, resolution);
                        }
                        else if (posY >= resolution && posX >= resolution)
                        {
                            Terrain neighbor = terrain.topNeighbor != null ? terrain.topNeighbor.rightNeighbor : (terrain.rightNeighbor != null ? terrain.rightNeighbor.topNeighbor : null);
                            count = SampleNeighborDetail(neighbor, d, posX, posY, resolution);
                        }
                        else if (posY >= resolution && posX < 0)
                        {
                            Terrain neighbor = terrain.topNeighbor != null ? terrain.topNeighbor.leftNeighbor : (terrain.leftNeighbor != null ? terrain.leftNeighbor.topNeighbor : null);
                            count = SampleNeighborDetail(neighbor, d, posX, posY, resolution);
                        }
                        else if (posY < 0 && posX >= resolution)
                        {
                            Terrain neighbor = terrain.bottomNeighbor != null ? terrain.bottomNeighbor.rightNeighbor : (terrain.rightNeighbor != null ? terrain.rightNeighbor.bottomNeighbor : null);
                            count = SampleNeighborDetail(neighbor, d, posX, posY, resolution);
                        }
                        else if (posY < 0 && posX < 0)
                        {
                            Terrain neighbor = terrain.bottomNeighbor != null ? terrain.bottomNeighbor.leftNeighbor : (terrain.leftNeighbor != null ? terrain.leftNeighbor.bottomNeighbor : null);
                            count = SampleNeighborDetail(neighbor, d, posX, posY, resolution);
                        }

                        player.Details5x5[player.GetDetailInfoIndex(x, y, d, player.DetailTypeStride)] = new CastedDetailInfo()
                        {
                            casted = true,
                            name = prototypeName,
                            kind = prototypes.kinds[d],
                            count = count,
                        };

                        if (x >= 1 && x <= 3 && y >= 1 && y <= 3) player.RecentDetailCount3x3 += count;
                        player.RecentDetailCount5x5 += count;
                    }
            }
        }
    }

}
