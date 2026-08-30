using System;
using System.Collections.Generic;

namespace ThemeForge.Models
{
    /// <summary>
    /// Case insensitive string keyed dictionary. Theme authors are inconsistent about
    /// casing in yaml files, so every lookup table in Theme Forge tolerates it.
    /// </summary>
    public class DictNoCase<TValue> : Dictionary<string, TValue>
    {
        public DictNoCase() : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public TValue Get(string key)
        {
            if (key == null)
            {
                return default(TValue);
            }

            TValue value;
            return TryGetValue(key, out value) ? value : default(TValue);
        }
    }
}
