#if !UNITY_2022_2_OR_NEWER
using System;
using System.Reflection;
#endif
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
#if UNITY_2022_2_OR_NEWER
using UnityEditor.AssetImporters;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal class ModelUtilityService : IModelUtilityService
    {
        private IAssetUtilityService _assetUtility;

#if !UNITY_2022_2_OR_NEWER
        // Rig fields
        private const string RigImportWarningsField = "m_RigImportWarnings";
        private const string RigImportErrorsField = "m_RigImportErrors";

        // Animation fields
        private const string AnimationImportWarningsField = "m_AnimationImportWarnings";
        private const string AnimationImportErrorsField = "m_AnimationImportErrors";

        private static Editor _modelImporterEditor = null;
#endif

        public ModelUtilityService(IAssetUtilityService assetUtility)
        {
            _assetUtility = assetUtility;
        }

        public Dictionary<Object, List<LogEntry>> GetImportLogs(params Object[] models)
        {
#if UNITY_2022_2_OR_NEWER
            return GetImportLogsDefault(models);
#else
            return GetImportLogsLegacy(models);
#endif
        }

#if UNITY_2022_2_OR_NEWER
        private Dictionary<Object, List<LogEntry>> GetImportLogsDefault(params Object[] models)
        {
            var modelsWithLogs = new Dictionary<Object, List<LogEntry>>();

            foreach (var model in models)
            {
                var modelLogs = new List<LogEntry>();

                var importLog = AssetImporter.GetImportLog(_assetUtility.ObjectToAssetPath(model));

                if (importLog == null)
                    continue;

                var entries = importLog.logEntries.Where(x => x.flags.HasFlag(ImportLogFlags.Warning) || x.flags.HasFlag(ImportLogFlags.Error));
                foreach (var entry in entries)
                {
                    var severity = entry.flags.HasFlag(ImportLogFlags.Error) ? LogType.Error : LogType.Warning;
                    modelLogs.Add(new LogEntry() { Message = entry.message, Severity = severity });
                }

                if (modelLogs.Count > 0)
                    modelsWithLogs.Add(model, modelLogs);
            }

            return modelsWithLogs;
        }
#endif

#if !UNITY_2022_2_OR_NEWER
        private Dictionary<Object, List<LogEntry>> GetImportLogsLegacy(params Object[] models)
        {
            var modelsWithLogs = new Dictionary<Object, List<LogEntry>>();

            foreach (var model in models)
            {
                var modelLogs = new List<LogEntry>();

                // Load the Model Importer
                var modelImporter = _assetUtility.GetAssetImporter(model) as ModelImporter;

                var editorAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name.Equals("UnityEditor"));

                var modelImporterEditorType = editorAssembly.GetType("UnityEditor.ModelImporterEditor");

                // Load its Model Importer Editor
                Editor.CreateCachedEditorWithContext(new Object[] { modelImporter }, model, modelImporterEditorType, ref _modelImporterEditor);

                // Find the base type
                var modelImporterEditorTypeBase = _modelImporterEditor.GetType().BaseType;

                // Get the tabs value
                var tabsArrayType = modelImporterEditorTypeBase.GetRuntimeProperties().FirstOrDefault(x => x.Name == "tabs");
                var tabsArray = (Array)tabsArrayType.GetValue(_modelImporterEditor);

                // Get the tabs (Model | Rig | Animation | Materials)
                var rigTab = tabsArray.GetValue(1);
                var animationTab = tabsArray.GetValue(2);

                var rigErrorsCheckSuccess = CheckFieldForSerializedProperty(rigTab, RigImportErrorsField, out var rigErrors);
                var rigWarningsCheckSuccess = CheckFieldForSerializedProperty(rigTab, RigImportWarningsField, out var rigWarnings);
                var animationErrorsCheckSuccess = CheckFieldForSerializedProperty(animationTab, AnimationImportErrorsField, out var animationErrors);
                var animationWarningsCheckSuccess = CheckFieldForSerializedProperty(animationTab, AnimationImportWarningsField, out var animationWarnings);

                if (!rigErrorsCheckSuccess || !rigWarningsCheckSuccess || !animationErrorsCheckSuccess || !animationWarningsCheckSuccess)
                    UnityEngine.Debug.LogWarning($"An error was encountered when checking import logs for model '{model.name}'");

                if (!string.IsNullOrEmpty(rigWarnings))
                    modelLogs.Add(new LogEntry() { Message = rigWarnings, Severity = LogType.Warning });
                if (!string.IsNullOrEmpty(rigErrors))
                    modelLogs.Add(new LogEntry() { Message = rigErrors, Severity = LogType.Error });
                if (!string.IsNullOrEmpty(animationWarnings))
                    modelLogs.Add(new LogEntry() { Message = animationWarnings, Severity = LogType.Warning });
                if (!string.IsNullOrEmpty(animationErrors))
                    modelLogs.Add(new LogEntry() { Message = animationErrors, Severity = LogType.Error });

                if (modelLogs.Count > 0)
                    modelsWithLogs.Add(model, modelLogs);
            }

            return modelsWithLogs;
        }

        private static bool CheckFieldForSerializedProperty(object source, string propertyName, out string message)
        {
            message = string.Empty;

            try
            {
                var propertyType = source.GetType().GetRuntimeFields().FirstOrDefault(x => x.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
                var propertyValue = propertyType.GetValue(source) as SerializedProperty;
                message = propertyValue.stringValue;
                return true;
            }
            catch
            {
                return false;
            }
        }
#endif
    }
}