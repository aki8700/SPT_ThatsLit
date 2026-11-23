using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ThatsLit
{
    public static class ThatsLitCompat
    {
        public const int MIN_COMPATIBLE_PROTOCOL = 1;

        private sealed class CompatSnapshot
        {
            public readonly Dictionary<string, ScopeTemplate> ScopeTemplates;
            public readonly Dictionary<string, Scope> Scopes;
            public readonly Dictionary<string, GoggleTemplate> GoggleTemplates;
            public readonly Dictionary<string, Goggle> Goggles;
            public readonly Dictionary<string, DeviceTemplate> DeviceTemplates;
            public readonly Dictionary<string, Device> Devices;
            public readonly Dictionary<string, Device> ExtraDevices;

            public CompatSnapshot(
                Dictionary<string, ScopeTemplate> scopeTemplates,
                Dictionary<string, Scope> scopes,
                Dictionary<string, GoggleTemplate> goggleTemplates,
                Dictionary<string, Goggle> goggles,
                Dictionary<string, DeviceTemplate> deviceTemplates,
                Dictionary<string, Device> devices,
                Dictionary<string, Device> extraDevices)
            {
                ScopeTemplates = scopeTemplates;
                Scopes = scopes;
                GoggleTemplates = goggleTemplates;
                Goggles = goggles;
                DeviceTemplates = deviceTemplates;
                Devices = devices;
                ExtraDevices = extraDevices;
            }
        }

        private static CompatSnapshot _snapshot = new CompatSnapshot(
            new Dictionary<string, ScopeTemplate>(),
            new Dictionary<string, Scope>(),
            new Dictionary<string, GoggleTemplate>(),
            new Dictionary<string, Goggle>(),
            new Dictionary<string, DeviceTemplate>(),
            new Dictionary<string, Device>(),
            new Dictionary<string, Device>());

        public static Dictionary<string, ScopeTemplate> ScopeTemplates    => Volatile.Read(ref _snapshot).ScopeTemplates;
        public static Dictionary<string, Scope> Scopes                    => Volatile.Read(ref _snapshot).Scopes;
        public static Dictionary<string, GoggleTemplate> GoggleTemplates  => Volatile.Read(ref _snapshot).GoggleTemplates;
        public static Dictionary<string, Goggle> Goggles                  => Volatile.Read(ref _snapshot).Goggles;
        public static Dictionary<string, DeviceTemplate> DeviceTemplates  => Volatile.Read(ref _snapshot).DeviceTemplates;
        public static Dictionary<string, Device> Devices                  => Volatile.Read(ref _snapshot).Devices;
        public static Dictionary<string, Device> ExtraDevices             => Volatile.Read(ref _snapshot).ExtraDevices;


        private static readonly object loadLock = new object();
        static Task running;
        public static Task LoadCompatFiles ()
        {
            lock (loadLock)
            {
                if (running != null && !running.IsCompleted) return running;

                var task = Task.Run(() => {
                    var scopeTemplates   = new Dictionary<string, ScopeTemplate>();
                    var scopes           = new Dictionary<string, Scope>();
                    var goggleTemplates  = new Dictionary<string, GoggleTemplate>();
                    var goggles          = new Dictionary<string, Goggle>();
                    var deviceTemplates  = new Dictionary<string, DeviceTemplate>();
                    var devices          = new Dictionary<string, Device>();
                    var extraDevices     = new Dictionary<string, Device>();

                    List<CompatFile> loaded = new List<CompatFile>();
                    IEnumerable<string> paths = Directory.EnumerateFiles(BepInEx.Paths.PluginPath, "**thatslitcompat.json", SearchOption.AllDirectories);
                    foreach (var path in paths)
                    {
                        CompatFile file;
                        try
                        {
                            file = Newtonsoft.Json.JsonConvert.DeserializeObject<CompatFile>(File.ReadAllText(path));
                        }
                        catch (Exception ex)
                        {
                            var message = $"[That's Lit] Failed to load thatslitcompat file: { path } ({ ex.Message })";
                            Utility.SafeWarningNotification(message);
                            Logger.LogWarning(message);
                            continue;
                        }

                        if (file == null)
                        {
                            var message = $"[That's Lit] Invalid thatslitcompat file: { path }";
                            Utility.SafeWarningNotification(message);
                            Logger.LogWarning(message);
                            continue;
                        }
                        if (file.protocol < MIN_COMPATIBLE_PROTOCOL)
                        {
                            var message = $"[That's Lit] Incompatible outdated thatslitcompat file: { path }, minimum compatible protocol: { MIN_COMPATIBLE_PROTOCOL }";
                            Utility.SafeWarningNotification(message);
                            Logger.LogWarning(message);
                            continue;
                        }
                        file.FilePath = path;
                        loaded.Add(file);
                    }
                    loaded.Sort();

                    // Pass 1: load all templates across all compat files so cross-file references are order-independent
                    foreach (var f in loaded)
                    {
                        if (ThatsLitPlugin.DebugCompat.Value)
                            Logger.LogWarning($"[That's Lit Debug] Loading compat file: { f.FilePath }");

                        if (f.scopeTemplates != null)
                        foreach (var c in f.scopeTemplates)
                        {
                            if (c == null || string.IsNullOrEmpty(c.name)) continue;
                            scopeTemplates[c.name] = c;
                            if (ThatsLitPlugin.DebugCompat.Value)
                                Logger.LogWarning($"[That's Lit Debug] Scope Template: { c.name }");
                        }

                        if (f.goggleTemplates != null)
                        foreach (var c in f.goggleTemplates)
                        {
                            if (c == null || string.IsNullOrEmpty(c.name)) continue;
                            goggleTemplates[c.name] = c;
                            if (ThatsLitPlugin.DebugCompat.Value)
                                Logger.LogWarning($"[That's Lit Debug] Goggle Template: { c.name }");
                        }

                        if (f.deviceTemplates != null)
                        foreach (var c in f.deviceTemplates)
                        {
                            if (c == null || string.IsNullOrEmpty(c.name)) continue;
                            if (c.modes == null || c.modes.Length == 0)
                            {
                                Logger.LogError($"[That's Lit] Device Template: { c.name } have 0 mode defined");
                                // continue; // keep it for references
                            }
                            deviceTemplates[c.name] = c;
                            if (ThatsLitPlugin.DebugCompat.Value)
                                Logger.LogWarning($"[That's Lit Debug] Device Template: { c.name }");
                        }
                    }

                    // Pass 2: validate and register items against the completed template dictionaries
                    foreach (var f in loaded)
                    {
                        if (f.scopes != null)
                        foreach (var c in f.scopes)
                        {
                            if (c == null || string.IsNullOrEmpty(c.id)) continue;
                            c.ResolveTemplate(scopeTemplates);
                            scopes[c.id] = c;
                            if (ThatsLitPlugin.DebugCompat.Value)
                                Logger.LogWarning($"[That's Lit Debug] Scope: { c.id }");
                        }

                        if (f.goggles != null)
                        foreach (var c in f.goggles)
                        {
                            if (c == null || string.IsNullOrEmpty(c.id)) continue;
                            c.ResolveTemplate(goggleTemplates);
                            goggles[c.id] = c;
                            if (ThatsLitPlugin.DebugCompat.Value)
                                Logger.LogWarning($"[That's Lit Debug] Goggle: { c.id }");
                        }

                        if (f.devices != null)
                        foreach (var c in f.devices)
                        {
                            if (c == null || string.IsNullOrEmpty(c.id)) continue;
                            var templateInstance = c.ResolveTemplate(deviceTemplates);
                            if (templateInstance == null)
                            {
                                Logger.LogError($"[That's Lit] Device: { c.id } template is invalid: { c.template }");
                                continue;
                            }
                            if (templateInstance.modes == null || templateInstance.modes.Length == 0)
                            {
                                Logger.LogError($"[That's Lit] Device: { c.id } have 0 mode defined");
                                continue;
                            }

                            devices[c.id] = c;
                            if (ThatsLitPlugin.DebugCompat.Value)
                                Logger.LogWarning($"[That's Lit Debug] Device: { c.id }");
                        }

                        if (f.extraDevices != null)
                        foreach (var c in f.extraDevices)
                        {
                            if (c == null || string.IsNullOrEmpty(c.id)) continue;
                            var templateInstance = c.ResolveTemplate(deviceTemplates);
                            if (templateInstance == null)
                            {
                                Logger.LogError($"[That's Lit] Device: { c.id } template is invalid: { c.template }");
                                continue;
                            }
                            if (templateInstance.modes == null || templateInstance.modes.Length == 0)
                            {
                                Logger.LogError($"[That's Lit] Device: { c.id } have 0 mode defined");
                                continue;
                            }
                            extraDevices[c.id] = c;
                            if (ThatsLitPlugin.DebugCompat.Value)
                                Logger.LogWarning($"[That's Lit Debug] Extra Device: { c.id }");
                        }
                    }

                    Volatile.Write(ref _snapshot, new CompatSnapshot(
                        scopeTemplates,
                        scopes,
                        goggleTemplates,
                        goggles,
                        deviceTemplates,
                        devices,
                        extraDevices));
                }).ContinueWith(t => {
                    if (t.IsFaulted && t.Exception != null) Logger.LogError(t.Exception.Flatten());
                });

                running = task;
                return task;
            }
        }

        [System.Serializable]
        public class Device
        {
            public string id { get; set; }
            public string template { get; set; }
            private DeviceTemplate templateInstance;
            internal DeviceTemplate ResolveTemplate(Dictionary<string, DeviceTemplate> templates)
            {
                if (templateInstance == null && !string.IsNullOrEmpty(template))
                {
                    templates.TryGetValue(template, out templateInstance);
                }
                return templateInstance;
            }
            public DeviceTemplate TemplateInstance => ResolveTemplate(ThatsLitCompat.DeviceTemplates);
        }

        [System.Serializable]
        public class DeviceTemplate
        {
            public string name { get; set; }
            public DeviceMode[] modes { get; set; }
            public DeviceMode SafeGetMode (int mode, bool fallbackLast = true)
            {
                if (modes == null || modes.Length == 0) return default;
                if (mode < 0) return default;
                if (mode >= modes.Length)
                    return fallbackLast ? modes[modes.Length - 1] : default;
                return modes[mode];
            }
        }

        [System.Serializable]
        public class Goggle
        {
            public string id { get; set; }
            public string template { get; set; }
            private GoggleTemplate templateInstance;
            internal GoggleTemplate ResolveTemplate(Dictionary<string, GoggleTemplate> templates)
            {
                if (templateInstance == null && !string.IsNullOrEmpty(template))
                {
                    templates.TryGetValue(template, out templateInstance);
                }
                return templateInstance;
            }
            public GoggleTemplate TemplateInstance => ResolveTemplate(ThatsLitCompat.GoggleTemplates);
        }

        [System.Serializable]
        public class GoggleTemplate
        {
            public string name { get; set; }
            public NightVision nightVision { get; set; }
            public Thermal thermal { get; set; }
        }

        [System.Serializable]
        public struct DeviceMode
        {
            public float light { get; set; }
            public float laser { get; set; }
            public float irLight { get; set; }
            public float irLaser { get; set; }
            public static DeviceMode MergeMax (DeviceMode a, DeviceMode b)
            {
                a.light = a.light > b.light ? a.light : b.light;
                a.laser = a.laser > b.laser ? a.laser : b.laser;
                a.irLight = a.irLight > b.irLight ? a.irLight : b.irLight;
                a.irLaser = a.irLaser > b.irLaser ? a.irLaser : b.irLaser;
                return a;
            }
        }

        [System.Serializable]
        public class NightVision
        {
            public int horizontalFOV { get; set; }
            public int verticalFOV { get; set; }
            public float nullification { get; set; }
            public float nullificationDarker { get; set; }
            public float nullificationExtremeDark { get; set; }
        }

        [System.Serializable]
        public class CompatFile: IComparable<CompatFile>
        {
            public int protocol { get; set; }
            public string FilePath { get; internal set; }
            public int priority { get; set; } = 999;
            public ScopeTemplate[] scopeTemplates { get; set; }
            public Scope[] scopes { get; set; }
            public GoggleTemplate[] goggleTemplates { get; set; }
            public Goggle[] goggles { get; set; }
            public DeviceTemplate[] deviceTemplates { get; set; }
            public Device[] devices { get; set; }
            public Device[] extraDevices { get; set; }

            public int CompareTo(CompatFile other)
            {
                if (other == null) return 1;
                int cmp = priority.CompareTo(other.priority);
                return cmp != 0 ? cmp : string.CompareOrdinal(FilePath, other.FilePath);
            }
        }

        [System.Serializable]
        public class Scope
        {

            public string id { get; set; }
            public string template { get; set; }
            private ScopeTemplate templateInstance;
            internal ScopeTemplate ResolveTemplate(Dictionary<string, ScopeTemplate> templates)
            {
                if (templateInstance == null && !string.IsNullOrEmpty(template))
                {
                    templates.TryGetValue(template, out templateInstance);
                }
                return templateInstance;
            }
            public ScopeTemplate TemplateInstance => ResolveTemplate(ThatsLitCompat.ScopeTemplates);
        }

        [System.Serializable]
        public class ScopeTemplate
        {
            public string name { get; set; }
            public NightVision nightVision { get; set; }
            public Thermal thermal { get; set; }
        }

        [System.Serializable]
        public class Thermal
        {
            public int effectiveDistance { get; set; }
            public int horizontalFOV { get; set; }
            public int verticalFOV { get; set; }
        }


    }
}
