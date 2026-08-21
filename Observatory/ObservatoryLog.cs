using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NINA.Plugin.OpenCode.Observatory {

    /// <summary>
    /// One line of JSON per thing that happened, one file per night.
    ///
    /// This exists to answer a question that cannot be answered by reasoning about it: which
    /// events actually occur at this observatory, and how often. A classifier written against
    /// a plausible list of events is a guess; one written against a night of this file is not.
    /// The format is deliberately dull - JSON Lines - because it has to be readable by a
    /// person, by a script, and by the agent itself, and it has to survive a process that is
    /// killed halfway through the night without taking the earlier lines with it.
    /// </summary>
    internal sealed class ObservatoryLog {

        /// <summary>
        /// A night is not a date: it runs across midnight, and splitting it there would put
        /// one session in two files with the interesting part at the boundary. Everything
        /// before noon belongs to the night that started the previous evening.
        /// </summary>
        private static DateTime NightOf(DateTime local) => local.Hour < 12 ? local.Date.AddDays(-1) : local.Date;

        private readonly string directory;
        private readonly object gate = new object();

        public ObservatoryLog(string directory) {
            this.directory = directory;
        }

        /// <summary>Where the plugin keeps its own files, beside its WebView2 profile.</summary>
        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Plugins", "OpenCode", "observatory");

        public string PathForTonight => Path.Combine(
            directory, "observatory-" + NightOf(DateTime.Now).ToString("yyyy-MM-dd") + ".jsonl");

        /// <summary>
        /// Appends one record. Failures are logged once and then swallowed: a recorder that
        /// takes the session down with it when a disk fills up would be worse than no recorder
        /// at all, and nothing in the observatory depends on this file existing.
        /// </summary>
        public void Write(string kind, IDictionary<string, object?> fields) {
            var record = new Dictionary<string, object?> {
                ["at"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
                ["kind"] = kind
            };
            foreach (var pair in fields) { record[pair.Key] = pair.Value; }

            try {
                lock (gate) {
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(PathForTonight,
                        JsonConvert.SerializeObject(record) + Environment.NewLine, Encoding.UTF8);
                }
                failed = false;
            } catch (Exception ex) {
                if (!failed) {
                    failed = true;
                    Logger.Warning($"OpenCode: cannot write the observatory log ({ex.Message}); recording is paused");
                }
            }
        }

        private bool failed;
    }
}
