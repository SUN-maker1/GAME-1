using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 调试用：不用走到地图边缘的传送条，也能直接切换场景。
/// 自动挂载（[RuntimeInitializeOnLoadMethod]），不用往场景里放东西。
///
///  Alt + 1..9 : 加载 Build Settings 里第 1..9 个场景（用该场景的 Start 入口）
///  Alt + 0    : 重新加载当前场景
///  Alt + `    : 打印当前场景名和 Build Settings 场景清单
///
/// 正式上线前把 enableHotkeys 关掉，或直接删掉这个文件。
/// </summary>
public class DebugSceneHotkeys : MonoBehaviour
{
    // 注意：不要改成 const —— const true 会让后面的 if 判断被编译器判为不可达代码（CS0162 警告）
    private static readonly bool enableHotkeys = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!enableHotkeys) return;
        if (FindObjectOfType<DebugSceneHotkeys>() != null) return;

        GameObject go = new GameObject("~DebugSceneHotkeys");
        go.hideFlags = HideFlags.DontSaveInEditor;
        go.AddComponent<DebugSceneHotkeys>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        if (!enableHotkeys) return;
        if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return;

        if (Input.GetKeyDown(KeyCode.BackQuote)) { DumpScenes(); return; }

        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            Reload(SceneManager.GetActiveScene().name);
            return;
        }

        for (int i = 1; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                int idx = i - 1;
                if (idx < SceneManager.sceneCountInBuildSettings)
                    LoadByBuildIndex(idx);
                else
                    Debug.LogWarning($"[调试热键] Build Settings 里没有第 {i} 个场景。");
                return;
            }
        }
    }

    private static void LoadByBuildIndex(int buildIndex)
    {
        string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        Reload(name);
    }

    private static void Reload(string sceneName)
    {
        if (SceneTransition.InputBlocked) return;          // 过场中别打断

        if (SceneLoader.Instance != null)
        {
            Debug.Log($"[调试热键] 切换到 {sceneName}");
            SceneLoader.Instance.LoadScene(sceneName, "Start");
        }
        else
        {
            Debug.LogWarning("[调试热键] 场景里没有 SceneLoader，改用直接加载（不走淡入淡出）。");
            SceneManager.LoadScene(sceneName);
        }
    }

    private static void DumpScenes()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder("[调试热键] Build Settings 场景：\n");
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string p = SceneUtility.GetScenePathByBuildIndex(i);
            sb.AppendLine($"  Alt+{i + 1}  {System.IO.Path.GetFileNameWithoutExtension(p)}");
        }
        sb.AppendLine($"  当前：{SceneManager.GetActiveScene().name}");
        Debug.Log(sb.ToString());
    }
}
