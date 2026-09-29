using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一条存档记录。字段全部是 public 字段（不是属性），因为 JsonUtility 只序列化字段。
/// 以后要往存档里加东西（背包、任务、好感度…），直接在这里加字段即可，
/// 老存档读出来时新字段会取这里的默认值，不会报错。
/// </summary>
[Serializable]
public class SaveData
{
    public int slot = 1;
    public string title = "存档";

    /// <summary>玩家当时所在的场景名（对应 Build Settings 里的场景）</summary>
    public string sceneName = "";

    public float posX = 0f;
    public float posY = 0f;

    /// <summary>
    /// 坐标是否有效。刚开的新档还没进过游戏，坐标是 (0,0) 这种没意义的值，
    /// 读档时不能用它落位（会把人扔到地图正中间，可能卡在墙里）。
    /// 只有真正抓到过玩家位置才置 true。
    /// </summary>
    public bool hasPosition = false;

    /// <summary>存档时间。用 Binary 存，比字符串省事也不会受系统区域设置影响</summary>
    public long timeBinary = 0L;

    [Header("游戏进度占位（以后有系统了直接往里写）")]
    public int day = 1;
    public int gold = 500;
    public float playedSeconds = 0f;

    public Vector3 Position
    {
        get { return new Vector3(posX, posY, 0f); }
    }

    public DateTime SaveTimeUTC
    {
        get { return timeBinary == 0L ? DateTime.MinValue : DateTime.FromBinary(timeBinary); }
    }

    public void SetPosition(Vector3 worldPos)
    {
        posX = worldPos.x;
        posY = worldPos.y;
    }

    public void Stamp()
    {
        timeBinary = DateTime.UtcNow.ToBinary();
    }

    /// <summary>给 UI 用的一行摘要，例如「农场 · 第 3 天 · 0:12」</summary>
    public string Summary()
    {
        string where = string.IsNullOrEmpty(sceneName) ? "未知地点" : sceneName;
        int minutes = Mathf.FloorToInt(playedSeconds / 60f);
        int hours = minutes / 60;
        minutes = minutes % 60;
        string time = (hours > 0 ? hours + "小时" : "") + minutes + "分钟";
        return string.Format("{0} · 第 {1} 天 · {2} 金币 · 已玩 {3}", where, day, gold, time);
    }
}

/// <summary>
/// 存档管家：3 个槽位，JSON 存在 Application.persistentDataPath/Saves 下。
///
/// 设计要点：
///   1. 读过的槽位在内存里缓存一份，菜单里反复打开面板不会一直读盘
///   2. 写 / 删立刻失效缓存，保证面板上看到的一定是最新的
///   3. 存档路径用 persistentDataPath，编辑器里和打包后都能读写，不用自己建目录
/// </summary>
public static class SaveSystem
{
    public const int SlotCount = 3;

    /// <summary>当前正在玩的槽位。快速保存会写到这里</summary>
    public static int CurrentSlot { get; set; }

    /// <summary>本次运行累计游玩秒数，存档时并进 playedSeconds</summary>
    public static float SessionSeconds { get; private set; }

    /// <summary>游戏进度占位：全局天数 / 金币。以后有正式系统了改成从那里读</summary>
    public static int CurrentDay { get; set; }
    public static int CurrentGold { get; set; }

    /// <summary>
    /// 强制读档落点：填了场景名，读档一律回这个场景（站在 ForceEntryID 入口点上），
    /// 忽略存档里记的场景。留空 = 回到存档时所在的场景和坐标。
    /// 主菜单的「读档都回农场」开关就是改这里。
    /// </summary>
    public static string ForceSceneName { get; set; }

    /// <summary>配合 ForceSceneName 用的入口点</summary>
    public static string ForceEntryID { get; set; }

    private static readonly Dictionary<int, SaveData> cache = new Dictionary<int, SaveData>();
    private static bool initialized;

    public static string SaveDir
    {
        get { return Path.Combine(Application.persistentDataPath, "Saves"); }
    }

    public static string SlotPath(int slot)
    {
        return Path.Combine(SaveDir, "save_" + slot + ".json");
    }

    private static void Init()
    {
        if (initialized) return;
        initialized = true;
        CurrentSlot = 1;
        CurrentDay = 1;
        CurrentGold = 500;
    }

    // ------------------------------------------------------------ 读写删

    public static bool HasSave(int slot)
    {
        Init();
        return File.Exists(SlotPath(slot));
    }

    /// <summary>读一个槽位。没有存档返回 null</summary>
    public static SaveData ReadSlot(int slot)
    {
        Init();

        if (cache.ContainsKey(slot))
            return cache[slot];

        string path = SlotPath(slot);
        if (!File.Exists(path))
        {
            cache[slot] = null;
            return null;
        }

        SaveData data = null;
        try
        {
            string json = File.ReadAllText(path);
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[存档] 读取失败：" + path + "\n" + e.Message);
        }

        if (data != null) data.slot = slot;
        cache[slot] = data;
        return data;
    }

    public static void WriteSlot(SaveData data)
    {
        Init();
        if (data == null) return;

        data.slot = Mathf.Clamp(data.slot, 1, SlotCount);
        data.Stamp();

        try
        {
            if (!Directory.Exists(SaveDir))
                Directory.CreateDirectory(SaveDir);

            File.WriteAllText(SlotPath(data.slot), JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[存档] 写入失败：" + e.Message);
            return;
        }

        cache[data.slot] = data;
        Debug.Log("[存档] 已保存到槽位 " + data.slot + " → " + SlotPath(data.slot));
    }

    public static void DeleteSlot(int slot)
    {
        Init();
        cache.Remove(slot);

        try
        {
            string path = SlotPath(slot);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[存档] 删除失败：" + e.Message);
        }
    }

    /// <summary>全部槽位快照，空槽位对应 null。菜单面板用它来列档</summary>
    public static SaveData[] AllSlots()
    {
        SaveData[] list = new SaveData[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            list[i] = ReadSlot(i + 1);
        return list;
    }

    /// <summary>最近一次保存的槽位；一个存档都没有返回 -1</summary>
    public static int LatestSlot()
    {
        int best = -1;
        long bestTime = long.MinValue;

        for (int i = 1; i <= SlotCount; i++)
        {
            SaveData d = ReadSlot(i);
            if (d == null) continue;
            if (d.timeBinary > bestTime)
            {
                bestTime = d.timeBinary;
                best = i;
            }
        }
        return best;
    }

    /// <summary>第一个空槽位；全满了返回 -1（此时由 UI 提示玩家覆盖哪个）</summary>
    public static int FirstEmptySlot()
    {
        for (int i = 1; i <= SlotCount; i++)
        {
            if (ReadSlot(i) == null) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------ 抓当前进度

    /// <summary>每帧累加游玩时长，由 GameSaveManager 调用</summary>
    public static void Tick(float unscaledDeltaTime)
    {
        SessionSeconds += Mathf.Max(0f, unscaledDeltaTime);
    }

    /// <summary>
    /// 把「现在这一刻」写成一条存档：当前场景名 + 玩家坐标 + 全局天数/金币。
    /// 玩家找不到时坐标保持存档里的旧值，不会把人扔到 (0,0)。
    /// </summary>
    public static SaveData CaptureCurrent(int slot)
    {
        Init();

        SaveData data = ReadSlot(slot);
        if (data == null)
        {
            data = new SaveData();
            data.slot = slot;
            data.title = "存档 " + slot;
        }

        data.sceneName = SceneManager.GetActiveScene().name;
        data.day = CurrentDay;
        data.gold = CurrentGold;
        data.playedSeconds = data.playedSeconds + SessionSeconds;

        Transform player = FindPlayer();
        if (player != null)
        {
            data.SetPosition(player.position);
            data.hasPosition = true;
        }

        return data;
    }

    public static SaveData NewGameData(int slot, string sceneName)
    {
        Init();
        SaveData data = new SaveData();
        data.slot = slot;
        data.title = "存档 " + slot;
        data.sceneName = sceneName;
        data.day = 1;
        data.gold = 500;
        data.playedSeconds = 0f;
        SessionSeconds = 0f;
        CurrentDay = 1;
        CurrentGold = 500;
        return data;
    }

    private static Transform FindPlayer()
    {
        if (PersistentPlayer.Instance != null && PersistentPlayer.Instance.gameObject != null)
            return PersistentPlayer.Instance.transform;

        try
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) return go.transform;
        }
        catch (UnityException) { /* 工程里没有 Player 标签，忽略 */ }

        return null;
    }

    // ------------------------------------------------------------ 跳转

    /// <summary>读档到底进哪个场景（考虑 ForceSceneName）</summary>
    public static string ResolveLoadScene(SaveData data)
    {
        if (!string.IsNullOrEmpty(ForceSceneName)) return ForceSceneName;
        return data != null ? data.sceneName : "";
    }

    /// <summary>
    /// 读档并跳转。走 SceneLoader 才有一致的淡入淡出和落位逻辑；
    /// 场景里没有 SceneLoader 时（比如直接在编辑器里打开菜单场景测试）退回直接加载。
    ///
    /// 目标场景 = ForceSceneName（填了的话）否则存档里记的场景。
    /// 只有「目标场景就是存档时的场景」时才用存档坐标落位；
    /// 被强制改了场景的话坐标没意义，改用入口点。
    /// </summary>
    public static void LoadSave(SaveData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[存档] 这条存档是空的，读不了。");
            return;
        }

        string target = ResolveLoadScene(data);
        if (string.IsNullOrEmpty(target))
        {
            Debug.LogWarning("[存档] 这条存档没记录场景名，也没设置强制场景，读不了。");
            return;
        }

        CurrentSlot = data.slot;

        // 把存档里的进度接上，否则下一次保存会用默认值把天数/金币冲掉
        CurrentDay = data.day;
        CurrentGold = data.gold;

        // 只有「目标场景就是存档时的场景」且坐标有效，才用存档坐标落位
        bool keepPosition = data.hasPosition
                            && !string.IsNullOrEmpty(data.sceneName)
                            && string.Equals(target, data.sceneName);
        string entryID = string.IsNullOrEmpty(ForceSceneName) ? "Start" : ForceEntryID;

        if (keepPosition)
            SceneLoader.SetArrivalOverride(data.Position);   // 场景激活后覆盖入口点，站在存档时的位置
        else
            SceneLoader.ClearArrivalOverride();

        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(target, entryID);
        }
        else
        {
            SceneLoader.ClearArrivalOverride();
            Debug.LogWarning("[存档] 场景里没有 SceneLoader，直接加载（没有过场淡入淡出、玩家会站在入口点）。");
            SceneManager.LoadScene(target);
        }
    }

    /// <summary>开新档：先落一份初始存档，再进第一个场景</summary>
    public static void StartNewGame(int slot, string sceneName, string entryID)
    {
        Init();

        SaveData data = NewGameData(slot, sceneName);
        WriteSlot(data);
        CurrentSlot = data.slot;

        // 新游戏用入口点落位，不要被上一次读档的 override 影响
        SceneLoader.ClearArrivalOverride();

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(sceneName, entryID);
        else
            SceneManager.LoadScene(sceneName);
    }

    // ------------------------------------------------------------ 显示用

    /// <summary>把存档时间显示成「今天 14:03」这种形式</summary>
    public static string TimeText(SaveData data)
    {
        if (data == null || data.timeBinary == 0L) return "--";

        DateTime local = data.SaveTimeUTC.ToLocalTime();
        DateTime now = DateTime.Now;

        string hm = local.ToString("HH:mm");
        if (local.Date == now.Date) return "今天 " + hm;
        if (local.Date == now.Date.AddDays(-1)) return "昨天 " + hm;
        return local.ToString("yyyy/M/d HH:mm");
    }
}
