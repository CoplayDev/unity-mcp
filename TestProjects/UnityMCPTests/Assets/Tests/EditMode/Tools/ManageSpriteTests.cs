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

            // Only the asset's existence: the texture is still Default-type here, and that
            // import rescales a non-power-of-two sheet. Slice() asserts the frame count.
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

            // Asserts which side of SpriteImportSetup's 4 MB ceiling this landed on rather
            // than trusting the compressor; changing that ceiling breaks these lines loudly.
            if (assertOverCeiling)
                Assert.Greater(new FileInfo(sysPath).Length, 4 * 1024 * 1024,
                    "fixture: the noise sheet must exceed the inline-image ceiling");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return assetPath;
        }

        /// <summary>A flat sheet of an exact pixel size, for grids that do not divide evenly.</summary>
        private static string CreateSheetOfSize(string name, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 0, 0, 255);
            tex.SetPixels32(pixels);
            tex.Apply();

            string assetPath = $"{TempRoot}/{name}.png";
            string sysPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, assetPath);
            File.WriteAllBytes(sysPath, tex.EncodeToPNG());
            // A Texture2D built in an EditMode test is not collected on its own.
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return assetPath;
        }

        private static JObject Run(JObject p) => ToJObject(ManageSprite.HandleCommand(p));

        private static string ErrorText(JObject result) => result.Value<string>("message") ?? "";

        private static JObject Slice(string path, int cols, int rows)
        {
            var result = Run(new JObject
            {
                ["action"] = "slice_sheet",
                ["path"] = path,
                ["cols"] = cols,
                ["rows"] = rows,
            });
            // The refusal tests call this helper too and check the failure themselves.
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
            // The count alone would pass against a response that reported slices it never counted.
            Assert.AreEqual(0, ((JArray)result["slices"]).Count);
        }

        [Test]
        public void GetInfo_AfterSlicing_ReportsEverySlice()
        {
            string path = CreateSheet("sliced", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });
            Assert.AreEqual(8, result.Value<int>("slice_count"));
            // slice_count is independent of the projected list - measured 2026-08-21,
            // emptying `slices` entirely left this test green.
            var names = ((JArray)result["slices"]).Select(t => t.Value<string>("name")).ToArray();
            Assert.That(names, Is.EquivalentTo(SpritesOf(path).Select(s => s.name)),
                "every slice is reported, not just counted");
        }

        [Test]
        public void GetInfo_ModestSheet_ComesBackInOnePageWithNoCursor()
        {
            string path = CreateSheet("onepage", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });

            // Under the default page of 512, so this asserts the default, not a guarantee.
            Assert.AreEqual(8, ((JArray)result["slices"]).Count);
            // Value<int?> rather than indexing: an omitted property would throw instead of
            // asserting. Absent and null both mean "finished" - do not pin the shape.
            Assert.IsNull(result.Value<int?>("next_cursor"),
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
            Assert.AreEqual(3, result.Value<int?>("next_cursor"));
        }

        [Test]
        public void GetInfo_WalkingTheCursor_VisitsEverySliceOnceAndThenStops()
        {
            string path = CreateSheet("walk", 4, 2);
            Slice(path, 4, 2);

            var seen = new List<string>();
            int? cursor = 0;
            // Bounded so a cursor that never advances fails rather than hanging the run.
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
                cursor = result.Value<int?>("next_cursor");
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

        // Skip(-3) yields the whole list, so without the guard a negative cursor answers
        // with every slice and reports success - a right-looking answer, hence the refusal.
        [TestCase(-3)]
        [TestCase(9)]
        public void GetInfo_CursorOutsideTheList_IsRefused(int cursor)
        {
            string path = CreateSheet("cursor", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path, ["cursor"] = cursor });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("cursor"));
        }

        [Test]
        public void GetInfo_MissingFileOnDisk_DoesNotPutTheAbsolutePathInTheResponse()
        {
            string path = CreateSheet("nofile", 4, 2);
            // Deleted WITHOUT Refresh, so the importer still resolves and the File.Exists
            // branch answers - the only branch that used to leak the absolute path.
            string full = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, path);
            File.Delete(full);

            var result = Run(new JObject { ["action"] = "get_info", ["path"] = path });
            string reason = result.Value<string>("image_omitted_reason");

            Assert.IsNotNull(reason, "fixture: the image was supposed to be dropped here");
            Assert.That(reason, Does.Not.Contain(Application.dataPath),
                "the response must not disclose where the project lives on disk");
            Assert.That(reason, Does.Contain(path),
                "it still has to say which asset it could not read");
        }

        [Test]
        public void SliceSheet_GridThatDoesNotCoverTheTexture_SucceedsButSaysSo()
        {
            // 100 / 6 = 16, so the grid covers 96px and drops four. Measured before the
            // warning existed: success, six sprites, empty diagnostics.
            string path = CreateSheetOfSize("remainder", 100, 16);

            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                           ["cols"] = 6, ["rows"] = 1 });

            // Still a success: a trailing margin is ordinary and refusing would break it.
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(6, SpritesOf(path).Length);
            Assert.That(result["diagnostics"].ToString(), Does.Contain("SLICE_GRID_REMAINDER"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("100"),
                "the warning has to name the texture size, or it cannot be acted on");
        }

        [Test]
        public void SliceSheet_GridThatCoversTheTextureExactly_WarnsAboutNothing()
        {
            // Without this, a warning firing on every slice looks like one firing correctly.
            string path = CreateSheetOfSize("exact", 96, 16);

            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                           ["cols"] = 6, ["rows"] = 1 });

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Not.Contain("SLICE_GRID_REMAINDER"));
        }

        [TestCase("cols")]
        [TestCase("rows")]
        [TestCase("frame_width")]
        [TestCase("frame_height")]
        public void SliceSheet_GridValueTooLargeForAnInt_IsRefusedNotThrown(string key)
        {
            string path = CreateSheet($"gridovf{key}", 4, 2);
            var request = new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                        ["cols"] = 4, ["rows"] = 2 };
            request[key] = 2147483648L;

            // Measured 2026-08-21, before the guard: all four raised an uncaught
            // OverflowException, so reaching the assertions at all is half of this test.
            var result = Run(request);

            Assert.IsFalse(result.Value<bool>("success"));
            // "32-bit", not just the key name: a wrapped value lands on a different message
            // that also contains the key - measured, the weaker assertion passed the mutation.
            Assert.That(ErrorText(result), Does.Contain(key).And.Contain("32-bit"));
        }

        [Test]
        public void SliceSheet_FractionalGridValue_IsRefusedRatherThanRounded()
        {
            string path = CreateSheet("gridfrac", 4, 2);
            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                           ["cols"] = 2.7, ["rows"] = 2 });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("cols"));
        }

        [TestCase("page_size")]
        [TestCase("cursor")]
        public void GetInfo_PagingValueTooLargeForAnInt_IsRefusedNotThrown(string key)
        {
            string path = CreateSheet($"overflow{key}", 4, 2);
            Slice(path, 4, 2);

            var request = new JObject { ["action"] = "get_info", ["path"] = path };
            request[key] = 2147483648L;

            // Measured before the guard: ToObject<int> raised an uncaught OverflowException,
            // so reaching the assertions at all is half of this test.
            var result = Run(request);

            Assert.IsFalse(result.Value<bool>("success"));
            // A wrapped value is still refused, but by the range guard downstream; only this
            // phrase tells the two apart.
            Assert.That(ErrorText(result), Does.Contain(key).And.Contain("32-bit"));
        }

        [Test]
        public void GetInfo_FractionalPageSize_IsRefusedRatherThanRounded()
        {
            string path = CreateSheet("fractional", 4, 2);
            Slice(path, 4, 2);

            var result = Run(new JObject
            {
                ["action"] = "get_info",
                ["path"] = path,
                ["page_size"] = 2.7,
            });

            // Measured before the guard: returned three slices and reported success.
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("page_size"));
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

        [TestCase("slice_sheet")]
        [TestCase("full_setup")]
        public void WithoutColsOrFrameWidth_ReturnsError(string action)
        {
            string path = CreateSheet("nogrid", 4, 2);
            var result = Run(new JObject { ["action"] = action, ["path"] = path });

            Assert.IsFalse(result.Value<bool>("success"));
            // Not just success=false: Newtonsoft reads an absent "success" as false too.
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
            // Sheets read top-to-bottom, but Unity's texture origin is bottom-left; getting
            // this backwards silently plays the animation in the wrong order.
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
            // 96px is not a power of two: a Default-type import rescales it to 128, giving
            // 21px cells whose last two frames fall outside the real texture - success anyway.
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
            // Pins the branch where the texture is already a Sprite, which used to skip the
            // conversion block and so never normalised npotScale. On 2021.3.45f2 that made
            // Unity refuse sprite generation and slice_sheet report six frames over an empty
            // asset; reverting the npotScale line turns this case red there.
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
        public void SliceSheet_FrameHeightAloneDerivesTheRowCount()
        {
            // rows defaulted to 1 even when frame_height was there to derive it from, so the
            // documented alternative produced a single row and a remainder warning.
            string path = CreateSheet("derive_rows", 4, 4);
            var result = Run(new JObject { ["action"] = "slice_sheet", ["path"] = path,
                                           ["cols"] = 4, ["frame_height"] = Cell });

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(4, result.Value<int>("rows"));
            Assert.AreEqual(16, SpritesOf(path).Length);
        }

        private static IEnumerable<TestCaseData> RefusedGrids()
        {
            TestCaseData Case(int sheetCols, int sheetRows, JObject grid, string code) =>
                new TestCaseData(sheetCols, sheetRows, grid, code)
                    .SetName($"{code} from {string.Join(",", grid.Properties().Select(p => p.Name + "=" + p.Value))}");

            // A frame wider than the sheet derives 0 columns.
            yield return Case(2, 1, new JObject { ["frame_width"] = 4096 }, "SLICE_EMPTY");
            // `?? 1` only covers a missing key; an explicit 0 reaches the `texH / rows` division.
            yield return Case(4, 1, new JObject { ["cols"] = 4, ["rows"] = 0 }, "BAD_PARAM");
            // Only reachable after the texture is measured, so moving the argument checks
            // earlier could not close this form of the class; the height axis reaches it too.
            yield return Case(2, 1, new JObject { ["cols"] = 2, ["frame_width"] = 4096 }, "SLICE_OUT_OF_BOUNDS");
            yield return Case(2, 1, new JObject { ["cols"] = 2, ["rows"] = 1, ["frame_height"] = 4096 }, "SLICE_OUT_OF_BOUNDS");
            // 64 columns across 32 pixels derives a 0-wide frame, whose product passes any
            // "does it fit" test - 64 degenerate rects, reported as success.
            yield return Case(2, 1, new JObject { ["cols"] = 64 }, "SLICE_OUT_OF_BOUNDS");
            yield return Case(2, 1, new JObject { ["cols"] = 2, ["rows"] = 32 }, "SLICE_OUT_OF_BOUNDS");
            // 65536 * 65536 wraps to 0 in 32-bit arithmetic and slipped under the comparison.
            // The code, not just the failure: measured 2026-08-21, with the (long) cast
            // removed this stayed GREEN because the frame ceiling refused it instead.
            yield return Case(2, 1, new JObject { ["cols"] = 65536, ["frame_width"] = 65536 }, "SLICE_OUT_OF_BOUNDS");
            // 16,384 entries that fit inside the texture: only the frame ceiling stops this.
            yield return Case(8, 8, new JObject { ["cols"] = 128, ["rows"] = 128 }, "SLICE_TOO_MANY_FRAMES");
            // A negative alternative used to be silently replaced by the value derived from cols.
            yield return Case(2, 1, new JObject { ["cols"] = 2, ["frame_width"] = -1 }, "BAD_PARAM");
        }

        [TestCaseSource(nameof(RefusedGrids))]
        public void SliceSheet_GridThatCannotBeCut_IsRefusedAndLeavesTheTextureAlone(
            int sheetCols, int sheetRows, JObject grid, string code)
        {
            string path = CreateSheet("badgrid", sheetCols, sheetRows);
            var before = ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;

            var request = new JObject { ["action"] = "slice_sheet", ["path"] = path };
            foreach (var p in grid.Properties())
                request[p.Name] = p.Value;
            var result = Run(request);

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain(code));
            Assert.AreEqual(0, SpritesOf(path).Length, "a refused grid must not write any frame");
            // Every refusal after the Sprite conversion owes a RestoreTextureType call, an
            // obligation the code cannot enforce; this assertion makes a forgotten one fail.
            Assert.AreEqual(before, ((TextureImporter)AssetImporter.GetAtPath(path)).textureType,
                "a refused request must not leave the texture converted behind it");
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
            // A plain string sort puts hero_10 between hero_1 and hero_2.
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

        private static IEnumerable<TestCaseData> RefusedClips()
        {
            TestCaseData Case(string label, JToken clip, string code) =>
                new TestCaseData(clip, code).SetName($"{code}: {label}");

            // Measured 2026-08-21: `loop: "maybe"` raised an uncaught FormatException, and
            // `loop: 2` was accepted silently. loop is the one flag with no type above C#.
            yield return Case("loop is not a bool", new JObject { ["name"] = "walk", ["start_frame"] = 0, ["end_frame"] = 3, ["loop"] = "maybe" }, "CLIP_BAD_LOOP");
            // Before the guard these threw OverflowException out of the tool.
            yield return Case("start_frame overflows int", new JObject { ["name"] = "walk", ["start_frame"] = 2147483648L, ["end_frame"] = 3 }, "CLIP_BAD_RANGE");
            yield return Case("end_frame overflows int", new JObject { ["name"] = "walk", ["start_frame"] = 0, ["end_frame"] = 2147483648L }, "CLIP_BAD_RANGE");
            // Measured before the guard: rounded to 3, wrote a clip, reported success.
            yield return Case("fractional start_frame", new JObject { ["name"] = "walk", ["start_frame"] = 2.7, ["end_frame"] = 5 }, "CLIP_BAD_RANGE");
            // `fps <= 0f` is false for NaN, so the clip was written with NaN keyframe times.
            yield return Case("NaN fps", new JObject { ["name"] = "walk", ["start_frame"] = 0, ["end_frame"] = 5, ["fps"] = double.NaN }, "CLIP_BAD_FPS");
            yield return Case("zero fps", new JObject { ["name"] = "walk", ["start_frame"] = 0, ["end_frame"] = 3, ["fps"] = 0f }, "CLIP_BAD_FPS");
            // Skip/Take clamps silently: an eight-frame clip for a hundred-frame request.
            yield return Case("end_frame past the sheet", new JObject { ["name"] = "walk", ["start_frame"] = 0, ["end_frame"] = 99 }, "CLIP_BAD_RANGE");
            yield return Case("range beyond the sheet", new JObject { ["name"] = "walk", ["start_frame"] = 90, ["end_frame"] = 99 }, "CLIP_BAD_RANGE");
            // Skip ignores a negative count, so [-2,3] used to select frames 0..5.
            yield return Case("negative start_frame", new JObject { ["name"] = "walk", ["start_frame"] = -2, ["end_frame"] = 3 }, "CLIP_BAD_RANGE");
            yield return Case("no name", new JObject { ["start_frame"] = 0, ["end_frame"] = 3 }, "CLIP_NO_NAME");
            // The name is joined into a file path, so separators would escape output_dir.
            yield return Case("name escapes output_dir", new JObject { ["name"] = "../../evil", ["start_frame"] = 0, ["end_frame"] = 3 }, "CLIP_BAD_NAME");
            yield return Case("name with a separator", new JObject { ["name"] = "nested/walk", ["start_frame"] = 0, ["end_frame"] = 3 }, "CLIP_BAD_NAME");
            // Forwarded unchanged by the Python surface; the typed cast threw on them.
            yield return Case("entry is a string", new JValue("not_an_object"), "CLIP_NOT_AN_OBJECT");
            yield return Case("entry is a number", new JValue(7), "CLIP_NOT_AN_OBJECT");
            // Legal in an asset path as far as SanitizeAssetPath is concerned, illegal in a file name.
            yield return Case("name with a character no file name allows", new JObject { ["name"] = "bad:name", ["start_frame"] = 0, ["end_frame"] = 3 }, "CLIP_BAD_NAME");
        }

        [TestCaseSource(nameof(RefusedClips))]
        public void SetupClips_ClipThatCannotBeBuilt_IsSkippedWithACode(JToken clip, string code)
        {
            string path = CreateSheet("badclip", 4, 2);
            Slice(path, 4, 2);

            var result = SetupClips(path, new JArray { clip });

            Assert.AreEqual(0, result.Value<int>("clip_count"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain(code));
            Assert.AreEqual(0, AssetDatabase.FindAssets("t:AnimationClip", new[] { TempRoot }).Length,
                "a skipped clip must not leave an asset behind");
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
        public void SetupClips_ExistingClipWithoutOverwrite_IsLeftAlone()
        {
            // Clips used to delete whatever sat at the composed path, so an unrelated clip
            // sharing a name was destroyed by a request that never asked for a replacement.
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
        public void SetupClips_NameThatMerelyContainsAKeyword_IsNotTreatedAsLocomotion()
        {
            // 'grunt' contains the letters of 'run'; substring matching makes it loop.
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
            // Not just success=false: Newtonsoft reads an absent "success" as false too.
            Assert.That(ErrorText(result), Does.Contain("clips"));
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

        // 'Assets' passes SanitizeAssetPath, and the suffix then made it 'Assets.controller' at
        // the project root; 'Assets/' became 'Assets/.controller', a file with no name.
        [TestCase("Assets", "Assets.controller")]
        [TestCase("Assets/", "Assets/.controller")]
        [TestCase("   ", "Assets/   .controller")]
        [TestCase(TempRoot, TempRoot + ".controller")]   // an existing folder, no trailing slash
        public void SetupController_ControllerPathThatIsOnlyAFolder_IsRefusedBeforeWriting(string controllerPath, string strayRelative)
        {
            var clips = BuildClips("folderctrl", "idle", "walk");
            var result = Run(new JObject
            {
                ["action"] = "setup_controller",
                ["clips"] = clips,
                ["controller_path"] = controllerPath,
            });

            string stray = Path.Combine(Directory.GetParent(Application.dataPath).FullName, strayRelative);
            bool leaked = File.Exists(stray);
            if (leaked) { File.Delete(stray); File.Delete(stray + ".meta"); }

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("controller_path"));
            Assert.IsFalse(leaked, $"nothing may be written at {strayRelative}");
        }

        [Test]
        public void SetupController_ClipEntryWithoutAName_IsSkippedWithAWarning()
        {
            var clips = BuildClips("nonamectrl", "idle", "walk");
            clips.Add(new JObject { ["path"] = $"{TempRoot}/walk.anim" });

            var result = SetupController(clips);

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_NO_NAME"));
        }

        [Test]
        public void SetupController_ClipsThatDoNotExist_ReturnsError()
        {
            var result = SetupController(new JArray
            {
                new JObject { ["name"] = "walk", ["path"] = $"{TempRoot}/missing.anim" },
            });
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_NOT_FOUND"));
        }

        [Test]
        public void SetupController_EveryEntrySkipped_StillReportsWhy()
        {
            // The all-skipped path returns a generic error, so the caller learned the clips
            // did not load without learning why.
            var result = Run(new JObject
            {
                ["action"] = "setup_controller",
                ["clips"] = new JArray { 7 },
                ["controller_path"] = $"{TempRoot}/Skipped.controller",
            });

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
            // The values, not just the order: with automatic thresholds left on, Unity
            // overwrote 1/2 with 0/1 and this line is what notices.
            Assert.AreEqual(new[] { 1f, 2f },
                tree.children.Select(c => c.threshold).ToArray());
        }

        [TestCase("attack", "Attack")]
        // Naming the trigger after the first segment would give 'Hero', not 'Attack'.
        [TestCase("hero_attack", "Attack", "Hero")]
        // The letters of 'hit' sit inside 'white', which under substring matching arms
        // a trigger the clip never asked for.
        [TestCase("white_flash", null, "Hit", "White")]
        // Detect used to lowercase before tokenizing, and the tokenizer splits camelCase
        // on char.IsUpper - so 'heroAttack' became one word and matched no keyword.
        [TestCase("heroAttack", "Attack")]
        // 'heroXMLAttack' has no lower-to-upper boundary at the acronym's end, so it
        // tokenized as 'xmlattack' and lost the trigger its snake_case twin gets.
        [TestCase("heroXMLAttack", "Attack")]
        // 'heroATTACK' held already - the break comes from the lowercase 'o' - so it is a
        // parity tripwire; 'XMLSlash' has no lowercase at the boundary and depends on the fix.
        [TestCase("heroATTACK", "Attack")]
        [TestCase("XMLSlash", "Slash")]
        public void SetupController_TriggerIsNamedAfterTheActionWord(string clipName, string trigger, params string[] absent)
        {
            var result = SetupController(BuildClips("trig", "idle", clipName));
            Assert.IsTrue(result.Value<bool>("success"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{TempRoot}/Hero.controller");
            var names = controller.parameters.Select(p => p.name).ToArray();
            if (trigger != null)
            {
                var found = controller.parameters.SingleOrDefault(p => p.name == trigger);
                Assert.IsNotNull(found, $"expected trigger '{trigger}'; parameters present: " + string.Join(", ", names));
                Assert.AreEqual(AnimatorControllerParameterType.Trigger, found.type);
            }
            foreach (string name in absent)
                Assert.That(names, Has.No.Member(name));
        }

        [Test]
        public void SetupController_ControllerPathEscapingAssets_FailsWithAMessage()
        {
            var clips = BuildClips("escapectrl", "idle", "walk");
            var result = Run(new JObject
            {
                ["action"] = "setup_controller",
                ["clips"] = clips,
                ["controller_path"] = $"{TempRoot}/../../../Hero.controller",
            });

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(ErrorText(result), Does.Contain("controller_path"));
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

            // Marked so reuse is distinguishable from replacement: two successes fit both.
            string ctrlPath = $"{TempRoot}/Hero.controller";
            var first = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            first.AddParameter("SentinelFromFirstBuild", AnimatorControllerParameterType.Bool);
            AssetDatabase.SaveAssets();

            Assert.IsTrue(SetupController(clips, overwrite: true).Value<bool>("success"));

            var second = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            Assert.That(second.parameters.Select(p => p.name), Has.No.Member("SentinelFromFirstBuild"),
                "an authorised overwrite must build a new controller, not reuse the old one");
        }

        [Test]
        public void SetupController_OverwriteThatCannotBuildAReplacement_KeepsTheOldController()
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

        // =====================================================================
        // full_setup
        // =====================================================================

        [Test]
        public void FullSetup_ControllerRefusal_StopsBeforeTouchingTheScene()
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
        public void FullSetup_RequestedSceneTargetMissing_IsNotReportedAsSuccess()
        {
            string path = CreateSheet("s6", 4, 1);
            var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                              ["output_dir"] = TempRoot, ["controller_path"] = $"{TempRoot}/S6.controller",
                              ["add_to_scene"] = true, ["scene_target"] = "NoSuchObject" });

            Assert.IsFalse(result.Value<bool>("success"),
                "an attachment that was asked for and did not happen is not a success");
            Assert.That(result["diagnostics"].ToString(), Does.Contain("SCENE_TARGET_NOT_FOUND"));
            // Asserted on the message rather than only the diagnostics array, because reading
            // the array was what let this refusal keep a shape no other refusal in the tool has.
            Assert.AreEqual("add_to_scene", result.Value<string>("step"));
            Assert.That(ErrorText(result), Does.Contain("NoSuchObject"));
        }

        [Test]
        public void FullSetup_ControllerPathWithoutExtension_StillReachesTheSceneObject()
        {
            string path = CreateSheet("s7", 4, 1);
            var go = new GameObject("SpriteTest_S7");
            try
            {
                var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot,
                                  ["controller_path"] = $"{TempRoot}/S7",   // no .controller suffix
                                  ["add_to_scene"] = true, ["scene_target"] = "SpriteTest_S7" });

                // Count the components rather than null-checking GetComponent: a missing one
                // compares equal to null without being a null reference.
                Assert.AreEqual(1, go.GetComponents<Animator>().Length,
                    "the object should have received an Animator; result was " + result.ToString(Newtonsoft.Json.Formatting.None));
                Assert.IsTrue(go.GetComponents<Animator>()[0].runtimeAnimatorController != null,
                    "the suffix the builder added must not lose the controller on the way to the scene");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FullSetup_SceneTargetWithoutASpriteRenderer_GetsOneAndSaysSo()
        {
            string path = CreateSheet("s8", 4, 1);
            var go = new GameObject("SpriteTest_S8");
            try
            {
                var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot, ["controller_path"] = $"{TempRoot}/S8.controller",
                                  ["add_to_scene"] = true, ["scene_target"] = "SpriteTest_S8" });

                Assert.IsTrue(result.Value<bool>("success"));
                Assert.AreEqual(1, go.GetComponents<SpriteRenderer>().Length,
                    "the clips animate a SpriteRenderer, so the attachment has to leave one behind");
                Assert.That(result["diagnostics"].ToString(), Does.Contain("SCENE_SPRITE_RENDERER_ADDED"),
                    "a component added to the caller's object must be reported");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FullSetup_SceneTargetThatAlreadyHasASpriteRenderer_KeepsTheOneItHas()
        {
            string path = CreateSheet("s9", 4, 1);
            var go = new GameObject("SpriteTest_S9");
            var existing = go.AddComponent<SpriteRenderer>();
            try
            {
                var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot, ["controller_path"] = $"{TempRoot}/S9.controller",
                                  ["add_to_scene"] = true, ["scene_target"] = "SpriteTest_S9" });

                // The scene step has to have run for the renderer assertions to mean anything.
                Assert.IsTrue(result.Value<bool>("success"));
                Assert.AreEqual(1, go.GetComponents<Animator>().Length);
                Assert.IsTrue(go.GetComponent<Animator>().runtimeAnimatorController != null);
                Assert.AreEqual(1, go.GetComponents<SpriteRenderer>().Length);
                Assert.AreSame(existing, go.GetComponent<SpriteRenderer>());
                Assert.That(result["diagnostics"].ToString(), Does.Not.Contain("SCENE_SPRITE_RENDERER_ADDED"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FullSetup_InactiveSceneTarget_StillReceivesTheComponents()
        {
            string path = CreateSheet("s10", 4, 1);
            var go = new GameObject("SpriteTest_S10");
            go.SetActive(false);
            try
            {
                var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot, ["controller_path"] = $"{TempRoot}/S10.controller",
                                  ["add_to_scene"] = true, ["scene_target"] = "SpriteTest_S10" });

                Assert.IsTrue(result.Value<bool>("success"),
                    "an inactive object is still the object the caller named; result was " + result.ToString(Newtonsoft.Json.Formatting.None));
                Assert.AreEqual(1, go.GetComponents<Animator>().Length);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FullSetup_DuplicateSceneTargetNames_AreRefusedBeforeTouchingEither()
        {
            string path = CreateSheet("s11", 4, 1);
            var first  = new GameObject("SpriteTest_S11");
            var second = new GameObject("SpriteTest_S11");
            try
            {
                var result = Run(new JObject { ["action"] = "full_setup", ["path"] = path, ["cols"] = 4,
                                  ["output_dir"] = TempRoot, ["controller_path"] = $"{TempRoot}/S11.controller",
                                  ["add_to_scene"] = true, ["scene_target"] = "SpriteTest_S11" });

                Assert.IsFalse(result.Value<bool>("success"));
                Assert.That(result["diagnostics"].ToString(), Does.Contain("SCENE_TARGET_AMBIGUOUS"));
                Assert.AreEqual(0, first.GetComponents<Animator>().Length + second.GetComponents<Animator>().Length,
                    "with two candidates the tool must not guess which one the caller meant");
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void FullSetup_RefusedClip_IsNotCountedAsCreated()
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
            Assert.AreEqual(2, onDisk, "idle and walk are valid, attack is refused");
            Assert.AreEqual(onDisk, result.Value<int>("clip_count"),
                "clip_count must count the clips that exist, not the ones that were asked for");
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_BAD_FPS"));
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
            var result = Run(new JObject
            {
                ["action"] = "full_setup",
                ["path"] = path,
                ["cols"] = 4,
                ["output_dir"] = TempRoot,
                ["controller_path"] = $"{TempRoot}/Named.controller",
            });

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{TempRoot}/hero_idle.anim"));
        }

        // =====================================================================
        // Refused paths
        //
        // SanitizeAssetPath answers a traversal path with null, which every AssetDatabase
        // entry point accepts, so each action used to describe the result of a lookup that
        // never happened. These pin the refusal itself.
        // =====================================================================

        [TestCase("get_info")]
        [TestCase("slice_sheet")]
        [TestCase("setup_clips")]
        [TestCase("full_setup")]
        public void PathEscapingAssets_IsRefusedInsteadOfLookedUp(string action)
        {
            var result = Run(new JObject
            {
                ["action"] = action,
                ["path"] = $"{TempRoot}/../../../outside.png",
                ["cols"] = 4,
                ["clips"] = OneClip("walk", 0, 3),
                ["output_dir"] = TempRoot,
            });

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

            var result = SetupController(clips);

            // The refused entry must be reported as refused, not as merely missing.
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.That(result["diagnostics"].ToString(), Does.Contain("CLIP_BAD_PATH"));
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
            // Noise, not a flat colour, which would compress below the ceiling; and a
            // power-of-two side, since a Default-type import rescales anything else
            // (measured: a 1200px sheet read back as 1024).
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
            // The ceiling is checked against the file, but base64 emits 4 bytes for every 3,
            // so a source under the limit still produces a payload above it.
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

    }
}
