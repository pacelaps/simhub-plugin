using System;
using System.Text.RegularExpressions;

namespace PaceLaps.Plugin.Core
{
    /// <summary>
    /// One of the driver's overlay URLs from the driver page (https://app.pacelaps.com/overlay/&lt;token&gt;): the site
    /// to connect to and the token the device API accepts in X-Overlay-Token. The token is a secret: never log it.
    /// </summary>
    public sealed class OverlayLink
    {
        private static readonly Regex TokenPattern = new Regex("^[A-Za-z0-9]{20,200}$", RegexOptions.CultureInvariant);

        private OverlayLink(Uri site, Uri page, string token)
        {
            Site = site;
            Page = page;
            Token = token;
        }

        /// <summary>The site's origin, for example https://app.pacelaps.com/.</summary>
        public Uri Site { get; }

        /// <summary>The overlay page itself (it carries the realtime server's public key).</summary>
        public Uri Page { get; }

        public string Token { get; }

        /// <summary>The site without the token, for the settings page and logs.</summary>
        public override string ToString() => Site.GetLeftPart(UriPartial.Authority);

        /// <summary>The link, or null with a reason the settings page can show.</summary>
        public static OverlayLink? Parse(string? text, out string? problem)
        {
            problem = null;
            if (!Uri.TryCreate((text ?? "").Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                problem = "Paste one of your overlay URLs from the driver page.";
                return null;
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !IsLocal(uri.Host))
            {
                problem = "The overlay URL must start with https://.";
                return null;
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length != 2 || segments[0] != "overlay" || !TokenPattern.IsMatch(segments[1]))
            {
                problem = "That is not an overlay URL. Copy one from your driver page (Overlay URLs).";
                return null;
            }

            var site = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
            return new OverlayLink(site, new Uri(site, "overlay/" + segments[1]), segments[1]);
        }

        /// <summary>Development hosts, where plain http is fine.</summary>
        private static bool IsLocal(string host) =>
            host == "localhost" || host == "127.0.0.1" || host == "[::1]" || host == "::1"
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
    }
}
