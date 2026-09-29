#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把「人物」做成预制体，两个区域场景共用同一份。
/// 这样以后换模型、调碰撞体、改移动速度，只改预制体一次，所有场景自动一致，
/// 不会出现「换个场景主角变了模样」。
///
/// 菜单：Tools ▸ 农场RPG ▸ 玩家 ▸ ...
/// </summary>
public static class PlayerPrefabTools
{
    private const string PrefabDir  = "Assets/Prefabs";
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string PlayerName = "Player";

    [MenuItem("Tools/农场RPG/玩家/① 把当前场景的 Player 存为预制体", false, 200)]
    private static void SavePlayerAsPrefab()
    {
        GameObject player = FindPlayer(SceneManager.GetActiveScene());
        if (player == null)
        {
            Debug.LogError("[玩家预制体] 当前场景里找不到叫 Player 的物体。");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        Object prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            player, PrefabPath, InteractionMode.UserAction);

        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[玩家预制体] 已保存并挂接到 {PrefabPath}\n" +
                  "接下来执行「② 把预制体同步到所有区域场景」，让其它场景也用它。", prefab);
    }

    [MenuItem("Tools/农场RPG/玩家/② 把预制体同步到所有区域场景", false, 201)]
    private static void SyncPrefabToAllScenes()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[玩家预制体] 找不到 {PrefabPath}，请先执行「① 把当前场景的 Player 存为预制体」。");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;   // 用户点了「取消」，别动他的场景

        int done = 0;
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled) continue;

            Scene scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);

            GameObject old = FindPlayer(scene);
            Vector3 pos = old != null ? old.transform.position : Vector3.zero;

            if (old != null)
                Undo.DestroyObjectImmediate(old);

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            inst.transform.position = pos;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            done++;

            Debug.Log($"[玩家预制体] {scene.name}：Player 已换成预制体实例（位置 {pos}）");
        }

        Debug.Log($"[玩家预制体] 完成，共同步 {done} 个场景。\n" +
                  "以后改角色只需改 Assets/Prefabs/Player.prefab（或在任一场景改完用 Overrides ▸ Apply All）。");
    }

    [MenuItem("Tools/农场RPG/玩家/③ 检查各场景的 Player 是否一致", false, 202)]
    private static void CheckPlayers()
    {
        string current = SceneManager.GetActiveScene().path;
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled) continue;
            Scene scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
            GameObject p = FindPlayer(scene);
            if (p == null)
            {
                Debug.LogWarning($"[检查] {scene.name}：没有 Player！");
                continue;
            }

            string status = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p);
            bool isPrefab = !string.IsNullOrEmpty(status);
            SpriteRenderer sr = p.GetComponent<SpriteRenderer>();
            Animator anim = p.GetComponent<Animator>();
            BoxCollider2D bc = p.GetComponent<BoxCollider2D>();

            Debug.Log($"[检查] {scene.name}：\n" +
                      $"   预制体实例 = {(isPrefab ? "是（" + status + "）" : "否 ← 建议同步")}\n" +
                      $"   精灵 = {(sr != null && sr.sprite != null ? sr.sprite.name : "空")}\n" +
                      $"   控制器 = {(anim != null && anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "空")}\n" +
                      $"   碰撞体 = {(bc != null ? bc.size.ToString() : "无")}");
        }

        if (!string.IsNullOrEmpty(current))
            EditorSceneManager.OpenScene(current, OpenSceneMode.Single);
    }

    [MenuItem("Tools/农场RPG/玩家/④ 检查玩家动画是不是空 clip", false, 203)]
    private static void CheckClips()
    {
        GameObject player = FindPlayer(SceneManager.GetActiveScene());
        Animator anim = player != null ? player.GetComponent<Animator>() : null;

        // 场景里没找到就从控制器资产本身查
        UnityEditor.Animations.AnimatorController ctrl = null;
        if (anim != null && anim.runtimeAnimatorController != null)
            ctrl = anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (ctrl == null)
        {
            string[] found = AssetDatabase.FindAssets("Player t:AnimatorController");
            if (found.Length == 0)
            {
                Debug.LogError("[检查动画] 找不到玩家控制器。");
                return;
            }
            ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                AssetDatabase.GUIDToAssetPath(found[0]));
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[检查动画] 控制器：{ctrl.name}");

        int empty = 0;
        foreach (UnityEditor.Animations.AnimatorControllerLayer layer in ctrl.layers)
        {
            foreach (UnityEditor.Animations.ChildAnimatorState child in layer.stateMachine.states)
            {
                UnityEditor.Animations.AnimatorState st = child.state;
                AnimationClip clip = st.motion as AnimationClip;

                if (clip == null)
                {
                    sb.AppendLine($"    {st.name,-14} 没有 Motion（空状态）  ← 切到它会停在上一帧/静态精灵");
                    empty++;
                    continue;
                }

                bool noSprite = clip.empty ||
                                UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0;
                if (noSprite)
                {
                    sb.AppendLine($"    {st.name,-14} → {clip.name}  【空 clip，没有精灵帧】← 停下会掉回静态精灵（正面）");
                    empty++;
                }
                else
                {
                    sb.AppendLine($"    {st.name,-14} → {clip.name}  正常（{clip.length:F2}s）");
                }
            }
        }

        sb.AppendLine(empty == 0
            ? "    ✓ 全部正常。四个 Idle 都有对应朝向的单帧，停下会保持朝向。"
            : $"    ✗ 有 {empty} 个状态有问题。空 clip 不会被 Animator 驱动，表现就是「停下后变回正面」。");
        Debug.Log(sb.ToString());
    }

    private static GameObject FindPlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == PlayerName) return root;
            Transform t = root.transform.Find(PlayerName);
            if (t != null) return t.gameObject;
        }
        return null;
    }
}
#endif
