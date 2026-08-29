using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.OpenCode.OpenCode {

    /// <summary>
    /// Talks to an opencode server over its HTTP API. It owns no agent, no model and no
    /// credentials: everything it can do is something the user's own opencode installation
    /// was already configured to do.
    ///
    /// The routes here were read from the server's own OpenAPI document (/doc) rather than
    /// from documentation, so they describe the build actually installed. The v2 routes are
    /// the ones under /api; they answer inside a "data" envelope.
    /// </summary>
    internal sealed class OpenCodeClient : IDisposable {
        private readonly HttpClient http;

        public Uri BaseUrl { get; }

        public OpenCodeClient(Uri baseUrl, string? password, HttpMessageHandler? handler = null) {
            BaseUrl = baseUrl;
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.BaseAddress = baseUrl;
            // Long-running prompts stream for as long as the agent works; the per-request
            // timeout would otherwise cut the answer off mid-thought.
            http.Timeout = Timeout.InfiniteTimeSpan;
            if (!string.IsNullOrEmpty(password)) {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"opencode:{password}"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            }
        }

        /// <summary>
        /// Whether a server is answering at this address. Deliberately short-fused: this is
        /// asked while the user waits for the panel to say connected or not, and a machine
        /// that is off should say so in a second rather than hang the panel.
        /// </summary>
        public async Task<bool> IsHealthyAsync(CancellationToken token) {
            try {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                var health = await GetAsync<HealthResponse>("api/health", cts.Token).ConfigureAwait(false);
                return health?.Healthy == true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>
        /// Opens a session for the interface to land in. Without this the web interface shows
        /// a project picker and an empty session list, and the user has to work out that they
        /// are supposed to create one - which is the plugin asking them to do its job.
        /// </summary>
        /// <summary>The address of a new session, and the working directory the server is in.</summary>
        internal readonly record struct OpenedSession(Uri? Url, string? Directory);

        public async Task<OpenedSession> OpenSessionAsync(string? agent, CancellationToken token) {
            var body = new CreateSessionRequest { Agent = string.IsNullOrWhiteSpace(agent) ? null : agent };
            var created = await PostAsync<SessionInfo>("api/session", body, token).ConfigureAwait(false);
            var id = created?.Id;
            if (string.IsNullOrEmpty(id)) { return default; }

            // The page's route is "/{directory}/session/{id}", and the directory is not a
            // readable path: it is the absolute working directory encoded as base64url,
            // which is what the interface's own link builder does to it. Guessing at it
            // cost an afternoon, because every readable form also returns the page: the
            // single-page app is served for any address the server does not claim, so a
            // wrong one looks like a working one until you notice the session never opens.
            var detail = await GetAsync<SessionInfo>($"session/{Uri.EscapeDataString(id!)}", token)
                .ConfigureAwait(false);
            var directory = detail?.Directory;

            return string.IsNullOrWhiteSpace(directory)
                ? new OpenedSession(null, null)
                : new OpenedSession(new Uri(BaseUrl, $"{EncodeDirectory(directory!)}/session/{id}"), directory);
        }

        /// <summary>base64url, as the interface encodes a directory into its own addresses.</summary>
        private static string EncodeDirectory(string directory)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(directory))
                      .Replace(char.Parse("+"), char.Parse("-"))
                      .Replace(char.Parse("/"), char.Parse("_"))
                      .TrimEnd(char.Parse("="));

        private async Task<T?> GetAsync<T>(string path, CancellationToken token) where T : class {
            using var response = await http.GetAsync(path, token).ConfigureAwait(false);
            return await ReadEnvelopeAsync<T>(response, token).ConfigureAwait(false);
        }

        private async Task<T?> PostAsync<T>(string path, object body, CancellationToken token) where T : class {
            var json = JsonConvert.SerializeObject(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(path, content, token).ConfigureAwait(false);
            return await ReadEnvelopeAsync<T>(response, token).ConfigureAwait(false);
        }

        private static async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken token) where T : class {
            var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                // The body of a failed call is where opencode says what was wrong; dropping
                // it in favour of the status code alone is how "400 Bad Request" becomes an
                // afternoon.
                throw new OpenCodeException($"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(payload)}");
            }
            if (string.IsNullOrWhiteSpace(payload)) { return null; }

            var envelope = JsonConvert.DeserializeObject<Envelope<T>>(payload);
            if (envelope?.Data != null) { return envelope.Data; }

            // The v1 routes answer without the envelope. Falling back rather than failing
            // keeps the client working across both families of route.
            try {
                return JsonConvert.DeserializeObject<T>(payload);
            } catch (JsonException ex) {
                Logger.Debug($"OpenCode: unrecognised response body ({ex.Message})");
                return null;
            }
        }

        private static string Truncate(string value)
            => value.Length <= 400 ? value : value.Substring(0, 400) + "...";

        public void Dispose() => http.Dispose();
    }

    internal sealed class OpenCodeException : Exception {
        public OpenCodeException(string message) : base(message) { }
    }
}
