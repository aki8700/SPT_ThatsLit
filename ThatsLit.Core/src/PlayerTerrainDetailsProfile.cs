// #define DEBUG_DETAILS
using UnityEngine;

namespace ThatsLit
{
    public class PlayerTerrainDetailsProfile
    {
        public CastedDetailInfo[] Details5x5 { get; internal set; }
        public TerrainDetailScore[] detailScoreCache = new TerrainDetailScore[20];
        public int RecentDetailCount3x3 { get; internal set; }
        public int RecentDetailCount5x5 { get; internal set; }
        public float LastCheckedTime { get; internal set; }
        public Vector3 LastCheckedPos { get; internal set; }
        internal int DetailTypeStride;
        internal float NextInitRetryTime;
        /// <summary>
        /// True when the bucket scores in <see cref="detailScoreCache"/> are supplied remotely (That's Lit Sync)
        /// instead of being sampled from the local GPUI detail maps (e.g. Fika Headless, which has no GPUI data).
        /// A proxy never has <see cref="Details5x5"/> and is never re-sampled locally.
        /// </summary>
        internal bool IsProxy;
        /// <summary>True once a local sample has completed (or confirmed no terrain beneath player); false while GPUI detail maps are still building.</summary>
        internal bool HasValidSample;
        /// <summary>Time.time of the last remote bucket update (proxy only). Stale data (> 3.5s) reads as zero concealment.</summary>
        internal float lastProxyUpdateTime = float.NegativeInfinity;
        internal int GetDetailInfoIndex(int x5x5, int y5x5, int detailId, int maxDetailTypes) => (y5x5 * 5 + x5x5) * maxDetailTypes + detailId;
    }
}