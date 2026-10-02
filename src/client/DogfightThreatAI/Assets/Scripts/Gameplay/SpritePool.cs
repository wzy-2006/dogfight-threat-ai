using System.Collections.Generic;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 极简精灵对象池。飞机、弹丸、特效都复用同一套，避免运行时频繁 Instantiate/Destroy。
    /// 每个池项就是一个带 SpriteRenderer 的 GameObject，用 active 标记回收。
    /// </summary>
    public class SpritePool
    {
        class Item
        {
            public GameObject Go;
            public SpriteRenderer Sr;
            public bool Active;
        }

        readonly List<Item> _items = new List<Item>();
        readonly Transform _root;
        readonly string _name;
        readonly int _sortingOrder;

        public SpritePool(Transform parent, string name, int sortingOrder)
        {
            _name = name;
            _sortingOrder = sortingOrder;
            var go = new GameObject(name + "Pool");
            go.transform.SetParent(parent, false);
            _root = go.transform;
        }

        public Transform Root { get { return _root; } }

        /// <summary>取一个空闲项（不够就新建）</summary>
        public SpriteRenderer Rent(out GameObject go)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Active)
                {
                    _items[i].Active = true;
                    _items[i].Go.SetActive(true);
                    go = _items[i].Go;
                    return _items[i].Sr;
                }
            }

            var newGo = new GameObject(_name + "_" + _items.Count);
            newGo.transform.SetParent(_root, false);
            var sr = newGo.AddComponent<SpriteRenderer>();
            sr.sortingOrder = _sortingOrder;
            var item = new Item { Go = newGo, Sr = sr, Active = true };
            _items.Add(item);
            go = newGo;
            return sr;
        }

        /// <summary>回收所有未被标记为 keep 的对象</summary>
        public void RecycleUnmarked(HashSet<GameObject> keep)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                if (!it.Active) continue;
                if (keep != null && keep.Contains(it.Go)) continue;
                it.Active = false;
                it.Go.SetActive(false);
            }
        }

        public void RecycleAll()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Active = false;
                _items[i].Go.SetActive(false);
            }
        }
    }
}
