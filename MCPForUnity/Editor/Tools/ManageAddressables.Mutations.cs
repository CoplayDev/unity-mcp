using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    public static partial class ManageAddressables
    {
        private static readonly Dictionary<string, string> SettingFields = new()
        {
            ["build_remote_catalog"] = "BuildRemoteCatalog",
            ["unique_bundle_ids"] = "UniqueBundleIds",
            ["contiguous_bundles"] = "ContiguousBundles",
            ["max_concurrent_web_requests"] = "MaxConcurrentWebRequests",
            ["catalog_requests_timeout"] = "CatalogRequestsTimeout",
            ["remote_catalog_build_path"] = "RemoteCatalogBuildPath",
            ["remote_catalog_load_path"] = "RemoteCatalogLoadPath"
        };

        private static bool ReadBool(ToolParams p, string name)
        {
            var value = p.GetRaw(name);
            if (value == null || value.Type == JTokenType.Null) return false;
            if (value.Type != JTokenType.Boolean) throw new ArgumentException($"'{name}' must be a boolean.");
            return (bool)value;
        }

        private static Dictionary<PropertyInfo, object> ValidateSettings(ToolParams p, Type type, object settings)
        {
            var updates = p.GetRaw("settings") as JObject;
            if (updates == null)
            {
                if (p.GetRaw("settings") != null || !ReadBool(p, "initialize"))
                    throw new ArgumentException("Provide a 'settings' object, or initialize=true to explicitly create settings.");
                updates = new JObject();
            }
            var result = new Dictionary<PropertyInfo, object>();
            foreach (var field in updates.Properties())
            {
                if (!SettingFields.TryGetValue(field.Name, out string propertyName))
                    throw new ArgumentException($"Unsupported setting '{field.Name}'. Valid settings: {string.Join(", ", SettingFields.Keys)}");
                var property = type.GetProperty(propertyName);
                if (property == null || !property.CanWrite) throw new MissingMemberException(type.FullName, propertyName);
                if (property.PropertyType == typeof(bool))
                {
                    if (field.Value.Type != JTokenType.Boolean)
                        throw new ArgumentException($"'{field.Name}' must be a boolean.");
                    result[property] = (bool)field.Value;
                }
                else if (field.Name.StartsWith("remote_catalog_", StringComparison.Ordinal))
                {
                    if (field.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)field.Value))
                        throw new ArgumentException($"'{field.Name}' must name an existing profile variable.");
                    result[property] = (string)field.Value;
                }
                else
                {
                    if (field.Value.Type != JTokenType.Integer || !int.TryParse(field.Value.ToString(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                        || value < (field.Name == "max_concurrent_web_requests" ? 1 : 0)
                        || value > (field.Name == "max_concurrent_web_requests" ? 1024 : 3600))
                        throw new ArgumentException($"Invalid integer value for '{field.Name}'.");
                    result[property] = value;
                }
            }
            if (result.Values.OfType<string>().Any())
            {
                // Validate defaults in memory before GetSettings(true) can persist a new configuration.
                var profiles = settings == null
                    ? Activator.CreateInstance(type.Assembly.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetProfileSettings"))
                    : Read(settings, "profileSettings");
                if (settings == null) Invoke(profiles, "Reset", Type.EmptyTypes);
                var names = Items(Invoke(profiles, "GetVariableNames", Type.EmptyTypes)).Cast<string>().ToHashSet(StringComparer.Ordinal);
                foreach (string variable in result.Values.OfType<string>())
                    if (!names.Contains(variable)) throw new ArgumentException($"Profile variable '{variable}' not found.");
            }
            return result;
        }

        private static object SetSettings(object settings, Dictionary<PropertyInfo, object> updates)
        {
            foreach (var property in updates.Keys.ToArray())
            {
                if (updates[property] is not string variable) continue;
                var reference = Activator.CreateInstance(property.PropertyType);
                var assigned = Invoke(reference, "SetVariableByName", new[] { settings.GetType(), typeof(string) }, settings, variable);
                if (assigned is bool success && !success) throw new ArgumentException("Unable to assign the catalog profile variable.");
                updates[property] = reference;
            }
            var previous = updates.Keys.ToDictionary(property => property, property => property.GetValue(settings));
            RecordMutation(settings);
            try
            {
                foreach (var update in updates) update.Key.SetValue(settings, update.Value);
            }
            catch
            {
                foreach (var old in previous) old.Key.SetValue(settings, old.Value);
                throw;
            }
            SaveChanges(settings);
            return new SuccessResponse("Addressables settings saved.", new
            {
                settings_path = AssetDatabase.GetAssetPath((UnityEngine.Object)settings),
                values = SettingFields.Where(field => settings.GetType().GetProperty(field.Value) != null)
                    .ToDictionary(field => field.Key, field => ReadSetting(settings, field.Key, field.Value))
            });
        }

        private static object ReadSetting(object settings, string name, string property)
        {
            if (!name.StartsWith("remote_catalog_", StringComparison.Ordinal)) return Read(settings, property);
            // The native getter can initialize a reference. Queries must inspect its serialized ID instead.
            using var serialized = new SerializedObject((UnityEngine.Object)settings);
            return serialized.FindProperty("m_" + property)?.FindPropertyRelative("m_Id")?.stringValue;
        }

        private static void SaveChanges(object settings, params object[] groups)
        {
            foreach (var group in groups.Where(group => group != null).Distinct())
                EditorUtility.SetDirty((UnityEngine.Object)group);
            var eventType = settings.GetType().GetNestedType("ModificationEvent");
            Invoke(settings, "SetDirty", new[] { eventType, typeof(object), typeof(bool), typeof(bool) },
                Enum.Parse(eventType, "BatchModification"), null, true, true);
            // Save only assets touched by this operation, not unrelated dirty scenes or assets.
            AssetDatabase.SaveAssetIfDirty((UnityEngine.Object)settings);
            foreach (var group in groups.Where(group => group != null).Distinct())
                AssetDatabase.SaveAssetIfDirty((UnityEngine.Object)group);
        }

        private static void RecordMutation(object settings, params object[] groups)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Addressables change");
            Undo.RegisterCompleteObjectUndo(new[] { settings }.Concat(groups).Where(item => item != null)
                .Distinct().Cast<UnityEngine.Object>().ToArray(), "Addressables change");
        }

        private static void RequireWritable(object group)
        {
            if ((bool)Read(group, "ReadOnly")) throw new ArgumentException("The group is read-only.");
        }

        private static object ExistingEntry(object settings, ToolParams p)
        {
            string guid = EntryGuid(p);
            var entry = Invoke(settings, "FindAssetEntry", new[] { typeof(string), typeof(bool) }, guid, false);
            if (entry == null) throw new ArgumentException("No explicit Addressables entry found for this asset.");
            if ((bool)Read(entry, "ReadOnly")) throw new ArgumentException("The entry is read-only.");
            RequireWritable(Read(entry, "parentGroup"));
            return entry;
        }

        private static string EntryGuid(ToolParams p)
        {
            string guid = ReadString(p, "guid");
            string path = ReadString(p, "path");
            if ((guid == null) == (path == null)) throw new ArgumentException("Provide exactly one of 'guid' or 'path'.");
            if (path != null)
            {
                path = path.Replace('\\', '/');
                if (!(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
                    || path.Split('/').Any(part => part == ".." || part == "."))
                    throw new ArgumentException("'path' must be a project asset path without traversal.");
                guid = AssetDatabase.AssetPathToGUID(path);
            }
            if (string.IsNullOrEmpty(guid) || guid.Length != 32 || guid.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("Asset identity is invalid or the asset does not exist.");
            return guid.ToLowerInvariant();
        }

        private static object Mutate(object settings, List<object> groups, string action, ToolParams p)
        {
            switch (action)
            {
                case "group_create":
                {
                    string name = ReadString(p, "name", true);
                    if (name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || name == "." || name == "..")
                        throw new ArgumentException("Group name must be a valid asset filename.");
                    if (groups.Any(group => SerializedString(group, "m_GroupName") == name))
                        throw new ArgumentException("A group with that name already exists.");
                    bool? staticContent = p.GetRaw("static_content") == null ? null : ReadBool(p, "static_content");
                    var assembly = settings.GetType().Assembly;
                    var schemaType = assembly.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetGroupSchema");
                    var bundled = assembly.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema");
                    var update = assembly.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema");
                    if (schemaType == null || bundled == null || update == null)
                        throw new MissingMemberException("Addressables group schemas are unavailable.");
                    var method = settings.GetType().GetMethod("CreateGroup", new[] { typeof(string), typeof(bool),
                        typeof(bool), typeof(bool), typeof(List<>).MakeGenericType(schemaType), typeof(Type[]) });
                    if (method == null) throw new MissingMethodException("CreateGroup");
                    object created = method.Invoke(settings, new object[] { name, false, false, true, null, new[] { bundled, update } });
                    if (created == null) throw new InvalidOperationException("Unity did not create the group.");
                    if (staticContent.HasValue)
                    {
                        var schema = Items(Read(created, "Schemas")).Single(item => item.GetType() == update);
                        update.GetProperty("StaticContent").SetValue(schema, staticContent.Value);
                        AssetDatabase.SaveAssetIfDirty((UnityEngine.Object)schema);
                    }
                    SaveChanges(settings, created);
                    return new SuccessResponse("Group created with bundled-asset and content-update schemas.", new
                    {
                        guid = SerializedString(created, "m_GUID"), name = SerializedString(created, "m_GroupName"),
                        undo_supported = false
                    });
                }
                case "group_remove":
                {
                    var group = FindGroup(groups, ReadString(p, "group", true));
                    RequireWritable(group);
                    int count = Items(Read(group, "entries")).Count();
                    if (Items(Read(group, "entries")).Any(entry => (bool)Read(entry, "ReadOnly")))
                        throw new ArgumentException("The group contains read-only entries.");
                    if (count > 0 && !ReadBool(p, "force"))
                        throw new ArgumentException("Group contains entries. Use force=true to remove their registrations as well.");
                    // The Addressables API deletes the group's schema/config assets; source assets are not deleted.
                    Invoke(settings, "RemoveGroup", new[] { group.GetType() }, group);
                    SaveChanges(settings);
                    return new SuccessResponse("Group configuration removed; source assets preserved.", new { removed_entries = count, undo_supported = false });
                }
                case "entry_add":
                {
                    string guid = EntryGuid(p);
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Asset is missing or is a script, which cannot be Addressable.");
                    string configFolder = Text(settings, "ConfigFolder").Replace('\\', '/');
                    if (assetPath == configFolder || assetPath.StartsWith(configFolder + "/", StringComparison.Ordinal))
                        throw new ArgumentException("Addressables configuration assets cannot be registered as runtime content.");
                    var group = FindGroup(groups, ReadString(p, "group", true));
                    RequireWritable(group);
                    if (Invoke(settings, "FindAssetEntry", new[] { typeof(string), typeof(bool) }, guid, false) != null)
                        throw new ArgumentException("Asset is already registered. Use entry_move or entry_set_address explicitly.");
                    RecordMutation(settings, group);
                    var entry = Invoke(settings, "CreateOrMoveEntry", new[] { typeof(string), group.GetType(), typeof(bool), typeof(bool) }, guid, group, false, true);
                    if (entry == null) throw new InvalidOperationException("Unity refused to register the asset.");
                    if ((bool)Read(entry, "ReadOnly"))
                    {
                        Invoke(settings, "RemoveAssetEntry", new[] { typeof(string), typeof(bool) }, guid, true);
                        SaveChanges(settings, group);
                        throw new ArgumentException("Unity marked this asset read-only; the new registration was rolled back.");
                    }
                    SaveChanges(settings, group);
                    return new SuccessResponse("Asset registered.", Entry(entry, group));
                }
                case "entry_remove":
                case "entry_move":
                case "entry_set_address":
                case "label_set":
                {
                    var entry = ExistingEntry(settings, p);
                    var source = Read(entry, "parentGroup");
                    if (action == "entry_move")
                    {
                        var target = FindGroup(groups, ReadString(p, "group", true));
                        RequireWritable(target);
                        RecordMutation(settings, source, target);
                        Invoke(settings, "MoveEntry", new[] { entry.GetType(), target.GetType(), typeof(bool), typeof(bool) }, entry, target, false, true);
                        SaveChanges(settings, source, target);
                        return new SuccessResponse("Entry moved.", Entry(entry, target));
                    }
                    if (action == "entry_set_address")
                    {
                        string address = ReadString(p, "address", true);
                        if (address.Contains('[') && address.Contains(']'))
                            throw new ArgumentException("Address cannot contain both '[' and ']'.");
                        RecordMutation(settings, source);
                        Invoke(entry, "SetAddress", new[] { typeof(string), typeof(bool) }, address, true);
                    }
                    else if (action == "label_set")
                    {
                        var labels = p.GetRaw("labels") as JArray;
                        if (labels == null || labels.Any(label => label.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)label)))
                            throw new ArgumentException("'labels' must be an array of non-empty strings; [] clears the entry's labels.");
                        var wanted = labels.Values<string>().ToHashSet(StringComparer.Ordinal);
                        var known = Items(Invoke(settings, "GetLabels", Type.EmptyTypes)).Cast<string>().ToHashSet(StringComparer.Ordinal);
                        if (!wanted.IsSubsetOf(known)) throw new ArgumentException("Unknown label; create label definitions with label_add first.");
                        RecordMutation(settings, source);
                        foreach (string label in Items(Read(entry, "labels")).Cast<string>().Union(wanted).ToArray())
                            Invoke(entry, "SetLabel", new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) }, label, wanted.Contains(label), false, true);
                    }
                    else
                    {
                        RecordMutation(settings, source);
                        Invoke(settings, "RemoveAssetEntry", new[] { typeof(string), typeof(bool) }, Text(entry, "guid"), true);
                        SaveChanges(settings, source);
                        return new SuccessResponse("Entry registration removed; source asset preserved.");
                    }
                    SaveChanges(settings, source);
                    return new SuccessResponse("Entry updated.", Entry(entry, source));
                }
                case "label_add":
                case "label_remove":
                {
                    string label = ReadString(p, "name", true);
                    if (action == "label_add" && label.Contains('[') && label.Contains(']'))
                        throw new ArgumentException("Label name cannot contain both '[' and ']'.");
                    var known = Items(Invoke(settings, "GetLabels", Type.EmptyTypes)).Cast<string>().ToHashSet(StringComparer.Ordinal);
                    var affected = groups.Where(group => Items(Read(group, "entries"))
                        .Any(entry => Items(Read(entry, "labels")).Cast<string>().Contains(label))).ToArray();
                    if (action == "label_remove" && affected.Length > 0)
                    {
                        if (!ReadBool(p, "force")) throw new ArgumentException("Label is in use. Use force=true to detach it and remove the definition.");
                        foreach (var group in affected) RequireWritable(group);
                        if (affected.SelectMany(group => Items(Read(group, "entries")))
                            .Any(entry => (bool)Read(entry, "ReadOnly") && Items(Read(entry, "labels")).Cast<string>().Contains(label)))
                            throw new ArgumentException("The label is used by a read-only entry.");
                    }
                    RecordMutation(settings, affected);
                    if (action == "label_remove")
                    {
                        foreach (var entry in affected.SelectMany(group => Items(Read(group, "entries")))
                            .Where(entry => Items(Read(entry, "labels")).Cast<string>().Contains(label)).ToArray())
                            Invoke(entry, "SetLabel", new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) }, label, false, false, true);
                        if (known.Contains(label)) Invoke(settings, "RemoveLabel", new[] { typeof(string), typeof(bool) }, label, true);
                    }
                    else if (action == "label_add" && !known.Contains(label))
                        Invoke(settings, "AddLabel", new[] { typeof(string), typeof(bool) }, label, true);
                    SaveChanges(settings, affected);
                    return new SuccessResponse("Label definitions updated.", new { name = label, present = action == "label_add" });
                }
                case "profile_set":
                case "profile_set_active":
                {
                    object profiles = Read(settings, "profileSettings");
                    string id = ReadString(p, "profile_id", true);
                    if (string.IsNullOrEmpty((string)Invoke(profiles, "GetProfileName", new[] { typeof(string) }, id)))
                        throw new ArgumentException("Profile ID not found.");
                    string name = null, value = null;
                    if (action == "profile_set")
                    {
                        name = ReadString(p, "name", true);
                        var token = p.GetRaw("value");
                        if (token?.Type != JTokenType.String) throw new ArgumentException("'value' must be a string (empty is allowed).");
                        value = (string)token;
                        if (!Items(Invoke(profiles, "GetVariableNames", Type.EmptyTypes)).Cast<string>().Contains(name))
                            throw new ArgumentException("Profile variable not found; profile_set changes existing variables only.");
                    }
                    RecordMutation(settings);
                    if (action == "profile_set")
                        Invoke(profiles, "SetValue", new[] { typeof(string), typeof(string), typeof(string) }, id, name, value);
                    else settings.GetType().GetProperty("activeProfileId").SetValue(settings, id);
                    SaveChanges(settings);
                    return new SuccessResponse("Profile updated.", new { profile_id = id, name, value });
                }
                default:
                    throw new ArgumentException($"Unknown mutation '{action}'.");
            }
        }
    }
}
