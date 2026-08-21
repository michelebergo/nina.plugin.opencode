using Newtonsoft.Json;

namespace NINA.Plugin.OpenCode.OpenCode {

    /// <summary>
    /// The v2 API wraps every successful body in a "data" envelope. Modelling it once here
    /// keeps that detail out of the call sites.
    /// </summary>
    internal sealed class Envelope<T> {
        [JsonProperty("data")] public T? Data { get; set; }
    }

    /// <summary>
    /// The whole model surface this plugin needs. It used to describe sessions, messages,
    /// parts, tool states and permissions - everything required to draw the conversation in
    /// WPF - and every one of those shapes was a piece of opencode's API this plugin would
    /// have had to follow forever. Hosting opencode's own interface instead left exactly one
    /// question worth asking over HTTP: is a server there.
    /// </summary>
    /// <summary>Just enough of a session to open the interface on it.</summary>
    internal sealed class SessionInfo {
        [JsonProperty("id")] public string? Id { get; set; }

        /// <summary>
        /// The directory as the interface addresses it: no drive letter, forward slashes -
        /// "Users/miche/Desktop/Nina_opencode". It is the first segment of the page's own
        /// route, and it is read from the server rather than derived from the path on disk so
        /// the plugin is not guessing at somebody else's URL scheme.
        /// </summary>
        [JsonProperty("path")] public string? Path { get; set; }

        /// <summary>
        /// The short name the interface uses in its own links - "witty-lagoon". Preferred
        /// over the path because it is what the page's own link builder uses, and because a
        /// session that sits at the root of its project reports an empty path, which would
        /// otherwise collapse the address back onto an API route.
        /// </summary>
        [JsonProperty("slug")] public string? Slug { get; set; }

        /// <summary>The working directory as the server reports it, absolute.</summary>
        [JsonProperty("directory")] public string? Directory { get; set; }
    }

    internal sealed class CreateSessionRequest {
        [JsonProperty("agent", NullValueHandling = NullValueHandling.Ignore)] public string? Agent { get; set; }
    }

    internal sealed class HealthResponse {
        [JsonProperty("healthy")] public bool Healthy { get; set; }
    }
}
