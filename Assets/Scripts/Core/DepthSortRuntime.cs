using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 「前景遮挡人物」——运行时自动版。
///
/// 不需要改场景、不需要改 prefab、不需要 Unity 重新载入场景文件。
/// 脚本一编译完，按 Play 就生效。
///
/// 做法：把人物和指定道具统统搬到「人物所在的那个排序图层」，
///       再按它们脚下的 Y 动态算 sortingOrder：
///       Y 越小（越靠屏幕下方＝越靠前）→ order 越大 → 画得越靠上 → 遮住别人。
/// </summary>
public static class DepthSortRuntime
{
    /// <summary>要参与遮挡的道具名字。想加东西直接往这里加。</summary>
    public static readonly string[] PropNames = { "盆栽", "盆栽2", "镜子" };

    /// <summary>基准 order。取这么大是为了压得住场景里手填的 0~6。</summary>
    public const int OrderBase = 20000;

    /// <summary>1 个世界单位 Y 换算成多少 order 差。</summary>
    public const float OrderPerUnit = 100f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject host = new GameObject("~DepthSortRuntime");
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<DepthSortDriver>();
    }
}

/// <summary>每帧把人物和道具的排序刷一遍。由 DepthSortRuntime 自动创建，不用手动挂。</summary>
[DefaultExecutionOrder(1000)]
public class DepthSortDriver : MonoBehaviour
{
    private readonly List<SpriteRenderer> _items = new List<SpriteRenderer>();
    private int _layerId = 0;
    private bool _layerPicked;
    private float _rescanTimer = 0f;
    private int _lastCount = -1;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Collect();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        Collect();
    }

    private void Update()
    {
        // 角色可能是传送进来、或者跨场景之后才生成的，隔 1 秒重扫一次
        _rescanTimer -= Time.deltaTime;
        if (_rescanTimer <= 0f)
        {
            _rescanTimer = 1f;
            Collect();
        }
    }

    private void LateUpdate()
    {
        if (_items.Count == 0) return;

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            SpriteRenderer r = _items[i];
            if (r == null) { _items.RemoveAt(i); continue; }

            if (r.sortingLayerID != _layerId)
                r.sortingLayerID = _layerId;

            int order = DepthSortRuntime.OrderBase - Mathf.RoundToInt(r.bounds.min.y * DepthSortRuntime.OrderPerUnit);
            order = Mathf.Clamp(order, short.MinValue, short.MaxValue);
            if (r.sortingOrder != order)
                r.sortingOrder = order;
        }
    }

    private void Collect()
    {
        _items.Clear();

        SpriteRenderer[] all = Object.FindObjectsOfType<SpriteRenderer>();
        if (all == null || all.Length == 0) return;

        // 先找人物，把它的排序图层当成统一目标（这样不管图层叫什么都能对上）
        SpriteRenderer player = null;
        foreach (SpriteRenderer r in all)
        {
            if (r == null) continue;
            Transform t = r.transform;
            while (t != null)
            {
                if (t.CompareTag("Player")) { player = r; break; }
                t = t.parent;
            }
            if (player != null) break;
        }

        if (player != null)
        {
            _layerId = player.sortingLayerID;
            _layerPicked = true;
        }
        else if (!_layerPicked)
        {
            _layerId = 0;
        }

        foreach (SpriteRenderer r in all)
        {
            if (r == null) continue;
            if (!IsPlayer(r) && !IsProp(r)) continue;
            _items.Add(r);
        }

        if (_items.Count != _lastCount)
        {
            _lastCount = _items.Count;
            Debug.Log("[遮挡排序] 参与 Y 轴排序的物体 " + _items.Count + " 个（图层 id=" + _layerId +
                      "，人物" + (player != null ? "已找到" : "没找到") + "）");
        }
    }

    private static bool IsPlayer(SpriteRenderer r)
    {
        Transform t = r.transform;
        while (t != null)
        {
            if (t.CompareTag("Player")) return true;
            t = t.parent;
        }
        return false;
    }

    private static bool IsProp(SpriteRenderer r)
    {
        string[] names = DepthSortRuntime.PropNames;
        if (names == null || names.Length == 0) return false;

        Transform t = r.transform;
        while (t != null)
        {
            string n = t.name;
            foreach (string key in names)
            {
                if (!string.IsNullOrEmpty(key) && n == key) return true;
            }
            t = t.parent;
        }
        return false;
    }
}
