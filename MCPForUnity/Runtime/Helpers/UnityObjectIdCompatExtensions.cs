using System.Reflection;
using UnityEngine;

public static class UnityObjectIdCompatExtensions
{
    public static int GetInstanceIDCompat(this Object obj)
    {
        if (obj == null)
        {
            return 0;
        }

        MethodInfo getInstanceId = typeof(Object).GetMethod("GetInstanceID", BindingFlags.Public | BindingFlags.Instance);
        if (getInstanceId != null)
        {
            object result = getInstanceId.Invoke(obj, null);
            if (result is int id)
            {
                return id;
            }
        }

        MethodInfo getEntityId = typeof(Object).GetMethod("GetEntityId", BindingFlags.Public | BindingFlags.Instance);
        if (getEntityId != null)
        {
            object entity = getEntityId.Invoke(obj, null);
            if (entity != null)
            {
                if (int.TryParse(entity.ToString(), out int parsed))
                {
                    return parsed;
                }

                return entity.GetHashCode();
            }
        }

        return obj.GetHashCode();
    }
}
