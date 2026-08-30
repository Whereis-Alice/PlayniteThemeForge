using System;
using System.IO;
using Playnite.SDK.Data;
using YamlDotNet.Serialization;

namespace Validator
{
    /// <summary>
    /// Playnite calls Serialization.Init() during host start-up, so the static hook is null when
    /// the plugin assembly is exercised outside the app. The validator installs this YamlDotNet
    /// backed shim, which mirrors the exact-name property mapping Playnite uses for theme
    /// manifests. Only the YAML paths are implemented; nothing else is reachable from the
    /// code paths under test.
    /// </summary>
    public class SerializerShim : IDataSerializer
    {
        private readonly IDeserializer yamlIn = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        private readonly ISerializer yamlOut = new SerializerBuilder().Build();

        public string ToYaml(object obj) { return yamlOut.Serialize(obj); }
        public T FromYaml<T>(string yaml) where T : class { return yamlIn.Deserialize<T>(yaml); }
        public T FromYamlFile<T>(string filePath) where T : class { using (var r = new StreamReader(filePath)) { return yamlIn.Deserialize<T>(r); } }
        public bool TryFromYaml<T>(string yaml, out T content) where T : class { try { content = FromYaml<T>(yaml); return true; } catch { content = null; return false; } }
        public bool TryFromYaml<T>(string yaml, out T content, out Exception error) where T : class { try { content = FromYaml<T>(yaml); error = null; return true; } catch (Exception e) { content = null; error = e; return false; } }
        public bool TryFromYamlFile<T>(string filePath, out T content) where T : class { try { content = FromYamlFile<T>(filePath); return true; } catch { content = null; return false; } }
        public bool TryFromYamlFile<T>(string filePath, out T content, out Exception error) where T : class { try { content = FromYamlFile<T>(filePath); error = null; return true; } catch (Exception e) { content = null; error = e; return false; } }

        public string ToJson(object obj, bool formatted = false) { throw new NotSupportedException(); }
        public void ToJsonStream(object obj, Stream stream, bool formatted = false) { throw new NotSupportedException(); }
        public T FromJson<T>(string json) where T : class { throw new NotSupportedException(); }
        public bool TryFromJson<T>(string json, out T content) where T : class { content = null; return false; }
        public bool TryFromJson<T>(string json, out T content, out Exception error) where T : class { content = null; error = null; return false; }
        public T FromJsonStream<T>(Stream stream) where T : class { throw new NotSupportedException(); }
        public bool TryFromJsonStream<T>(Stream stream, out T content) where T : class { content = null; return false; }
        public bool TryFromJsonStream<T>(Stream stream, out T content, out Exception error) where T : class { content = null; error = null; return false; }
        public T FromJsonFile<T>(string filePath) where T : class { throw new NotSupportedException(); }
        public bool TryFromJsonFile<T>(string filePath, out T content) where T : class { content = null; return false; }
        public bool TryFromJsonFile<T>(string filePath, out T content, out Exception error) where T : class { content = null; error = null; return false; }
        public T FromToml<T>(string toml) where T : class { throw new NotSupportedException(); }
        public bool TryFromToml<T>(string toml, out T content) where T : class { content = null; return false; }
        public bool TryFromToml<T>(string toml, out T content, out Exception error) where T : class { content = null; error = null; return false; }
        public T FromTomlFile<T>(string filePath) where T : class { throw new NotSupportedException(); }
        public bool TryFromTomlFile<T>(string filePath, out T content) where T : class { content = null; return false; }
        public bool TryFromTomlFile<T>(string filePath, out T content, out Exception error) where T : class { content = null; error = null; return false; }
        public T GetClone<T>(T source) where T : class { throw new NotSupportedException(); }
        public U GetClone<T, U>(T source) where T : class where U : class { throw new NotSupportedException(); }
        public bool AreObjectsEqual(object object1, object object2) { return Equals(object1, object2); }
    }
}
