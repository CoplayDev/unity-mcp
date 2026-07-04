using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Branding
{
    /// <summary>
    /// The MCP for Unity "Ocean split-cube" brand mark.
    ///
    /// On Unity 2022.1+ it is drawn as resolution-independent vector geometry via UI Toolkit's
    /// Painter2D — crisp at any DPI, theme-independent (fixed brand colors matching the source
    /// SVG), and with no package dependency. Painter2D does not exist on the 2021.3 floor, so
    /// there we fall back to the raster brand icon (package-icon.png) that ships with the package.
    ///
    /// Geometry is transcribed from website/static/img/logo-mark.svg (viewBox "30 30 140 140"),
    /// which remains the human-facing source of truth. Size the element via USS (width/height);
    /// the drawing scales to the resolved content box.
    /// </summary>
    public sealed class OceanMark : VisualElement
    {
        public OceanMark()
        {
#if UNITY_2022_1_OR_NEWER
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
#else
            ApplyRasterFallback();
#endif
            pickingMode = PickingMode.Ignore; // purely decorative
        }

#if UNITY_2022_1_OR_NEWER
        // The source SVG viewBox spans 30..170 on both axes (a 140-unit square).
        private const float SvgOrigin = 30f;
        private const float SvgSize = 140f;

        // Brand palette (hex values lifted directly from logo-mark.svg).
        private static readonly Color LeftFill = FromHex(0x2563EB);   // blue cube faces
        private static readonly Color LeftStroke = FromHex(0x60A5FA); // blue cube outline
        private static readonly Color RightFill = FromHex(0x0D9488);  // teal cube faces
        private static readonly Color RightStroke = FromHex(0x2DD4BF);// teal cube outline
        private static readonly Color Bridge = FromHex(0x22D3EE);     // cyan bridge

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Rect rect = contentRect;
            float box = Mathf.Min(rect.width, rect.height);
            if (box <= 1f) return; // not laid out yet / too small to draw

            float scale = box / SvgSize;
            Vector2 P(float x, float y) => MapSvgPoint(x, y, rect);
            float S(float w) => w * scale;

            Painter2D p = mgc.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            // --- Left cube (blue) ---
            FillPoly(p, WithAlpha(LeftFill, 0.22f), P(94, 40), P(42, 70), P(94, 100));
            FillPoly(p, WithAlpha(LeftFill, 0.12f), P(42, 70), P(42, 130), P(94, 160), P(94, 100));
            StrokePath(p, LeftStroke, S(3f), true, P(94, 40), P(42, 70), P(42, 130), P(94, 160));
            StrokePath(p, WithAlpha(LeftStroke, 0.65f), S(1.8f), false, P(42, 70), P(94, 100));

            // --- Right cube (teal) ---
            FillPoly(p, WithAlpha(RightFill, 0.22f), P(106, 40), P(158, 70), P(106, 100));
            FillPoly(p, WithAlpha(RightFill, 0.12f), P(158, 70), P(158, 130), P(106, 160), P(106, 100));
            StrokePath(p, RightStroke, S(3f), true, P(106, 40), P(158, 70), P(158, 130), P(106, 160));
            StrokePath(p, WithAlpha(RightStroke, 0.65f), S(1.8f), false, P(158, 70), P(106, 100));

            // --- Bridge rungs (cyan) ---
            StrokePath(p, Bridge, S(3f), false, P(94, 70), P(106, 70));
            StrokePath(p, Bridge, S(3f), false, P(94, 100), P(106, 100));
            StrokePath(p, Bridge, S(3f), false, P(94, 130), P(106, 130));

            // --- Bridge dots (cyan) ---
            float dot = S(2.2f);
            FillDot(p, Bridge, P(94, 70), dot);
            FillDot(p, Bridge, P(106, 70), dot);
            FillDot(p, Bridge, P(94, 100), dot);
            FillDot(p, Bridge, P(106, 100), dot);
            FillDot(p, Bridge, P(94, 130), dot);
            FillDot(p, Bridge, P(106, 130), dot);
        }

        private static void FillPoly(Painter2D p, Color color, params Vector2[] pts)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }

        private static void StrokePath(Painter2D p, Color color, float width, bool closed, params Vector2[] pts)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            if (closed) p.ClosePath();
            p.Stroke();
        }

        private static void FillDot(Painter2D p, Color color, Vector2 center, float radius)
        {
            if (radius <= 0.01f) return;
            p.fillColor = color;
            p.BeginPath();
            p.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>Map a point from SVG viewBox space into the element's content rect (square, centered).</summary>
        private static Vector2 MapSvgPoint(float svgX, float svgY, Rect content)
        {
            float box = Mathf.Min(content.width, content.height);
            float scale = box / SvgSize;
            float offsetX = (content.width - box) * 0.5f;
            float offsetY = (content.height - box) * 0.5f;
            return new Vector2(
                offsetX + (svgX - SvgOrigin) * scale,
                offsetY + (svgY - SvgOrigin) * scale);
        }

        /// <summary>Convert a 0xRRGGBB literal into an opaque Color.</summary>
        private static Color FromHex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
#else
        // Painter2D is unavailable before Unity 2022.1 — show the raster brand icon instead.
        private void ApplyRasterFallback()
        {
            Texture2D tex = LoadBrandTexture();
            if (tex == null) return;
            style.backgroundImage = new StyleBackground(tex);
            style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
        }

        private static Texture2D LoadBrandTexture()
        {
            try
            {
                string root = MCPForUnity.Editor.Helpers.AssetPathUtility.GetMcpPackageRootPath();
                return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}/package-icon.png");
            }
            catch (System.Exception ex)
            {
                MCPForUnity.Editor.Helpers.McpLog.Warn($"OceanMark: failed to load brand icon fallback: {ex.Message}");
                return null;
            }
        }
#endif
    }
}
