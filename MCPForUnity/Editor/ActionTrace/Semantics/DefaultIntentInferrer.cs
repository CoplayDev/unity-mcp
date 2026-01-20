using System;
using System.Collections.Generic;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Core.Models;

namespace MCPForUnity.Editor.ActionTrace.Semantics
{
    /// <summary>
    /// Default implementation of intent inference.
    /// Analyzes events to infer user intent based on type and context.
    /// </summary>
    public sealed class DefaultIntentInferrer : IIntentInferrer
    {
        /// <summary>
        /// Infer the intent behind an event.
        /// Uses event type and payload to determine user intent.
        /// </summary>
        public string Infer(EditorEvent evt, IReadOnlyList<EditorEvent> surrounding)
        {
            // For dehydrated events (Payload is null), intent cannot be inferred, return null
            if (evt.Payload == null)
                return null;

            // Normalize null to empty list for safe enumeration in helper methods
            surrounding ??= Array.Empty<EditorEvent>();

            return evt.Type switch
            {
                // Asset-related intents
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScript(evt) => "Coding",
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScene(evt) => "Creating Scene",
                EventTypes.AssetCreated or EventTypes.AssetImported when IsTexture(evt) => "Importing Texture",
                EventTypes.AssetCreated or EventTypes.AssetImported when IsAudio(evt) => "Importing Audio",
                EventTypes.AssetCreated or EventTypes.AssetImported when IsPrefab(evt) => "Creating Prefab",
                EventTypes.AssetCreated or EventTypes.AssetImported => "Importing Asset",

                // GameObject operations
                EventTypes.GameObjectCreated => "Adding GameObject",
                EventTypes.GameObjectDestroyed => "Removing GameObject",

                // Component operations
                EventTypes.ComponentAdded when IsRigidBody(evt) => "Adding Physics Component",
                EventTypes.ComponentAdded when IsCollider(evt) => "Adding Collider",
                EventTypes.ComponentAdded when IsScript(evt) => "Attaching Script",
                EventTypes.ComponentAdded => "Adding Component",
                EventTypes.ComponentRemoved => "Removing Component",

                // Scene operations
                EventTypes.SceneSaved => "Saving Scene",
                EventTypes.SceneOpened => "Opening Scene",
                EventTypes.NewSceneCreated => "Creating New Scene",

                // Build operations
                EventTypes.BuildStarted => "Build Started",
                EventTypes.BuildCompleted => "Build Completed",
                EventTypes.BuildFailed => "Build Failed",

                // Script operations
                EventTypes.ScriptCompiled => "Compiling Scripts",
                EventTypes.ScriptCompilationFailed => "Script Compilation Failed",

                // Hierarchy operations
                EventTypes.HierarchyChanged when IsReparenting(surrounding) => "Adjusting Hierarchy",
                EventTypes.HierarchyChanged when IsBatchOperation(surrounding) => "Batch Operation",
                EventTypes.HierarchyChanged => null,  // Too frequent, don't infer

                _ => null
            };
        }

        private static bool IsScript(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext) && ext != null)
                return ext.ToString() == ".cs";
            if (e.Payload.TryGetValue("component_type", out var type) && type != null)
                return type.ToString()?.Contains("MonoBehaviour") == true;
            return false;
        }

        private static bool IsScene(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext) && ext != null)
                return ext.ToString() == ".unity";
            return false;
        }

        private static bool IsPrefab(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext) && ext != null)
                return ext.ToString() == ".prefab";
            return false;
        }

        private static bool IsTexture(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext) && ext != null)
            {
                var extStr = ext.ToString();
                return extStr == ".png" || extStr == ".jpg" || extStr == ".jpeg" ||
                       extStr == ".psd" || extStr == ".tga" || extStr == ".exr";
            }
            if (e.Payload.TryGetValue("type", out var type) && type != null)
                return type.ToString()?.Contains("Texture") == true;
            return false;
        }

        private static bool IsAudio(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext) && ext != null)
            {
                var extStr = ext.ToString();
                return extStr == ".wav" || extStr == ".mp3" || extStr == ".ogg" ||
                       extStr == ".aif" || extStr == ".aiff";
            }
            return false;
        }

        private static bool IsRigidBody(EditorEvent e)
        {
            if (e.Payload.TryGetValue("component_type", out var type) && type != null)
            {
                var typeStr = type.ToString();
                return typeStr == "Rigidbody" || typeStr == "Rigidbody2D";
            }
            return false;
        }

        private static bool IsCollider(EditorEvent e)
        {
            if (e.Payload.TryGetValue("component_type", out var type) && type != null)
            {
                var typeStr = type.ToString();
                return typeStr?.Contains("Collider") == true;
            }
            return false;
        }

        private static bool IsReparenting(IReadOnlyList<EditorEvent> surrounding)
        {
            // If there are multiple hierarchy changes in quick succession,
            // it's likely a reparenting operation
            int count = 0;
            foreach (var e in surrounding)
            {
                if (e.Type == EventTypes.HierarchyChanged) count++;
                if (count >= 3) return true;
            }
            return false;
        }

        private static bool IsBatchOperation(IReadOnlyList<EditorEvent> surrounding)
        {
            // Many events of the same type suggest a batch operation
            if (surrounding.Count < 5) return false;

            var firstType = surrounding[0].Type;
            int sameTypeCount = 0;
            foreach (var e in surrounding)
            {
                if (e.Type == firstType) sameTypeCount++;
            }
            return sameTypeCount >= 5;
        }
    }
}
