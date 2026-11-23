using System.Runtime.InteropServices;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace ThatsLit.Sync
{
    /// <summary>
    /// Terrain detail (grass) bucket scores of one player, sampled by the owning client (which has GPUI detail data) and applied
    /// to that player's terrain proxy on the host (Fika Headless has none). Wire layout (77 bytes payload):
    ///   byte version | int netId | ushort prone[18] | ushort regular[18]
    /// Bucket order = ThatsLitGameworld.ActiveTerrainBuckets (5, 15, 1, 2, 3, 4, 6, 7, 8, 9, 11, 12, 13, 14, 16, 17, 18, 19).
    ///
    /// Quantization: IEEE 754 binary16 (half) bit patterns, round-to-nearest-even. TerrainDetailScore.prone/.regular are sums of
    /// per-cell grass scores (e.g. 0.06 * density per cell, x9/4 scaling, summed over every detail prototype): non-negative but
    /// not bounded by design, and SeenCoefPatch multiplies them by factors &lt; 1 before its 2.5 cap, so any linear 16-bit range
    /// could clip a value that still matters. Half covers 0 ~ 65504 (saturating, never Inf) with &lt;= 0.0488% relative error
    /// (abs. error &lt;= 0.00003 around the 0.1 gate, &lt;= 0.001 around the 2.45 cap), and encodes 0 exactly.
    /// </summary>
    public struct TerrainScorePacket : INetSerializable
    {
        public const byte CurrentVersion = 1;
        public const int BucketCount = 18;
        public const int PayloadSize = 1 + 4 + BucketCount * 2 * 2; // 77
        public const float MaxEncodable = 65504f;

        public byte version;
        public int netId;
        public ushort[] prone;
        public ushort[] regular;

        public void Deserialize(NetDataReader reader)
        {
            version = reader.GetByte();
            netId = reader.GetInt();
            // Always consume the full v1 payload (keeps the reader aligned if several packets share a datagram);
            // packets with another version are rejected by validation afterwards.
            if (prone == null || prone.Length != BucketCount) prone = new ushort[BucketCount];
            if (regular == null || regular.Length != BucketCount) regular = new ushort[BucketCount];
            for (int i = 0; i < BucketCount; i++) prone[i] = reader.GetUShort();
            for (int i = 0; i < BucketCount; i++) regular[i] = reader.GetUShort();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutUnmanaged(version);
            writer.PutUnmanaged(netId);
            for (int i = 0; i < BucketCount; i++) writer.PutUnmanaged(prone[i]);
            for (int i = 0; i < BucketCount; i++) writer.PutUnmanaged(regular[i]);
        }

        [StructLayout(LayoutKind.Explicit)]
        struct FloatBits
        {
            [FieldOffset(0)] public float f;
            [FieldOffset(0)] public uint u;
        }

        /// <summary>
        /// Non-negative float -> binary16 bits, round-to-nearest-even. NaN / negative / zero -> 0, >= 65504 -> 65504 (0x7BFF).
        /// </summary>
        public static ushort Encode(float value)
        {
            if (!(value > 0f)) return 0;
            if (value >= MaxEncodable) return 0x7BFF;

            FloatBits fb = default;
            fb.f = value;
            uint u = fb.u;
            int exp = (int)((u >> 23) & 0xFF) - 127 + 15;
            uint mant = u & 0x7FFFFFu;

            if (exp <= 0)
            {
                // Half subnormal: value = m * 2^-24
                if (exp < -10) return 0;
                mant |= 0x800000u;
                int shift = 14 - exp; // 14 ~ 24
                uint halfMant = mant >> shift;
                uint rem = mant & ((1u << shift) - 1u);
                uint halfway = 1u << (shift - 1);
                if (rem > halfway || (rem == halfway && (halfMant & 1u) != 0)) halfMant++; // may carry into the smallest normal: still correct
                return (ushort)halfMant;
            }

            uint h = ((uint)exp << 10) | (mant >> 13);
            uint r = mant & 0x1FFFu;
            if (r > 0x1000u || (r == 0x1000u && (h & 1u) != 0)) h++; // carry propagates into the exponent naturally
            if (h > 0x7BFFu) h = 0x7BFFu;
            return (ushort)h;
        }

        /// <summary>True for a finite, non-negative binary16 pattern (what <see cref="Encode"/> can produce).</summary>
        public static bool IsValidEncoded(ushort h)
        {
            return (h & 0x8000) == 0 && ((h >> 10) & 0x1F) != 0x1F;
        }

        /// <summary>Non-negative finite binary16 bits -> float (exact). Non-finite or negative patterns clamp to 0.</summary>
        public static float Decode(ushort h)
        {
            if ((h & 0x8000) != 0) return 0f;
            int exp = (h >> 10) & 0x1F;
            if (exp == 31) return 0f;
            int mant = h & 0x3FF;
            if (exp == 0) return mant * (1f / 16777216f); // subnormal: m * 2^-24 (exact)
            FloatBits fb = default;
            fb.u = ((uint)(exp - 15 + 127) << 23) | ((uint)mant << 13);
            return fb.f;
        }
    }
}
