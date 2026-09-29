using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 对话 UI 一键搭建工具。
///
/// 菜单：Tools ▸ 农场RPG ▸ 对话 ▸ 自动搭建对话UI
///
/// 点一下自动在场景里生成：
///   Canvas（没有则新建，ScreenSpaceOverlay，1920x1080 缩放）
///   └─ DialoguePanel（底部对话框，挂 DialogueUI，所有引用自动接好）
///       ├─ BoxBackground（对话框背景 Image，拉伸填满面板）
///       ├─ PortraitImage（左侧立绘 Image）
///       ├─ NameText（名字 TMP 文本）
///       └─ ContentText（内容 TMP 文本）
///   EventSystem（场景没有则新建，UI 交互需要）
///
/// 面板生成后默认隐藏（DialogueUI.Awake 也会保证隐藏）。
/// 生成后只需在 DialogueUI 上指定中文字体 fontAsset 即可。
/// </summary>
public static class DialogueUIBuilder
{
    [MenuItem("Tools/农场RPG/对话/自动搭建对话UI")]
    public static void BuildDialogueUI()
    {
        // ---- 1. 找 / 建 Canvas ----
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGo = new GameObject("Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGo, "创建 Canvas");
        }

        // ---- 2. 场景里已有 DialogueUI 就不重复建 ----
        DialogueUI existing = Object.FindObjectOfType<DialogueUI>();
        if (existing != null)
        {
            EditorUtility.DisplayDialog("已存在", "场景里已经有 DialogueUI 了（" + existing.gameObject.name + "），不用重复搭建。", "好的");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // ---- 3. 对话框面板 ----
        GameObject panel = CreateUIObject("DialoguePanel", canvas.transform);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.13f, 0.12f, 0.17f, 0.97f);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        // 底部条：左右留 4%，高度占 25%，距底 5%
        panelRect.anchorMin = new Vector2(0.04f, 0.05f);
        panelRect.anchorMax = new Vector2(0.96f, 0.30f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // ---- 4. 对话框背景（拉伸填满面板，换 boxStyle 换的就是它）----
        GameObject boxBg = CreateUIObject("BoxBackground", panel.transform);
        Image boxBgImg = boxBg.AddComponent<Image>();
        boxBgImg.color = Color.white;
        RectTransform boxBgRect = boxBg.GetComponent<RectTransform>();
        Stretch(boxBgRect, 0f, 0f, 0f, 0f);
        // 注意：BoxBackground 在最底层，盖住面板底色；没指定样式时会自动隐藏，露出面板底色

        // ---- 5. 左侧立绘 ----
        GameObject portrait = CreateUIObject("PortraitImage", panel.transform);
        Image portraitImg = portrait.AddComponent<Image>();
        portraitImg.preserveAspect = true;
        RectTransform portraitRect = portrait.GetComponent<RectTransform>();
        portraitRect.anchorMin = new Vector2(0f, 0f);
        portraitRect.anchorMax = new Vector2(0f, 1f);
        portraitRect.pivot = new Vector2(0.5f, 0.5f);
        portraitRect.anchoredPosition = new Vector2(120f, 0f);
        portraitRect.sizeDelta = new Vector2(210f, -24f); // 高度随面板，上下各留 12

        // ---- 6. 名字文本（左上）----
        GameObject nameGo = CreateUIObject("NameText", panel.transform);
        TextMeshProUGUI nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
        nameTmp.text = "名字";
        nameTmp.fontSize = 30;
        nameTmp.color = new Color(1f, 0.92f, 0.6f);
        nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform nameRect = nameGo.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.anchoredPosition = new Vector2(254f, -14f);
        nameRect.sizeDelta = new Vector2(-278f, 40f);

        // ---- 7. 内容文本（名字下方，占满剩余区域）----
        GameObject contentGo = CreateUIObject("ContentText", panel.transform);
        TextMeshProUGUI contentTmp = contentGo.AddComponent<TextMeshProUGUI>();
        contentTmp.text = "对话内容……";
        contentTmp.fontSize = 26;
        contentTmp.color = Color.white;
        contentTmp.alignment = TextAlignmentOptions.TopLeft;
        contentTmp.enableWordWrapping = true;
        RectTransform contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 0f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.offsetMin = new Vector2(254f, 12f);
        contentRect.offsetMax = new Vector2(-24f, -58f);

        // ---- 8. 挂 DialogueUI 并自动接好引用 ----
        DialogueUI ui = panel.AddComponent<DialogueUI>();
        ui.dialoguePanel = panel;
        ui.nameText = nameTmp;
        ui.contentText = contentTmp;
        ui.portraitImage = portraitImg;
        ui.boxBackgroundImage = boxBgImg;
        // 如果 TMP 设置存在，先用默认字体顶着（中文仍需自己换中文字体资源）
        if (TMP_Settings.defaultFontAsset != null)
            ui.fontAsset = TMP_Settings.defaultFontAsset;

        // ---- 9. EventSystem（UI 需要，没有就建）----
        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(es, "创建 EventSystem");
        }

        // ---- 10. 默认隐藏 + 选中面板方便查看 ----
        panel.SetActive(false);
        Undo.RegisterCreatedObjectUndo(panel, "搭建对话UI");
        Selection.activeGameObject = panel;

        Debug.Log("[DialogueUIBuilder] 对话UI搭建完成！\n" +
                  "下一步：把含中文的 TMP 字体资源拖到 DialogueUI 的 Font Asset 上\n" +
                  "（没有的话：Window ▸ TextMeshPro ▸ Font Asset Creator 生成一个）。");
    }

    // ------------------------------------------------------------------ 工具

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>四边拉伸填满父物体，带内边距</summary>
    private static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
