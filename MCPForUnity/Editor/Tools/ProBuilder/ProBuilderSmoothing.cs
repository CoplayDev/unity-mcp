using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProBuilder
{
    internal static class ProBuilderSmoothing
    {
        internal static object SetSmoothing(JObject @params)
        {
            var pbMesh = ManageProBuilder.RequireProBuilderMesh(@params);
            var props = ManageProBuilder.ExtractProperties(@params);

            var faceIndicesToken = props["faceIndices"] ?? props["face_indices"];
            if (faceIndicesToken == null)
                return new ErrorResponse("faceIndices parameter is required.");

            var smoothingGroup = props["smoothingGroup"]?.Value<int>()
                              ?? props["smoothing_group"]?.Value<int>()
                              ?? 0;

            var faces = ManageProBuilder.GetFacesByIndices(pbMesh, faceIndicesToken);
            var smProp = ManageProBuilder._faceType.GetProperty("smoothingGroup");
            if (smProp == null)
                return new ErrorResponse("Could not find smoothingGroup property on Face type.");

            Undo.RecordObject(pbMesh, "Set Smoothing Groups");

            foreach (var face in faces)
                smProp.SetValue(face, smoothingGroup);

            ManageProBuilder.RefreshMesh(pbMesh);

            return new SuccessResponse($"Set smoothing group {smoothingGroup} on {faces.Length} face(s)", new
            {
                facesModified = faces.Length,
                smoothingGroup,
            });
        }

        internal static object AutoSmooth(JObject @params)
        {
            var pbMesh = ManageProBuilder.RequireProBuilderMesh(@params);
            var props = ManageProBuilder.ExtractProperties(@params);

            var angleThreshold = props["angleThreshold"]?.Value<float>()
                              ?? props["angle_threshold"]?.Value<float>()
                              ?? 30f;

            if (ManageProBuilder._smoothingType == null)
                return new ErrorResponse("Smoothing type not found in ProBuilder assembly.");

            var allFaces = ManageProBuilder.GetFacesArray(pbMesh);
            var facesList = (System.Collections.IList)allFaces;

            // Check for faceIndices to limit scope
            var faceIndicesToken = props["faceIndices"] ?? props["face_indices"];
            object facesToSmooth;
            if (faceIndicesToken != null)
            {
                facesToSmooth = ManageProBuilder.GetFacesByIndices(pbMesh, faceIndicesToken);
            }
            else
            {
                facesToSmooth = allFaces;
            }

            Undo.RecordObject(pbMesh, "Auto Smooth");

            // Smoothing.ApplySmoothingGroups(ProBuilderMesh mesh, IEnumerable<Face> faces, float angle)
            var applyMethod = ManageProBuilder._smoothingType.GetMethod("ApplySmoothingGroups",
                BindingFlags.Static | BindingFlags.Public);

            if (applyMethod != null)
            {
                applyMethod.Invoke(null, new object[] { pbMesh, facesToSmooth, angleThreshold });
            }
            else
            {
                // Fallback: manually set smoothing groups based on angle
                return new ErrorResponse("Smoothing.ApplySmoothingGroups method not found.");
            }

            ManageProBuilder.RefreshMesh(pbMesh);

            return new SuccessResponse($"Auto-smoothed with angle threshold {angleThreshold}°", new
            {
                angleThreshold,
                faceCount = facesList.Count,
            });
        }
    }
}
