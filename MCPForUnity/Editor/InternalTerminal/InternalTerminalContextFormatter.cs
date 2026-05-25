using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WTL.InternalTerminal.Editor
{
    internal static class InternalTerminalContextFormatter
    {
        public static string FormatObjects(IEnumerable<UnityEngine.Object> objects)
        {
            var builder = new StringBuilder();
            if (objects == null)
            {
                return string.Empty;
            }

            var seen = new HashSet<string>();
            foreach (var item in objects)
            {
                var text = FormatObject(item);
                if (string.IsNullOrEmpty(text) || !seen.Add(text))
                {
                    continue;
                }

                builder.AppendLine(text);
            }

            return builder.ToString();
        }

        public static string FormatObject(UnityEngine.Object item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (item is Component component)
            {
                return FormatComponent(component);
            }

            var assetPath = AssetDatabase.GetAssetPath(item);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return FormatAsset(assetPath);
            }

            if (item is GameObject gameObject)
            {
                return FormatGameObject(gameObject);
            }

            return FormatFields(new Dictionary<string, string>
            {
                { "type", item.GetType().Name },
                { "path", item.name },
                { "gid", TryGetGlobalObjectId(item) }
            });
        }

        public static string FormatAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var projectPath = NormalizeProjectPath(path);
            if (string.IsNullOrEmpty(projectPath))
            {
                return string.Empty;
            }

            return FormatAsset(projectPath);
        }

        public static string FormatConsoleBlock(IEnumerable<UnityConsoleContextReader.ConsoleEntry> entries, DateTimeOffset generated)
        {
            var list = new List<UnityConsoleContextReader.ConsoleEntry>();
            if (entries != null)
            {
                list.AddRange(entries);
            }

            var builder = new StringBuilder();
            builder.Append("<unity-console generated:");
            AppendQuoted(builder, generated.ToString("yyyy-MM-ddTHH:mm:sszzz"));
            builder.Append(" count:");
            AppendQuoted(builder, list.Count.ToString());
            builder.AppendLine(">");

            for (var index = 0; index < list.Count; index++)
            {
                var entry = list[index];
                builder.Append("--- entry ");
                builder.Append(index + 1);
                builder.Append("/");
                builder.Append(list.Count);
                builder.AppendLine(" ---");
                builder.Append("level: ");
                builder.AppendLine(entry.Level ?? string.Empty);
                builder.Append("file: ");
                builder.AppendLine(entry.File ?? string.Empty);
                builder.Append("line: ");
                builder.AppendLine(entry.Line > 0 ? entry.Line.ToString() : string.Empty);
                builder.AppendLine("message:");
                builder.AppendLine(entry.Message ?? string.Empty);
                builder.AppendLine();
                builder.AppendLine("stack:");
                builder.AppendLine(entry.Stack ?? string.Empty);
                if (index + 1 < list.Count)
                {
                    builder.AppendLine();
                }
            }

            builder.AppendLine("</unity-console>");
            return builder.ToString();
        }

        public static string FormatConsoleFailure(string reason)
        {
            return FormatConsoleBlock(new[]
            {
                new UnityConsoleContextReader.ConsoleEntry
                {
                    Level = "warning",
                    Message = "Unity Console context could not be read.",
                    Stack = reason ?? string.Empty
                }
            }, DateTimeOffset.Now);
        }

        private static string FormatAsset(string assetPath)
        {
            return FormatFields(new Dictionary<string, string>
            {
                { "type", GetAssetType(assetPath) },
                { "path", assetPath }
            });
        }

        private static string FormatGameObject(GameObject gameObject)
        {
            return FormatFields(new Dictionary<string, string>
            {
                { "type", "gameObject" },
                { "path", GetSceneObjectPath(gameObject) },
                { "gid", TryGetGlobalObjectId(gameObject) },
                { "prefab", GetPrefabAssetPath(gameObject) }
            });
        }

        private static string FormatComponent(Component component)
        {
            var componentPath = GetSceneObjectPath(component.gameObject) + ":" + component.GetType().FullName + GetComponentIndexSuffix(component);
            return FormatFields(new Dictionary<string, string>
            {
                { "type", "component" },
                { "path", componentPath },
                { "gid", TryGetGlobalObjectId(component) }
            });
        }

        private static string FormatFields(Dictionary<string, string> fields)
        {
            var builder = new StringBuilder();
            builder.Append("@unity(");
            var wroteAny = false;
            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field.Value))
                {
                    continue;
                }

                if (wroteAny)
                {
                    builder.Append(", ");
                }

                builder.Append(field.Key);
                builder.Append(":");
                AppendQuoted(builder, field.Value);
                wroteAny = true;
            }

            builder.Append(")");
            return wroteAny ? builder.ToString() : string.Empty;
        }

        private static void AppendQuoted(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var ch in value ?? string.Empty)
            {
                if (ch == '\\' || ch == '"')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }

            builder.Append('"');
        }

        private static string NormalizeProjectPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized == "Assets")
            {
                return normalized;
            }

            if (!Path.IsPathRooted(path))
            {
                return normalized;
            }

            var dataPath = Application.dataPath.Replace('\\', '/');
            var fullPath = Path.GetFullPath(path).Replace('\\', '/');
            if (string.Equals(fullPath, dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }

            if (fullPath.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + fullPath.Substring(dataPath.Length);
            }

            return string.Empty;
        }

        private static string GetAssetType(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return "folder";
            }

            switch (Path.GetExtension(assetPath).ToLowerInvariant())
            {
                case ".cs":
                    return "script";
                case ".unity":
                    return "scene";
                case ".prefab":
                    return "prefab";
                case ".mat":
                    return "material";
                case ".asset":
                    return "asset";
                default:
                    return "asset";
            }
        }

        private static string GetSceneObjectPath(GameObject gameObject)
        {
            var scenePath = GetScenePath(gameObject.scene);
            var hierarchyPath = GetHierarchyPath(gameObject.transform);
            return string.IsNullOrEmpty(scenePath) ? hierarchyPath : scenePath + "#" + hierarchyPath;
        }

        private static string GetScenePath(Scene scene)
        {
            if (!scene.IsValid())
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(scene.path))
            {
                return scene.path;
            }

            return scene.name;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var builder = new StringBuilder(transform.name);
            var current = transform.parent;
            while (current != null)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return builder.ToString();
        }

        private static string GetComponentIndexSuffix(Component component)
        {
            var components = component.gameObject.GetComponents(component.GetType());
            if (components.Length <= 1)
            {
                return string.Empty;
            }

            for (var index = 0; index < components.Length; index++)
            {
                if (components[index] == component)
                {
                    return "[" + index + "]";
                }
            }

            return string.Empty;
        }

        private static string GetPrefabAssetPath(GameObject gameObject)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            if (source == null)
            {
                return string.Empty;
            }

            return AssetDatabase.GetAssetPath(source);
        }

        private static string TryGetGlobalObjectId(UnityEngine.Object item)
        {
            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(item).ToString();
            }
            catch
            {
                // Best effort metadata; the readable path remains the primary anchor.
            }

            return string.Empty;
        }
    }
}
