using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    [McpForUnityTool("manage_physics", AutoRegister = false, Group = "core")]
    public static class ManagePhysics
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                switch (action)
                {
                    // --- Health check ---
                    case "ping":
                        return PhysicsSettingsOps.Ping(@params);

                    // --- Settings actions ---
                    case "get_settings":
                        return PhysicsSettingsOps.GetSettings(@params);
                    case "set_settings":
                        return PhysicsSettingsOps.SetSettings(@params);

                    // --- Collision matrix actions ---
                    case "get_collision_matrix":
                        return CollisionMatrixOps.GetCollisionMatrix(@params);
                    case "set_collision_matrix":
                        return CollisionMatrixOps.SetCollisionMatrix(@params);

                    // --- Physics material actions ---
                    case "create_physics_material":
                        return PhysicsMaterialOps.Create(@params);
                    case "configure_physics_material":
                        return PhysicsMaterialOps.Configure(@params);
                    case "assign_physics_material":
                        return PhysicsMaterialOps.Assign(@params);

                    // --- Joint actions ---
                    case "add_joint":
                        return JointOps.AddJoint(@params);
                    case "configure_joint":
                        return JointOps.ConfigureJoint(@params);
                    case "remove_joint":
                        return JointOps.RemoveJoint(@params);

                    // --- Query actions ---
                    case "raycast":
                        return PhysicsQueryOps.Raycast(@params);
                    case "raycast_all":
                        return PhysicsQueryOps.RaycastAll(@params);
                    case "linecast":
                        return PhysicsQueryOps.Linecast(@params);
                    case "shapecast":
                        return PhysicsQueryOps.Shapecast(@params);
                    case "overlap":
                        return PhysicsQueryOps.Overlap(@params);

                    // --- Force actions ---
                    case "apply_force":
                        return PhysicsForceOps.ApplyForce(@params);

                    // --- Rigidbody actions ---
                    case "get_rigidbody":
                        return PhysicsRigidbodyOps.GetRigidbody(@params);
                    case "configure_rigidbody":
                        return PhysicsRigidbodyOps.ConfigureRigidbody(@params);

                    // --- Validation ---
                    case "validate":
                        return PhysicsValidationOps.Validate(@params);

                    // --- Simulation ---
                    case "simulate_step":
                        return PhysicsSimulationOps.SimulateStep(@params);

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: ping, "
                            + "get_settings, set_settings, "
                            + "get_collision_matrix, set_collision_matrix, "
                            + "create_physics_material, configure_physics_material, assign_physics_material, "
                            + "add_joint, configure_joint, remove_joint, "
                            + "raycast, raycast_all, linecast, shapecast, overlap, "
                            + "apply_force, get_rigidbody, configure_rigidbody, validate, simulate_step.");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManagePhysics] Action '{action}' failed: {ex}");
                return new ErrorResponse(
                    $"Error in action '{action}': {ex.Message}",
                    new { stackTrace = ex.StackTrace }
                );
            }
        }
    }
}
