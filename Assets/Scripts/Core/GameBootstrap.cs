using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 运行时自检 + 补件。编译完按 Play 自动跑，不需要改场景。
///
/// 解决：直接打开 a 场景按 Play 时，场景里没有 SceneLoader，
/// 而 ScenePortal.Fire() 写的是 SceneLoader.Instance?.LoadScene(...)，
/// Instance 为 null 就静默什么都不做 —— 表现就是「走进传送框没反应，直接走出地图外」。
/// </summary>
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject host = new GameObject("~GameBootstrap");
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<GameBootstrapDriver>();
    }
}

public class GameBootstrapDriver : MonoBehaviour
{
    private float _timer = 0.5f;
    private bool _logged;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Check();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _logged = false;
        Check();
    }

    private void Check()
    {
        // 1) 没有 SceneLoader 就补一个 —— 传送功能必需
        if (SceneLoader.Instance == null)
        {
            GameObject go = new GameObject("SceneLoader(自动补)");
            go.AddComponent<SceneLoader>();
            Debug.LogWarning("[自检] 当前场景「" + SceneManager.GetActiveScene().name +
                             "」里没有 SceneLoader，已自动补一个。传送现在可以正常工作了。\n" +
                             "建议：把 SceneLoader 也拖一份进这个场景并保存，下次就不会靠自动补件。");
        }

        if (_logged) return;
        _logged = true;

        // 2) 报一下传送点的情况，方便确认位置对不对
        ScenePortal[] portals = Object.FindObjectsOfType<ScenePortal>();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("[自检] 场景「").Append(SceneManager.GetActiveScene().name).Append("」：")
          .Append("SceneLoader ").Append(SceneLoader.Instance != null ? "有" : "无")
          .Append("，传送点 ").Append(portals.Length).Append(" 个");
        foreach (ScenePortal p in portals)
        {
            if (p == null) continue;
            Collider2D c = p.GetComponent<Collider2D>();
            Bounds b = c != null ? c.bounds : new Bounds(p.transform.position, Vector3.one);
            sb.Append("\n    ").Append(p.name)
              .Append(" → ").Append(p.targetSceneName).Append(" / ").Append(p.entryID)
              .Append("　触发框 X[").Append(b.min.x.ToString("F2")).Append(", ").Append(b.max.x.ToString("F2"))
              .Append("] Y[").Append(b.min.y.ToString("F2")).Append(", ").Append(b.max.y.ToString("F2")).Append("]");
        }
        Debug.Log(sb.ToString());
    }

    private void Update()
    {
        // 每隔一会儿看一眼玩家有没有踩到传送点（诊断用，只提示不干预）
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = 1f;

        ScenePortal[] portals = Object.FindObjectsOfType<ScenePortal>();
        if (portals == null || portals.Length == 0) return;

        Collider2D player = FindPlayerCollider();
        if (player == null) return;

        foreach (ScenePortal p in portals)
        {
            if (p == null || !p.IsOverlapping(player)) continue;
            Debug.Log("[自检] 玩家正踩在传送点「" + p.name + "」上 → 目标 " + p.targetSceneName +
                      "（如果一直刷这条却不传送，说明 SceneLoader 还是没有）");
        }
    }

    private static Collider2D FindPlayerCollider()
    {
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject g in tagged)
        {
            if (g == null) continue;
            Collider2D c = g.GetComponentInChildren<Collider2D>();
            if (c != null) return c;
        }
        return null;
    }
}
