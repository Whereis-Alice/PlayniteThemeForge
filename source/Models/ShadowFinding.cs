using System;

namespace ThemeForge.Models
{
    /// <summary>Why an individual override is worth pointing out to the user.</summary>
    public enum ShadowReason
    {
        /// <summary>A selected preset declares this exact key, so the override replaces it outright.</summary>
        Direct,

        /// <summary>
        /// A selected preset declares a key this override is built from. Helium style themes
        /// derive brushes from a palette of colours, so overriding <c>GlyphBrush</c> silently
        /// defeats an accent preset that only ships <c>GlyphColor</c>.
        /// </summary>
        Derived,

        /// <summary>The override resolves to the value the theme already had: it does nothing.</summary>
        Redundant
    }

    /// <summary>One diagnosed override, produced by <c>ForgeEngine.ShadowedPresetKeys</c>.</summary>
    public class ShadowFinding
    {
        public ShadowFinding()
        {
        }

        public ShadowFinding(string key, ShadowReason reason, string viaKey)
        {
            Key = key;
            Reason = reason;
            ViaKey = viaKey;
        }

        public string Key { get; set; }

        public ShadowReason Reason { get; set; }

        /// <summary>
        /// For <see cref="ShadowReason.Derived"/>, the preset supplied key that is being masked.
        /// Empty otherwise.
        /// </summary>
        public string ViaKey { get; set; }

        public override string ToString()
        {
            return Key + " (" + Reason + (string.IsNullOrEmpty(ViaKey) ? "" : " via " + ViaKey) + ")";
        }
    }
}
