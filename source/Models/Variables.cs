using System.Collections.Generic;

namespace ThemeForge.Models
{
    /// <summary>Theme declared variables, keyed by the XAML resource key they drive.</summary>
    public class Variables : DictNoCase<Variable>
    {
        public IEnumerable<KeyValuePair<string, Variable>> Ordered()
        {
            return this;
        }
    }

    /// <summary>User chosen values, keyed the same way. This is what gets persisted.</summary>
    public class VariablesValues : DictNoCase<VariableValue>
    {
        public void Set(string key, string type, string value)
        {
            VariableValue existing;
            if (TryGetValue(key, out existing))
            {
                existing.Type = type ?? existing.Type;
                existing.Value = value;
                return;
            }

            this[key] = new VariableValue { Type = type, Value = value };
        }
    }
}
