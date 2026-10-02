using System.Collections.Generic;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 多层视差背景（立项书要求「多层视差滚动，营造空间纵深感」）。
    ///
    /// 实现方式：每个图层是一个使用 Tiled 绘制模式的 SpriteRenderer，
    /// 每帧把图层位置设为 相机位置 × parallax —— 相机移动 1 个单位，图层只移动 parallax 个单位，
    /// 于是看上去就比相机慢 (1 - parallax)，形成纵深。
    /// 不需要写 Shader，也不依赖任何自定义材质。
    /// </summary>
    public class ParallaxBackground : MonoBehaviour
    {
        class Layer
        {
            public Transform T;
            public float Parallax;
            public float Drift;
            public Vector3 Base;
        }

        readonly List<Layer> _layers = new List<Layer>();
        Camera _cam;

        [Tooltip("图层覆盖范围（世界单位），要足够大以免看到边缘")]
        public float Coverage = 700f;

        public void Build(Camera cam, Sprite seaSprite, Texture2D terrainTex, Texture2D cloudTex, float cloudOpacity)
        {
            _cam = cam;
            Clear();

            if (terrainTex != null)
            {
                AddLayer("Terrain", SpriteFromTexture(terrainTex, "terrain"), 0.30f, 0f, 1f,
                    DogfightConst.Order.Terrain);
            }
            else if (seaSprite != null)
            {
                AddLayer("Sea", seaSprite, 0.30f, 0f, 1f, DogfightConst.Order.Terrain);
            }

            // 云层：只是薄薄一层装饰，透明度可调，也可以整个关掉
            if (cloudTex != null && cloudOpacity > 0.001f)
            {
                AddLayer("Clouds", SpriteFromTexture(cloudTex, "clouds"), 0.62f, 0.6f, cloudOpacity,
                    DogfightConst.Order.Clouds);
            }

            if (_cam != null) Sync(_cam.transform.position);
        }

        Sprite SpriteFromTexture(Texture2D tex, string name)
        {
            var sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), DogfightConst.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sp.name = "bg_" + name;
            return sp;
        }

        void AddLayer(string name, Sprite sprite, float parallax, float drift, float alpha, int order)
        {
            var go = new GameObject("Layer_" + name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            sr.sortingOrder = order;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(Coverage, Coverage);

            var layer = new Layer { T = go.transform, Parallax = parallax, Drift = drift, Base = Vector3.zero };
            _layers.Add(layer);
        }

        /// <summary>运行时调云层透明度（0 = 关掉）</summary>
        public void SetCloudOpacity(float alpha)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].T != null && _layers[i].T.name == "Layer_Clouds")
                {
                    SpriteRenderer sr = _layers[i].T.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            Sync(_cam.transform.position);
        }

        void Sync(Vector3 camPos)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                Layer l = _layers[i];
                float dx = camPos.x * l.Parallax;
                float dy = camPos.y * l.Parallax;
                if (l.Drift > 0f)
                {
                    dx += Mathf.Repeat(Time.time * l.Drift, Coverage) - Coverage * 0.5f;
                }
                l.T.position = new Vector3(l.Base.x + dx, l.Base.y + dy, l.T.position.z);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].T != null) Destroy(_layers[i].T.gameObject);
            }
            _layers.Clear();
        }
    }
}
