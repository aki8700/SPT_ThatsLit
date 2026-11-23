#define DEBUG_DETAILS
using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace ThatsLit
{
    public class PlayerLitScoreProfile : IDisposable
    {
        public bool IsProxy { get; set; }
        internal float lum3s, lum1s, lum10s;
        public FrameStats frame0, frame1, frame2, frame3, frame4, frame5;
        internal float detailBonusSmooth;
        internal float litScoreFactor;
        public ThatsLitPlayer Player { get; }
        public CountPixelsJob PixelCountingJob { get; set; }
        public JobHandle CountingJobHandle { get; set; }
        internal object ScoreCalcData { get; set; }

        public PlayerLitScoreProfile(ThatsLitPlayer player)
        {
            Player = player;
        }

        internal void UpdateLumTrackers (float avgLumMultiFrames)
        {
            lum1s = Mathf.Lerp(lum1s, avgLumMultiFrames, Time.deltaTime);
            lum3s = Mathf.Lerp(lum3s, avgLumMultiFrames, Time.deltaTime / 3f);
            lum10s = Mathf.Lerp(lum10s, avgLumMultiFrames, Time.deltaTime / 10f);
        }

        internal float FindHighestAvgLumRecentFrame(bool includeThis, float thisframe)
        {
            float avgLum = includeThis ? thisframe : frame1.avgLum;
            if (frame1.avgLum > avgLum) avgLum = frame1.avgLum;
            if (frame2.avgLum > avgLum) avgLum = frame2.avgLum;
            if (frame3.avgLum > avgLum) avgLum = frame3.avgLum;
            if (frame4.avgLum > avgLum) avgLum = frame4.avgLum;
            if (frame5.avgLum > avgLum) avgLum = frame5.avgLum;
            return avgLum;
        }

        internal float FindLowestAvgLumRecentFrame(bool includeThis, float calculating)
        {
            float avgLum = includeThis ? calculating : frame1.avgLum;
            if (frame1.avgLum < avgLum) avgLum = frame1.avgLum;
            if (frame2.avgLum < avgLum) avgLum = frame2.avgLum;
            if (frame3.avgLum < avgLum) avgLum = frame3.avgLum;
            if (frame4.avgLum < avgLum) avgLum = frame4.avgLum;
            if (frame5.avgLum < avgLum) avgLum = frame5.avgLum;
            return avgLum;
        }

        internal float FindHighestMFAvgLumRecentFrame(bool includeThis, float thisframe)
        {
            float mfAvgLum = includeThis ? thisframe : frame1.avgLumMultiFrames;
            if (frame1.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame1.avgLumMultiFrames;
            if (frame2.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame2.avgLumMultiFrames;
            if (frame3.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame3.avgLumMultiFrames;
            if (frame4.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame4.avgLumMultiFrames;
            if (frame5.avgLumMultiFrames > mfAvgLum) mfAvgLum = frame5.avgLumMultiFrames;
            return mfAvgLum;
        }

        internal float FindLowestMFAvgLumRecentFrame(bool includeThis, float calculating)
        {
            float mfAvgLum = includeThis ? calculating : frame1.avgLumMultiFrames;
            if (frame1.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame1.avgLumMultiFrames;
            if (frame2.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame2.avgLumMultiFrames;
            if (frame3.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame3.avgLumMultiFrames;
            if (frame4.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame4.avgLumMultiFrames;
            if (frame5.avgLumMultiFrames < mfAvgLum) mfAvgLum = frame5.avgLumMultiFrames;
            return mfAvgLum;
        }

        internal float FindHighestScoreRecentFrame (bool includeThis, float calculating)
        {
            float score = includeThis ? calculating : frame1.score;
            if (frame1.score > score) score = frame1.score;
            if (frame2.score > score) score = frame2.score;
            if (frame3.score > score) score = frame3.score;
            if (frame4.score > score) score = frame4.score;
            if (frame5.score > score) score = frame5.score;
            return score;
        }

        internal float FindLowestScoreRecentFrame(bool includeThis, float calculating)
        {
            float score = includeThis ? calculating : frame1.score;
            if (frame1.score < score) score = frame1.score;
            if (frame2.score < score) score = frame2.score;
            if (frame3.score < score) score = frame3.score;
            if (frame4.score < score) score = frame4.score;
            if (frame5.score < score) score = frame5.score;
            return score;
        }

        // Persistent pixel counting buffers, allocated lazily once and reused every readback
        // (previously 3 TempJob NativeArrays were allocated + disposed per counted frame).
        internal NativeArray<float> persistentThresholds;
        internal NativeArray<int> persistentCounted;
        internal NativeArray<float> persistentLum;
        /// <summary>
        /// True between StartCountPixels and the next CompleteCountPixels. Frames without a newly scheduled count read as
        /// "nothing counted" (exactly what the former disposed-per-frame arrays produced: IsCreated == false).
        /// </summary>
        internal bool hasPendingCount;
        internal struct CachedPixelSample
        {
            public int shine, high, highMid, mid, midLow, low, dark, valid;
            public float lum, lumNonDark;
            public bool hasSample;
        }
        internal readonly CachedPixelSample[] cachedAngleSamples = new CachedPixelSample[6];
        internal int pendingCountAngleIndex;
        internal int lastCompletedAngleIndex;
        internal int cachedReadAngleIndex;
        internal bool useCachedPixelsOnSkip;

        internal void ClearCachedAngleSamples()
        {
            for (int i = 0; i < cachedAngleSamples.Length; i++)
                cachedAngleSamples[i] = default;
            pendingCountAngleIndex = 0;
            lastCompletedAngleIndex = 0;
            cachedReadAngleIndex = 0;
            useCachedPixelsOnSkip = false;
        }
        static int[] zeroCounted;
        static float[] zeroLum;

        internal static int CountedLength => 8 * Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount;
        internal static int LumLength => 2 * Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount;

        /// <summary>
        /// Complete any in-flight count, (lazily) allocate the persistent buffers and zero-clear counted/lum.
        /// </summary>
        internal void PrepareBuffers()
        {
            CountingJobHandle.Complete();
            if (!persistentThresholds.IsCreated) persistentThresholds = new NativeArray<float>(6, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (!persistentCounted.IsCreated) persistentCounted = new NativeArray<int>(CountedLength, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (!persistentLum.IsCreated) persistentLum = new NativeArray<float>(LumLength, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (zeroCounted == null || zeroCounted.Length != persistentCounted.Length) zeroCounted = new int[persistentCounted.Length];
            if (zeroLum == null || zeroLum.Length != persistentLum.Length) zeroLum = new float[persistentLum.Length];
            NativeArray<int>.Copy(zeroCounted, persistentCounted); // memcpy of zeros, no managed allocation
            NativeArray<float>.Copy(zeroLum, persistentLum);
        }

        /// <summary>
        /// Complete any in-flight job and release the persistent buffers. Safe to call repeatedly.
        /// </summary>
        internal void DisposeBuffers()
        {
            CountingJobHandle.Complete();
            hasPendingCount = false;
            ClearCachedAngleSamples();
            if (persistentThresholds.IsCreated) persistentThresholds.Dispose();
            if (persistentCounted.IsCreated) persistentCounted.Dispose();
            if (persistentLum.IsCreated) persistentLum.Dispose();
            persistentThresholds = default;
            persistentCounted = default;
            persistentLum = default;
            // The job struct holds copies of the (now freed) persistent arrays: drop them so nothing reads freed memory.
            var job = PixelCountingJob;
            job.tex = default; // Borrowed from ThatsLitPlayer; only that owner disposes it.
            job.thresholds = default;
            job.counted = default;
            job.lum = default;
            PixelCountingJob = job;
        }

        public void Dispose()
        {
            CountingJobHandle.Complete();
            var job = PixelCountingJob;
            job.tex = default;
            PixelCountingJob = job;
            DisposeBuffers();
        }

        public struct CountPixelsJob : IJobParallelFor, IDisposable
        {
            [Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndex]
            public int threadIndex;
            [ReadOnly]
            public NativeArray<Color32> tex;
            [WriteOnly]
            [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
            public NativeArray<int> counted; // pxS, pxH, pxHM, pxM, pxML, pxL, pxD, valid
            [WriteOnly]
            [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
            public NativeArray<float> lum; // lum, lumNonDark
            [ReadOnly]
            public NativeArray<float> thresholds; // thresholdShine, thresholdHigh, thresholdHighMid, thresholdMid, thresholdMidLow, thresholdLow

            public void Dispose()
            {
                counted.Dispose();
                lum.Dispose();
                thresholds.Dispose();
            }

            public void Execute(int index)
            {
                Color32 c = tex[index];
                // Exact byte equivalent of (c == Color.white || c.a <= 0.5f) without the Color32 -> Color conversion:
                // Color == is approximate (sqr diff < 1e-10) but 8-bit channels differ by >= 1/255, so only RGBA 255 matches.
                if (c.a == 0 || (c.r & c.g & c.b & c.a) == 255)
                    return;

                const float Inv765 = 1f / 765f;
                var pxLum = (c.r + c.g + c.b) * Inv765;

                int threadIndexOffset = threadIndex * 8;
                int lumIndexOffset = threadIndex * 2;
                lum[lumIndexOffset + 0] += pxLum;
                if (pxLum < thresholds[5])
                {
                    counted[threadIndexOffset + 6] += 1;
                    lum[lumIndexOffset + 1] += pxLum;
                }
                else if (pxLum >= thresholds[0]) counted[threadIndexOffset + 0] += 1;
                else if (pxLum >= thresholds[1]) counted[threadIndexOffset + 1] += 1;
                else if (pxLum >= thresholds[2]) counted[threadIndexOffset + 2] += 1;
                else if (pxLum >= thresholds[3]) counted[threadIndexOffset + 3] += 1;
                else if (pxLum >= thresholds[4]) counted[threadIndexOffset + 4] += 1;
                else if (pxLum >= thresholds[5]) counted[threadIndexOffset + 5] += 1;

                counted[threadIndexOffset + 7] += 1;
            }
        }
    }
}