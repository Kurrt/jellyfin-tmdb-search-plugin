using System.Reflection;
using System.Text;

namespace Jellyfin.Plugin.TmdbSearch.Web;

/// <summary>
/// Inserts the async stream-loader script into jellyfin-web HTML.
/// </summary>
public static class WebInjection
{
    /// <summary>
    /// Marker id used to detect an already-injected script tag.
    /// </summary>
    public const string ScriptElementId = "tmdbsearch-async-streams";

    /// <summary>
    /// Embedded resource suffix for the client patch.
    /// </summary>
    public const string ScriptResourceSuffix = "Frontend.js.async-stream-loader.js";

    /// <summary>
    /// Payload shape accepted by the File Transformation plugin callback.
    /// </summary>
    public sealed class FileTransformationPayload
    {
        /// <summary>
        /// Gets or sets the current HTML contents of the requested file.
        /// </summary>
        public string? Contents { get; set; }
    }

    /// <summary>
    /// Reads the embedded async stream-loader script from this assembly.
    /// </summary>
    /// <returns>The JavaScript source.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the resource is missing.</exception>
    public static string ReadAsyncStreamLoaderScript()
    {
        var assembly = typeof(WebInjection).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(ScriptResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource ending with {ScriptResourceSuffix} was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded resource {resourceName} could not be opened.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// File Transformation callback. Injects the loader into index.html when enabled.
    /// </summary>
    /// <param name="payload">Transformation payload whose Contents field holds HTML.</param>
    /// <returns>The HTML to serve.</returns>
    public static string TransformIndexHtml(object payload)
    {
        var enabled = Plugin.Instance?.Configuration.EnableAsyncStreamUi ?? true;
        return ApplyTransformation(payload, enabled);
    }

    /// <summary>
    /// Applies or strips the loader script based on the feature flag.
    /// </summary>
    /// <param name="payload">Transformation payload whose Contents field holds HTML.</param>
    /// <param name="enabled">True to inject the loader; false to strip it.</param>
    /// <returns>The HTML to serve.</returns>
    public static string ApplyTransformation(object payload, bool enabled)
    {
        var html = ReadContents(payload) ?? string.Empty;
        if (!enabled)
        {
            return StripInjectedScript(html);
        }

        return InjectScript(html, ReadAsyncStreamLoaderScript());
    }

    /// <summary>
    /// Inserts a unique script tag before <c>&lt;/body&gt;</c>, or <c>&lt;/html&gt;</c> as fallback.
    /// </summary>
    /// <param name="html">Existing HTML document.</param>
    /// <param name="script">JavaScript source to inject.</param>
    /// <returns>HTML with the script tag, unchanged when already present or empty.</returns>
    public static string InjectScript(string html, string script)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(script))
        {
            return html;
        }

        if (html.Contains($"id=\"{ScriptElementId}\"", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var tag = BuildScriptTag(script);
        var bodyIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex >= 0)
        {
            return html.Insert(bodyIndex, tag);
        }

        var htmlIndex = html.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
        if (htmlIndex >= 0)
        {
            return html.Insert(htmlIndex, tag);
        }

        return html + tag;
    }

    /// <summary>
    /// Removes a previously injected script tag when the feature is disabled.
    /// </summary>
    /// <param name="html">HTML that may contain the loader script.</param>
    /// <returns>HTML without the injected script tag.</returns>
    public static string StripInjectedScript(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        var idToken = $"id=\"{ScriptElementId}\"";
        var idIndex = html.IndexOf(idToken, StringComparison.OrdinalIgnoreCase);
        if (idIndex < 0)
        {
            return html;
        }

        var openIndex = html.LastIndexOf("<script", idIndex, StringComparison.OrdinalIgnoreCase);
        if (openIndex < 0)
        {
            return html;
        }

        var closeIndex = html.IndexOf("</script>", idIndex, StringComparison.OrdinalIgnoreCase);
        if (closeIndex < 0)
        {
            return html;
        }

        return html.Remove(openIndex, closeIndex + "</script>".Length - openIndex);
    }

    /// <summary>
    /// Builds the script tag wrapped around the loader source.
    /// </summary>
    /// <param name="script">JavaScript source.</param>
    /// <returns>An HTML script element.</returns>
    public static string BuildScriptTag(string script)
    {
        return $"<script id=\"{ScriptElementId}\">{script}</script>";
    }

    private static string? ReadContents(object payload)
    {
        if (payload is FileTransformationPayload typed)
        {
            return typed.Contents;
        }

        var type = payload.GetType();
        var contentsProperty = type.GetProperty("Contents", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (contentsProperty?.GetValue(payload) is string contents)
        {
            return contents;
        }

        var indexer = type.GetProperty("Item", [typeof(string)]);
        if (indexer is not null)
        {
            var token = indexer.GetValue(payload, ["contents"]) ?? indexer.GetValue(payload, ["Contents"]);
            if (token is string text)
            {
                return text;
            }

            if (token is not null)
            {
                return token.ToString();
            }
        }

        return payload.ToString();
    }
}
