using System.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Animation
{
    internal static class AnimatorResolver
    {
        /// <summary>
        /// Resolves the Animator that read and control operations should act on: the one on
        /// <paramref name="go"/> itself, or the single Animator among its descendants.
        /// </summary>
        /// <remarks>
        /// Imported models keep their Animator on the model root, which is normally a child of
        /// the GameObject a caller names, so an exact-match lookup fails on the most common rig
        /// setup. Inactive descendants are included because a disabled rig is still readable.
        /// Operations that ADD an Animator must not use this - they need the exact target.
        /// </remarks>
        /// <param name="candidates">
        /// The descendant Animators found when the target carried none. Unity's descendant search
        /// is depth-first, so with several rigs under one wrapper it returns the first branch
        /// however deep, which is not the nearest rig and not what a caller would predict. More
        /// than one candidate therefore resolves to null and is reported, never guessed.
        /// </param>
        /// <returns>The resolved Animator, or null when there is none or the choice is ambiguous.</returns>
        public static Animator Find(GameObject go, out Animator[] candidates)
        {
            candidates = System.Array.Empty<Animator>();
            if (go == null)
                return null;

            var own = go.GetComponent<Animator>();
            if (own != null)
                return own;

            // go carries none, so every hit here is a descendant.
            candidates = go.GetComponentsInChildren<Animator>(true);
            return candidates.Length == 1 ? candidates[0] : null;
        }

        /// <summary>
        /// The error for a target whose Animator could not be resolved - missing, or ambiguous
        /// because several descendants carry one.
        /// </summary>
        public static object NotResolvedError(GameObject go, Animator[] candidates)
        {
            if (candidates != null && candidates.Length > 1)
            {
                string names = string.Join(", ", candidates.Select(a => $"'{a.gameObject.name}'"));
                return new
                {
                    success = false,
                    message = $"'{go.name}' has no Animator and {candidates.Length} of its children do " +
                              $"({names}). Target one of them directly."
                };
            }

            return new { success = false, message = $"No Animator component on '{go.name}' or its children" };
        }

        /// <summary>
        /// Names the object a response should report as changed. When resolution retargeted to a
        /// descendant, the caller is told so - otherwise the response claims the wrapper changed.
        /// </summary>
        /// <summary>
        /// A suffix disclosing that resolution retargeted to a descendant; empty when the target
        /// carried the Animator itself, so responses that already read correctly are untouched.
        /// </summary>
        public static string ResolvedSuffix(GameObject target, Animator resolved)
        {
            return resolved.gameObject == target
                ? string.Empty
                : $" (on '{resolved.gameObject.name}', resolved from '{target.name}')";
        }

        public static string Describe(GameObject target, Animator resolved)
        {
            return resolved.gameObject == target
                ? $"'{target.name}'"
                : $"'{resolved.gameObject.name}' (resolved from '{target.name}')";
        }
    }
}
