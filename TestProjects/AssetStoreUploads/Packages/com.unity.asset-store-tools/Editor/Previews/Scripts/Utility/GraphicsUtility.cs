using UnityEngine;

namespace AssetStoreTools.Previews.Utility
{
    internal static class GraphicsUtility
    {
        public static Texture2D GetTextureFromCamera(Camera camera, int desiredWidth, int desiredHeight, int desiredDepth)
        {
            var texture = new Texture2D(desiredWidth, desiredHeight);
            var originalRenderTexture = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(desiredWidth, desiredHeight, desiredDepth);
            var cameraInitiallyEnabled = camera.enabled;

            try
            {
                if (cameraInitiallyEnabled)
                    camera.enabled = false;

                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                texture.Apply();
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = originalRenderTexture;
                RenderTexture.ReleaseTemporary(renderTexture);
                camera.enabled = cameraInitiallyEnabled;
            }

            return texture;
        }

        public static Texture2D ResizeTexture(Texture2D source, int desiredWidth, int desiredHeight)
        {
            var texture = new Texture2D(desiredWidth, desiredHeight);
            var originalRenderTexture = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(desiredWidth, desiredHeight, 32);

            try
            {
                RenderTexture.active = renderTexture;
                Graphics.Blit(source, renderTexture);

                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();
            }
            finally
            {
                RenderTexture.active = originalRenderTexture;
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            return texture;
        }

        public static Texture2D ResizeTextureNormalMap(Texture2D source, int desiredWidth, int desiredHeight)
        {
            var texture = new Texture2D(desiredWidth, desiredHeight);
            var originalRenderTexture = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(desiredWidth, desiredHeight, 32, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);

            try
            {
                RenderTexture.active = renderTexture;
                Graphics.Blit(source, renderTexture);

                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);

                for (int i = 0; i < texture.width; i++)
                {
                    for (int j = 0; j < texture.height; j++)
                    {
                        var color = texture.GetPixel(i, j);
                        color.b = color.r;
                        color.r = color.a;
                        color.a = 1;
                        texture.SetPixel(i, j, color);
                    }
                }

                texture.Apply();
            }
            finally
            {
                RenderTexture.active = originalRenderTexture;
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            return texture;
        }
    }
}