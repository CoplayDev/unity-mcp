using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.AssetGen
{
    /// <summary>
    /// Conservative compatibility gate for fal's queue OpenAPI schemas. Only local references
    /// and the request/response shapes understood by our adapters are accepted. An unknown
    /// schema cannot pass preflight; metadata discovery alone never authorizes a paid submit.
    /// </summary>
    internal static class FalModelSchema
    {
        internal static bool SafeId(string id)
            => !string.IsNullOrEmpty(id) && id.Length <= 200
               && Regex.IsMatch(id, @"^[A-Za-z0-9_-]+(?:/[A-Za-z0-9_.-]+)+$")
               && !id.Split('/').Any(part => part == "." || part == "..");

        internal static bool IsCandidate(JObject model, string kind)
        {
            if ((string)model?["metadata"]?["status"] != "active") return false;
            string category = (string)model?["metadata"]?["category"];
            if (kind == "model") return category == "text-to-3d" || category == "image-to-3d";
            if (kind == "image") return (category == "text-to-image" || category == "image-to-image")
                && !((string)model?["endpoint_id"] ?? "").Contains("text-to-vector");
            return (string)model?["metadata"]?["category"] == "text-to-audio" && AudioUseCase(model) != null;
        }

        private static string AudioUseCase(JObject model)
        {
            string signals = ((string)model["endpoint_id"] + " " + string.Join(" ", model["metadata"]?["tags"]?.Values<string>() ?? Enumerable.Empty<string>())).ToLowerInvariant();
            if (signals.Contains("tts") || signals.Contains("speech") || signals.Contains("voice")) return null;
            if (signals.Contains("stable-audio")) return "Music + SFX";
            if (signals.Contains("sound-effect") || signals.Contains("sfx")) return "Sound effects";
            if (signals.Contains("music") || signals.Contains("lyria")) return "Background music";
            return "Audio";
        }

        internal static ModelEntry Discover(JObject model, string kind)
        {
            if (!IsCandidate(model, kind) || !SafeId((string)model["endpoint_id"])) return null;
            string id = (string)model["endpoint_id"];
            var metadata = model["metadata"];
            return new ModelEntry
            {
                Id = id, Provider = "fal", Kind = kind, Label = (string)metadata["display_name"] ?? id,
                UseCase = kind == "audio" ? AudioUseCase(model) : (string)metadata["category"],
                FromRefresh = true, Modes = new[] { ((string)metadata["category"]).StartsWith("image-") ? "image" : "text" },
                LicenseType = (string)metadata["license_type"], ModelUrl = "https://fal.ai/models/" + id,
                CommercialNote = "Review the model's license and provider terms before commercial use.",
            };
        }

        internal static ModelEntry Parse(JObject model, string kind, string verifiedAt, bool edit = false)
        {
            string id = (string)model?["endpoint_id"];
            var metadata = model?["metadata"] as JObject;
            if (!SafeId(id) || (string)metadata?["status"] != "active") return null;
            string category = (string)metadata?["category"];
            if (!IsCandidate(model, kind) || edit && category != "image-to-image") return null;
            bool imageInputRequired = category.StartsWith("image-", StringComparison.Ordinal);

            var api = model["openapi"] as JObject;
            var paths = api?["paths"] as JObject;
            if (paths == null) return null;
            // fal may expose an alias ID whose canonical OpenAPI path differs from that ID.
            var submissions = paths.Properties().Where(p => p.Value["post"] != null).ToArray();
            if (submissions.Length != 1) return null;
            var input = Resolve(api, submissions[0].Value["post"]?["requestBody"]?["content"]?["application/json"]?["schema"]);
            var resultPath = paths.Properties().FirstOrDefault(p => p.Name.EndsWith("/requests/{request_id}", StringComparison.Ordinal));
            var output = Resolve(api, resultPath?.Value["get"]?["responses"]?["200"]?["content"]?["application/json"]?["schema"]);
            var properties = input?["properties"] as JObject;
            if (properties == null || input["allOf"] != null || input["oneOf"] != null || input["anyOf"] != null) return null;

            string promptField = properties["prompt"] != null ? "prompt" : kind == "audio" && properties["text"] != null ? "text" : null;
            if (promptField == null && !imageInputRequired || promptField != null && !IsString(api, properties[promptField])) return null;

            var entry = new ModelEntry
            {
                Id = id, Provider = "fal", Kind = kind,
                Label = (string)metadata["display_name"] ?? id,
                PromptField = promptField, FromRefresh = true, VerifiedAt = verifiedAt,
                LicenseType = (string)metadata["license_type"],
                ModelUrl = "https://fal.ai/models/" + id,
                CommercialNote = "Review the model's license and provider terms before commercial use.",
                Modes = new[] { imageInputRequired ? "image" : "text" },
                SupportsNumImages = (string)Resolve(api, properties["num_images"])?["type"] == "integer"
                    && AcceptsOne(Resolve(api, properties["num_images"])),
                SupportsImageSize = SupportsDimensions(api, properties["image_size"]),
            };

            if (kind == "audio")
            {
                entry.UseCase = AudioUseCase(model);
                if (entry.UseCase == null) return null;
                if (!HasFile(api, output, "audio") && !HasFile(api, output, "audio_file")
                    && (string)Resolve(api, output?["properties"]?["audio_url"])?["type"] != "string") return null;

                foreach (string field in new[] { "seconds_total", "duration", "duration_seconds", "music_length_ms" })
                {
                    if (properties[field] == null) continue;
                    var duration = Numeric(api, properties[field]);
                    if (duration == null || duration["exclusiveMinimum"] != null || duration["exclusiveMaximum"] != null) return null;
                    float scale = field == "music_length_ms" ? 1000f : 1f;
                    float min = (float?)duration["minimum"] ?? 1f * scale;
                    float max = (float?)duration["maximum"] ?? 0f;
                    if (!Finite(min) || !Finite(max) || min < 0 || max <= 0 || min > max) return null;
                    entry.DurationField = field;
                    entry.DurationScale = scale;
                    entry.DurationIsInteger = (string)duration["type"] == "integer";
                    entry.MinDurationSeconds = min / scale;
                    entry.MaxDurationSeconds = max / scale;
                    // Keep the interactive default short, while respecting the live schema.
                    float defaultSeconds = ((float?)duration["default"] ?? 10f * scale) / scale;
                    if (!Finite(defaultSeconds)) return null;
                    entry.DefaultDurationSeconds = Math.Min(max / scale, Math.Max(min / scale, Math.Min(30f, defaultSeconds)));
                    break;
                }
            }
            else if (kind == "model")
            {
                entry.UseCase = imageInputRequired ? "Image -> 3D" : "Text -> 3D";
                entry.ModelOutputField = HasFile(api, output, "model_glb") ? "model_glb"
                    : HasFile(api, Resolve(api, output?["properties"]?["model_urls"]), "glb") ? "model_urls.glb" : null;
                if (entry.ModelOutputField == null) return null;
                if ((string)Resolve(api, properties["texture"])?["type"] == "boolean") entry.TextureField = "texture";
            }
            else
            {
                entry.UseCase = edit ? "Image editing" : "General image";
                if (properties["output_format"] != null)
                {
                    var format = Resolve(api, properties["output_format"]);
                    var formats = format?["enum"]?.Values<string>().ToArray();
                    entry.OutputFormat = formats?.Contains("png") == true ? "png"
                        : formats?.Contains("jpeg") == true ? "jpeg" : formats?.Contains("jpg") == true ? "jpg" : null;
                    if (entry.OutputFormat == null) return null;
                }
                var images = Resolve(api, output?["properties"]?["images"]);
                bool imageArray = (string)images?["type"] == "array"
                    && (string)Resolve(api, Resolve(api, images["items"])?["properties"]?["url"])?["type"] == "string";
                if (!imageArray && !HasFile(api, output, "image")) return null;
            }
            if (imageInputRequired)
            {
                string field = properties["image_urls"] != null ? "image_urls" : properties["image_url"] != null ? "image_url" : null;
                var imageInput = Resolve(api, field == null ? null : properties[field]);
                bool array = (string)imageInput?["type"] == "array" && (string)Resolve(api, imageInput["items"])?["type"] == "string";
                if (field == null || (!array && (string)imageInput?["type"] != "string")
                    || array && (((int?)imageInput["minItems"] ?? 1) > 1 || ((int?)imageInput["maxItems"] ?? 1) < 1)) return null;
                entry.ImageInputField = field;
                entry.ImageInputIsArray = array;
                if (kind == "image")
                {
                    entry.EditModelId = id;
                    entry.EditSupportsNumImages = entry.SupportsNumImages;
                    entry.EditOutputFormat = entry.OutputFormat;
                }
            }

            foreach (string required in input["required"]?.Values<string>() ?? Enumerable.Empty<string>())
            {
                if (required == promptField || required == entry.DurationField || imageInputRequired && required == entry.ImageInputField) continue;
                if (kind == "model" && required == entry.TextureField) continue;
                if (kind == "image" && required == "num_images" && entry.SupportsNumImages) continue;
                if (kind == "image" && required == "output_format" && entry.OutputFormat != null) continue;
                return null;
            }
            return entry;
        }

        private static bool Finite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);

        private static bool IsString(JObject api, JToken token)
        {
            var schema = Resolve(api, token);
            return (string)schema?["type"] == "string" || schema?["anyOf"] is JArray options
                && options.Any(option => (string)Resolve(api, option)?["type"] == "string");
        }

        private static bool AcceptsOne(JObject schema)
            => schema != null && ((float?)schema["minimum"] ?? 1f) <= 1f
               && ((float?)schema["maximum"] ?? 1f) >= 1f && schema["enum"] == null;

        private static bool SupportsDimensions(JObject api, JToken token, int depth = 0)
        {
            if (depth >= 8) return false;
            var schema = Resolve(api, token);
            if (schema == null) return false;
            if (schema["anyOf"] is JArray options) return options.Any(option => SupportsDimensions(api, option, depth + 1));
            return (string)Resolve(api, schema["properties"]?["width"])?["type"] == "integer"
                   && (string)Resolve(api, schema["properties"]?["height"])?["type"] == "integer";
        }

        private static bool HasFile(JObject api, JObject output, string field)
            => (string)Resolve(api, Resolve(api, output?["properties"]?[field])?["properties"]?["url"])?["type"] == "string";

        private static JObject Numeric(JObject api, JToken token, int depth = 0)
        {
            if (depth >= 8) return null;
            var schema = Resolve(api, token);
            if (schema == null) return null;
            string type = (string)schema["type"];
            if (type == "integer" || type == "number") return schema;
            return (schema["anyOf"] as JArray)?.Select(option => Numeric(api, option, depth + 1)).FirstOrDefault(option => option != null);
        }

        private static JObject Resolve(JObject api, JToken token, int depth = 0)
        {
            if (depth >= 8) return null;
            var schema = token as JObject;
            while (schema?["$ref"] != null)
            {
                if (++depth >= 8) return null;
                string reference = (string)schema["$ref"];
                const string prefix = "#/components/schemas/";
                if (!reference.StartsWith(prefix, StringComparison.Ordinal)) return null;
                schema = api?["components"]?["schemas"]?[reference.Substring(prefix.Length)] as JObject;
            }
            if (schema?["anyOf"] is JArray options)
            {
                var nonNull = options.Where(option => (string)option["type"] != "null").ToArray();
                if (nonNull.Length == 1) return Resolve(api, nonNull[0], depth + 1);
            }
            return schema?["$ref"] == null ? schema : null;
        }
    }
}
