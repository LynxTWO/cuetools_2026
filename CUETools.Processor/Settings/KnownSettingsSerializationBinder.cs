using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CUETools.Processor.Settings
{
    /// <summary>
    /// Resolves JSON type metadata only for codec settings types that the application already
    /// discovered and instantiated. It never loads an assembly named by the settings file.
    /// </summary>
    internal sealed class KnownSettingsSerializationBinder : ISerializationBinder
    {
        private readonly Dictionary<string, Type> _knownTypes;

        public KnownSettingsSerializationBinder(IEnumerable<Type> knownTypes)
        {
            _knownTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in knownTypes.Where(type => type != null).Distinct())
            {
                string typeName = type.FullName;
                string assemblyName = type.Assembly.GetName().Name;
                if (typeName == null || assemblyName == null)
                    continue;

                _knownTypes[GetKey(assemblyName, typeName)] = type;
            }
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            string simpleAssemblyName;
            try
            {
                simpleAssemblyName = new System.Reflection.AssemblyName(assemblyName).Name;
            }
            catch (Exception ex)
            {
                throw new JsonSerializationException("Invalid settings type metadata.", ex);
            }

            Type type;
            if (simpleAssemblyName == null ||
                !_knownTypes.TryGetValue(GetKey(simpleAssemblyName, typeName), out type))
                throw new JsonSerializationException("Settings type is not in the discovered codec allowlist.");

            return type;
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            typeName = serializedType.FullName;
            assemblyName = serializedType.Assembly.GetName().Name;
            if (typeName == null || assemblyName == null ||
                !_knownTypes.ContainsKey(GetKey(assemblyName, typeName)))
                throw new JsonSerializationException("Cannot persist an unknown codec settings type.");
        }

        private static string GetKey(string assemblyName, string typeName)
        {
            return assemblyName + "\n" + typeName;
        }
    }
}
