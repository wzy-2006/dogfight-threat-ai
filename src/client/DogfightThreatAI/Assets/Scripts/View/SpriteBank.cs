using System.Collections.Generic;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 精灵台账。由编辑器工具「空战/① 生成精灵台账」用 AssetDatabase 从
    /// Assets/ThirdParty/kenney-pixel-shmup/ 自动填好，运行时只做查表。
    ///
    /// 这样素材仍然住在 ThirdParty/ 下（符合 NOTES.md 的导入约定），
    /// 不需要把它们复制一份到 Resources/。
    ///
    /// 任何查不到的情况都会退化为程序化生成的占位图，保证素材没配好时游戏仍然能跑。
    /// </summary>
    [CreateAssetMenu(fileName = "SpriteBank", menuName = "空战/精灵台账", order = 1)]
    public class SpriteBank : ScriptableObject
    {
        [Tooltip("24 架飞机，索引对应 ship_0000 ~ ship_0023")]
        public Sprite[] Ships = new Sprite[24];

        [Tooltip("120 个地形块，索引对应 tile_0000 ~ tile_0119")]
        public Sprite[] Tiles = new Sprite[120];

        [Tooltip("敌机精灵的统一着色（让同款机型区分敌我）")]
        public Color EnemyTint = new Color(1f, 0.62f, 0.62f, 1f);

        readonly Dictionary<string, Sprite> _fallbacks = new Dictionary<string, Sprite>();

        /// <summary>按工程内路径取精灵，例如 "ThirdParty/kenney-pixel-shmup/Ships/ship_0000.png"</summary>
        public Sprite ForPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            int slash = assetPath.LastIndexOf('/');
            string file = slash >= 0 ? assetPath.Substring(slash + 1) : assetPath;
            int dot = file.LastIndexOf('.');
            if (dot >= 0) file = file.Substring(0, dot);

            if (file.StartsWith("ship_"))
            {
                int idx;
                if (int.TryParse(file.Substring(5), out idx)) return GetShip(idx);
            }
            else if (file.StartsWith("tile_"))
            {
                int idx;
                if (int.TryParse(file.Substring(5), out idx)) return GetTile(idx);
            }
            return null;
        }

        public Sprite GetShip(int index)
        {
            if (Ships != null && index >= 0 && index < Ships.Length) return Ships[index];
            return null;
        }

        public Sprite GetTile(int index)
        {
            if (Tiles != null && index >= 0 && index < Tiles.Length) return Tiles[index];
            return null;
        }

        /// <summary>取不到真实素材时，生成一个能看的占位精灵（三角箭头）</summary>
        public Sprite Fallback(string key, Color color)
        {
            Sprite cached;
            if (_fallbacks.TryGetValue(key, out cached) && cached != null) return cached;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            var px = new Color32[size * size];
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            Color32 c = color;
            Color32 edge = new Color32(
                (byte)Mathf.Clamp(color.r * 255f * 0.55f, 0f, 255f),
                (byte)Mathf.Clamp(color.g * 255f * 0.55f, 0f, 255f),
                (byte)Mathf.Clamp(color.b * 255f * 0.55f, 0f, 255f),
                255);

            // 一个朝上的三角箭头
            for (int y = 0; y < size; y++)
            {
                float t = (float)y / (size - 1);
                int half = Mathf.RoundToInt(t * (size * 0.45f));
                for (int x = size / 2 - half; x <= size / 2 + half; x++)
                {
                    if (x < 0 || x >= size) continue;
                    bool border = (x == size / 2 - half || x == size / 2 + half || y == 0 || y == size - 1);
                    px[y * size + x] = border ? edge : c;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            Sprite sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                DogfightConst.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sp.name = "fallback_" + key;
            _fallbacks[key] = sp;
            return sp;
        }

        /// <summary>读一个地形块的像素（需要贴图勾选 Read/Write）。失败返回 null。</summary>
        public Color32[] ReadTilePixels(int index)
        {
            Sprite s = GetTile(index);
            if (s == null) return null;
            Texture2D t = s.texture;
            if (t == null) return null;
            try
            {
                return t.GetPixels32();
            }
            catch (UnityException)
            {
                // 贴图没有勾 Read/Write —— 由 PixelArtPostprocessor 负责打开
                return null;
            }
        }
    }
}
