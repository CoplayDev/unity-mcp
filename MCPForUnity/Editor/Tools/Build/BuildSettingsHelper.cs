using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace MCPForUnity.Editor.Tools.Build
{
    public static class BuildSettingsHelper
    {
        public static object ReadProperty(string property, NamedBuildTarget namedTarget)
        {
            switch (property.ToLowerInvariant())
            {
                case "product_name":
                    return new { property, value = PlayerSettings.productName };
                case "company_name":
                    return new { property, value = PlayerSettings.companyName };
                case "version":
                    return new { property, value = PlayerSettings.bundleVersion };
                case "bundle_id":
                    return new { property, value = PlayerSettings.GetApplicationIdentifier(namedTarget) };
                case "scripting_backend":
                    var backend = PlayerSettings.GetScriptingBackend(namedTarget);
                    return new { property, value = backend == ScriptingImplementation.IL2CPP ? "il2cpp" : "mono" };
                case "defines":
                    return new { property, value = PlayerSettings.GetScriptingDefineSymbols(namedTarget) };
                case "architecture":
                    var arch = PlayerSettings.GetArchitecture(namedTarget);
                    string archName = arch switch { 0 => "x86_64", 1 => "arm64", 2 => "universal", _ => "unknown" };
                    return new { property, value = archName, raw = arch };
                default:
                    return null;
            }
        }

        public static string WriteProperty(string property, string value, NamedBuildTarget namedTarget)
        {
            try
            {
                switch (property.ToLowerInvariant())
                {
                    case "product_name":
                        PlayerSettings.productName = value;
                        return null;
                    case "company_name":
                        PlayerSettings.companyName = value;
                        return null;
                    case "version":
                        PlayerSettings.bundleVersion = value;
                        return null;
                    case "bundle_id":
                        PlayerSettings.SetApplicationIdentifier(namedTarget, value);
                        return null;
                    case "scripting_backend":
                        var backendValue = value.ToLowerInvariant();
                        if (backendValue != "il2cpp" && backendValue != "mono")
                            return $"Unknown scripting_backend '{value}'. Valid: mono, il2cpp";
                        var impl = backendValue == "il2cpp"
                            ? ScriptingImplementation.IL2CPP
                            : ScriptingImplementation.Mono2x;
                        PlayerSettings.SetScriptingBackend(namedTarget, impl);
                        return null;
                    case "defines":
                        PlayerSettings.SetScriptingDefineSymbols(namedTarget, value);
                        return null;
                    case "architecture":
                        int arch = value.ToLowerInvariant() switch
                        {
                            "x86_64" or "none" or "default" => 0,
                            "arm64" => 1,
                            "universal" => 2,
                            _ => -1
                        };
                        if (arch < 0)
                            return $"Unknown architecture '{value}'. Valid: x86_64, arm64, universal";
                        PlayerSettings.SetArchitecture(namedTarget, arch);
                        return null;
                    default:
                        return $"Unknown property '{property}'. Valid: product_name, company_name, version, bundle_id, scripting_backend, defines, architecture";
                }
            }
            catch (Exception ex)
            {
                return $"Failed to set {property}: {ex.Message}";
            }
        }

        public static readonly IReadOnlyList<string> ValidProperties = new[]
        {
            "product_name", "company_name", "version", "bundle_id",
            "scripting_backend", "defines", "architecture"
        };
    }
}
