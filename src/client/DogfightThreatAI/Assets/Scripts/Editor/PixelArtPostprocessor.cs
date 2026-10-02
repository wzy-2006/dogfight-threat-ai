using UnityEditor;
using UnityEngine;

namespace Dogfight.EditorTools
{
    /// <summary>
    /// 把 Kenney 素材的导入设置固化下来（见 docs/02-design/美术规范.md）：
    ///   32 Pixels Per Unit —— 32×32 的飞机正好 1×1 世界单位，16×16 地形块占 0.5×0.5
    ///   Point 过滤 + 关 Mipmap —— 像素画必须关闭插值，否则会糊
    ///   Read/Write 打开 —— 程序化地表生成需要读单块像素
    ///   地形块用 Repeat —— 视差背景用 Tiled 绘制模式
    ///
    /// 用 AssetPostprocessor 而不是手工改每张图的 Inspector：
    /// 素材有 148 个文件，手工改必然漏；而且以后换素材也会自动套用同一套规范。
    /// </summary>
    public class PixelArtPostprocessor : AssetPostprocessor
    {
        /// <summary>与 DogfightConst.PixelsPerUnit 保持一致</summary>
        public const float PixelsPerUnit = 32f;

        const string KenneyRoot = "ThirdParty/kenney-pixel-shmup";

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (path.IndexOf(KenneyRoot, System.StringComparison.OrdinalIgnoreCase) < 0) return;

            var importer = assetImporter as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;                       // 程序化地表要读像素
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = path.Contains("/Tiles/")
                ? TextureWrapMode.Repeat                      // 地形块用于 Tiled 平铺
                : TextureWrapMode.Clamp;
        }
    }
}
