using System;
using System.Linq;
using UnityEngine;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace UniGLTF
{
    public interface ITextureConverter
    {
        Texture2D GetImportTexture(Texture2D texture);
        Texture2D GetExportTexture(Texture2D texture);
    }

    public static class TextureConverter
    {
        public delegate Color32 ColorConversion(Color32 color);

        public static Texture2D Convert(Texture2D texture, glTFTextureTypes textureType, ColorConversion colorConversion, Material convertMaterial)
        {
            var copyTexture = CopyTexture(texture, TextureIO.GetColorSpace(textureType), convertMaterial);
            if (colorConversion != null)
            {
                copyTexture.SetPixels32(copyTexture.GetPixels32().Select(x => colorConversion(x)).ToArray());
                copyTexture.Apply();
            }
            copyTexture.name = texture.name;
            return copyTexture;
        }

        struct ColorSpaceScope : IDisposable
        {
            bool m_sRGBWrite;

            public ColorSpaceScope(RenderTextureReadWrite colorSpace)
            {
                m_sRGBWrite = GL.sRGBWrite;
                switch (colorSpace)
                {
                    case RenderTextureReadWrite.Linear:
                        GL.sRGBWrite = false;
                        break;

                    case RenderTextureReadWrite.sRGB:
                    default:
                        GL.sRGBWrite = true;
                        break;
                }
            }
            public ColorSpaceScope(bool sRGBWrite)
            {
                m_sRGBWrite = GL.sRGBWrite;
                GL.sRGBWrite = sRGBWrite;
            }

            public void Dispose()
            {
                GL.sRGBWrite = m_sRGBWrite;
            }
        }

#if UNITY_EDITOR && VRM_DEVELOP
        [MenuItem("Assets/CopySRGBWrite", true)]
        static bool CopySRGBWriteIsEnable()
        {
            return Selection.activeObject is Texture;
        }

        [MenuItem("Assets/CopySRGBWrite")]
        static void CopySRGBWrite()
        {
            CopySRGBWrite(true);
        }

        [MenuItem("Assets/CopyNotSRGBWrite", true)]
        static bool CopyNotSRGBWriteIsEnable()
        {
            return Selection.activeObject is Texture;
        }

        [MenuItem("Assets/CopyNotSRGBWrite")]
        static void CopyNotSRGBWrite()
        {
            CopySRGBWrite(false);
        }

        static string AddPath(string path, string add)
        {
            return string.Format("{0}/{1}{2}{3}",
            Path.GetDirectoryName(path),
            Path.GetFileNameWithoutExtension(path),
            add,
            Path.GetExtension(path));
        }

        static void CopySRGBWrite(bool isSRGB)
        {
            var src = Selection.activeObject as Texture;
            var texturePath = UnityPath.FromAsset(src);

            var path = EditorUtility.SaveFilePanel("save prefab", "Assets",
            Path.GetFileNameWithoutExtension(AddPath(texturePath.FullPath, ".sRGB")), "prefab");
            var assetPath = UnityPath.FromFullpath(path);
            if (!assetPath.IsUnderAssetsFolder)
            {
                return;
            }
            Debug.LogFormat("[CopySRGBWrite] {0} => {1}", texturePath, assetPath);

            var renderTexture = new RenderTexture(src.width, src.height, 0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            using (var scope = new ColorSpaceScope(isSRGB))
            {
                Graphics.Blit(src, renderTexture);
            }

            var dst = new Texture2D(src.width, src.height, TextureFormat.ARGB32, false,
                RenderTextureReadWrite.sRGB == RenderTextureReadWrite.Linear);
            dst.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            dst.Apply();

            RenderTexture.active = null;

            assetPath.CreateAsset(dst);

            GameObject.DestroyImmediate(renderTexture);
        }
#endif

        public static Texture2D CopyTexture(Texture src, RenderTextureReadWrite colorSpace, Material material)
        {
            Texture2D dst = null;

            var renderTexture = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, colorSpace);

            using (var scope = new ColorSpaceScope(colorSpace))
            {
                if (material != null)
                {
                    Graphics.Blit(src, renderTexture, material);
                }
                else
                {
                    Graphics.Blit(src, renderTexture);
                }
            }

            dst = new Texture2D(src.width, src.height, TextureFormat.ARGB32, false, colorSpace == RenderTextureReadWrite.Linear);
            dst.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            dst.name = src.name;
            dst.anisoLevel = src.anisoLevel;
            dst.filterMode = src.filterMode;
            dst.mipMapBias = src.mipMapBias;
            dst.wrapMode = src.wrapMode;
#if UNITY_2017_1_OR_NEWER
            dst.wrapModeU = src.wrapModeU;
            dst.wrapModeV = src.wrapModeV;
            dst.wrapModeW = src.wrapModeW;
#endif
            dst.Apply();

            RenderTexture.active = null;
            if (Application.isEditor)
            {
                GameObject.DestroyImmediate(renderTexture);
            }
            else
            {
                GameObject.Destroy(renderTexture);
            }
            return dst;
        }
    }

    public class MetallicRoughnessConverter : ITextureConverter
    {
        private float _smoothnessOrRoughness;

        public MetallicRoughnessConverter(float smoothnessOrRoughness)
        {
            _smoothnessOrRoughness = smoothnessOrRoughness;
        }

        public Texture2D GetImportTexture(Texture2D texture)
        {
            var converted = TextureConverter.Convert(texture, glTFTextureTypes.Metallic, Import, null);
            return converted;
        }

        public Texture2D GetExportTexture(Texture2D texture)
        {
            var converted = TextureConverter.Convert(texture, glTFTextureTypes.Metallic, Export, null);
            return converted;
        }

        public Color32 Import(Color32 src)
        {
            // Roughness(glTF): dst.g -> Smoothness(Unity): src.a (with conversion)
            // Metallic(glTF) : dst.b -> Metallic(Unity)  : src.r

            var pixelRoughnessFactor = (src.g * _smoothnessOrRoughness) / 255.0f; // roughness
            var pixelSmoothness = 1.0f - Mathf.Sqrt(pixelRoughnessFactor);

            return new Color32
            {
                r = src.b,
                g = 0,
                b = 0,
                // Bake roughness values into a texture.
                // See: https://github.com/dwango/UniVRM/issues/212.
                a = (byte)Mathf.Clamp(pixelSmoothness * 255, 0, 255),
            };
        }

        public Color32 Export(Color32 src)
        {
            // Smoothness(Unity): src.a -> Roughness(glTF): dst.g (with conversion)
            // Metallic(Unity)  : src.r -> Metallic(glTF) : dst.b

            var pixelSmoothness = (src.a * _smoothnessOrRoughness) / 255.0f; // smoothness
            // https://blogs.unity3d.com/jp/2016/01/25/ggx-in-unity-5-3/
            var pixelRoughnessFactorSqrt = (1.0f - pixelSmoothness);
            var pixelRoughnessFactor = pixelRoughnessFactorSqrt * pixelRoughnessFactorSqrt;

            return new Color32
            {
                r = 0,
                // Bake smoothness values into a texture.
                // See: https://github.com/dwango/UniVRM/issues/212.
                g = (byte)Mathf.Clamp(pixelRoughnessFactor * 255, 0, 255),
                b = src.r,
                a = 255,
            };
        }
    }

    public class NormalConverter : ITextureConverter
    {
        private Material m_decoder;
        private Material GetDecoder()
        {
            if (m_decoder == null)
            {
                m_decoder = new Material(Shader.Find("UniGLTF/NormalMapDecoder"));
            }
            return m_decoder;
        }

        private Material m_encoder;
        private Material GetEncoder()
        {
            if (m_encoder == null)
            {
                m_encoder = new Material(Shader.Find("UniGLTF/NormalMapEncoder"));
            }
            return m_encoder;
        }

        // GLTF data to Unity texture
        // ConvertToNormalValueFromRawColorWhenCompressionIsRequired
        public Texture2D GetImportTexture(Texture2D texture)
        {
            var mat = GetEncoder();
            var converted = TextureConverter.Convert(texture, glTFTextureTypes.Normal, null, mat);
            return converted;
        }

        // Unity texture to GLTF data
        // ConvertToRawColorWhenNormalValueIsCompressed
        public Texture2D GetExportTexture(Texture2D texture)
        {
            var mat = GetDecoder();
            var converted = TextureConverter.Convert(texture, glTFTextureTypes.Normal, null, mat);
            return converted;
        }
    }

    public class OcclusionConverter : ITextureConverter
    {

        public Texture2D GetImportTexture(Texture2D texture)
        {
            var converted = TextureConverter.Convert(texture, glTFTextureTypes.Occlusion, Import, null);
            return converted;
        }

        public Texture2D GetExportTexture(Texture2D texture)
        {
            var converted = TextureConverter.Convert(texture, glTFTextureTypes.Occlusion, Export, null);
            return converted;
        }

        public Color32 Import(Color32 src)
        {
            return new Color32
            {
                r = 0,
                g = src.r,
                b = 0,
                a = 255,
            };
        }

        public Color32 Export(Color32 src)
        {
            return new Color32
            {
                r = src.g,
                g = 0,
                b = 0,
                a = 255,
            };
        }
    }
}
