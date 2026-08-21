using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.OpenCode.OpenCode {

    /// <summary>
    /// Asks an Ollama host which models it is serving.
    ///
    /// Ollama speaks two dialects on the same port: its own API at /api, and an
    /// OpenAI-compatible one at /v1. The list lives on the first and opencode talks to the
    /// second, which is why the host is stored without a suffix and each caller appends its
    /// own.
    /// </summary>
    internal static class LocalModels {

        /// <summary>Why the last listing failed, for a message that names the cause.</summary>
        public static string? LastError { get; private set; }

        /// <returns>The model names, or null when nothing answered.</returns>
        public static async Task<List<string>?> ListAsync(string host, CancellationToken token) {
            LastError = null;
            if (string.IsNullOrWhiteSpace(host)) {
                LastError = "no host set";
                return null;
            }

            try {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var url = host.Trim().TrimEnd('/') + "/api/tags";
                Logger.Info($"OpenCode: asking {url} for its models");
                var payload = await http.GetStringAsync(url, token).ConfigureAwait(false);
                var listing = JsonConvert.DeserializeObject<TagListing>(payload);
                return listing?.Models?
                    .Select(m => m.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToList() ?? new List<string>();
            } catch (Exception ex) {
                LastError = ex.Message;
                Logger.Warning($"OpenCode: no model list from {host} ({ex.Message})");
                return null;
            }
        }

        private sealed class TagListing {
            [JsonProperty("models")] public List<TagEntry>? Models { get; set; }
        }

        private sealed class TagEntry {
            [JsonProperty("name")] public string? Name { get; set; }
        }
    }
}
