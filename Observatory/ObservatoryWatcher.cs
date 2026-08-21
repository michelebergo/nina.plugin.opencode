using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyDome;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Equipment.MyWeatherData;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NINA.Plugin.OpenCode.Observatory {

    /// <summary>
    /// Watches the observatory and writes down what changes. It does not act, it is not asked
    /// anything, and it never calls a model.
    ///
    /// This is the first half of unattended operation, and deliberately the half with no
    /// authority. The events an autonomous agent would react to - star lost, device gone,
    /// weather turned unsafe, a flip finished - are not events N.I.N.A. raises: they are
    /// transitions between states it reports continuously. Which of them actually happen at a
    /// given observatory, and how often, is a question about that observatory, not about
    /// software, so it is answered by recording a few nights rather than by writing a list of
    /// events that sounds right.
    ///
    /// Two rules keep the recording honest. Properties are read by reflection rather than
    /// named, so nothing is missed because it did not occur to whoever wrote this; and only
    /// state-like properties - booleans, enumerations, strings - are diffed, because a
    /// coordinate that changes on every update is not a transition, it is tracking. The
    /// numbers are captured once per saved frame instead, which is where they belong: that is
    /// the moment a decision about the session would be taken.
    /// </summary>
    internal sealed class ObservatoryWatcher : ICameraConsumer, ITelescopeConsumer, IGuiderConsumer,
                                               IFocuserConsumer, IDomeConsumer, ISafetyMonitorConsumer,
                                               IWeatherDataConsumer {

        private readonly ObservatoryLog log;

        private readonly ICameraMediator? camera;
        private readonly ITelescopeMediator? telescope;
        private readonly IGuiderMediator? guider;
        private readonly IFocuserMediator? focuser;
        private readonly IDomeMediator? dome;
        private readonly ISafetyMonitorMediator? safetyMonitor;
        private readonly IWeatherDataMediator? weather;
        private readonly IImageSaveMediator? imageSave;

        /// <summary>The last state seen per device, and the last full info object per device.</summary>
        private readonly Dictionary<string, Dictionary<string, string?>> lastState = new();
        private readonly Dictionary<string, object> lastInfo = new();
        private readonly object gate = new object();

        private bool started;

        public ObservatoryWatcher(ObservatoryLog log,
                                  ICameraMediator? camera, ITelescopeMediator? telescope,
                                  IGuiderMediator? guider, IFocuserMediator? focuser,
                                  IDomeMediator? dome, ISafetyMonitorMediator? safetyMonitor,
                                  IWeatherDataMediator? weather, IImageSaveMediator? imageSave) {
            this.log = log;
            this.camera = camera;
            this.telescope = telescope;
            this.guider = guider;
            this.focuser = focuser;
            this.dome = dome;
            this.safetyMonitor = safetyMonitor;
            this.weather = weather;
            this.imageSave = imageSave;
        }

        /// <summary>
        /// Subscribes to everything that was supplied. Each mediator is optional: a plugin
        /// that fails to load because one export was unavailable would cost the user the whole
        /// panel in exchange for a log file, which is not a trade worth making.
        /// </summary>
        public void Start() {
            if (started) { return; }
            started = true;

            Try(() => camera?.RegisterConsumer(this), nameof(camera));
            Try(() => telescope?.RegisterConsumer(this), nameof(telescope));
            Try(() => guider?.RegisterConsumer(this), nameof(guider));
            Try(() => focuser?.RegisterConsumer(this), nameof(focuser));
            Try(() => dome?.RegisterConsumer(this), nameof(dome));
            Try(() => safetyMonitor?.RegisterConsumer(this), nameof(safetyMonitor));
            Try(() => weather?.RegisterConsumer(this), nameof(weather));
            if (imageSave != null) { imageSave.ImageSaved += OnImageSaved; }

            log.Write("recording-started", new Dictionary<string, object?>());
            Logger.Info($"OpenCode: recording observatory transitions to {log.PathForTonight}");
        }

        public void Stop() {
            if (!started) { return; }
            started = false;

            Try(() => camera?.RemoveConsumer(this), nameof(camera));
            Try(() => telescope?.RemoveConsumer(this), nameof(telescope));
            Try(() => guider?.RemoveConsumer(this), nameof(guider));
            Try(() => focuser?.RemoveConsumer(this), nameof(focuser));
            Try(() => dome?.RemoveConsumer(this), nameof(dome));
            Try(() => safetyMonitor?.RemoveConsumer(this), nameof(safetyMonitor));
            Try(() => weather?.RemoveConsumer(this), nameof(weather));
            if (imageSave != null) { imageSave.ImageSaved -= OnImageSaved; }

            log.Write("recording-stopped", new Dictionary<string, object?>());
        }

        private static void Try(Action action, string what) {
            try {
                action();
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: could not watch the {what} ({ex.Message})");
            }
        }

        #region Device updates

        public void UpdateDeviceInfo(CameraInfo deviceInfo) => Observe("camera", deviceInfo);
        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) => Observe("mount", deviceInfo);
        public void UpdateDeviceInfo(GuiderInfo deviceInfo) => Observe("guider", deviceInfo);
        public void UpdateDeviceInfo(FocuserInfo deviceInfo) => Observe("focuser", deviceInfo);
        public void UpdateDeviceInfo(DomeInfo deviceInfo) => Observe("dome", deviceInfo);
        public void UpdateDeviceInfo(SafetyMonitorInfo deviceInfo) => Observe("safety", deviceInfo);
        public void UpdateDeviceInfo(WeatherDataInfo deviceInfo) => Observe("weather", deviceInfo);

        /// <summary>
        /// The one real event in this whole surface rather than a state to be diffed:
        /// N.I.N.A. says outright when an autofocus run has finished. Worth its own line -
        /// "focus was corrected at 02:14" is exactly the kind of fact that explains an HFR
        /// that improved without anyone touching anything.
        /// </summary>
        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            var record = new Dictionary<string, object?>();
            if (info != null) {
                foreach (var property in NumericOrTextPropertiesOf(info.GetType())) {
                    var value = ReadAsValue(property, info);
                    if (value != null) { record[property.Name] = value; }
                }
            }
            log.Write("autofocus-finished", record);
        }

        /// <summary>Focusing done by hand is not a transition worth a line of its own.</summary>
        public void UpdateUserFocused(FocuserInfo info) { }

        /// <summary>
        /// Diffs the state-like properties of one device against what it reported last time,
        /// and writes a single line naming everything that changed. One line per update rather
        /// than one per property: a device going offline resets a dozen fields at once, and
        /// twelve lines would make one event look like twelve.
        /// </summary>
        private void Observe(string device, object? info) {
            if (info == null) { return; }

            Dictionary<string, object?>? changes = null;
            bool baseline;

            lock (gate) {
                lastInfo[device] = info;

                // Whether this is the first sight of the device is decided once, here, and not
                // per property: the dictionary is filled by the loop below, so asking it
                // mid-loop says "empty" for the first property and "not empty" for every one
                // after it. That read the opening state of every device as a burst of
                // transitions out of nothing, which is exactly the noise this class exists to
                // avoid - and it was invisible until the first reading was actually looked at.
                baseline = !lastState.TryGetValue(device, out var previous);
                if (baseline) {
                    previous = new Dictionary<string, string?>();
                    lastState[device] = previous;
                }

                foreach (var property in StatePropertiesOf(info.GetType())) {
                    var value = ReadAsText(property, info);
                    if (baseline) {
                        changes ??= new Dictionary<string, object?>();
                        changes[property.Name] = value;
                    } else if (!previous!.TryGetValue(property.Name, out var before) || before != value) {
                        changes ??= new Dictionary<string, object?>();
                        changes[property.Name] = new[] { before, value };
                    }
                    previous![property.Name] = value;
                }
            }

            if (changes == null) { return; }
            changes["device"] = device;

            // The opening state is worth recording - it says what the night started from - but
            // it is not a transition, and calling it one would make every startup look like an
            // event storm to anything counting them later.
            log.Write(baseline ? "state" : "transition", changes);
        }

        #endregion

        #region Frames

        /// <summary>
        /// A saved frame is the natural heartbeat of a night: it is when the numbers that
        /// describe how well things are going become available, and it is when an unattended
        /// decision would sensibly be taken. The frame's own measurements are recorded together
        /// with a numeric snapshot of every device, which is the raw material of the context
        /// packet a future agent turn would be given.
        /// </summary>
        private void OnImageSaved(object sender, ImageSavedEventArgs e) {
            try {
                var record = new Dictionary<string, object?>();
                foreach (var property in NumericOrTextPropertiesOf(e.GetType())) {
                    var value = ReadAsValue(property, e);
                    if (value != null) { record["frame." + property.Name] = value; }
                }

                lock (gate) {
                    foreach (var pair in lastInfo) {
                        foreach (var property in NumericPropertiesOf(pair.Value.GetType())) {
                            var value = ReadAsValue(property, pair.Value);
                            if (value != null) { record[pair.Key + "." + property.Name] = value; }
                        }
                    }
                }

                log.Write("frame", record);
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: could not record a frame ({ex.Message})");
            }
        }

        #endregion

        #region Reflection

        private static readonly Dictionary<Type, PropertyInfo[]> stateCache = new();
        private static readonly Dictionary<Type, PropertyInfo[]> numericCache = new();
        private static readonly Dictionary<Type, PropertyInfo[]> frameCache = new();

        /// <summary>
        /// Booleans, enumerations and short strings: the properties that describe what a
        /// device is doing rather than where it is pointing. These are what transitions are
        /// made of - Connected, Tracking, AtPark, IsSafe, ShutterStatus, SideOfPier.
        /// </summary>
        private static PropertyInfo[] StatePropertiesOf(Type type) => Cached(stateCache, type, p => {
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            return t == typeof(bool) || t.IsEnum || t == typeof(string);
        });

        private static PropertyInfo[] NumericPropertiesOf(Type type) => Cached(numericCache, type, p => IsNumeric(p.PropertyType));

        private static PropertyInfo[] NumericOrTextPropertiesOf(Type type) => Cached(frameCache, type, p => {
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            return IsNumeric(p.PropertyType) || t == typeof(string) || t == typeof(bool) || t.IsEnum;
        });

        private static bool IsNumeric(Type type) {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t == typeof(double) || t == typeof(float) || t == typeof(int)
                || t == typeof(short) || t == typeof(long) || t == typeof(decimal);
        }

        private static PropertyInfo[] Cached(Dictionary<Type, PropertyInfo[]> cache, Type type, Func<PropertyInfo, bool> keep) {
            lock (cache) {
                if (cache.TryGetValue(type, out var cached)) { return cached; }
                var selected = new List<PropertyInfo>();
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0) { continue; }
                    if (keep(property)) { selected.Add(property); }
                }
                var result = selected.ToArray();
                cache[type] = result;
                return result;
            }
        }

        /// <summary>
        /// A property getter on a disconnected device can throw, and one that does must not
        /// stop the rest of the snapshot from being recorded.
        /// </summary>
        private static string? ReadAsText(PropertyInfo property, object instance) {
            try {
                return property.GetValue(instance)?.ToString();
            } catch (Exception) {
                return null;
            }
        }

        private static object? ReadAsValue(PropertyInfo property, object instance) {
            try {
                var value = property.GetValue(instance);
                if (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) { return null; }
                if (value is float f && (float.IsNaN(f) || float.IsInfinity(f))) { return null; }
                return value is Enum ? value.ToString() : value;
            } catch (Exception) {
                return null;
            }
        }

        #endregion

        public void Dispose() => Stop();
    }
}
