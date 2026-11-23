using System;
using System.Collections;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.EnvironmentEffect;
using EFT.UI;
using EFT.Weather;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ThatsLit
{
    // TODO:
    // # Experiment: Full body lit check
    // Assign player thorax to layer N and set ShadowCastingMode on
    // un-cull N for all Lights
    // Cull N for FPS cam
    // Un-Cull N for TL cam
    // ! Could breaks Vision check?
    public class ThatsLitPlayer : MonoBehaviour
    {
        public const int DEBUG_INTERVAL = 19;
        public static bool IsDebugSampleFrame { get => ThatsLitPlugin.DebugInfo.Value && Time.frameCount % DEBUG_INTERVAL == 0; }
        readonly int RESOLUTION = 32 * ThatsLitPlugin.ResLevel.Value;
        public const int POWER = 3;
        public CustomRenderTexture rt;
        Texture slowRT;
        public Camera cam, envCam;
        Unity.Collections.NativeArray<Color32> observed;
        public RawImage display;
        public PlayerDebugInfo DebugInfo { get; internal set; }
        public LightAndLaserState LightAndLaserState { get; internal set; }
        float setupTime;
        float lastCheckedLights;
        public RaidSettings ActiveRaidSettings => gameworld.activeRaidSettings;
        public Player Player { get; internal set; }
        public float fog, rain, cloud;
        AsyncGPUReadbackRequest gquReq;
        internal float lastOutside;
        /// <summary>
        /// 0~10
        /// </summary>
        internal float ambienceShadownRating;
        /// <summary>
        /// Used in ScoreCalculator
        /// </summary>
        internal float AmbienceShadowFactor => Mathf.Pow(ambienceShadownRating / 10f, 2); 
        internal float bunkerTimeClamped;
        internal float lastInBunkerTime, lastOutBunkerTime;
        internal Vector3 lastInBunderPos;
        internal PlayerTerrainDetailsProfile TerrainDetails;
        float terrainScoreHintProne, terrainScoreHintRegular;
        internal PlayerFoliageProfile Foliage;
        internal PlayerLitScoreProfile PlayerLitScoreProfile { get; set;}
        internal Vector3 lastShotVector;
        internal float lastShotTime;
        internal static readonly LayerMask ambienceRaycastMask = (1 << LayerMask.NameToLayer("Terrain")) | (1 << LayerMask.NameToLayer("HighPolyCollider")) | (1 << LayerMask.NameToLayer("Grass")) | (1 << LayerMask.NameToLayer("Foliage"));
        // CheckEffectDelegate (reflection + CreateDelegate on Player.ActiveHealthController) was removed:
        // Fika ObservedPlayer has no ActiveHealthController, which made CreateDelegate throw ArgumentException.
        // Use Player?.ActiveHealthController?.HasStimulatorBuff(...) directly instead.
        static float canLoadTime = 0;
        internal RaycastHit flashLightHit;
        internal BotOwner lastNearest;
        ThatsLitGameworld gameworld;
        static Camera prefab;
        int cameraThrottleFrequency = 1;
        int _camAngleIndex;
        int _renderedAngleIndex;
        int _pendingReadbackAngle;
        bool _hasUnreadRender;
        float _lastCamRenderTime = -1000f;
        float _lastWakeShotTime = -1f;
        float _nextDebugCopyTime;
        Vector3 _lastCamRenderPos;
        float _lastCamRenderPose;
        bool _lastCamRenderProne;
        LightAndLaserState _lastCamRenderLights;
        float _lastCamRenderShadow;
        int _camWakeSweepRemaining = 6;
        int _camWarmupRemaining = 6;

        void ResetCameraCacheState()
        {
            _camAngleIndex = 0;
            _renderedAngleIndex = 0;
            _pendingReadbackAngle = 0;
            _hasUnreadRender = false;
            _lastCamRenderTime = -1000f;
            _lastWakeShotTime = -1f;
            _nextDebugCopyTime = 0f;
            _camWakeSweepRemaining = 6;
            _camWarmupRemaining = 6;
            PlayerLitScoreProfile?.ClearCachedAngleSamples();
        }

        static bool LightsEqual(LightAndLaserState a, LightAndLaserState b)
        {
            return a.VisibleLight == b.VisibleLight
                && a.VisibleLaser == b.VisibleLaser
                && a.IRLight == b.IRLight
                && a.IRLaser == b.IRLaser
                && a.VisibleLightSub == b.VisibleLightSub
                && a.VisibleLaserSub == b.VisibleLaserSub
                && a.IRLightSub == b.IRLightSub
                && a.IRLaserSub == b.IRLaserSub;
        }
        public static bool CanLoad ()
        {
            // Headless (batchmode) never renders: no camera prefab is needed, and remote ObservedPlayers
            // must still be set up so foliage / proxy brightness (from That's Lit Sync) apply to bots.
            if (Application.isBatchMode) return true;

            // Prefab already built: same result as the final return below, without re-traversing CameraManager every frame.
            if (prefab != null) return canLoadTime + 10 < Time.realtimeSinceStartup;

            Camera opticCamera = EFT.CameraControl.CameraManager.Instance?.OpticCameraManager?.Camera;
            if (opticCamera != null
             && prefab == null)
            {
                opticCamera.gameObject.SetActive(false);
                prefab = GameObject.Instantiate<Camera>(opticCamera);
                opticCamera.gameObject.SetActive(true);
                prefab.gameObject.name = "That's Lit Camera (Prefab)";
                foreach (var c in prefab.gameObject.GetComponents<MonoBehaviour>())
                switch (c) {
                    case VolumetricLightRenderer volumetricLightRenderer:
                        if (ThatsLitPlugin.VolumetricLightRenderer.Value) volumetricLightRenderer.IsOptic = false;
                        break;
                    case AreaLightManager areaLightManager:
                        MonoBehaviour.Destroy(c);
                        break;
                    default:
                        MonoBehaviour.Destroy(c);
                        break;
                }
                prefab.clearFlags = CameraClearFlags.SolidColor;
                prefab.backgroundColor = new Color (0, 0, 0, 0);

                prefab.nearClipPlane = 0.001f;
                prefab.farClipPlane = 3.5f;

                prefab.cullingMask = LayersMaskController.PlayerMask;
                prefab.fieldOfView = 44;


                canLoadTime = Time.realtimeSinceStartup;
                Logger.LogWarning($"[That's Lit] Can load players. Time: {canLoadTime}");
                return false; // wait for next frame
            }

            return prefab != null && canLoadTime + 10 < Time.realtimeSinceStartup;
        }

        internal void Setup (ThatsLitGameworld gameworld)
        {
            setupTime = Time.time;
            if (Player.IsYourPlayer)
                DebugInfo = new PlayerDebugInfo();

            this.gameworld = gameworld;
            MaybeEnableBrightness();
        }

        internal void MaybeEnableBrightness ()
        {
            if (!ThatsLitPlugin.EnabledLighting.Value
             || gameworld?.ScoreCalculator == null // Disabled on the map?
             || Application.isBatchMode // No rendering on headless; scores arrive as proxy values (ThatsLitAPI.ToggleBrightnessProxyDirect)
             || prefab == null)
                return;

            if (PlayerLitScoreProfile == null) PlayerLitScoreProfile = new PlayerLitScoreProfile(this);
            if (PlayerLitScoreProfile.IsProxy) return;

            if (rt == null)
            {
                // rt = new RenderTexture(RESOLUTION, RESOLUTION, 0, RenderTextureFormat.ARGB32);
                rt = new CustomRenderTexture(RESOLUTION, RESOLUTION, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                rt.depth = 0;
                rt.doubleBuffered = true;
                rt.useMipMap = false;
                rt.filterMode = FilterMode.Point;
                rt.Create();
            }

            if (cam == null)
            {
                cam = GameObject.Instantiate<Camera>(prefab);
                cam.gameObject.name = "That's Lit Camera";
                cam.transform.SetParent(Player.Transform.Original);
                cam.targetTexture = rt;
                cam.gameObject.SetActive(true);
            }
            else cam.enabled = true;
            ResetCameraCacheState();


            if (Player.IsYourPlayer && !_debugTextureSubscribed)
            {
                ThatsLitPlugin.DebugTexture.SettingChanged += HandleDebugTextureSettingChanged;
                _debugTextureSubscribed = true;
            }
            if (!ThatsLitPlugin.DebugTexture.Value)
            {
                if (display) display.enabled = false;
                return;
            }
            if (MonoBehaviourSingleton<GameUI>.Instance == null) return;
            if (Player.IsYourPlayer)
            {
                if (slowRT == null) slowRT = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32, false);
                if (display == null)
                {
                    display = new GameObject().AddComponent<RawImage>();
                    display.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                    display.RectTransform().sizeDelta = new Vector2(160, 160);
                    display.texture = slowRT;
                    display.RectTransform().anchoredPosition = new Vector2(-720, -360);
                }
                else display.enabled = true;
            }
        }

        internal bool IsBrightnessProxyRequested { get; private set; }
        // Tracks the DebugTexture.SettingChanged subscription so DisableBrightness() (called every frame while the mod is
        // disabled) does not allocate a new delegate per frame for a no-op unsubscribe.
        bool _debugTextureSubscribed;
        void DisposeObserved()
        {
            PlayerLitScoreProfile?.CountingJobHandle.Complete();
            if (observed.IsCreated) { observed.Dispose(); observed = default; }
        }
        internal void DisableBrightness ()
        {
            _readbackGeneration++;
            DisposeObserved();
            ResetCameraCacheState();
            if (cam) cam.enabled = false;
            if (display) display.enabled = false;
            if (!IsBrightnessProxyRequested)
            {
                PlayerLitScoreProfile?.DisposeBuffers();
                PlayerLitScoreProfile = null;
            }
            if (_debugTextureSubscribed)
            {
                ThatsLitPlugin.DebugTexture.SettingChanged -= HandleDebugTextureSettingChanged;
                _debugTextureSubscribed = false;
            }
        }
        internal void ToggleBrightnessProxy (bool toggle)
        {
            IsBrightnessProxyRequested = toggle;
            if (cam) cam.enabled = !toggle;
            if (display) display.enabled = !toggle;
            PlayerLitScoreProfile.IsProxy = toggle;
            if (toggle)
            {
                _readbackGeneration++;
                DisposeObserved();
                ResetCameraCacheState();
                PlayerLitScoreProfile.DisposeBuffers(); // A proxy never counts pixels locally
                PlayerLitScoreProfile.frame0 = default;
                PlayerLitScoreProfile.litScoreFactor = 0f;
            }
        }

        /// <summary>
        /// Requested by That's Lit Sync for remote humans: terrain detail (grass) scores are received from the owning client
        /// instead of being sampled locally (Fika Headless has no GPUI detail data). See <see cref="ThatsLitAPI.ToggleTerrainProxyDirect"/>.
        /// </summary>
        internal bool IsTerrainProxyRequested { get; private set; }
        internal void ToggleTerrainProxy (bool toggle)
        {
            IsTerrainProxyRequested = toggle;
            if (toggle)
            {
                if (TerrainDetails != null && !TerrainDetails.IsProxy)
                    TerrainDetails = null; // Drop any locally sampled profile; a fresh proxy profile is created below / on demand
                EnsureTerrainProxyProfile();
            }
            else if (TerrainDetails != null && TerrainDetails.IsProxy)
            {
                TerrainDetails = null; // Back to local sampling (re-created by Update when available)
            }
        }

        /// <summary>
        /// Create the proxy terrain profile if requested and the grass module is usable on this map. Works in batchmode.
        /// </summary>
        internal bool EnsureTerrainProxyProfile ()
        {
            if (!IsTerrainProxyRequested) return false;
            if (TerrainDetails != null) return TerrainDetails.IsProxy;
            if (!ThatsLitPlugin.EnabledGrasses.Value || gameworld == null || gameworld.terrainDetailsUnavailable)
                return false;
            TerrainDetails = new PlayerTerrainDetailsProfile() { IsProxy = true };
            return true;
        }

        private void Update()
        {
            if (Player == null) return;
            if (Player?.HealthController?.IsAlive == false)
            {
                MonoBehaviour.Destroy(this);
                return;
            }
            
            if (!ThatsLitPlugin.EnabledMod.Value)
            {
                DisableBrightness();
                return;
            }

            #region BENCHMARK
            ThatsLitPlugin.swUpdate.MaybeResume();
            #endregion


            if (Player.MainParts == null || !Player.MainParts.TryGetValue(BodyPartType.body, out var bodyPart) || bodyPart == null)
            {
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }
            Vector3 bodyPos = bodyPart.Position;
            if ((!ThatsLitPlugin.EnabledGrasses.Value || gameworld.terrainDetailsUnavailable) && !IsTerrainProxyRequested) TerrainDetails = null;
            if (!ThatsLitPlugin.EnabledFoliage.Value) Foliage = null;
            if (!ThatsLitPlugin.EnableEquipmentCheck.Value) LightAndLaserState = default;

            if (!Player.AIData.IsInside)
                lastOutside = Time.time;

            bool inBunker = EnvironmentManager.Instance?.InBunker == true;
            if (inBunker && lastOutBunkerTime >= lastInBunkerTime)
            {
                lastInBunkerTime = Time.time;
                lastInBunderPos = bodyPos;
            }

            if (!inBunker && lastOutBunkerTime < lastInBunkerTime)
            {
                lastOutBunkerTime = Time.time;
            }

            if (lastOutBunkerTime < lastInBunkerTime && bodyPos.SqrDistance(lastInBunderPos) > 2.25f)
                 bunkerTimeClamped += Time.deltaTime;
            else bunkerTimeClamped -= Time.deltaTime * 5;

            bunkerTimeClamped = Mathf.Clamp(bunkerTimeClamped, 0, 10);

            ThatsLitPlugin.swTerrain.MaybeResume();
            if (IsTerrainProxyRequested)
            {
                // Remote scores only (also on headless/batchmode): never sampled locally, never nulled here.
                if (TerrainDetails == null) EnsureTerrainProxyProfile();
            }
            else if (ThatsLitPlugin.EnabledGrasses.Value
                && !gameworld.terrainDetailsUnavailable
                && TerrainDetails == null
                && !Application.isBatchMode)
            {
                TerrainDetails = new PlayerTerrainDetailsProfile();
            }

            if (TerrainDetails != null
            && !TerrainDetails.IsProxy
            && Time.time > TerrainDetails.LastCheckedTime + 0.41f)
            {
                gameworld.CheckTerrainDetails(bodyPos, TerrainDetails);
                if (gameworld.terrainDetailsUnavailable && !IsTerrainProxyRequested) TerrainDetails = null;
                ThatsLitAPI.OnPlayerSurroundingTerrainSampledDirect?.Invoke(this);
                ThatsLitAPI.OnPlayerSurroundingTerrainSampled?.Invoke(this.Player);
                if (ThatsLitPlugin.TerrainInfo.Value && TerrainDetails != null)
                {
                    var score = gameworld.CalculateDetailScore(TerrainDetails, Vector3.zero, 0, 0);
                    terrainScoreHintProne = score.prone;
                    var pf = Utility.GetPoseFactor(Player.PoseLevel, (Player.Physical != null && Player.Physical.MaxPoseLevel > 0f ? Player.Physical.MaxPoseLevel : 1f), Player.IsInPronePose);
                    terrainScoreHintRegular = Utility.GetPoseWeightedRegularTerrainScore(pf, score);
                }
            }
            ThatsLitPlugin.swTerrain.Stop();

            ThatsLitPlugin.swFoliage.MaybeResume();
            // Foliage only uses Physics.OverlapSphereNonAlloc (no GPU), so it also runs on headless.
            if (ThatsLitPlugin.EnabledFoliage.Value
             && !gameworld.foliageUnavailable
             && Foliage == null)
            {
                Foliage = new PlayerFoliageProfile(new FoliageInfo[16], new Collider[16]);
            }
            if (Foliage != null)
            {
                if (!Foliage.IsFoliageSorted)
                    Foliage.IsFoliageSorted = SlicedBubbleSort(Foliage.Foliage, Foliage.FoliageCount * 2, Foliage.FoliageCount);
                gameworld.UpdateFoliageScore(bodyPos, Foliage);
            }
            ThatsLitPlugin.swFoliage.Stop();

            if (ThatsLitPlugin.EnableEquipmentCheck.Value
             && Time.time > lastCheckedLights + 0.41f)
            {
                lastCheckedLights = Time.time;
                var state = LightAndLaserState;
                (state.deviceStateCache, state.deviceStateCacheSub) = Utility.DetermineShiningEquipments(Player);
                state.VisibleLight = state.deviceStateCache.light > 0;
                state.VisibleLaser = state.deviceStateCache.laser > 0;
                state.IRLight = state.deviceStateCache.irLight > 0;
                state.IRLaser = state.deviceStateCache.irLaser > 0;
                state.VisibleLightSub = state.deviceStateCacheSub.light > 0;
                state.VisibleLaserSub = state.deviceStateCacheSub.laser > 0;
                state.IRLightSub = state.deviceStateCacheSub.irLight > 0;
                state.IRLaserSub = state.deviceStateCacheSub.irLaser > 0;
                LightAndLaserState = state;
            }

            CastFlashlight();

            overheadHaxRating = UpdateOverheadHaxCastRating(bodyPos, overheadHaxRating);

            if (PlayerLitScoreProfile == null && ThatsLitPlugin.EnabledLighting.Value)
            {
                MaybeEnableBrightness();
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }
            else if (PlayerLitScoreProfile != null && !PlayerLitScoreProfile.IsProxy && !ThatsLitPlugin.EnabledLighting.Value)
            {
                DisableBrightness();
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }

            if (PlayerLitScoreProfile == null || PlayerLitScoreProfile.IsProxy || gameworld?.ScoreCalculator == null
             || cam == null || Application.isBatchMode) // a non-proxy profile without a camera (e.g. headless) has nothing to render
            {
                ThatsLitPlugin.swUpdate.Stop();
                return;
            }

            // ! Consdier this the box of all Brightness required statements
            // Remember that proxies also have a PlayerLitScoreProfile

            // 1. Always read back the previously rendered frame before deciding whether to render again.
            if (gquReq.done && rt != null)
            {
                if (_hasUnreadRender)
                {
                    _hasUnreadRender = false;
                    if (ThatsLitPlugin.DebugTexture.Value
                        && display?.enabled == true
                        && slowRT != null
                        && Time.time >= _nextDebugCopyTime)
                    {
                        Graphics.CopyTexture(rt, slowRT);
                        _nextDebugCopyTime = Time.time + 0.5f;
                    }
                    if (_readbackCallback == null) _readbackCallback = OnReadbackComplete;
                    _pendingReadbackGen = _readbackGeneration;
                    _pendingReadbackAngle = _renderedAngleIndex;
                    gquReq = AsyncGPUReadback.Request(rt, 0, _readbackCallback);
                }
                else if (cam?.enabled == false)
                {
                    DisposeObserved();
                }
            }

            // 2. Dynamic 6-angle camera scheduling: reuse cached 6-angle pixel counts on skipped frames
            //    so darkness/indoor CPU culling drops dramatically with zero stealth fidelity loss.
            bool useThrottling = ThatsLitPlugin.EnabledCameraThrottling.Value;
            bool shouldRender = false;

            if (ThatsLitPlugin.EnabledLighting.Value)
            {
                bool moved = (bodyPos - _lastCamRenderPos).sqrMagnitude > 0.0225f; // > 0.15m
                bool poseChanged = Player.IsInPronePose != _lastCamRenderProne
                    || Mathf.Abs(Player.PoseLevel - _lastCamRenderPose) > 0.05f;
                bool lightsChanged = !LightsEqual(LightAndLaserState, _lastCamRenderLights);
                bool newShot = lastShotTime > 0f && lastShotTime != _lastWakeShotTime;
                bool shadowChanged = Mathf.Abs(ambienceShadownRating - _lastCamRenderShadow) > 0.5f;

                bool anyWake = moved || poseChanged || lightsChanged || newShot || shadowChanged;
                if (anyWake)
                {
                    _camWakeSweepRemaining = 6;
                }

                bool readbackSlotFree = !_hasUnreadRender || gquReq.done;
                if (!useThrottling)
                {
                    shouldRender = readbackSlotFree;
                }
                else if (readbackSlotFree)
                {
                    int maxHz = ThatsLitPlugin.CameraMaxRateHz.Value;
                    int rawIdleHz = Mathf.Max(1, ThatsLitPlugin.CameraIdleRateHz.Value);
                    int idleHz = maxHz > 0 ? Mathf.Min(rawIdleHz, maxHz) : rawIdleHz;
                    float minInterval = maxHz > 0 ? 1f / maxHz : 0f;

                    float brightFactor = Mathf.InverseLerp(-0.1f, 0.35f, PlayerLitScoreProfile.frame0.ambienceScore);
                    float interval;
                    if (_camWarmupRemaining > 0 || _camWakeSweepRemaining > 0)
                    {
                        // Complete the full 6-angle warmup or wake sweep at CameraMaxRateHz without bright-daylight stalls.
                        interval = minInterval;
                    }
                    else if (brightFactor >= 0.99f)
                    {
                        interval = Mathf.Max(minInterval, 1.0f);
                    }
                    else
                    {
                        float idleInterval = 1f / idleHz;
                        interval = Mathf.Lerp(idleInterval, Mathf.Max(idleInterval, 1.0f), brightFactor * brightFactor);
                    }

                    cameraThrottleFrequency = interval <= 0f ? 1 : Mathf.Max(1, Mathf.RoundToInt(interval / Mathf.Max(Time.deltaTime, 0.001f)));
                    if (Time.time >= _lastCamRenderTime + interval)
                    {
                        shouldRender = true;
                    }
                }
            }

            PlayerLitScoreProfile.useCachedPixelsOnSkip = useThrottling && ThatsLitPlugin.EnabledLighting.Value;
            cam.enabled = shouldRender;

            if (shouldRender)
            {
                var camHeight = Player.IsInPronePose ? 0.45f : 2.2f * (0.6f + 0.4f * Player.PoseLevel);
                var targetHeight = Player.IsInPronePose ? 0.2f : 0.7f;
                var horizontalScale = Player.IsInPronePose ? 1.2f : 0.8f;
                int angle = _camAngleIndex;
                _renderedAngleIndex = angle;
                _camAngleIndex = (angle + 1) % 6;
                _hasUnreadRender = true;

                _lastCamRenderTime = Time.time;
                _lastCamRenderPos = bodyPos;
                _lastCamRenderPose = Player.PoseLevel;
                _lastCamRenderProne = Player.IsInPronePose;
                _lastCamRenderLights = LightAndLaserState;
                _lastCamRenderShadow = ambienceShadownRating;
                if (lastShotTime > 0f) _lastWakeShotTime = lastShotTime;
                if (_camWarmupRemaining > 0) _camWarmupRemaining--;
                if (_camWakeSweepRemaining > 0) _camWakeSweepRemaining--;

                switch (angle)
                {
                    case 0:
                        {
                            if (Player.IsInPronePose)
                            {
                                cam.transform.localPosition = new Vector3(0, 2, 0);
                                cam.transform.LookAt(Player.Transform.Original.position);
                            }
                            else
                            {
                                cam.transform.localPosition = new Vector3(0, camHeight, 0);
                                cam.transform.LookAt(Player.Transform.Original.position);
                            }
                            break;
                        }
                    case 1:
                        {
                            cam.transform.localPosition = new Vector3(horizontalScale, camHeight, horizontalScale);
                            cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                            break;
                        }
                    case 2:
                        {
                            cam.transform.localPosition = new Vector3(horizontalScale, camHeight, -horizontalScale);
                            cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                            break;
                        }
                    case 3:
                        {
                            if (Player.IsInPronePose)
                            {
                                cam.transform.localPosition = new Vector3(0, 2f, 0);
                                cam.transform.LookAt(Player.Transform.Original.position);
                            }
                            else
                            {
                                cam.transform.localPosition = new Vector3(0, -0.5f, 0.35f);
                                cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * 1f);
                            }
                            break;
                        }
                    case 4:
                        {
                            cam.transform.localPosition = new Vector3(-horizontalScale, camHeight, -horizontalScale);
                            cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                            break;
                        }
                    case 5:
                        {
                            cam.transform.localPosition = new Vector3(-horizontalScale, camHeight, horizontalScale);
                            cam.transform.LookAt(Player.Transform.Original.position + Vector3.up * targetHeight);
                            break;
                        }
                }

            }
            
            // Ambient shadow
            // Not required for proxies
            // Only the body part sampled this frame (Time.frameCount % 5) is read; the other 4 lookups were unused.
            int ambienceSlice = Time.frameCount % 5;
            BodyPartType ambiencePart = ambienceSlice switch
            {
                0 => BodyPartType.head,
                1 => BodyPartType.leftLeg,
                2 => BodyPartType.rightLeg,
                3 => BodyPartType.leftArm,
                _ => BodyPartType.rightArm,
            };
            Vector3 ambiencePartPos = Player.MainParts[ambiencePart].Position;
            if (TOD_Sky.Instance != null)
            {
                Ray ray = default;
                float? sunlightFactor = gameworld.ScoreCalculator?.CalculateSunLightTimeFactor(gameworld.LocationId, Utility.GetInGameDayTime());
                if (sunlightFactor == null)
                    sunlightFactor = TOD_Sky.Instance.IsDay ? 1f : 0f;
                Vector3 ambienceDir = sunlightFactor > 0.05f ? TOD_Sky.Instance.LocalSunDirection : TOD_Sky.Instance.LightDirection;

                ray = new Ray(ambiencePartPos, ambienceDir);

                var casted1 = RaycastIgnoreGlass(ray, 2000, ambienceRaycastMask, out var highPolyHit, out var lastPenetrated); // Anything high poly that is not glass

                // if (Time.frameCount % 213 == 0)
                //     EFT.UI.ConsoleScreen.Log($"---");
                // Fuck GZ buildings (low poly)
                bool casted2 = false;

                // if (!casted1) casted2 = RaycastIgnoreGlass(ray, 1250, ambienceRaycastMask_lowPriority, out var lowPolyHit, out var _, 0, 3) && lastPenetrated.distance < lowPolyHit.distance; // high/low poly any non-glass that is further than 0 or casted glass
                if (casted1 || casted2)
                {
                    ambienceShadownRating += 10f * Time.deltaTime;
                }
                else ambienceShadownRating -= 25f * Time.deltaTime;
                ambienceShadownRating = Mathf.Clamp(ambienceShadownRating, 0, 10f);
            }

            // if (ThatsLitPlugin.DebugTexture.Value && envCam)
            // {
            //     envCam.transform.localPosition = envCamOffset;
            //     switch (camPos)
            //     {
            //         case 0:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.left * 25);
            //                 break;
            //             }
            //         case 1:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.right * 25);
            //                 break;
            //             }
            //         case 2:
            //             {
            //                 envCam.transform.localPosition = envCamOffset;
            //                 envCam.transform.LookAt(bodyPos + Vector3.down * 10);
            //                 break;
            //             }
            //         case 3:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.back * 25);
            //                 break;
            //             }
            //         case 4:
            //             {
            //                 envCam.transform.LookAt(bodyPos + Vector3.right * 25);
            //                 break;
            //             }
            //     }
            // }

            ThatsLitPlugin.swUpdate.Stop();
        }

        void FixedUpdate ()
        {
            ThatsLitPlugin.swFUpdate.MaybeResume();
            if (ThatsLitPlugin.BotLookDirectionTweaks.Value == false
             || Player?.HealthController?.IsAlive == false
             || gameworld == null
             || !ThatsLitPlugin.EnabledMod.Value)
            {
                ThatsLitPlugin.swFUpdate.Stop();
                return;
            }

            if (lastNearest == null || lastNearest.IsDead)
            {
                ThatsLitPlugin.swFUpdate.Stop();
                return;
            }
            Player nearestBotPlayer = lastNearest.GetPlayer;
            if (ThatsLitPlugin.EnableNearestBotSteering.Value
             && nearestBotPlayer != null
             && nearestBotPlayer.isActiveAndEnabled
             && nearestBotPlayer.MainParts != null
             && nearestBotPlayer.MainParts.ContainsKey(BodyPartType.head)
             && lastNearest.Steering != null
             && lastNearest.Mover != null)
            {
                var nearestBotGoalEnemy = lastNearest.Memory?.GoalEnemy;

                ThatsLitGameworld.SingleIdThrottler throttler;
                gameworld.singleIdThrottlers.TryGetValue(lastNearest.ProfileId, out throttler);

                if (ReferenceEquals(nearestBotGoalEnemy?.Person, Player)
                 && nearestBotGoalEnemy?.HaveSeen == true
                 && Time.time - throttler.lastForceLook > 1f
                 && lastNearest.Mover.IsMoving == true && nearestBotPlayer.IsSprintEnabled == false
                //  && lastNearest.Steering.SteeringMode == EBotSteering.ToMovingDirection
                 && Vector3.Distance(Player.Position, lastNearest.Position) < 5f
                 && UnityEngine.Random.Range(0f, 1f) < 0.6f * Mathf.InverseLerp(15f, 5f, Time.time - nearestBotGoalEnemy.TimeLastSeenReal) + 0.5f * Mathf.InverseLerp(7.5f, 1f, Vector3.Distance(Player.Position, nearestBotGoalEnemy.EnemyLastPositionReal)))
                {
                    lastNearest.Steering.LookToPoint(Player.Position);
                    if (DebugInfo != null)
                        DebugInfo.forceLooks++;
                    
                    throttler.lastForceLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }

                if (ReferenceEquals(nearestBotGoalEnemy?.Person, Player)
                 && nearestBotGoalEnemy?.HaveSeen == true
                 && Time.time - nearestBotGoalEnemy.TimeLastSeen < 10f
                 && lastNearest.Mover.IsMoving == true && !nearestBotPlayer.IsSprintEnabled
                 && Time.time - lastOutside > 1f
                 && Time.time - throttler.lastForceLook > 1f
                 && UnityEngine.Random.Range(0f, 1f) < 0.05f * Mathf.InverseLerp(5f, 1f, nearestBotGoalEnemy.Distance))
                {
                    lastNearest.Steering?.LookToPoint(Player.Position);
                    if (DebugInfo != null)
                        DebugInfo.forceLooks++;

                    throttler.lastForceLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }

                if (!nearestBotPlayer.IsSprintEnabled
                //  && lastNearest.Steering.SteeringMode == EBotSteering.ToMovingDirection
                 && !(nearestBotGoalEnemy != null && Time.time - nearestBotGoalEnemy.TimeLastSeenReal < 10f)
                 && Time.time - throttler.lastSideLook > 10f)
                {
                    lastNearest.StartCoroutine(MakeBotPeekSide(lastNearest));
                    if (DebugInfo != null)
                        DebugInfo.sideLooks++;
                    
                    throttler.lastSideLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }

                Vector3 botHeadPos = nearestBotPlayer.MainParts[BodyPartType.head].Position;
                if (flashLightHit.collider != null
                 && Time.time - throttler.lastForceLook > 1f
                 && (ReferenceEquals(nearestBotGoalEnemy?.Person, Player) || nearestBotGoalEnemy == null)
                //  && lastNearest.Steering.SteeringMode == EBotSteering.ToMovingDirection
                 && Vector3.Angle(lastNearest.LookDirection, flashLightHit.normal) > 90f
                 && Vector3.Angle(lastNearest.LookDirection, flashLightHit.point - botHeadPos) < 90f
                 && Vector3.Angle(lastNearest.LookDirection, Player.Position - botHeadPos) > 30f
                 && UnityEngine.Random.Range(0f, 1f) < 0.05f * Mathf.InverseLerp(20f, 2f, Vector3.Distance(lastNearest.Position, Player.Position)))
                {
                    lastNearest.Steering?.LookToPoint(Player.Position);
                    if (DebugInfo != null)
                        DebugInfo.forceLooks++;
                    
                    throttler.lastForceLook = Time.time;
                    gameworld.singleIdThrottlers[lastNearest.ProfileId] = throttler;
                    ThatsLitPlugin.swFUpdate.Stop();
                    return;
                }
            }
            ThatsLitPlugin.swFUpdate.Stop();
        }


        IEnumerator MakeBotPeekSide (BotOwner bot)
        {
            if (bot == null || bot.IsDead) yield break;
            Vector3 lookDirection = bot.LookDirection;
            Vector3 dir = lookDirection.RotateAroundPivot(Vector3.up, Quaternion.Euler(0f, UnityEngine.Random.Range(-180f, 180f), 0f));
            bot.Steering?.LookToDirection(dir);
            yield return new WaitForSeconds(UnityEngine.Random.Range(1, 4f));
            if (bot == null || bot.IsDead) yield break;
            bot.Steering?.LookToMovingDirection();
        }

        private float UpdateOverheadHaxCastRating(Vector3 bodyPos, float currentRating)
        {
            if (OverheadHaxCast(bodyPos, out var haxHit))
            {
                currentRating += Time.timeScale * (Mathf.InverseLerp(10f, 1f, haxHit.distance) - 0.01f);
            }
            else
            {
                currentRating -= Time.timeScale * 1.75f;
            }
            return Mathf.Clamp(currentRating, 0f, 10f);
        }

        private float UpdateSurroundingCastRating(Vector3 bodyPos, float currentRating)
        {
            if (SurroundingCast(bodyPos, out var hit))
            {
                currentRating += Time.timeScale * (Mathf.InverseLerp(5f, 1f, hit.distance) - 0.01f);
            }
            else
            {
                currentRating -= Time.timeScale * 1.75f;
            }
            return Mathf.Clamp(currentRating, 0f, 10f);
        }

        void LateUpdate()
        {
            if (Player == null) return;
            gameworld.GetWeatherStats(out fog, out rain, out cloud);
            if (PlayerLitScoreProfile == null) return;

            //if (debugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(tex, debugTex);
            // if (envDebugTex != null && Time.frameCount % 61 == 0) Graphics.CopyTexture(envTex, envDebugTex);

            ThatsLitPlugin.swScoreCalc.MaybeResume();
                gameworld.ScoreCalculator?.CalculateMultiFrameScore(cloud, fog, rain, gameworld, PlayerLitScoreProfile, Utility.GetInGameDayTime(), gameworld.LocationId);
            ThatsLitPlugin.swScoreCalc.Stop();

            if (!PlayerLitScoreProfile.IsProxy)
            {
                ThatsLitAPI.OnPlayerBrightnessScoreCalculatedDirect?.Invoke(this, PlayerLitScoreProfile.frame0.multiFrameLitScore, PlayerLitScoreProfile.frame0.ambienceScore);
                ThatsLitAPI.OnPlayerBrightnessScoreCalculated?.Invoke(Player, PlayerLitScoreProfile.frame0.multiFrameLitScore, PlayerLitScoreProfile.frame0.ambienceScore);
            }


        }
        Action<AsyncGPUReadbackRequest> _readbackCallback;
        bool _destroyed;
        int _readbackGeneration;
        int _pendingReadbackGen;
        void OnReadbackComplete (AsyncGPUReadbackRequest req)
        {
            DisposeObserved();
            // Stale callbacks (component destroyed, brightness disabled, re-enabled, or switched to proxy while the readback was in flight)
            // are dropped so an old frame can never feed a new profile.
            if (_destroyed || req.hasError || _pendingReadbackGen != _readbackGeneration) return;
            var profile = PlayerLitScoreProfile;
            if (profile == null || profile.IsProxy) return;

            ThatsLitPlugin.swScoreCalc.MaybeResume();
            observed = new Unity.Collections.NativeArray<Color32>(req.GetData<Color32>(), Unity.Collections.Allocator.Persistent);
            profile.pendingCountAngleIndex = _pendingReadbackAngle;
            gameworld?.ScoreCalculator?.PreCalculate(profile, observed, Utility.GetInGameDayTime());
            ThatsLitPlugin.swScoreCalc.Stop();
        }

        internal float overheadHaxRating;
        internal float OverheadHaxRatingFactor => overheadHaxRating / 10f;
        // The 24 overhead cast directions, indexed by Time.frameCount % 24 (slice = i % 6, expansion = i / 6),
        // computed once with the exact same Quaternion formula as before.
        static readonly Vector3[] overheadCastDirs = BuildOverheadCastDirs();
        static Vector3[] BuildOverheadCastDirs ()
        {
            var dirs = new Vector3[24];
            for (int i = 0; i < 24; i++)
            {
                Vector3 cast = new Vector3(0, 1, 0);
                int slice = i % 6;
                int expansion = i / 6;
                cast = Quaternion.Euler((expansion + 1) * 10, 0, 0) * cast;
                cast = Quaternion.Euler(0, slice * -60f, 0) * cast;
                dirs[i] = cast;
            }
            return dirs;
        }
        private bool OverheadHaxCast (Vector3 from, out RaycastHit hit)
        {
            Vector3 cast = overheadCastDirs[Time.frameCount % 24];

            var ray = new Ray(from, cast);
            return RaycastIgnoreGlass(ray, 30, ambienceRaycastMask, out hit, out var lp);
        }

        private bool SurroundingCast (Vector3 from, out RaycastHit hit)
        {
            Vector3 cast = new Vector3(1, 0, 0);
            int slice = Time.frameCount % 60;
            cast = Quaternion.Euler(0, slice * 6, 0) * cast;
            
            var ray = new Ray(from, cast);
            return Physics.Raycast(ray, out hit, 5, ambienceRaycastMask);
        }

        /// <summary>
        /// Detect where the active flashlight is hitting.
        /// </summary>
        private void CastFlashlight ()
        {
            flashLightHit = default;
            if (!LightAndLaserState.AnyLightMain)
            {
                return;
            }
            if (Player.HandsController is Player.FirearmController fc)
            {
                var ray = new Ray(fc.FireportPosition, fc.WeaponDirection + UnityEngine.Random.insideUnitSphere / 7.5f);
                Physics.Raycast(ray, out flashLightHit, 20, ambienceRaycastMask);
            }
        }

        static readonly int IgnoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        GameObject[] glassObjects = new GameObject[10];
        int[] glassLayers = new int[10];
        float[] glassDistances = new float[10];
        private bool RaycastIgnoreGlass(Ray ray, float distance, LayerMask mask, out RaycastHit hit, out RaycastHit lastPenetrated, int depth = 0, int maxDepth = 10)
        {
            hit = default;
            lastPenetrated = default;
            int count = 0;
            RaycastHit first = default;
            if (glassObjects.Length < maxDepth)
            {
                Array.Resize(ref glassObjects, maxDepth);
                Array.Resize(ref glassLayers, maxDepth);
                Array.Resize(ref glassDistances, maxDepth);
            }
            try
            {
                for (; distance >= 0f && depth < maxDepth; depth++)
                {
                    if (!Physics.Raycast(ray, out var current, distance, mask)) break;
                    BallisticCollider c = null;
                    if (!(current.collider is TerrainCollider))
                    {
                        current.collider.TryGetComponent<BallisticCollider>(out c);
                        if (c == null) c = current.collider?.transform?.parent?.GetComponent<BallisticCollider>();
                    }
                    if (c == null || (c.TypeOfMaterial != MaterialType.Glass && c.TypeOfMaterial != MaterialType.GlassShattered))
                    {
                        hit = count == 0 ? current : first;
                        float totalDistance = current.distance;
                        for (int i = count - 1; i >= 0; i--) totalDistance = glassDistances[i] + totalDistance;
                        hit.distance = totalDistance;
                        return true;
                    }
                    if (count == 0) first = current;
                    var obj = current.collider.gameObject;
                    glassObjects[count] = obj;
                    glassLayers[count] = obj.layer;
                    glassDistances[count++] = current.distance + 0.1f;
                    obj.layer = IgnoreRaycastLayer;
                    float advance = current.distance + 0.1f;
                    distance -= advance;
                    ray.origin = current.point + ray.direction.normalized * 0.1f;
                }
                hit = first;
                if (count > 0) hit.distance = first.distance + 0.1f;
                return false;
            }
            finally
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    if (glassObjects[i]) glassObjects[i].layer = glassLayers[i];
                    glassObjects[i] = null;
                }
            }
        }

        /// <summary>
        /// This get called every frame. It performs bubble sort on the "foliage" array by the element's dis (float).
        /// The sort is sliced by frame, and only "step" steps are performed every frame.
        /// if any swap is performed in steps, set the local sorted bool to false;
        /// </summary>
        /// <param name="step">Steps to perform this frame</param>
        private bool SlicedBubbleSort<T>(T[] subject, int step, int valid) where T : IComparable<T>
        {
            if (valid <= 1 || step <= 0) return true;
            int right = Mathf.Min(valid, subject.Length) - 1;
            if (right <= 0) return true;
            while (step > 0)
            {
                bool sorted = true;
                for (int i = 0; i < right; i++)
                {
                    if (subject[i].CompareTo(subject[i + 1]) > 0)
                    {
                        var temp = subject[i];
                        subject[i] = subject[i + 1];
                        subject[i + 1] = temp;
                        sorted = false;
                    }
                    if (--step == 0) return sorted && i == right - 1;
                }
                if (sorted) return true;
            }
            return false;
        }

        void HandleConfigEvents (bool enable)
        {
            if (enable)
                ThatsLitPlugin.DebugTexture.SettingChanged += HandleDebugTextureSettingChanged;
            else
                ThatsLitPlugin.DebugTexture.SettingChanged -= HandleDebugTextureSettingChanged;
        }

        private void HandleDebugTextureSettingChanged(object sender, EventArgs e)
        {
            if (!ThatsLitPlugin.DebugTexture.Value)
            {
                if (display) display.enabled = false;
                return;
            }
            if (MonoBehaviourSingleton<GameUI>.Instance == null) return;
            if (slowRT == null)
                slowRT = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32, false);
            if (display == null)
            {
                display = new GameObject().AddComponent<RawImage>();
                display.transform.SetParent(MonoBehaviourSingleton<GameUI>.Instance.RectTransform());
                display.RectTransform().sizeDelta = new Vector2(160, 160);
                display.texture = slowRT;
                display.RectTransform().anchoredPosition = new Vector2(-720, -360);
            }
            
            display.enabled = ThatsLitPlugin.DebugTexture.Value;
        }

        private void OnDestroy()
        {
            _destroyed = true;
            _readbackGeneration++;
            if (!gquReq.done) gquReq.WaitForCompletion();
            // Always release the persistent pixel-count buffers (a proxy-requested profile survives DisableBrightness()).
            DisposeObserved();
            PlayerLitScoreProfile?.DisposeBuffers();
            DisableBrightness();
            if (display) GameObject.Destroy(display.gameObject);
            if (cam) GameObject.Destroy(cam.gameObject);
            if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            if (slowRT) { UnityEngine.Object.Destroy(slowRT); slowRT = null; }
            if (gameworld?.AllThatsLitPlayers != null && Player != null) gameworld.AllThatsLitPlayers.Remove(Player);
        }
        float litFactorSample, ambScoreSample;
        static float benchmarkSampleSeenCoef, benchmarkSampleEncountering, benchmarkSampleExtraVisDis, benchmarkSampleScoreCalculator, benchmarkSampleUpdate, benchmarkSampleFUpdate, benchmarkSampleFoliageCheck, benchmarkSampleTerrainCheck, benchmarkSampleGUI, benchmarkSampleNoBushOverride, benchmarkSampleBlindFire;
        int guiFrame;
        string infoCache1, infoCache2, infoCacheBenchmark;

        void OnGUIInfo ()
        {
            if (ThatsLitPlugin.ScoreInfo.Value
             || ThatsLitPlugin.WeatherInfo.Value
             || ThatsLitPlugin.EquipmentInfo.Value
             || ThatsLitPlugin.TerrainInfo.Value
             || ThatsLitPlugin.FoliageInfo.Value)
            {
                switch (ThatsLitPlugin.InfoOffset.Value)
                {
                    case 1:
                        GUILayout.Label("\n\n\n\n", style);
                        break;
                    case 2:
                        GUILayout.Label("\n\n\n\n\n\n\n\n", style);
                        break;
                    case 3:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 4:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 5:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 6:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                    case 7:
                        GUILayout.Label("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", style);
                        break;
                }
            }
            if (ThatsLitPlugin.ScoreInfo.Value
             && gameworld.ScoreCalculator != null
             && Time.time < setupTime + 10)
                GUILayout.Label("  [That's Lit] The HUD can be configured in plugin settings.", style);

            if (ThatsLitPlugin.ScoreInfo.Value && PlayerLitScoreProfile != null)
            {
                Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.multiFrameLitScore / 0.0999f), false, style);
                Utility.GUILayoutDrawAsymetricMeter((int)(Mathf.Pow(PlayerLitScoreProfile.frame0.multiFrameLitScore, POWER) / 0.0999f), false, style);
            }

            if (ThatsLitPlugin.EquipmentInfo.Value && LightAndLaserState.storage != 0)
                GUILayout.Label(LightAndLaserState.Format(), style);

            if (ThatsLitPlugin.FoliageInfo.Value
             && Foliage != null
             && Foliage.FoliageScore > 0)
                Utility.GUILayoutFoliageMeter((int)(Foliage.FoliageScore / 0.0999f), false, style);
            if (ThatsLitPlugin.TerrainInfo.Value && TerrainDetails != null && terrainScoreHintProne > 0.0998f)
                if (Player.IsInPronePose)
                    Utility.GUILayoutTerrainMeter((int)(terrainScoreHintProne / 0.0999f), false, style);
                else
                    Utility.GUILayoutTerrainMeter((int)(terrainScoreHintRegular / 0.0999f), false, style);

            if (ThatsLitPlugin.WeatherInfo.Value && PlayerLitScoreProfile != null)
            {
                if (cloud <= -1.1f)
                    GUILayout.Label("  CLEAR ☀☀☀", style);
                else if (cloud <= -0.7f)
                    GUILayout.Label("  CLEAR ☀☀", style);
                else if (cloud <= -0.25f)
                    GUILayout.Label("  CLEAR ☀", style);
                else if (cloud >= 1.1f)
                    GUILayout.Label("  CLOUDY ☁☁☁", style);
                else if (cloud >= 0.7f)
                    GUILayout.Label("  CLOUDY ☁☁", style);
                else if (cloud >= 0.25f)
                    GUILayout.Label("  CLOUDY ☁", style);
            }
        }

        GUIStyle style;

        private void OnGUI()
        {
            if (Player?.IsYourPlayer != true)
                return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                if (ThatsLitPlugin.InfoFontSizeOverride.Value != 0) style.fontSize = ThatsLitPlugin.InfoFontSizeOverride.Value;
            }
            if (style.fontSize != ThatsLitPlugin.InfoFontSizeOverride.Value)
            {
                if (ThatsLitPlugin.InfoFontSizeOverride.Value != 0)
                    style.fontSize = ThatsLitPlugin.InfoFontSizeOverride.Value;
                else
                    style.fontSize = GUI.skin.label.fontSize;
            }

            bool layoutCall = guiFrame < Time.frameCount;
            ThatsLitPlugin.swGUI.MaybeResume();
            if (PlayerLitScoreProfile == null)
            {
                if (Time.time - setupTime < 30f && !ThatsLitPlugin.HideMapTip.Value)
                    GUILayout.Label("  [That's Lit] Brightness module is disabled in configs or not supported on this map.", style);
            }

            var poseFactor = Player.PoseLevel / (Player.Physical != null && Player.Physical.MaxPoseLevel > 0f ? Player.Physical.MaxPoseLevel : 1f) * 0.6f + 0.4f; // crouch: 0.4f
            if (Player.IsInPronePose)
                poseFactor -= 0.4f; // prone: 0
            poseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f

            OnGUIInfo();
            ThatsLitAPI.OnMainPlayerGUI?.Invoke();

            if (!ThatsLitPlugin.DebugInfo.Value || DebugInfo == null)
            {
                ThatsLitPlugin.swGUI.Stop();
                guiFrame = Time.frameCount;
                return;
            }

            float fog = WeatherController.Instance?.WeatherCurve?.Fog ?? 0;
            float rain = WeatherController.Instance?.WeatherCurve?.Rain ?? 0;
            float cloud = WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0;
            
            if (layoutCall)
                infoCache1 = $"  IMPACT: {DebugInfo.lastCalcFrom:0.000} -> {DebugInfo.lastCalcTo0:0.000} -> {DebugInfo.lastCalcTo1:0.000} -> {DebugInfo.lastCalcTo2:0.000} -> {DebugInfo.lastCalcTo3:0.000} -> {DebugInfo.lastCalcTo4:0.000} -> {DebugInfo.lastCalcTo5:0.000} -> {DebugInfo.lastCalcTo6:0.000} -> {DebugInfo.lastCalcTo7:0.000} -> {DebugInfo.lastCalcTo8:0.000}\n ({DebugInfo.lastFactor2:0.000} <- {DebugInfo.lastFactor1:0.000} <- {DebugInfo.lastScore:0.000}) AMB: {ambScoreSample:0.00} LIT: {litFactorSample:0.00} (SAMPLE)\n  AFFECTED: {DebugInfo.calced} (+{DebugInfo.calcedLastFrame})\n  ENCOUNTER: {DebugInfo.encounter}  V.HINT: { DebugInfo.vagueHint }  V.CANCEL: { DebugInfo.vagueHintCancel }  SIG.D: { DebugInfo.signalDanger }\n  LAST SHOT: { lastShotVector } { Time.time - lastShotTime:0.0}s { DebugInfo.lastEncounterShotAngleDelta }deg  -{ DebugInfo.lastEncounteringShotCutoff }x\n  LAST_PARTS: { DebugInfo.lastVisiblePartsFactor} (x9)  G.OVL: { DebugInfo.lastGlobalOverlookChance:P1}\n  V.DIS.COMP: { DebugInfo.lastDisCompThermal }(T)  { DebugInfo.lastDisCompNVG }(NVG)  { DebugInfo.lastDisCompDay }(Day)  { DebugInfo.lastDisComp }  Focus: { DebugInfo.lastNearestFocusAngleX:0.0}degX/{ DebugInfo.lastNearestFocusAngleY:0.0}degY\n  TERRAIN: { terrainScoreHintProne :0.000}/{ terrainScoreHintRegular :0.000}  3x3/5x5: { TerrainDetails?.RecentDetailCount3x3 }/{ TerrainDetails?.RecentDetailCount5x5 } (score-{ PlayerLitScoreProfile?.detailBonusSmooth:0.00})  FOLIAGE: {Foliage?.FoliageScore:0.000} ({Foliage?.FoliageCount}) (H{Foliage?.Nearest?.dis:0.00} to {Foliage?.Nearest?.name}) RAT: { DebugInfo.lastBushRat:0.00}\n  FOG: {fog:0.000} / RAIN: {rain:0.000} / CLOUD: {cloud:0.000} / TIME: {Utility.GetInGameDayTime():0.000} / WINTER: {gameworld.IsWinter}\n  POSE: {poseFactor} SPEED: { Player.Velocity.magnitude :0.000}  INSIDE: { Time.time - lastOutside:0.000}  AMB: { ambienceShadownRating:0.000}  OVH: { overheadHaxRating:0.000}  BNKR: { bunkerTimeClamped:0.000}\n  {DebugInfo.nearestOffset}  Caution: { DebugInfo.nearestCaution }  NoBush.Cancel: {DebugInfo.cancelledSAINNoBush}/{DebugInfo.attemptToCancelSAINNoBush} {DebugInfo.lastInterruptChance:P1}  {DebugInfo.lastInterruptChanceDis:0.0}m SNP: {DebugInfo.sniperHintOffset} ({DebugInfo.sniperHintChance:P1})  FL: {flashLightHit.point:000.0}  HINT:{DebugInfo.flashLightHint}\n  F.L:{DebugInfo.forceLooks}  S.L:{DebugInfo.sideLooks}\n Throttle: { cam?.enabled != true } { cameraThrottleFrequency }";
            GUILayout.Label(infoCache1, style);
            // GUILayout.Label(string.Format(" FOG: {0:0.000} / RAIN: {1:0.000} / CLOUD: {2:0.000} / TIME: {3:0.000} / WINTER: {4}", WeatherController.Instance?.WeatherCurve?.Fog ?? 0, WeatherController.Instance?.WeatherCurve?.Rain ?? 0, WeatherController.Instance?.WeatherCurve?.Cloudiness ?? 0, GetInGameDayTime(), isWinterCache));
            
            OnGUIScoreCalc(style);

            if (IsDebugSampleFrame)
            {
                litFactorSample = PlayerLitScoreProfile?.litScoreFactor ?? 0;
                ambScoreSample = PlayerLitScoreProfile?.frame0.ambienceScore ?? 0;
            }
            if (Time.frameCount % DEBUG_INTERVAL == 1 && ThatsLitPlugin.EnableBenchmark.Value && layoutCall) // The trap here is OnGUI is called multiple times per frame, make sure to reset the stopwatches only once
            {
                ConcludeBenchmarks();
            }

            if (ThatsLitPlugin.EnableBenchmark.Value)
            {
                if (layoutCall)
                    infoCacheBenchmark = $"  Update:         {benchmarkSampleUpdate,8:0.0000}\n  FUpdate:         {benchmarkSampleFUpdate,8:0.0000}\n  Foliage:        {benchmarkSampleFoliageCheck,8:0.0000}\n  Terrain:        {benchmarkSampleTerrainCheck,8:0.0000}\n  SeenCoef:       {benchmarkSampleSeenCoef,8:0.0000}\n  Encountering:   {benchmarkSampleEncountering,8:0.0000}\n  ExtraVisDis:    {benchmarkSampleExtraVisDis,8:0.0000}\n  ScoreCalculator:{benchmarkSampleScoreCalculator,8:0.0000}\n  Info(+Debug):    {benchmarkSampleGUI,8:0.0000}\n  No Bush OVR:    {benchmarkSampleNoBushOverride,8:0.0000}\n  BlindFire:    {benchmarkSampleBlindFire,8:0.0000} ms";
                GUILayout.Label(infoCacheBenchmark, style);
                if (Time.frameCount % 6000 == 0)
                    if (layoutCall) EFT.UI.ConsoleScreen.Log(infoCacheBenchmark);
            }

            gameworld.ScoreCalculator?.OnGUI(PlayerLitScoreProfile, layoutCall);

            if (ThatsLitPlugin.DebugTerrain.Value && TerrainDetails?.Details5x5 != null && TerrainDetails.DetailTypeStride > 0)
            {
                if (layoutCall) infoCache2 = $"  DETAIL (SAMPLE): {DebugInfo?.lastFinalDetailScoreNearest:+0.00;-0.00;+0.00} ({DebugInfo?.lastDisFactorNearest:0.000}df) 3x3: { TerrainDetails.RecentDetailCount3x3}\n  {Utility.DetermineDir(DebugInfo?.lastTriggeredDetailCoverDirNearest ?? Vector3.zero)} {DebugInfo?.lastNearest:0.00}m {DebugInfo?.lastTiltAngle} {DebugInfo?.lastRotateAngle}";
                GUILayout.Label(infoCache2, style);
                // GUILayout.Label(string.Format(" DETAIL (SAMPLE): {0:+0.00;-0.00;+0.00} ({1:0.000}df) 3x3: {2}", arg0: lastFinalDetailScoreNearest, lastDisFactorNearest, recentDetailCount3x3));
                // GUILayout.Label(string.Format(" {0} {1:0.00}m {2} {3}", Utility.DetermineDir(lastTriggeredDetailCoverDirNearest), lastNearest, lastTiltAngle, lastRotateAngle));
                for (int i = TerrainDetails.GetDetailInfoIndex(2, 2, 0, TerrainDetails.DetailTypeStride); i < TerrainDetails.GetDetailInfoIndex(3, 2, 0, TerrainDetails.DetailTypeStride); i++) // List the underfoot
                    if (TerrainDetails.Details5x5[i].casted)
                        GUILayout.Label($"  { TerrainDetails.Details5x5[i].count } Detail#{i}({ TerrainDetails.Details5x5[i].name }))", style);
                Utility.GUILayoutDrawAsymetricMeter((int)(DebugInfo.lastFinalDetailScoreNearest / 0.0999f), false, style);
            }

            ThatsLitPlugin.swGUI.Stop();
            guiFrame = Time.frameCount;
            
        }
        string infoCache;
        internal virtual void OnGUIScoreCalc (GUIStyle style)
        {
            if (PlayerLitScoreProfile == null || DebugInfo == null) return;
            if (PlayerLitScoreProfile.IsProxy)
            {
                GUILayout.Label("  [PROXY]", style);
                return;
            }
            if (ThatsLitPlayer.IsDebugSampleFrame)
            {
                DebugInfo.shinePixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioShinePixels + PlayerLitScoreProfile.frame1.RatioShinePixels + PlayerLitScoreProfile.frame2.RatioShinePixels + PlayerLitScoreProfile.frame3.RatioShinePixels + PlayerLitScoreProfile.frame4.RatioShinePixels + PlayerLitScoreProfile.frame5.RatioShinePixels) / 6f;
                DebugInfo.highLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioHighPixels + PlayerLitScoreProfile.frame1.RatioHighPixels + PlayerLitScoreProfile.frame2.RatioHighPixels + PlayerLitScoreProfile.frame3.RatioHighPixels + PlayerLitScoreProfile.frame4.RatioHighPixels + PlayerLitScoreProfile.frame5.RatioHighPixels) / 6f;
                DebugInfo.highMidLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioHighMidPixels + PlayerLitScoreProfile.frame1.RatioHighMidPixels + PlayerLitScoreProfile.frame2.RatioHighMidPixels + PlayerLitScoreProfile.frame3.RatioHighMidPixels + PlayerLitScoreProfile.frame4.RatioHighMidPixels + PlayerLitScoreProfile.frame5.RatioHighMidPixels) / 6f;
                DebugInfo.midLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioMidPixels + PlayerLitScoreProfile.frame1.RatioMidPixels + PlayerLitScoreProfile.frame2.RatioMidPixels + PlayerLitScoreProfile.frame3.RatioMidPixels + PlayerLitScoreProfile.frame4.RatioMidPixels + PlayerLitScoreProfile.frame5.RatioMidPixels) / 6f;
                DebugInfo.midLowLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioMidLowPixels + PlayerLitScoreProfile.frame1.RatioMidLowPixels + PlayerLitScoreProfile.frame2.RatioMidLowPixels + PlayerLitScoreProfile.frame3.RatioMidLowPixels + PlayerLitScoreProfile.frame4.RatioMidLowPixels + PlayerLitScoreProfile.frame5.RatioMidLowPixels) / 6f;
                DebugInfo.lowLightPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioLowPixels + PlayerLitScoreProfile.frame1.RatioLowPixels + PlayerLitScoreProfile.frame2.RatioLowPixels + PlayerLitScoreProfile.frame3.RatioLowPixels + PlayerLitScoreProfile.frame4.RatioLowPixels + PlayerLitScoreProfile.frame5.RatioLowPixels) / 6f;
                DebugInfo.darkPixelsRatioSample = (PlayerLitScoreProfile.frame0.RatioDarkPixels + PlayerLitScoreProfile.frame1.RatioDarkPixels + PlayerLitScoreProfile.frame2.RatioDarkPixels + PlayerLitScoreProfile.frame3.RatioDarkPixels + PlayerLitScoreProfile.frame4.RatioDarkPixels + PlayerLitScoreProfile.frame5.RatioDarkPixels) / 6f;
            }
            if (guiFrame < Time.frameCount) infoCache = $"  PIXELS: {DebugInfo.shinePixelsRatioSample * 100:000}% - {DebugInfo.highLightPixelsRatioSample * 100:000}% - {DebugInfo.highMidLightPixelsRatioSample * 100:000}% - { DebugInfo.midLightPixelsRatioSample * 100:000}% - {DebugInfo.midLowLightPixelsRatioSample * 100:000}% - {DebugInfo.lowLightPixelsRatioSample * 100:000}% | {DebugInfo.darkPixelsRatioSample * 100:000}% (AVG Sample)\n  AvgLum: {PlayerLitScoreProfile.frame0.avgLum:0.000}  AvgLumMF: {PlayerLitScoreProfile.frame0.avgLumMultiFrames:0.000} / {gameworld.ScoreCalculator.GetMinAmbianceLum():0.000} ~ {gameworld?.ScoreCalculator?.GetMaxAmbianceLum():0.000} ({gameworld?.ScoreCalculator?.GetAmbianceLumRange():0.000})\n   Sun: {gameworld?.ScoreCalculator?.sunLightScore:0.000}/{gameworld?.ScoreCalculator?.GetMaxSunlightScore():0.000}, Moon: {gameworld?.ScoreCalculator?.moonLightScore:0.000}/{gameworld?.ScoreCalculator?.GetMaxMoonlightScore():0.000}\n  SCORE : {DebugInfo.scoreRawBase:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw0:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw1:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw2:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw3:＋0.00;－0.00;+0.00} -> {DebugInfo.scoreRaw4:＋0.00;－0.00;+0.00} (SAMPLE)";            
            GUILayout.Label(infoCache, style);

            Utility.GUILayoutDrawAsymetricMeter((int)(PlayerLitScoreProfile.frame0.score / 0.0999f), false, style);
        }

        private void ConcludeBenchmarks()
        {
            benchmarkSampleSeenCoef         = ThatsLitPlugin.swSeenCoef.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleEncountering     = ThatsLitPlugin.swEncountering.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleExtraVisDis      = ThatsLitPlugin.swExtraVisDis.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleScoreCalculator  = ThatsLitPlugin.swScoreCalc.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleUpdate           = ThatsLitPlugin.swUpdate.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleFUpdate           = ThatsLitPlugin.swFUpdate.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleGUI              = ThatsLitPlugin.swGUI.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleFoliageCheck     = ThatsLitPlugin.swFoliage.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleTerrainCheck     = ThatsLitPlugin.swTerrain.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleNoBushOverride              = ThatsLitPlugin.swNoBushOverride.ConcludeMs() / (float) DEBUG_INTERVAL;
            benchmarkSampleBlindFire              = ThatsLitPlugin.swBlindFireScatter.ConcludeMs() / (float) DEBUG_INTERVAL;
        }
    }
}