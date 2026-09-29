# 项目长期笔记（农场 RPG，Unity 2022.3 团结引擎）

## 代码风格红线
- **禁止对 Unity Object（组件、GameObject）用 `??` 或 `?.` 判空**：Unity 重载了 `==`，GetComponent 失败返回假 null（C# 引用非 null），`??`/`?.` 走 C# 引用判断会漏掉，随后访问成员抛 MissingComponentException / MissingReferenceException。必须写 `if (x == null) x = ...`。
- 运行时脚本（非 Editor 目录）不要引用 UnityEditor：本项目 Assembly-CSharp 无此先例，编辑期逻辑用 `[ExecuteAlways] + 标记 + Update` 或拆到 Assets/Editor 下。
- **「运行时改数值 → 要存到磁盘」的标准做法**：运行时脚本不能引用 UnityEditor，就在运行时类里声明 `public static event System.Action XxxChanged` 并在改动后触发；由 Assets/Editor 下 `[InitializeOnLoad]` 的类订阅，处理里**必须先 `EditorUtility.SetDirty(资源)` 再 `AssetDatabase.SaveAssets()`**（SaveAssets 只写已标脏的资源），并用 EditorApplication.update 节流避免每帧写盘。
- **Play 模式改的场景物体必然回滚**：凡是要跨会话保留的数据，必须落在 ScriptableObject 资源里（例如对话每句立绘的位置，存在 DialogueLayout.slotOverrides 这种 key→数值 的表里），不要指望写在场景 MonoBehaviour 的字段上。

## 工程关键约定
- 场景：Area_Farm / Area_Town / Area_Room（卧室，guid a748b205388242dcb17a3cb941fa8add）+ SampleScene，均已入 Build Settings。
- 地图自适应：MapBoundary 运行时按 Map_Background SpriteRenderer bounds 建墙/吸附传送点/同步相机；PPU 决定世界尺寸（Farm 地图 PPU=8，room PPU=200，attack 200）。
- 传送：ScenePortal(entryID 字符串) → SceneLoader.LoadScene；落地优先"回程传送点内侧 1.2 单位"，其次 SceneEntryPoint，再 Start。autoSnapToEdge=0 的传送点/入口不吸附（室内门口用）。
- Editor 菜单统一在 Tools ▸ 农场RPG ▸ 下（中文二级菜单）。
- 场景/资源常直接改 YAML（外部改序列化值需改字段名防旧值残留；Play 模式改动不保存）。
- 本机无法离线编译 C#（编译器被安全策略拦截），运行时脚本改动后需用户在 Unity Console 确认。

## 开始界面 / 存档（2026-09-28 新增）
- 游戏名暂定「晨曦农场 / DAWNLIGHT FARM」。启动流程：MainMenu 场景（Build Settings 第一位）→ 开始游戏 → Area_Farm。
- 菜单 UI 全部由 `MainMenuUI` 在运行时代码生成（背景渐变/标题/按钮/存档面板/设置面板），改标题配色只改 Inspector，不要手摆预制体。场景里挂 MainMenuUI + SceneLoader + EventSystem 即可，一键生成走 Tools ▸ 农场RPG ▸ 生成开始界面。
- 存档：`SaveSystem`（3 槽，persistentDataPath/Saves/save_N.json）；读档回原位置靠 `SceneLoader.SetArrivalOverride`（一次性落位覆盖，优先于入口点）。
- 游戏内：F5 保存 / F9 读档 / Esc 暂停菜单（`GameSaveManager`，自动安装，主菜单场景自动让位）。
- **红线：`SceneLoader` 必须独占一个 GameObject**。它 Awake 里有 `DontDestroyOnLoad(gameObject)`，带的是整个物体——把 MainMenuUI 挂在同一个物体上会把标题界面整块拖进游戏场景并盖在画面上（表现为"进了游戏又看到主菜单"，2026-09-28 踩过）。各场景（Farm/Town/Room）的 SceneLoader 都在独占的 `_Systems` 物体上，MainMenu 场景是 MainMenuUI / SceneLoader 两个物体。
- **红线（黑屏事故根源）**：主菜单的「出生场景名」必须取 `SceneManager.GetActiveScene().name`，**不能取 `gameObject.scene.name`**——同物体上有 SceneLoader 时物体已被挪进 `DontDestroyOnLoad` 场景，拿到的名字会变成 `"DontDestroyOnLoad"`，于是主菜单把自己当成"已离开菜单场景"当场自毁 → 一进 Play 黑屏 / 标题一闪就没 / 进了游戏又弹回标题。MainMenuUI 现有 `menuSceneName` 字段可手填，取不到名字时宁可不拆（停用自毁保护）。
- **红线：运行时搭出来的 UI Canvas 一律是独立根物体**，不要 `SetParent` 到宿主物体下（否则会被 DontDestroyOnLoad 捎进游戏场景）。MainMenuUI 的 `MainMenuCanvas` 就是这样，随 MainMenu 场景正常卸载。
- **红线：单例的"多余实例自毁"一律 `Destroy(this)`，绝不 `Destroy(gameObject)`**（SceneLoader / DialogueManager 已改）。历史事故：从游戏返回主菜单时，新场景的 SceneLoader 发现自己不是 Instance，`Destroy(gameObject)` 把整个 MainMenu 物体（含 MainMenuUI）连锅端 → 黑屏。
- 相关：判断"当前是不是主菜单场景"不能只 `FindObjectOfType<MainMenuUI>() != null`（被带走的残留 UI 也会被找到），要加 `ui.gameObject.scene == active` 或比对场景名。
- **退出游戏一律走 `AppQuit.Request()`**（运行时发事件 + Application.Quit），Editor 侧 MainMenuEditorBridge 订阅后停 Play —— 运行时脚本不许碰 UnityEditor。

## 用户偏好
- 中文交流；期待"一键/自动"方案（菜单一键生成、自动适配），少手动摆坐标。
- 像素风 2D 农场 RPG，人物素材 try2（walk 四向 + attack），身高约 1.28 世界单位。

## 对话系统（NPC/ 目录）约定
- 三层可复用配置：`DialogueLayout`（全局位置/大小/字体/颜色）、`DialoguePreset`（外观预设=字体+字号+颜色+描边+框体+立绘尺寸整套快照）、`DialogueLine.preset`（每句临时整套换外观，优先于 line.boxStyle）。
- **立绘位置模板优先级**：本句 slotOverrides > 同图模板(key 图:<spriteName>) > 同角色模板(key 角色:<speaker>|L/R) > 台词原值 > 布局默认。默认拖动存 BySprite（picture 通用），用户不想一个个调的关键。
- 改 DialoguePortraitSlot 时注意 `CaptureBaseOnce()` 的 base 值机制（NonSerialized），新加的数据要走同一套路，避免每次显示累加偏移。
- 字体只有放在 Assets/Resources/Fonts 才能在运行时被找到；导入时必须字符集 Dynamic（否则中文方块）。设导入参数一律用反射（勿直接依赖 TrueTypeFontImporter / FontCharacterSet 的具体类型）。
- 运行时 IMGUI（F7 面板）不能用 EditorGUILayout，工具栏/下拉只能用 GUILayout.Toggle + BeginScrollView 手写。

## 碰撞/物理红线
- **玩家碰撞体必须以"视觉脚底"为底边**：try2 图集切片轴心是底边中心（帧内脚底高于轴心 0.08~0.18），Player BoxCollider2D = size 0.7×0.5 + offset (0, 0.1)，即碰撞盒覆盖下半身、底边贴脚底。若以轴心为中心（offset 0），碰撞盒会悬到脚底虚空里，从家具"后面"接近时被提前 0.5 单位顶住 = 空气墙（2026-09-28 已修，勿改回）。
- **手写 Unity .meta 必须复制完整文件再 sed 改字段**：head -N 截断会丢 textureType/spritePixelsToUnits 等关键行，导致资源被当普通纹理导入、LoadAssetAtPath<Sprite> 返回 null。
- **障碍物"完全贴合"实现**：要让"选中多大区域，角色身体就走不到多大区域"，障碍碰撞体必须 = 选中的区域**原样**（BoxCollider2D.size = size；Polygon 椭圆半径 = size/2）。玩家自身碰撞体由物理自动顶在框边，身体边缘恰好停在框上，**不要**向内收缩玩家体型（收缩反而让人能探进框里）。
- 同理，地图边界墙（MapBoundary）也是"碰撞体=地图四边"，角色被自身碰撞体顶在边界，无需额外偏移。
- 绘制工具若用 `Plane(z=0)` 接鼠标射线求世界点，Scene 视图必须是 2D 正视角，否则斜视角会把拖出的区域放大偏移（ObstacleShapeBrush 已强制切 2D）。
