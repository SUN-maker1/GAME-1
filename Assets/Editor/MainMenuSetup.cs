#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 一键生成开始界面场景。
///
/// 菜单：Tools / 农场RPG / 生成开始界面（MainMenu 场景）
/// 会做这些事：
///   1. 新建 Assets/Scenes/MainMenu.unity（已存在就覆盖）
///   2. 场景里放好：相机、MainMenuUI（标题 + 按钮，界面全部由代码生成）、SceneLoader（过场）、EventSystem
///   3. 把 MainMenu 加到 Build Settings 的第一位，作为启动场景
///
/// 生成后直接点 Play 就能看到开始界面。
/// </summary>
public static class MainMenuSetup
{
    private const string MenuRoot = "Tools/农场RPG/";
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";

    [MenuItem(MenuRoot + "生成开始界面（MainMenu 场景）", false, 11)]
    public static void BuildMainMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[农场RPG] 已取消：请先处理未保存的场景改动。");
            return;
        }

        UnityEngine.SceneManagement.Scene scene =
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        MakeCamera();

        // 菜单本体：界面全部由 MainMenuUI 在运行时搭出来
        GameObject menu = new GameObject("MainMenuUI");
        menu.AddComponent<MainMenuUI>();

        // 过场淡入淡出 + 落位，读档回原位置靠它。
        // 【必须单独一个物体】SceneLoader.Awake 里有 DontDestroyOnLoad(gameObject)，
        // 它带的是整个 GameObject —— 和 MainMenuUI 挂在一起会把标题界面一起拖进游戏场景，
        // 表现为"进了农场又看到主菜单盖在上面"。
        GameObject loader = new GameObject("SceneLoader");
        loader.AddComponent<SceneLoader>();

        // EventSystem：没有它按钮点不动
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        PutFirstInBuildSettings();

        EditorSceneManager.OpenScene(ScenePath);
        Selection.activeGameObject = menu;

        Debug.Log("[农场RPG] 开始界面已生成：" + ScenePath + "，并且已经放到 Build Settings 第一位。\n" +
                  "点 Play 就能看到标题界面。想改标题/配色，选中 MainMenu 物体，在 MainMenuUI 上改。");
    }

    [MenuItem(MenuRoot + "把 MainMenu 设为启动场景（移到第一位）", false, 12)]
    public static void PutFirstInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s != null && s.path == ScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log("[农场RPG] Build Settings 第一位现在是 " + ScenePath);
    }

    private static void MakeCamera()
    {
        GameObject go = new GameObject("Main Camera");
        go.tag = "MainCamera";

        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.nearClipPlane = -100f;
        cam.farClipPlane = 100f;

        go.AddComponent<AudioListener>();
        go.transform.position = new Vector3(0f, 0f, -10f);
    }
}

/// <summary>
/// 编辑器侧的退出桥接。
/// Application.Quit() 在编辑器里没反应，所以主菜单点「退出游戏」时要靠这里真正停掉 Play。
/// 运行时脚本不引用 UnityEditor，事件机制把两边解耦。
/// </summary>
[InitializeOnLoad]
public static class MainMenuEditorBridge
{
    static MainMenuEditorBridge()
    {
        AppQuit.Requested += OnQuitRequested;
    }

    private static void OnQuitRequested()
    {
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }
}
#endif
