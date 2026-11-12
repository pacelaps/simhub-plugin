using System;
using System.Security.Cryptography;
using System.Text;

namespace PaceLaps.Plugin
{
    /// <summary>The plugin's settings, saved by SimHub as JSON in its PluginsData folder.</summary>
    public class PluginSettings
    {
        /// <summary>The overlay URL, encrypted for this Windows user (DPAPI): the token in it is a secret.</summary>
        public string? ProtectedOverlayUrl { get; set; }

        public bool Sound { get; set; } = true;

        /// <summary>0 to 100.</summary>
        public int Volume { get; set; } = 70;

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PaceLaps.SimHub overlay URL");

        public string? OverlayUrl()
        {
            if (string.IsNullOrEmpty(ProtectedOverlayUrl))
            {
                return null;
            }

            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedOverlayUrl), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception e) when (e is CryptographicException || e is FormatException)
            {
                // Another Windows user or PC: paste it again.
                return null;
            }
        }

        public void SetOverlayUrl(string? url) =>
            ProtectedOverlayUrl = string.IsNullOrWhiteSpace(url)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(url!.Trim()), Entropy, DataProtectionScope.CurrentUser));
    }
}
