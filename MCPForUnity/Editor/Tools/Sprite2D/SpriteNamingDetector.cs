using System.Collections.Generic;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal enum SpriteAnimCategory
    {
        Idle,
        Locomotion,   // walk or run: a candidate for a 1D blend tree.
        Jump,
        Combat,       // attack, slash, combo and the like: a trigger state.
        Object,       // open, close, activate: a single state.
        Generic,
    }

    internal class SpriteAnimEntry
    {
        public string ClipName;
        public SpriteAnimCategory Category;
        public bool Loop;
        public string TriggerName;
        public float BlendValue; // Position on the 1D blend tree: walk=1, run=2.
    }

    internal static class SpriteNamingDetector
    {
        public static SpriteAnimEntry Detect(string clipName)
        {
            var entry = new SpriteAnimEntry { ClipName = clipName };
            // The raw name, not a lowercased one: Words splits camelCase on char.IsUpper, so
            // 'heroAttack' pre-lowered collapsed to 'heroattack' and lost its Attack trigger.
            Categorize(clipName, entry);
            entry.Loop = AutoDetectLoop(entry.Category);
            return entry;
        }

        // ── Private ──────────────────────────────────────────────────────────

        private static void Categorize(string name, SpriteAnimEntry entry)
        {
            var words = Words(name);

            if (Has(words, "idle", "stand"))
            { entry.Category = SpriteAnimCategory.Idle; return; }

            if (words.Contains("walk"))
            { entry.Category = SpriteAnimCategory.Locomotion; entry.BlendValue = 1f; return; }

            if (Has(words, "run", "sprint"))
            { entry.Category = SpriteAnimCategory.Locomotion; entry.BlendValue = 2f; return; }

            string hit = Match(words, "jump", "fall", "land");
            if (hit != null)
            { entry.Category = SpriteAnimCategory.Jump; entry.TriggerName = Capitalize(hit); return; }

            hit = Match(words, "attack", "slash", "punch", "combo", "cast", "shoot");
            if (hit != null)
            { entry.Category = SpriteAnimCategory.Combat; entry.TriggerName = Capitalize(hit); return; }

            hit = Match(words, "open", "close", "activate", "die", "death", "hurt", "hit");
            if (hit != null)
            { entry.Category = SpriteAnimCategory.Object; entry.TriggerName = Capitalize(hit); return; }

            entry.Category    = SpriteAnimCategory.Generic;
            entry.TriggerName = Capitalize(name.ToLowerInvariant());
        }

        /// <summary>
        /// The words in a clip name, split on separators, camelCase humps and letter/digit
        /// boundaries. Raw substring matching instead files 'white_flash' under 'hit' and
        /// 'drunk_walk' under 'run', shaping the controller around the wrong category.
        /// </summary>
        private static HashSet<string> Words(string name)
        {
            var words = new HashSet<string>();
            var word  = new System.Text.StringBuilder();

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool breaks = !char.IsLetterOrDigit(c)
                    || (i > 0 && char.IsUpper(c)  && char.IsLower(name[i - 1]))
                    // End of an acronym: 'heroXMLAttack' has no lower-to-upper boundary at the
                    // 'A', so the tail read as 'xmlattack' and lost its keyword.
                    || (i > 0 && i + 1 < name.Length
                        && char.IsUpper(c) && char.IsUpper(name[i - 1]) && char.IsLower(name[i + 1]))
                    || (i > 0 && char.IsDigit(c)  && char.IsLetter(name[i - 1]))
                    || (i > 0 && char.IsLetter(c) && char.IsDigit(name[i - 1]));

                if (breaks && word.Length > 0)
                {
                    words.Add(word.ToString().ToLowerInvariant());
                    word.Clear();
                }
                if (char.IsLetterOrDigit(c)) word.Append(c);
            }
            if (word.Length > 0) words.Add(word.ToString().ToLowerInvariant());

            return words;
        }

        private static bool Has(HashSet<string> words, params string[] keys) =>
            Match(words, keys) != null;

        /// <summary>The first key the name contains, so the trigger is named after the action.</summary>
        private static string Match(HashSet<string> words, params string[] keys)
        {
            foreach (string k in keys)
                if (words.Contains(k)) return k;
            return null;
        }

        private static bool AutoDetectLoop(SpriteAnimCategory cat) =>
            cat == SpriteAnimCategory.Idle || cat == SpriteAnimCategory.Locomotion;

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
