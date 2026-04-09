using System.Reflection;
using UnityEngine;

public static class UnityObjectIdCompatExtensions
{
    private static readonly MethodInfo GetInstanceIdMethod = typeof(Object).GetMethod("GetInstanceID", BindingFlags.Public | BindingFlags.Instance);
    private static readonly MethodInfo GetEntityIdMethod = typeof(Object).GetMethod("GetEntityId", BindingFlags.Public | BindingFlags.Instance);

    public static int GetInstanceIDCompat(this Object obj)
    {
        if (obj == null)
        {
            return 0;
        }

        if (GetInstanceIdMethod != null)
        {
            object result = GetInstanceIdMethod.Invoke(obj, null);
            if (result is int id)
            {
                return id;
            }
        }

        if (GetEntityIdMethod != null)
        {
            object entity = GetEntityIdMethod.Invoke(obj, null);
            if (entity != null)
            {
                if (int.TryParse(entity.ToString(), out int parsed))
                {
                    return parsed;
                }
            }
        }

        Debug.LogWarning($"[UnityObjectIdCompatExtensions] Failed to resolve Unity object ID for '{obj.name}'. Returning 0.");
        return 0;
    }
}
