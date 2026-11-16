using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace com.insanitydesign.MarkdownViewerPlusPlus.Helper
{
    /// <summary>
    /// Removes active content (scripts, embedded objects, event handlers, script URLs, CSS expressions)
    /// from HTML converted from (untrusted) markdown before it is handed to a script-capable engine,
    /// e.g. the Internet Explorer based WebBrowser control used for printing or an e-mail.
    /// Formatting and plain HTML are kept.
    /// </summary>
    public static class HtmlSanitizer
    {
        /// <summary>
        /// Elements removed together with their content
        /// </summary>
        private static readonly Regex DangerousElementsWithContent = new Regex(
            @"<\s*(script|iframe|frame|frameset|object|applet|embed|noscript|xml|template)\b[\s\S]*?(<\s*/\s*\1\s*>|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// (Unclosed) tags that are removed
        /// </summary>
        private static readonly Regex DangerousTags = new Regex(
            @"<\s*/?\s*(script|iframe|frame|frameset|object|applet|embed|noscript|xml|template|meta|link|base|param|import|bgsound)\b(?:""[^""]*""|'[^']*'|[^'"">])*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Processing instructions like &lt;?import implementation="..."&gt; (IE behaviors)
        /// </summary>
        private static readonly Regex ProcessingInstructions = new Regex(@"<\?[\s\S]*?\?*>", RegexOptions.Compiled);

        /// <summary>
        /// Any start tag with its attributes (quoted values may contain '&gt;')
        /// </summary>
        private static readonly Regex StartTag = new Regex(
            @"<(?<name>[a-zA-Z][a-zA-Z0-9:-]*)(?<attributes>(?:""[^""]*""|'[^']*'|[^'"">])*)>",
            RegexOptions.Compiled);

        /// <summary>
        /// A single attribute with optional value
        /// </summary>
        private static readonly Regex Attribute = new Regex(
            @"(?<name>[^\s""'>/=]+)(?:\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'>]+)))?",
            RegexOptions.Compiled);

        /// <summary>
        /// CSS constructs that execute script in Internet Explorer
        /// </summary>
        private static readonly Regex DangerousCss = new Regex(
            @"expression\s*\(|behavio(u)?r\s*:|-moz-binding\s*:|(java|vb|live)script\s*:|@import",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attributes whose value is a URL
        /// </summary>
        private static readonly string[] UrlAttributes = { "href", "src", "action", "formaction", "background", "lowsrc", "dynsrc", "data", "poster", "codebase", "cite", "longdesc", "usemap", "xlink:href" };

        /// <summary>
        /// URL schemes that are allowed in URL attributes (relative URLs are always allowed)
        /// </summary>
        private static readonly string[] AllowedSchemes = { "http", "https", "file", "mailto", "ftp", "data" };

        /// <summary>
        /// Data URLs are only allowed for images
        /// </summary>
        private static readonly string[] AllowedDataMediaTypes = { "image/png", "image/gif", "image/jpeg", "image/jpg", "image/bmp", "image/webp" };

        /// <summary>
        ///
        /// </summary>
        /// <param name="html"></param>
        /// <returns>The HTML without active content</returns>
        public static string Sanitize(string html)
        {
            if (string.IsNullOrEmpty(html)) return html ?? "";

            string result = ProcessingInstructions.Replace(html, "");
            result = DangerousElementsWithContent.Replace(result, "");
            result = DangerousTags.Replace(result, "");
            result = StartTag.Replace(result, SanitizeTag);
            //<style> blocks inside the document
            result = Regex.Replace(result, @"(<\s*style\b[^>]*>)([\s\S]*?)(<\s*/\s*style\s*>)",
                match => match.Groups[1].Value + NeutralizeCss(match.Groups[2].Value) + match.Groups[3].Value,
                RegexOptions.IgnoreCase);
            return result;
        }

        /// <summary>
        /// Rebuilds a start tag with only safe attributes
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        private static string SanitizeTag(Match tag)
        {
            string attributes = tag.Groups["attributes"].Value;
            bool selfClosing = attributes.TrimEnd().EndsWith("/");
            StringBuilder sanitized = new StringBuilder("<").Append(tag.Groups["name"].Value);

            foreach (Match attribute in Attribute.Matches(attributes))
            {
                string name = attribute.Groups["name"].Value;
                string lowerName = name.ToLowerInvariant();
                if (lowerName == "/") continue;
                //Event handlers and other script entry points
                if (lowerName.StartsWith("on") || lowerName == "srcdoc" || lowerName == "dynsrc" || lowerName == "lowsrc" || lowerName == "datasrc" || lowerName == "datafld" || lowerName.StartsWith("xmlns")) continue;

                if (!attribute.Groups["value"].Success)
                {
                    sanitized.Append(' ').Append(name);
                    continue;
                }

                string value = attribute.Groups["value"].Value;
                if (UrlAttributes.Contains(lowerName) && !IsSafeUrl(value)) continue;
                if (lowerName == "style") value = NeutralizeCss(WebUtility.HtmlDecode(value));

                sanitized.Append(' ').Append(name).Append("=\"").Append(value.Replace("\"", "&quot;")).Append('"');
            }

            return sanitized.Append(selfClosing ? " />" : ">").ToString();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="url">The (HTML encoded) attribute value</param>
        /// <returns>True for relative URLs and URLs with an allowed scheme</returns>
        public static bool IsSafeUrl(string url)
        {
            //Browsers decode entities and ignore whitespace/control characters in schemes ("java&#09;script:")
            string decoded = new string(WebUtility.HtmlDecode(url ?? "").Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
            Match scheme = Regex.Match(decoded, @"^([a-zA-Z][a-zA-Z0-9+.\-]*):");
            //No scheme -> relative URL (or anchor)
            if (!scheme.Success) return true;
            //Drive letters (C:\...) are local paths
            if (scheme.Groups[1].Value.Length == 1) return true;

            string schemeName = scheme.Groups[1].Value.ToLowerInvariant();
            if (!AllowedSchemes.Contains(schemeName)) return false;
            if (schemeName == "data")
            {
                string mediaType = decoded.Substring(5).Split(';', ',')[0].ToLowerInvariant();
                return AllowedDataMediaTypes.Contains(mediaType);
            }
            return true;
        }

        /// <summary>
        /// Disables CSS constructs that can execute script
        /// </summary>
        /// <param name="css"></param>
        /// <returns></returns>
        private static string NeutralizeCss(string css)
        {
            //Remove CSS escapes and comments used to hide the keywords (e.g. "exp/**/ression", "\65xpression")
            string normalized = Regex.Replace(css, @"/\*[\s\S]*?\*/", "");
            normalized = Regex.Replace(normalized, @"\\([0-9a-fA-F]{1,6})\s?", match =>
            {
                int codePoint = Convert.ToInt32(match.Groups[1].Value, 16);
                bool valid = codePoint > 0 && codePoint < 0x110000 && (codePoint < 0xD800 || codePoint > 0xDFFF);
                return valid ? char.ConvertFromUtf32(codePoint) : "";
            });
            normalized = normalized.Replace("\\", "");
            return DangerousCss.Replace(normalized, "blocked-");
        }
    }
}
