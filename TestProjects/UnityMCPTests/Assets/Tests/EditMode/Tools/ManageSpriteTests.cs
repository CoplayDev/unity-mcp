using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MCPForUnity.Editor.Tools.Sprite2D;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageSpriteTests
    {
        private const string TempRoot = "Assets/Temp/ManageSpriteTests";

        // Each cell is 16x16, so a 4x2 sheet is 64x32. Small enough to import fast,
        // big enough that a wrong row/column order is visible in the rects.
        private const int Cell = 16;

        [SetUp]
        public void SetUp() => EnsureFolder(TempRoot);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.DeleteAsset(TempRoot);
            CleanupEmptyParentFolders(TempRoot);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Writes a real PNG into the project and imports it, so the tools run against
        /// an actual TextureImporter rather than a stand-in.
        /// </summary>
        private static string CreateSheet(string name, int cols, int rows)
        {
            var tex = new Texture2D(cols * Cell, rows * Cell, TextureFormat.RGBA32, false);
            var pixels = new Color32[tex.width * tex.height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 0, 0, 255);
            tex.SetPixels32(pixels);
            tex.Apply();

            string assetPath = $"{TempRoot}/{name}.png";
            string sysPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, assetPath);
            File.WriteAllBytes(sysPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            // A fixture that quietly produces less than it claims weakens every test built on
            // it, so it states what it can: the asset exists. Its dimensions cannot be asserted
            // here - the texture is still Default-type at this point, and that import rescales a
            // non-power-of-two sheet (96px is read back as 128px), which is the very behaviour
            // slice_sheet works around. The frame count after slicing is the real postcondition
            // and Slice() below asserts it.
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath),
                $"fixture: {assetPath} did not import");
            return assetPath;
        }


        /// <summary>
        /// A square PNG of incompressible noise, used to exceed the inline-image ceiling.
        /// Written through the same import path as CreateSheet.
        /// </summary>
        private static string CreateNoiseSheet(string name, int side, bool assertOverCeiling = true)
        {
            var tex = new Texture2D(side, side, TextureFormat.RGBA32, false);
            var pixels = new Color32[side * side];
            uint state = 0x13579BDFu;   // fixed seed: the file size must not vary between runs
            for (int i = 0; i < pixels.Length; i++)
            {
                state = state * 1664525u + 1013904223u;
                pixels[i] = new Color32((byte)(state >> 24), (byte)(state >> 16), (byte)(state >> 8), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            string assetPath = $"{TempRoot}/{name}.png";
            string sysPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, assetPath);
            File.WriteAllBytes(sysPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            // Both callers depend on where this lands relative to the 4 MB ceiling in
            // SpriteImportSetup, so the fixture asserts the side it was asked for rather
            // than trusting the compressor. Changing that ceiling breaks these two lines
            // loudly, which is the intent.
            if (assertOverCeiling)
                Assert.Greater(new FileInfo(sysPath).Length, 4 * 1024 * 1024,
                    "fixture: the noise sheet must exceed the inline-image ceiling");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return assetPath;
        }

        private static JObject Run(JObject p) => ToJObject(ManageSprite.HandleCommand(p));

        /// <summary>
        /// The failure text, whichever key it arrived under. ErrorResponse serialises it as
        /// "error", while anonymous failures elsewhere in the codebase use "message", and
        /// Server/src/services/tools/__init__.py reads both. Pinning one key here would test
        /// the response shape rather than the behaviour.
        /// </summary>
        private static string ErrorText(JObject result) =>
            result.Value<string>("error") ?? result.Value<string>("message") ?? "";

        private static JObject Slice(string path, int cols, int rows)
        {
            var result = Run(new JObject
            {
                ["action"] = "slice_sheet",
                ["path"] = path,
                ["cols"] = cols,
                ["rows"] = rows,
            });
            // Only assert the postcondition for a call that was meant to succeed; the refusal
            // tests call this helper too and check the failure themselves.
            if (result.Value<bool>("success"))
                Assert.AreEqual(cols * rows, SpritesOf(path).Length,
                    "fixture: slice_sheet produced fewer frames than the grid asked for");
            return result;
        }

        /// <summary>The sliced frames, in the natural order their names imply.</summary>
        private static Sprite[] SpritesOf(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(s => int.Parse(s.name.Split('_').Last()))
                .ToArray();

        // =====================================================================
        // Dispatch
        // =====================================================================

        [Test]
        public void HandleCommand_MissingAction_ReturnsError()
        {
            var result = Run(new JObject());
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("'action' is required"));
        }

        [Test]
        public void HandleCommand_UnknownAction_NamesTheValidOnes()
        {
            var result = Run(new JObject { ["action"] = "not_an_action" });
            Assert.IsFalse(result.Value<bool>("success"));
            // Listing the alternatives is the difference between a dead end and a retry.
            Assert.That(ErrorText(result), Does.Contain("slice_sheet"));
            Assert.That(ErrorText(result), Does.Contain("full_setup"));
        }

        // =====================================================================
        // get_info
        // =====================================================================

        [Test]
        public void GetInfo_MissingPath_ReturnsError()
        {
            var result = Run(new JObject { ["action"] = "get_info" });
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("'path' is required"),
                "success alone would also be false on the importer-not-found branch");
        }

        [Test]
        public void GetInfo_PathIsNotATexture_ReturnsError()
        {
            var result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = $"{TempRoot}/nothing_here.png",
            });
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("TextureImporter"));
        }

        [Test]
        public void GetInfo_ReportsTheTextureDimensions()
        {
            string path = CreateSheet("info", 4, 2);
            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(4 * Cell, result.Value<int>("width"));
            Assert.AreEqual(2 * Cell, result.Value<int>("height"));
        }

        [Test]
        public void GetInfo_OnAnUnslicedSheet_ReportsNoSlices()
        {
            string path = CreateSheet("unsliced", 4, 2);
            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });

            Assert.IsNotNull(result["slice_count"],
                "an absent field also reads as 0, so the field itself has to be there");
            Assert.AreEqual(0, result.Value<int>("slice_count"));
        }

        [Test]
        public void GetInfo_AfterSlicing_ReportsEverySlice()
        {
            string path = CreateSheet("sliced", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });
            Assert.AreEqual(8, result.Value<int>("slice_count"));
        }

        [Test]
        public void GetInfo_ModestSheet_ComesBackInOnePageWithNoCursor()
        {
            string path = CreateSheet("onepage", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });

            // The point of the default page size is that a sheet anyone would slice through
            // this tool never meets paging at all. If this turns red the default shrank.
            Assert.AreEqual(8, ((JArray)result["slices"]).Count);
            Assert.IsNull(result["next_cursor"].Value<int?>(),
                "a finished list has no next cursor");
        }

        [Test]
        public void GetInfo_MoreSlicesThanThePage_ReturnsOnePageAndPointsAtTheRest()
        {
            string path = CreateSheet("paged", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["page_size"] = 3,
            });

            Assert.AreEqual(3, ((JArray)result["slices"]).Count, "the page is bounded");
            Assert.AreEqual(8, result.Value<int>("slice_count"),
                "slice_count stays the total, not the size of the page");
            Assert.AreEqual(3, result["next_cursor"].Value<int?>());
        }

        [Test]
        public void GetInfo_WalkingTheCursor_VisitsEverySliceOnceAndThenStops()
        {
            string path = CreateSheet("walk", 4, 2);
            Slice(path, 4, 2);

            var seen = new List<string>();
            int? cursor = 0;
            // Bounded so a cursor that never advances fails as a wrong count rather than
            // hanging the whole EditMode run.
            for (int page = 0; page < 10 && cursor != null; page++)
            {
                var result = Run(new JObject
                {
                    ["action"] = "get_info",
                    ["path"] = path,
                    ["page_size"] = 3,
                    ["cursor"] = cursor.Value,
                });
                seen.AddRange(((JArray)result["slices"]).Select(t => t.Value<string>("name")));
                cursor = result["next_cursor"].Value<int?>();
            }

            Assert.IsNull(cursor, "the walk has to terminate on its own");
            Assert.AreEqual(8, seen.Count, "no slice returned twice and none skipped");
            CollectionAssert.AllItemsAreUnique(seen);
        }

        [Test]
        public void GetInfo_CursorAtTheEnd_ReturnsAnEmptyPageRatherThanAnError()
        {
            string path = CreateSheet("tail", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["cursor"] = 8,
            });

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(0, ((JArray)result["slices"]).Count);
        }

        [Test]
        public void GetInfo_NegativeCursor_IsRefusedRatherThanReadAsPageOne()
        {
            string path = CreateSheet("negcursor", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["cursor"] = -3,
            });

            // Skip(-3) yields the whole list, so without the guard this call answers with
            // every slice and reports success - the failure mode is a right-looking answer,
            // which is why the assertion is on the refusal and not on the count.
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("cursor"));
        }

        [Test]
        public void GetInfo_CursorPastTheEnd_IsRefused()
        {
            string path = CreateSheet("farcursor", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["cursor"] = 9,
            });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("cursor"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(4097)]
        public void GetInfo_PageSizeOutsideItsRange_IsRefused(int pageSize)
        {
            string path = CreateSheet($"pagesize{pageSize}", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["page_size"] = pageSize,
            });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("page_size"));
        }

        [Test]
        public void GetInfo_PagesAfterTheFirst_DropTheImageAndSayWhy()
        {
            string path = CreateSheet("imageonce", 4, 2);
            Slice(path, 4, 2);

            var first = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["page_size"] = 3,
            });
            var second = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["page_size"] = 3,
                ["cursor"] = 3,
            });

            Assert.IsNotNull(first.Value<string>("image_base64"),
                "fixture: the first page is supposed to carry the image");
            Assert.IsNull(second.Value<string>("image_base64"));
            Assert.That(second.Value<string>("image_omitted_reason"), Does.Contain("first page"));
        }

        // =====================================================================
        // slice_sheet
        // =====================================================================

        [Test]
        public void SliceSheet_WithoutColsOrFrameWidth_ReturnsError()
        {
            string path = CreateSheet("nogrid", 4, 2);
            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("frame_width"));
        }

        [Test]
        public void SliceSheet_ProducesOneSpritePerGridCell()
        {
            string path = CreateSheet("grid", 4, 2);
            var result = Slice(path, 4, 2);

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(8, result.Value<int>("total_frames"));
            // The reported count is a claim; the sub-assets on disk are the fact.
            Assert.AreEqual(8, SpritesOf(path).Length);
        }

        [Test]
        public void SliceSheet_FrameZeroIsTheTopLeftCell()
        {
            // Sprite sheets are read left-to-right, top-to-bottom, but Unity's texture
            // origin is bottom-left. Getting this backwards silently plays the animation
            // in the wrong order, which no success flag would reveal.
            string path = CreateSheet("order", 4, 2);
            Slice(path, 4, 2);

            var first = SpritesOf(path).First();
            Assert.AreEqual(0, (int)first.rect.x, "frame 0 should sit at the left edge");
            Assert.AreEqual(Cell, (int)first.rect.y, "frame 0 should sit on the top row");
        }

        [Test]
        public void SliceSheet_LastFrameIsTheBottomRightCell()
        {
            string path = CreateSheet("order2", 4, 2);
            Slice(path, 4, 2);

            var last = SpritesOf(path).Last();
            Assert.AreEqual(3 * Cell, (int)last.rect.x);
            Assert.AreEqual(0, (int)last.rect.y);
        }

        [Test]
        public void SliceSheet_EveryFrameHasTheCellSize()
        {
            string path = CreateSheet("size", 4, 2);
            Slice(path, 4, 2);

            foreach (var s in SpritesOf(path))
            {
                Assert.AreEqual(Cell, (int)s.rect.width, $"{s.name} width");
                Assert.AreEqual(Cell, (int)s.rect.height, $"{s.name} height");
            }
        }

        [Test]
        public void SliceSheet_NonPowerOfTwoSheet_KeepsEveryFrame()
        {
            // 6 cells of 16px is 96px wide, which is not a power of two. A Default-type
            // import rescales it to 128, and a grid measured there is 21px per cell - the
            // last two frames then fall outside the real texture and Unity discards them,
            // reporting success all the same.
            string path = CreateSheet("npot", 6, 1);
            var result = Slice(path, 6, 1);

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(Cell, result.Value<int>("frame_width"),
                "the grid must be measured against the sheet's real width");
            Assert.AreEqual(6, SpritesOf(path).Length, "no frame may be dropped");
        }

        [Test]
        public void SliceSheet_TextureAlreadyConvertedToSprite_KeepsEveryFrame()
        {
            // The conversion above is skipped when the texture is already a Sprite, so this
            // pins the other branch. It survives every mutation of the slicing code, because
            // Unity ignores npotScale on sprite textures - it is a boundary guard, not
            // evidence for the fix.
            string path = CreateSheet("npot_preset", 6, 1);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            var result = Slice(path, 6, 1);
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(6, SpritesOf(path).Length, "no frame may be dropped");
        }

        [Test]
        public void SliceSheet_FrameWidthAloneDerivesTheColumnCount()
        {
            string path = CreateSheet("derive", 4, 1);
            var result = Run(new JObject
            {
                ["action"] = "slice_sheet",
                ["path"] = path,
                ["frame_width"] = Cell,
                ["frame_height"] = Cell,
            });

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(4, result.Value<int>("cols"));
            Assert.AreEqual(4, SpritesOf(path).Length);
        }

        [Test]
        public void SliceSheet_BaseNameOverridesTheFileName()
        {
            string path = CreateSheet("filename", 2, 1);
            Run(new JObject
            {
                ["action"] = "slice_sheet",
                ["path"] = path,
                ["cols"] = 2,
                ["base_name"] = "hero",
            });

            Assert.That(SpritesOf(path).Select(s => s.name), Is.EquivalentTo(new[] { "hero_0", "hero_1" }));
        }

        [Test]
        public void SliceSheet_FrameWiderThanTheTexture_ReportsSliceEmpty()
        {
            string path = CreateSheet("toobig", 2, 1);
            var result = Run(new JObject
            {
                ["action"] = "slice_sheet",
                ["path"] = path,
                ["frame_width"] = 4096,
            });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("SLICE_EMPTY"));
        }

        [Test]
        public void SliceSheet_ZeroRows_FailsWithAMessageInsteadOfThrowing()
        {
            // rows is read as `?? 1`, which only covers a missing key - an explicit 0
            // survives and reaches the `texH / rows` division.
            string path = CreateSheet("zerorows", 4, 1);

            JObject result = null;
            Assert.DoesNotThrow(() => result = Slice(path, 4, 0),
                "a bad grid value must come back as an error, not an exception");
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void SliceSheet_FrameWiderThanTheTexture_LeavesTheTextureTypeAlone()
        {
            // This refusal is only reachable after the texture has been measured, so it is the
            // form of the class that moving the argument checks earlier could not close.
            string path = CreateSheet("restore_w", 2, 1);
            var before = ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;

            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path, ["frame_width"] = 4096 });
            Assert.IsFalse(result.Value<bool>("success"));

            Assert.AreEqual(before, ((TextureImporter)AssetImporter.GetAtPath(path)).textureType,
                "a refused request must not leave the texture converted behind it");
        }

        [Test]
        public void SliceSheet_FrameTallerThanTheTexture_LeavesTheTextureTypeAlone()
        {
            // The second form of the same class: the height axis reaches the same refusal.
            string path = CreateSheet("restore_h", 2, 1);
            var before = ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;

            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                           ["frame_width"] = Cell, ["frame_height"] = 4096 });
            Assert.IsFalse(result.Value<bool>("success"));

            Assert.AreEqual(before, ((TextureImporter)AssetImporter.GetAtPath(path)).textureType,
                "a refused request must not leave the texture converted behind it");
        }

        [Test]
        public void SliceSheet_MoreColumnsThanPixels_IsRefused()
        {
            // 64 columns across 32 pixels derives a 0-wide frame. The bounds product is then
            // 0, which passes any "does it fit" test, and 64 degenerate rects were written
            // with the call reporting success.
            string path = CreateSheet("degenerate_w", 2, 1);   // 32x16
            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path, ["cols"] = 64 });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("SLICE_OUT_OF_BOUNDS"));
            Assert.AreEqual(0, SpritesOf(path).Length, "no degenerate frame may be written");
        }

        [Test]
        public void SliceSheet_MoreRowsThanPixels_IsRefused()
        {
            string path = CreateSheet("degenerate_h", 2, 1);   // 32x16
            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                           ["cols"] = 2, ["rows"] = 32 });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("SLICE_OUT_OF_BOUNDS"));
        }

        [Test]
        public void SliceSheet_GridProductThatOverflowsInt_IsStillRefused()
        {
            // 65536 * 65536 wraps to 0 in unchecked 32-bit arithmetic and slipped under the
            // comparison; the product is computed in long for that reason.
            string path = CreateSheet("overflow", 2, 1);
            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                           ["cols"] = 65536, ["frame_width"] = 65536 });

            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void SetupClips_ClipEntryThatIsNotAnObject_IsSkipped()
        {
            // The Python surface forwards these unchanged - measured - and the typed foreach
            // cast threw InvalidCastException on them.
            string path = CreateSheet("nonobj", 4, 1);
            Slice(path, 4, 1);

            JObject result = null;
            Assert.DoesNotThrow(() => result = Run(new JObject
            {
                ["action"] = "setup_clips",
                ["path"] = path,
                ["clips"] = new JArray { "not_an_object", 7 },
                ["output_dir"] = TempRoot,
            }), "a malformed clips entry must come back as a diagnostic, not an exception");

            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_NOT_AN_OBJECT"));
        }

        [Test]
        public void SliceSheet_ReslicingWithADifferentGrid_ReplacesTheOldFrames()
        {
            string path = CreateSheet("reslice", 4, 2);
            Slice(path, 4, 2);
            Assert.AreEqual(8, SpritesOf(path).Length);

            Slice(path, 2, 1);
            var after = SpritesOf(path).Select(s => s.name).ToArray();
#pragma warning disable CS0618 // same API the tool writes through
            int configured = ((TextureImporter)AssetImporter.GetAtPath(path)).spritesheet.Length;
#pragma warning restore CS0618
            Assert.AreEqual(2, after.Length,
                $"stale frames must not survive a reslice; importer holds {configured}, " +
                "project holds: " + string.Join(", ", after));
        }

        // =====================================================================
        // setup_clips
        // =====================================================================

        private static JObject SetupClips(string path, JArray clips) => Run(new JObject
        {
            ["action"] = "setup_clips",
            ["path"] = path,
            ["clips"] = clips,
            ["output_dir"] = TempRoot,
        });

        private static JArray OneClip(string name, int start, int end, float? fps = null, bool? loop = null)
        {
            var clip = new JObject { ["name"] = name, ["start_frame"] = start, ["end_frame"] = end };
            if (fps.HasValue) clip["fps"] = fps.Value;
            if (loop.HasValue) clip["loop"] = loop.Value;
            return new JArray { clip };
        }

        [Test]
        public void SetupClips_OnAnUnslicedSheet_TellsYouToSliceFirst()
        {
            string path = CreateSheet("noslice", 4, 1);
            var result = SetupClips(path, OneClip("walk", 0, 3));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("slice_sheet"));
        }

        [Test]
        public void SetupClips_WritesAClipAssetWithOneKeyPerFrame()
        {
            string path = CreateSheet("clips", 4, 1);
            Slice(path, 4, 1);

            var result = SetupClips(path, OneClip("walk", 0, 3));
            Assert.IsTrue(result.Value<bool>("success"));

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            Assert.IsNotNull(clip, "the .anim asset should exist on disk");

            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            Assert.AreEqual(typeof(SpriteRenderer), binding.type);
            Assert.AreEqual("m_Sprite", binding.propertyName,
                "anything else animates the wrong property and shows nothing");
            Assert.AreEqual(4, AnimationUtility.GetObjectReferenceCurve(clip, binding).Length);
        }

        [Test]
        public void SetupClips_FpsDrivesTheFrameRateAndTheKeyTimes()
        {
            string path = CreateSheet("fps", 4, 1);
            Slice(path, 4, 1);
            SetupClips(path, OneClip("walk", 0, 3, fps: 8f));

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            Assert.AreEqual(8f, clip.frameRate);

            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            Assert.AreEqual(0f, keys[0].time, 0.0001f);
            Assert.AreEqual(1f / 8f, keys[1].time, 0.0001f);
        }

        [Test]
        public void SetupClips_KeyframesFollowTheSlicedOrder()
        {
            string path = CreateSheet("seq", 4, 1);
            Slice(path, 4, 1);
            SetupClips(path, OneClip("walk", 0, 3));

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);

            var expected = SpritesOf(path).Select(s => s.name).ToArray();
            var actual = keys.Select(k => k.value.name).ToArray();
            Assert.AreEqual(expected, actual, "frames must play in sheet order");
        }

        [Test]
        public void SetupClips_TenthFrameSortsAfterTheSecond()
        {
            // A plain string sort puts hero_10 between hero_1 and hero_2, which reorders
            // the animation without failing anything.
            string path = CreateSheet("natural", 11, 1);
            Slice(path, 11, 1);
            SetupClips(path, OneClip("walk", 0, 10));

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);

            Assert.AreEqual("natural_2", keys[2].value.name);
            Assert.AreEqual("natural_10", keys[10].value.name);
        }

        [Test]
        public void SetupClips_LoopIsInferredFromTheClipName()
        {
            string path = CreateSheet("loopname", 4, 1);
            Slice(path, 4, 1);
            SetupClips(path, new JArray
            {
                new JObject { ["name"] = "walk", ["start_frame"] = 0, ["end_frame"] = 1 },
                new JObject { ["name"] = "attack", ["start_frame"] = 2, ["end_frame"] = 3 },
            });

            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            var attack = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/attack.anim");

            Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(walk).loopTime,
                "locomotion should loop");
            Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(attack).loopTime,
                "a one-shot attack should not loop");
        }

        [Test]
        public void SetupClips_ExplicitLoopBeatsTheNameGuess()
        {
            string path = CreateSheet("loopflag", 4, 1);
            Slice(path, 4, 1);
            SetupClips(path, OneClip("walk", 0, 3, loop: false));

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        }

        [Test]
        public void SetupClips_RangeBeyondTheSheet_WarnsAndWritesNothing()
        {
            string path = CreateSheet("range", 4, 1);
            Slice(path, 4, 1);

            var result = SetupClips(path, OneClip("walk", 90, 99));
            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_EMPTY"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim"));
        }

        [Test]
        public void SetupClips_UnnamedClip_IsSkippedWithAWarning()
        {
            string path = CreateSheet("noname", 4, 1);
            Slice(path, 4, 1);

            var result = SetupClips(path, new JArray { new JObject { ["start_frame"] = 0, ["end_frame"] = 3 } });
            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_NO_NAME"));
        }

        [Test]
        public void SetupClips_OutputDirEscapingAssets_IsRefused()
        {
            string path = CreateSheet("escape", 4, 1);
            Slice(path, 4, 1);

            var result = Run(new JObject
            {
                ["action"] = "setup_clips",
                ["path"] = path,
                ["clips"] = OneClip("walk", 0, 3),
                ["output_dir"] = $"{TempRoot}/../../../outside",
            });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("output_dir"));
        }

        [Test]
        public void SetupClips_ClipNameEscapingTheOutputDir_IsSkipped()
        {
            // The clip name is joined into a file path, so a name carrying separators would
            // otherwise write outside the directory the caller asked for.
            string path = CreateSheet("escapename", 4, 1);
            Slice(path, 4, 1);

            var result = SetupClips(path, OneClip("../../evil", 0, 3));
            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_BAD_NAME"));
        }

        [Test]
        public void SetupClips_ClipNameWithASeparator_IsSkipped()
        {
            // A separator is not traversal, so the '..' check lets it through - and the name
            // then selects a path in a descendant directory instead of a leaf in output_dir.
            // The tool's own CLIP_BAD_NAME hint already tells callers to remove separators.
            string path = CreateSheet("sepname", 4, 1);
            Slice(path, 4, 1);

            var result = SetupClips(path, OneClip("nested/walk", 0, 3));
            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/nested/walk.anim"),
                "a clip name must not choose the directory it lands in");
        }

        [Test]
        public void SetupClips_ExistingClipWithoutOverwrite_IsLeftAlone()
        {
            // setup_controller refuses an existing controller unless overwrite is set. Clips
            // took the opposite policy and deleted whatever sat at the composed path, so an
            // unrelated clip that merely shared a name was destroyed by a request that never
            // asked for a replacement.
            string path = CreateSheet("existing", 4, 1);
            Slice(path, 4, 1);

            var sentinel = new AnimationClip { frameRate = 99f };
            AssetDatabase.CreateAsset(sentinel, $"{TempRoot}/walk.anim");
            AssetDatabase.SaveAssets();

            var result = SetupClips(path, OneClip("walk", 0, 3));

            var after = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            Assert.IsNotNull(after, "the existing clip must survive");
            Assert.AreEqual(99f, after.frameRate, "the existing clip must not be replaced");
            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_EXISTS"));
        }

        [Test]
        public void SetupClips_ExistingClipWithOverwrite_IsReplaced()
        {
            string path = CreateSheet("existing2", 4, 1);
            Slice(path, 4, 1);

            var sentinel = new AnimationClip { frameRate = 99f };
            AssetDatabase.CreateAsset(sentinel, $"{TempRoot}/walk.anim");
            AssetDatabase.SaveAssets();

            var result = Run(new JObject
            {
                ["action"] = "setup_clips",
                ["path"] = path,
                ["clips"] = OneClip("walk", 0, 3),
                ["output_dir"] = TempRoot,
                ["overwrite"] = true,
            });

            Assert.AreEqual(1, result.Value<int>("clip_count"));
            var after = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim");
            Assert.AreEqual(12f, after.frameRate, "an authorised overwrite must actually replace it");
        }

        [Test]
        public void SetupClips_ZeroFps_IsSkippedInsteadOfWritingInfiniteKeyTimes()
        {
            string path = CreateSheet("zerofps", 4, 1);
            Slice(path, 4, 1);

            var result = SetupClips(path, OneClip("walk", 0, 3, fps: 0f));
            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_BAD_FPS"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim"));
        }

        [Test]
        public void SetupClips_NameThatMerelyContainsAKeyword_IsNotTreatedAsLocomotion()
        {
            // 'grunt' contains the letters of 'run'. Matching on substrings makes it loop
            // like a walk cycle.
            string path = CreateSheet("substr", 4, 1);
            Slice(path, 4, 1);
            SetupClips(path, OneClip("grunt", 0, 3));

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/grunt.anim");
            Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        }

        // =====================================================================
        // setup_controller
        // =====================================================================

        private static JObject SetupController(JArray clips, bool overwrite = false) => Run(new JObject
        {
            ["action"] = "setup_controller",
            ["clips"] = clips,
            ["controller_path"] = $"{TempRoot}/Hero.controller",
            ["overwrite"] = overwrite,
        });

        /// <summary>Slices a sheet and builds the named clips, returning [{name, path}] for the controller.</summary>
        private static JArray BuildClips(string sheet, params string[] names)
        {
            string path = CreateSheet(sheet, names.Length * 2, 1);
            Slice(path, names.Length * 2, 1);

            var defs = new JArray();
            for (int i = 0; i < names.Length; i++)
                defs.Add(new JObject { ["name"] = names[i], ["start_frame"] = i * 2, ["end_frame"] = i * 2 + 1 });
            var clipResult = SetupClips(path, defs);

            var refs = new JArray();
            foreach (string n in names)
            {
                string clipPath = $"{TempRoot}/{n}.anim";
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath),
                    $"fixture: clip '{n}' was not written to {clipPath}; setup_clips said " +
                    clipResult.ToString(Newtonsoft.Json.Formatting.None));
                refs.Add(new JObject { ["name"] = n, ["path"] = clipPath });
            }
            return refs;
        }

        [Test]
        public void SetupController_WithoutClips_ReturnsError()
        {
            var result = Run(new JObject
            {
                ["action"] = "setup_controller",
                ["controller_path"] = $"{TempRoot}/Hero.controller",
            });
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void SetupController_WithoutControllerPath_ReturnsError()
        {
            var result = Run(new JObject
            {
                ["action"] = "setup_controller",
                ["clips"] = new JArray { new JObject { ["name"] = "walk", ["path"] = "x.anim" } },
            });
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("controller_path"));
        }

        [Test]
        public void SetupController_ClipsThatDoNotExist_ReturnsError()
        {
            var result = SetupController(new JArray
            {
                new JObject { ["name"] = "walk", ["path"] = $"{TempRoot}/missing.anim" },
            });
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void SetupController_EveryEntrySkipped_StillReportsWhy()
        {
            // The builder records why it skipped each entry, but the all-skipped path returns
            // a generic error - so the caller was told the clips did not load without being
            // told that none of them were objects.
            JObject result = null;
            Assert.DoesNotThrow(() => result = Run(new JObject
            {
                ["action"] = "setup_controller",
                ["clips"] = new JArray { 7 },
                ["controller_path"] = $"{TempRoot}/Skipped.controller",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result.ToString(), Does.Contain("CLIP_NOT_AN_OBJECT"),
                "the response must carry the reason the builder recorded");
        }

        [Test]
        public void SetupController_IdleAndWalk_WritesAControllerWithBothStates()
        {
            var result = SetupController(BuildClips("ctrl", "idle", "walk"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            Assert.IsNotNull(controller);

            var states = controller.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
            Assert.That(states, Contains.Item("Idle"));
            Assert.That(states, Contains.Item("walk"));
            Assert.AreEqual("Idle", controller.layers[0].stateMachine.defaultState.name,
                "idle is the state a character rests in, so it should be the entry point");
        }

        [Test]
        public void SetupController_WalkAndRun_BuildsASpeedDrivenBlendTree()
        {
            var result = SetupController(BuildClips("blend", "idle", "walk", "run"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            Assert.That(controller.parameters.Select(p => p.name), Contains.Item("Speed"));

            var loco = controller.layers[0].stateMachine.states
                .Select(s => s.state)
                .SingleOrDefault(s => s.name == "Locomotion");
            Assert.IsNotNull(loco,
                "two locomotion clips should collapse into one blend tree state; states were: " +
                string.Join(", ", controller.layers[0].stateMachine.states.Select(s => s.state.name)));

            var tree = loco.motion as BlendTree;
            Assert.IsNotNull(tree);
            Assert.AreEqual("Speed", tree.blendParameter);
            // walk sits below run on the axis, otherwise the character sprints while strolling.
            Assert.AreEqual(new[] { "walk", "run" },
                tree.children.Select(c => c.motion.name).ToArray());
        }

        [Test]
        public void SetupController_CombatClip_GetsATrigger()
        {
            var result = SetupController(BuildClips("combat", "idle", "attack"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            var attack = controller.parameters.SingleOrDefault(p => p.name == "Attack");
            Assert.IsNotNull(attack, "a combat clip needs a trigger to be reachable");
            Assert.AreEqual(AnimatorControllerParameterType.Trigger, attack.type);
        }

        [Test]
        public void SetupController_ControllerPathEscapingAssets_FailsWithAMessage()
        {
            var clips = BuildClips("escapectrl", "idle", "walk");
            JObject result = null;
            Assert.DoesNotThrow(() => result = Run(new JObject
            {
                ["action"] = "setup_controller",
                ["clips"] = clips,
                ["controller_path"] = $"{TempRoot}/../../../Hero.controller",
            }), "a refused path must not surface as an exception");

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("controller_path"));
        }

        [Test]
        public void SetupController_TriggerIsNamedAfterTheAction()
        {
            // 'hero_attack' should arm an Attack trigger. Naming it after the first segment
            // of the clip name gives 'Hero', which tells the caller nothing.
            var result = SetupController(BuildClips("trig", "idle", "hero_attack"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            var names = controller.parameters.Select(p => p.name).ToArray();
            Assert.That(names, Contains.Item("Attack"));
            Assert.That(names, Has.No.Member("Hero"));
        }

        [Test]
        public void SetupController_NameThatMerelyContainsAKeyword_GetsNoTrigger()
        {
            // The letters of 'hit' sit inside 'white'. Under substring matching the clip is
            // filed as an object animation and picks up a trigger it never asked for.
            var result = SetupController(BuildClips("wf", "idle", "white_flash"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            var names = controller.parameters.Select(p => p.name).ToArray();
            Assert.That(names, Has.No.Member("Hit"));
            Assert.That(names, Has.No.Member("White"));
        }

        [Test]
        public void SetupController_ExistingControllerWithoutOverwrite_RefusesInsteadOfReplacing()
        {
            var clips = BuildClips("exists", "idle", "walk");
            Assert.IsTrue(SetupController(clips).Value<bool>("success"));

            var second = SetupController(clips);
            Assert.IsFalse(second.Value<bool>("success"));
            Assert.That(second["diagnostics"].ToString(), Does.Contain("CONTROLLER_EXISTS"));
        }

        [Test]
        public void SetupController_ExistingControllerWithOverwrite_Replaces()
        {
            var clips = BuildClips("overwrite", "idle", "walk");
            Assert.IsTrue(SetupController(clips).Value<bool>("success"));

            // Mark the first controller so a run that merely reused it is distinguishable
            // from one that replaced it: two successes alone are true of both.
            string ctrlPath = $"{TempRoot}/Hero.controller";
            var first = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            first.AddParameter("SentinelFromFirstBuild", AnimatorControllerParameterType.Bool);
            AssetDatabase.SaveAssets();

            Assert.IsTrue(SetupController(clips, overwrite: true).Value<bool>("success"));

            var second = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            Assert.That(second.parameters.Select(p => p.name), Has.No.Member("SentinelFromFirstBuild"),
                "an authorised overwrite must build a new controller, not reuse the old one");
        }

        // =====================================================================
        // Audit verification - each of these asserts the behaviour a finding says
        // is missing. Red here means the finding reproduces.
        // =====================================================================

        [Test]
        public void AuditS2_OverwriteThatCannotBuildAReplacement_KeepsTheOldController()
        {
            var clips = BuildClips("s2", "idle", "walk");
            Assert.IsTrue(SetupController(clips).Value<bool>("success"));
            string ctrl = $"{TempRoot}/Hero.controller";
            var before = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrl);
            Assert.IsNotNull(before);

            // Every replacement clip is unloadable, so the rebuild cannot succeed.
            var doomed = new JArray {
                new JObject { ["name"] = "idle", ["path"] = $"{TempRoot}/does_not_exist.anim" },
            };
            Run(new JObject
            {
                ["action"] = "setup_controller",
                ["clips"] = doomed,
                ["controller_path"] = ctrl,
                ["overwrite"] = true,
            });

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrl),
                "a failed rebuild must not leave the caller without the controller they had");
        }

        [Test]
        public void AuditS5_ControllerRefusal_StopsBeforeTouchingTheScene()
        {
            string path = CreateSheet("s5", 4, 1);
            var go = new GameObject("SpriteTest_S5");
            try
            {
                string ctrl = $"{TempRoot}/S5.controller";
                Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot, ["controller_path"] = ctrl });

                // Second run: the controller exists and overwrite is not set, so the
                // controller step fails - and a failed step must not fall through.
                var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot, ["controller_path"] = ctrl,
                                  ["add_to_scene"] = true, ["scene_target"] = "SpriteTest_S5" });

                Assert.IsFalse(result.Value<bool>("success"));
                Assert.AreEqual("setup_controller", result.Value<string>("step"),
                    "the response must name the step that failed");
                Assert.IsNull(go.GetComponent<Animator>(),
                    "a refused controller step must not go on to modify the scene");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AuditS6_RequestedSceneTargetMissing_IsNotReportedAsSuccess()
        {
            string path = CreateSheet("s6", 4, 1);
            var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                              ["output_dir"] = TempRoot, ["controller_path"] = $"{TempRoot}/S6.controller",
                              ["add_to_scene"] = true, ["scene_target"] = "NoSuchObject" });

            Assert.IsFalse(result.Value<bool>("success"),
                "an attachment that was asked for and did not happen is not a success");
        }

        [Test]
        public void AuditS7_ControllerPathWithoutExtension_StillReachesTheSceneObject()
        {
            string path = CreateSheet("s7", 4, 1);
            var go = new GameObject("SpriteTest_S7");
            try
            {
                var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot,
                                  ["controller_path"] = $"{TempRoot}/S7",   // no .controller suffix
                                  ["add_to_scene"] = true, ["scene_target"] = "SpriteTest_S7" });

                // Count the components rather than null-checking the result of GetComponent:
                // a missing component compares equal to null but is not a null reference, so
                // Assert.IsNotNull passes and the next member access throws instead of failing.
                Assert.AreEqual(1, go.GetComponents<Animator>().Length,
                    "the object should have received an Animator; result was " + result.ToString(Newtonsoft.Json.Formatting.None));
                Assert.IsTrue(go.GetComponents<Animator>()[0].runtimeAnimatorController != null,
                    "the suffix the builder added must not lose the controller on the way to the scene");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AuditS4_RefusedClip_IsNotCountedAsCreated()
        {
            string path = CreateSheet("s4", 6, 1);
            var result = Run(new JObject
            {
                ["action"] = "full_setup", ["path"] = path, ["cols"] = 6,
                ["output_dir"] = TempRoot, ["controller_path"] = $"{TempRoot}/S4.controller",
                ["clips"] = new JArray {
                    new JObject { ["name"] = "idle",   ["start_frame"] = 0, ["end_frame"] = 1 },
                    new JObject { ["name"] = "attack", ["start_frame"] = 2, ["end_frame"] = 3, ["fps"] = 0 },
                    new JObject { ["name"] = "walk",   ["start_frame"] = 4, ["end_frame"] = 5 },
                },
            });

            int onDisk = AssetDatabase.FindAssets("t:AnimationClip", new[] { TempRoot }).Length;
            Assert.AreEqual(onDisk, result.Value<int>("clip_count"),
                "clip_count must count the clips that exist, not the ones that were asked for");
        }

        [Test]
        public void AuditS1_RowsRejected_LeavesTheTextureTypeAlone()
        {
            string path = CreateSheet("s1", 4, 1);
            var before = ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;

            var result = Slice(path, 4, 0);   // refused: rows must be >= 1
            Assert.IsFalse(result.Value<bool>("success"));

            var after = ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;
            Assert.AreEqual(before, after,
                "a refused request must not leave the texture converted behind it");
        }

        // =====================================================================
        // full_setup
        // =====================================================================

        [Test]
        public void FullSetup_WithoutColsOrFrameWidth_ReturnsError()
        {
            string path = CreateSheet("fullnogrid", 4, 1);
            var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path });
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void FullSetup_SlicesBuildsClipsAndWritesAController()
        {
            string path = CreateSheet("full", 4, 1);
            var result = Run(new JObject
            {
                ["action"] = "full_setup",
                ["path"] = path,
                ["cols"] = 4,
                ["animation_name"] = "walk",
                ["output_dir"] = TempRoot,
                ["controller_path"] = $"{TempRoot}/Full.controller",
            });

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(4, SpritesOf(path).Length, "the sheet should end up sliced");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim"),
                "the clip should end up on disk");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Full.controller"),
                "the controller should end up on disk");
        }

        [Test]
        public void FullSetup_DefaultsTheClipNameToTheFileName()
        {
            string path = CreateSheet("hero_idle", 4, 1);
            Run(new JObject
            {
                ["action"] = "full_setup",
                ["path"] = path,
                ["cols"] = 4,
                ["output_dir"] = TempRoot,
                ["controller_path"] = $"{TempRoot}/Named.controller",
            });

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/hero_idle.anim"));
        }

        // =====================================================================
        // Refused paths
        //
        // SanitizeAssetPath answers a traversal path with null, and null is a value every
        // AssetDatabase entry point accepts. Each action below used to hand that null on and
        // then describe the result - "no TextureImporter here", "no sprites found" - which
        // names a lookup that never happened. These pin the refusal itself.
        // =====================================================================

        [Test]
        public void GetInfo_PathEscapingAssets_RefusesInsteadOfLookingItUp()
        {
            JObject result = null;
            Assert.DoesNotThrow(() => result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = $"{TempRoot}/../../../outside.png",
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain(".."));
        }

        [Test]
        public void SliceSheet_PathEscapingAssets_RefusesInsteadOfLookingItUp()
        {
            JObject result = null;
            Assert.DoesNotThrow(() => result = Run(new JObject
            {
                ["action"] = "slice_sheet",
                ["path"] = $"{TempRoot}/../../../outside.png",
                ["cols"] = 4,
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain(".."));
        }

        [Test]
        public void SetupClips_PathEscapingAssets_RefusesInsteadOfLookingItUp()
        {
            JObject result = null;
            Assert.DoesNotThrow(() => result = SetupClips(
                $"{TempRoot}/../../../outside.png", OneClip("walk", 0, 3)));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain(".."));
        }

        [Test]
        public void FullSetup_PathEscapingAssets_RefusesInsteadOfLookingItUp()
        {
            JObject result = null;
            Assert.DoesNotThrow(() => result = Run(new JObject
            {
                ["action"] = "full_setup",
                ["path"] = $"{TempRoot}/../../../outside.png",
                ["cols"] = 4,
                ["output_dir"] = TempRoot,
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain(".."));
        }

        [Test]
        public void SetupController_ClipPathEscapingAssets_SkipsThatClipAndSaysWhy()
        {
            var clips = BuildClips("badclippath", "idle", "walk");
            clips.Add(new JObject
            {
                ["name"] = "attack",
                ["path"] = $"{TempRoot}/../../../outside.anim",
            });

            JObject result = null;
            Assert.DoesNotThrow(() => result = SetupController(clips));

            // The other two clips are fine, so the controller is still built - but the refused
            // entry must be reported as refused, not as merely missing.
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_BAD_PATH"));
        }

        // =====================================================================
        // Bounds
        // =====================================================================

        [Test]
        public void SetupClips_NegativeStartFrame_IsRefusedRatherThanShiftedToZero()
        {
            // Enumerable.Skip ignores a negative count, so [-2,3] used to select frames 0..5
            // and report success with a clip the caller never asked for.
            string path = CreateSheet("negrange", 4, 1);
            Slice(path, 4, 1);

            var result = SetupClips(path, OneClip("walk", -2, 3));
            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_BAD_RANGE"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/walk.anim"));
        }

        [Test]
        public void SliceSheet_GridFarBeyondAnyRealSheet_IsRefusedBeforeAllocating()
        {
            // 128x128 cut into 1px frames is 16,384 entries. It fits inside the texture, so
            // every bounds check above passes; what stops it is the frame ceiling.
            string path = CreateSheet("huge", 8, 8);   // 128x128
            var before = ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;

            var result = Run(new JObject
            {
                ["action"] = "slice_sheet",
                ["path"] = path,
                ["cols"] = 128,
                ["rows"] = 128,
            });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("SLICE_TOO_MANY_FRAMES"));
            Assert.AreEqual(0, SpritesOf(path).Length, "nothing may be written past the limit");
            // Every refusal after the Sprite conversion owes a RestoreTextureType call, and
            // that obligation is carried by whoever adds the next early return rather than by
            // the code. This assertion is what makes a forgotten one fail loudly.
            Assert.AreEqual(before, ((TextureImporter)AssetImporter.GetAtPath(path)).textureType,
                "a refused request must not leave the texture converted behind it");
        }

        // =====================================================================
        // Clip-name shapes
        // =====================================================================

        [Test]
        public void SetupController_CamelCaseClipName_StillGetsItsTrigger()
        {
            // Detect used to lowercase the name before the tokenizer saw it, and the tokenizer
            // splits camelCase by testing char.IsUpper - never true on a lowered string. So
            // 'heroAttack' became one word, matched no keyword, and was filed Generic.
            var result = SetupController(BuildClips("camel", "idle", "heroAttack"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            var attack = controller.parameters.SingleOrDefault(p => p.name == "Attack");
            Assert.IsNotNull(attack,
                "a camelCase combat clip needs the same trigger a snake_case one gets; " +
                "parameters present: " + string.Join(", ", controller.parameters.Select(p => p.name)));
            Assert.AreEqual(AnimatorControllerParameterType.Trigger, attack.type);
        }

        // =====================================================================
        // Inline image bound
        // =====================================================================

        [Test]
        public void GetInfo_SmallSheet_CarriesTheImageInline()
        {
            string path = CreateSheet("inline", 4, 2);
            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });

            Assert.That(result.Value<string>("image_base64"), Does.StartWith("data:image/png;base64,"));
            Assert.IsNull(result.Value<string>("image_omitted_reason"));
        }

        [Test]
        public void GetInfo_OversizeSheet_DropsTheImageAndSaysWhy()
        {
            // Two things the fixture has to get right. Noise, not a flat colour: a solid
            // sheet compresses to a few kilobytes and would never reach the ceiling. And a
            // power-of-two side: a Default-type import rescales anything else, so a 1200px
            // sheet reads back as 1024 - measured here first - and the dimensions below
            // would then be pinning the rescale rather than the file.
            string path = CreateNoiseSheet("oversize", 2048);
            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });

            Assert.IsTrue(result.Value<bool>("success"), "the call still answers");
            Assert.IsNull(result.Value<string>("image_base64"));
            Assert.That(result.Value<string>("image_omitted_reason"), Does.Contain("limit"));
            // Everything a caller needs to work out a grid is still here.
            Assert.AreEqual(2048, result.Value<int>("width"));
            Assert.AreEqual(2048, result.Value<int>("height"));
        }

        [Test]
        public void GetInfo_ImageJustUnderTheSourceLimit_StillDoesNotBlowThePayloadLimit()
        {
            // The ceiling is checked against the file on disk, but what travels in the
            // response is base64 - 4 bytes out for every 3 in. A source comfortably under
            // the limit therefore still produces a payload above it.
            string path = CreateNoiseSheet("midsize", 1024, assertOverCeiling: false);
            long sourceBytes = new FileInfo(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, path)).Length;
            Assert.Less(sourceBytes, 4 * 1024 * 1024,
                "fixture: this sheet must pass the source-size check to test what happens after it");

            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });

            string b64 = result.Value<string>("image_base64");
            if (b64 != null)
                Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(b64), 4 * 1024 * 1024,
                    $"inline payload is {System.Text.Encoding.UTF8.GetByteCount(b64)} bytes " +
                    $"from a {sourceBytes}-byte source; the bound must cover what is sent, not what was read");
            else
                Assert.IsNotEmpty(result.Value<string>("image_omitted_reason") ?? "",
                    "an omitted image must say why");
        }

        [Test]
        public void SetupController_AcronymInACamelCaseClipName_StillGetsItsTrigger()
        {
            // 'heroAttack' splits because a capital follows a lowercase. 'heroXMLAttack' has
            // no such boundary at the acronym's end, so it used to tokenize as one word
            // 'xmlattack', match nothing, and lose the trigger its snake_case twin gets.
            var result = SetupController(BuildClips("acronym", "idle", "heroXMLAttack"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            var attack = controller.parameters.SingleOrDefault(p => p.name == "Attack");
            Assert.IsNotNull(attack,
                "an acronym must not swallow the keyword after it; parameters present: " +
                string.Join(", ", controller.parameters.Select(p => p.name)));
        }

        [Test]
        public void SetupController_TwoOtherAcronymShapes_AlsoKeepTheirTriggers()
        {
            // Closing the class rather than the one spelling that was reported, with the two
            // variants carrying different verdicts - which is the point of naming them.
            // 'heroATTACK': a trailing all-caps keyword. Measured to hold ALREADY - the break
            // comes from the lowercase 'o' before the run, so the new rule is not what saves
            // it. Kept as a parity tripwire, not offered as evidence for the fix.
            // 'XMLSlash': an acronym followed directly by the keyword, with no lowercase in
            // between. Nothing in the original rule set sees that boundary, so this one does
            // depend on the fix - reverting the rule turns this test red.
            var result = SetupController(BuildClips("acroshapes", "idle", "heroATTACK", "XMLSlash"));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            var names = controller.parameters.Select(p => p.name).ToArray();
            Assert.Contains("Attack", names, "a trailing all-caps keyword still names its trigger");
            Assert.Contains("Slash", names, "an acronym running straight into the keyword must still split");
        }
    }
}
