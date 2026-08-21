using System;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    /// <summary>
    /// Reads the sprite tool's numeric parameters off the request without throwing and
    /// without rounding.
    ///
    /// It exists because ToObject&lt;T&gt; does both, and the difference was measured on
    /// 2026-08-21 by sending values through the live tool:
    ///
    ///   cols / rows / frame_width / frame_height = 2147483648  -> OverflowException
    ///   start_frame = 2147483648                               -> OverflowException
    ///   start_frame = 2.7                                      -> silently became 3
    ///   fps = NaN                                              -> clip written with NaN times
    ///
    /// Nothing between ManageSprite.HandleCommand and the bridge catches, so each of those
    /// overflows left the tool as a transport failure rather than a named refusal. The
    /// rounding is the worse half: the caller asked for something the tool cannot do and
    /// got a success it has no way to question.
    ///
    /// This is one file rather than a private helper because the same parameters are read
    /// in three places - the grid in SpriteImportSetup, the frame range and rate in
    /// SpriteClipBuilder - and a guard that lives in one of them is how the first version
    /// of this fix closed one path and left the class open.
    /// </summary>
    internal static class SpriteParams
    {
        /// <summary>
        /// Reads an optional whole number. Returns false with a caller-facing reason when
        /// the value is present but is not a whole number an int can hold.
        /// </summary>
        internal static bool TryReadWholeNumber(JObject @params, string key, int fallback,
                                                out int value, out string error)
        {
            value = fallback;
            error = null;

            JToken token = @params?[key];
            // An explicit JSON null arrives as a JValue, not a C# null, so it has to be
            // named here: it means "unset", which is the default.
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.Integer)
            {
                error = $"'{key}' must be a whole number; got {token.Type.ToString().ToLowerInvariant()}.";
                return false;
            }

            long raw;
            try
            {
                raw = token.Value<long>();
            }
            catch (Exception)
            {
                // An integer too large for long parses as a BigInteger, still typed Integer,
                // and the read throws. That is out of range by definition.
                error = $"'{key}' must fit in a 32-bit integer.";
                return false;
            }

            if (raw < int.MinValue || raw > int.MaxValue)
            {
                // "must fit in a 32-bit integer", not "must be between X and Y": the range
                // guards elsewhere phrase themselves the same way, so a test asserting the
                // generic wording passes when this conversion wraps and a LATER guard does
                // the refusing. Measured on the page_size and cols tests, which both did.
                error = $"'{key}' must fit in a 32-bit integer; got {raw}.";
                return false;
            }

            value = (int)raw;
            return true;
        }

        /// <summary>
        /// Reads an optional flag. Measured 2026-08-21: `loop: "maybe"` raised an uncaught
        /// FormatException and `loop: 2` was accepted silently, because ToObject&lt;bool?&gt;
        /// converts rather than validates. `loop` needs this because it hides inside the
        /// untyped `clips` array, where nothing above C# looks at it.
        ///
        /// The top-level flags do not, but not for the reason it first looked like. FastMCP
        /// does not REFUSE a non-boolean `overwrite`; it coerces one - measured 2026-08-21
        /// through server.call_tool: 'yes' and 1 both arrive here as a real bool, 'off'
        /// arrives as false, and only a value Pydantic cannot read as a boolean (2) is
        /// refused. Either way what reaches C# is already the right type. The same holds
        /// for the top-level integers: '4' arrives as 4 and 2.7 is refused outright, so of
        /// the classes below only an out-of-int-range integer reaches C# from a real
        /// caller - Python ints have no ceiling. The guards still cover the rest, because
        /// this layer owns the conversion and a caller-facing refusal is not something to
        /// leave to the layer above.
        /// </summary>
        internal static bool TryReadBool(JObject @params, string key, bool fallback,
                                         out bool value, out string error)
        {
            value = fallback;
            error = null;

            JToken token = @params?[key];
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.Boolean)
            {
                error = $"'{key}' must be true or false; got {token.Type.ToString().ToLowerInvariant()}.";
                return false;
            }

            value = token.Value<bool>();
            return true;
        }

        /// <summary>
        /// Reads an optional rate. Rejects NaN and both infinities, which pass every
        /// comparison-based guard: `fps &lt;= 0f` is false for NaN, so a NaN rate reached
        /// the keyframe arithmetic and produced a clip whose frame times were all NaN.
        /// </summary>
        internal static bool TryReadFiniteFloat(JObject @params, string key, float fallback,
                                                out float value, out string error)
        {
            value = fallback;
            error = null;

            JToken token = @params?[key];
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                error = $"'{key}' must be a number; got {token.Type.ToString().ToLowerInvariant()}.";
                return false;
            }

            double raw;
            try
            {
                raw = token.Value<double>();
            }
            catch (Exception)
            {
                error = $"'{key}' is out of range for a number.";
                return false;
            }

            if (double.IsNaN(raw) || double.IsInfinity(raw))
            {
                error = $"'{key}' must be a finite number.";
                return false;
            }

            // Read as double first so a value outside float's range is refused by name
            // rather than silently becoming an infinity on the cast.
            if (raw > float.MaxValue || raw < -float.MaxValue)
            {
                error = $"'{key}' is out of range for a 32-bit float.";
                return false;
            }

            value = (float)raw;
            return true;
        }
    }
}
