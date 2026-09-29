#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 一键生成修女 NPC 的 5 段动画 + Animator 控制器。
///
/// 源图在 Assets/IMAGE/try1/idle/ 下，已经切成一排 64×64 的单帧，这里把它们拼成 AnimationClip，
/// 再建一个含 Idle / IdleToPray / Pray / PrayToIdle / Walk 五个状态的 AnimatorController。
///
/// 走法：
///   · 菜单 Tools ▸ 修女NPC ▸ ① 生成动画与控制器   （随时可以手动重跑，会覆盖旧的）
///   · 本脚本还会在 Unity 重新加载脚本后自动跑一次，而且只在 NunNPC.controller 还不存在时才动手，
///     所以第一次你不用做任何事，切回 Unity 等它编译完就自动生成好了。
/// </summary>
public static class NunNPCAnimationBuilder
{
    /// <summary>已切好多帧的源图目录</summary>
    private const string SpriteFolder = "Assets/IMAGE/try1/idle";

    /// <summary>生成物目录</summary>
    private const string OutputParent = "Assets/Animation";
    private const string OutputFolder = "Assets/Animation/NunNPC";

    /// <summary>生成出来的 Animator 控制器路径</summary>
    public const string ControllerPath = OutputFolder + "/NunNPC.controller";

    // Animator 参数名（要和 Assets/Scripts/NunNPCController.cs 里的一致）
    private const string ParamSpeed = "Speed";
    private const string ParamPray = "IsPraying";

    /// <summary>一段动画的定义：用哪张图、播放速度、要不要循环</summary>
    private struct ClipDef
    {
        public readonly string ClipName;    // 动画名，同时也是 Animator 里的状态名
        public readonly string TextureName; // 源 png 文件名（不含扩展名）
        public readonly float Fps;          // 播放帧率
        public readonly bool Loop;          // true = 循环播放

        public ClipDef(string clipName, string textureName, float fps, bool loop)
        {
            ClipName = clipName;
            TextureName = textureName;
            Fps = fps;
            Loop = loop;
        }
    }

    // 按需求表配好的 5 段动画
    private static readonly ClipDef[] Defs =
    {
        // 动画名          源图                            帧率   循环
        new ClipDef("Idle",       "NunNPC_Idle",       8f,  true),   // 原地一晃一晃的循环待机
        new ClipDef("IdleToPray", "NunNPC_IdleToPray", 8f,  false),  // 待机 → 祈祷，只播一次
        new ClipDef("Pray",       "NunNPC_Pray",       8f,  true),   // 保持祈祷，可循环
        new ClipDef("PrayToIdle", "NunNPC_PrayToIdle", 8f,  false),  // 祈祷 → 待机，只播一次
        new ClipDef("Walk",       "NunNPC_Walk",      12f,  true),   // 走路循环
    };

    // ───────────────────────────── 菜单入口 ─────────────────────────────

    [MenuItem("Tools/修女NPC/① 生成动画与控制器", false, 10)]
    public static void GenerateFromMenu()
    {
        Build(true);
    }

    [MenuItem("Tools/修女NPC/② 在场景里生成 NPC 物体", false, 20)]
    public static void CreateNpcInScene()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "修女NPC",
                "还没生成动画控制器。\n请先执行菜单 Tools ▸ 修女NPC ▸ ① 生成动画与控制器。",
                "好");
            return;
        }

        var go = new GameObject("NunNPC");
        Undo.RegisterCreatedObjectUndo(go, "Create NunNPC");

        // 贴图：先放一张静态的待机第 0 帧，免得没进 Play 时物体是透明的
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFrames(SpriteFolder + "/NunNPC_Idle.png").FirstOrDefault();
        sr.sortingOrder = 1;

        // 动画
        var animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        // 物理
        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        var col = go.AddComponent<CapsuleCollider2D>();
        col.direction = CapsuleDirection2D.Vertical;
        col.size = new Vector2(0.5f, 0.8f);

        // 精灵的轴心不一定是正中心，这里把碰撞体按实际轴心对齐，省得撞墙位置歪掉
        if (sr.sprite != null)
        {
            Vector2 pivotPx = sr.sprite.pivot;
            Rect rect = sr.sprite.rect;
            Vector2 offsetPx = new Vector2(pivotPx.x - rect.width * 0.5f, pivotPx.y - rect.height * 0.5f);
            col.offset = offsetPx / sr.sprite.pixelsPerUnit;
        }

        // 行为脚本
        go.AddComponent<NunNPCController>();

        // 放到场景视图正中间，别叠在一个奇怪的坐标上
        if (SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        Debug.Log("[修女NPC] 已在当前场景里生成 NunNPC。按 Play 就能看到待机动作：WASD 走、E 键祈祷。");
    }

    // ───────────────────────────── 主流程 ─────────────────────────────

    /// <summary>
    /// 生成 5 段动画和控制器。
    /// </summary>
    /// <param name="overwriteExisting">true = 覆盖已存在的资源（原地覆盖，不会破坏场景里已有的引用）</param>
    public static void Build(bool overwriteExisting)
    {
        try
        {
            EnsureFolder(OutputParent);
            EnsureFolder(OutputFolder);

            // ---------- 第 1 步：5 段动画 ----------
            var clips = new Dictionary<string, AnimationClip>();

            foreach (ClipDef def in Defs)
            {
                string clipPath = OutputFolder + "/" + def.ClipName + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

                if (existing != null && !overwriteExisting)
                {
                    clips[def.ClipName] = existing;
                    continue;
                }

                string texPath = SpriteFolder + "/" + def.TextureName + ".png";
                Sprite[] frames = LoadFrames(texPath);

                if (frames.Length == 0)
                {
                    Debug.LogError("[修女NPC] 在 " + texPath + " 里没找到切好的精灵，生成动画失败了。" +
                                   "请确认这张图的 Sprite Mode 是 Multiple 且已经切好图。");
                    return;
                }

                AnimationClip clip = BuildClip(def, frames);

                if (existing != null)
                {
                    // 已存在就原地覆盖，保住它的 GUID —— 场景和控制器里的引用不会断
                    EditorUtility.CopySerialized(clip, existing);
                    clips[def.ClipName] = existing;
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(clip, clipPath);
                    clips[def.ClipName] = clip;
                }

                Debug.Log(string.Format("[修女NPC] 动画 {0} 已生成：{1} 帧 @ {2}fps，{3}",
                    def.ClipName, frames.Length, def.Fps, def.Loop ? "循环" : "只播一次"));
            }

            // ---------- 第 2 步：Animator 控制器 ----------
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // 清掉新建控制器自带的那个空状态，我们要按自己的布局重新摆
            foreach (ChildAnimatorState child in sm.states.ToArray())
                sm.RemoveState(child.state);
            foreach (ChildAnimatorStateMachine child in sm.stateMachines.ToArray())
                sm.RemoveStateMachine(child.stateMachine);

            // 参数
            EnsureFloatParam(controller, ParamSpeed);
            EnsureBoolParam(controller, ParamPray);

            // 状态（位置只是编辑器里看着舒服）
            AnimatorState idle = sm.AddState("Idle", new Vector3(300f, 60f, 0f));
            AnimatorState idleToPray = sm.AddState("IdleToPray", new Vector3(570f, 60f, 0f));
            AnimatorState pray = sm.AddState("Pray", new Vector3(840f, 60f, 0f));
            AnimatorState prayToIdle = sm.AddState("PrayToIdle", new Vector3(840f, 250f, 0f));
            AnimatorState walk = sm.AddState("Walk", new Vector3(300f, -140f, 0f));

            idle.motion = clips["Idle"];
            idleToPray.motion = clips["IdleToPray"];
            pray.motion = clips["Pray"];
            prayToIdle.motion = clips["PrayToIdle"];
            walk.motion = clips["Walk"];

            idle.writeDefaultValues = true;
            idleToPray.writeDefaultValues = true;
            pray.writeDefaultValues = true;
            prayToIdle.writeDefaultValues = true;
            walk.writeDefaultValues = true;
            walk.speed = 1f;

            sm.defaultState = idle;

            // ---- 过渡连线 ----

            // 待机 → 待机转祈祷：想祈祷了就走这一段
            AnimatorStateTransition t = idle.AddTransition(idleToPray);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.10f;
            t.AddCondition(AnimatorConditionMode.If, 0f, ParamPray);

            // 待机转祈祷 → 保持祈祷：过渡动画播完自动接上，不用条件
            t = idleToPray.AddTransition(pray);
            t.hasExitTime = true;
            t.exitTime = 1f;
            t.hasFixedDuration = true;
            t.duration = 0.15f;

            // 保持祈祷 → 祈祷转待机
            t = pray.AddTransition(prayToIdle);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.10f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, ParamPray);

            // 祈祷转待机 → 待机
            t = prayToIdle.AddTransition(idle);
            t.hasExitTime = true;
            t.exitTime = 1f;
            t.hasFixedDuration = true;
            t.duration = 0.15f;

            // 待机 ↔ 行走
            t = idle.AddTransition(walk);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.05f;
            t.AddCondition(AnimatorConditionMode.Greater, 0.01f, ParamSpeed);

            t = walk.AddTransition(idle);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.05f;
            t.AddCondition(AnimatorConditionMode.Less, 0.01f, ParamSpeed);

            // 走起来可以随时打断祈祷序列（一边走一边祷告很怪）
            foreach (AnimatorState from in new[] { idleToPray, pray, prayToIdle })
            {
                t = from.AddTransition(walk);
                t.hasExitTime = false;
                t.hasFixedDuration = true;
                t.duration = 0.10f;
                t.AddCondition(AnimatorConditionMode.Greater, 0.01f, ParamSpeed);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[修女NPC] 全部生成完毕 → " + OutputFolder +
                      "\n  · Idle / IdleToPray / Pray / PrayToIdle / Walk 五段动画" +
                      "\n  · NunNPC.controller（参数 Speed、IsPraying）");
        }
        catch (Exception e)
        {
            Debug.LogError("[修女NPC] 生成动画时出错：" + e);
        }
    }

    // ───────────────────────────── 工具方法 ─────────────────────────────

    /// <summary>把一张切好的图拼成一段精灵动画</summary>
    private static AnimationClip BuildClip(ClipDef def, Sprite[] frames)
    {
        var clip = new AnimationClip
        {
            name = def.ClipName,
            frameRate = def.Fps,
            legacy = false
        };

        // 每帧一个关键帧；末尾再多补一帧，让最后一帧有正常的停留时间
        //   循环动画补第 0 帧 → 首尾无缝
        //   一次性动画补最后一帧 → 定格一下再切走
        var keys = new ObjectReferenceKeyframe[frames.Length + 1];
        for (int i = 0; i < frames.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i / def.Fps,
                value = frames[i]
            };
        }
        keys[frames.Length] = new ObjectReferenceKeyframe
        {
            time = frames.Length / def.Fps,
            value = def.Loop ? frames[0] : frames[frames.Length - 1]
        };

        // 精灵动画 = 对 SpriteRenderer.m_Sprite 打一条关键帧曲线
        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,        // 空路径 = 挂在动画自己的这个物体上
            propertyName = "m_Sprite"
        };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        // 循环标记
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = def.Loop;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        return clip;
    }

    /// <summary>读出这张图里所有切好的精灵，并按名字末尾的序号排好（免得 10 排在 2 前面）</summary>
    private static Sprite[] LoadFrames(string texturePath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(texturePath)
            .OfType<Sprite>()
            .OrderBy(s => FrameIndex(s.name))
            .ToArray();
    }

    /// <summary>从 "NunNPC_Idle_12" 里取出 12</summary>
    private static int FrameIndex(string name)
    {
        int i = name.LastIndexOf('_');
        if (i < 0) return int.MaxValue;

        int n;
        return int.TryParse(name.Substring(i + 1), out n) ? n : int.MaxValue;
    }

    /// <summary>递归创建文件夹（Assets/Animation/NunNPC 这种多级路径）</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path);
        string leaf = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

        parent = parent.Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void EnsureFloatParam(AnimatorController c, string name)
    {
        foreach (AnimatorControllerParameter p in c.parameters)
            if (p.name == name && p.type == AnimatorControllerParameterType.Float) return;

        c.AddParameter(name, AnimatorControllerParameterType.Float);
    }

    private static void EnsureBoolParam(AnimatorController c, string name)
    {
        foreach (AnimatorControllerParameter p in c.parameters)
            if (p.name == name && p.type == AnimatorControllerParameterType.Bool) return;

        c.AddParameter(name, AnimatorControllerParameterType.Bool);
    }
}

/// <summary>
/// 自动兜底：Unity 重新加载脚本之后，如果还没生成过 NunNPC.controller，就自己跑一次。
/// 所以第一次你什么都不用点 —— 切回 Unity 等它编译完，动画就自己出来了。
/// 生成过之后这段逻辑就再也不动了。
/// </summary>
[InitializeOnLoad]
internal static class NunNPCAnimationAutoRun
{
    static NunNPCAnimationAutoRun()
    {
        // 用 delayCall 推迟到资源数据库准备就绪之后，静态构造里去碰 AssetDatabase 会报错
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(NunNPCAnimationBuilder.ControllerPath) != null)
                return;

            Debug.Log("[修女NPC] 检测到还没生成过动画资源，自动开始生成……");
            NunNPCAnimationBuilder.Build(false);
        };
    }
}
#endif
