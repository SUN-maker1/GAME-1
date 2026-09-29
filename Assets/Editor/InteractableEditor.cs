using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Interactable 自定义 Inspector —— 让添加对话像填表一样简单
/// </summary>
[CustomEditor(typeof(Interactable))]
public class InteractableEditor : Editor
{
    private SerializedProperty _dialogueAsset;
    private SerializedProperty _dialogueLines;
    private SerializedProperty _speakerName;
    private SerializedProperty _portrait;
    private SerializedProperty _portraitSide;
    private SerializedProperty _interactRange;
    private SerializedProperty _interactKey;
    private SerializedProperty _enableMouseClick;
    private SerializedProperty _showPrompt;
    private SerializedProperty _promptText;
    private SerializedProperty _promptHeight;
    private SerializedProperty _onInteractEnd;

    private bool _showDialogueEditor = true;
    private bool _showPortraitEditor = true;
    private Vector2 _scrollPos;

    private void OnEnable()
    {
        _dialogueAsset = serializedObject.FindProperty("dialogueAsset");
        _dialogueLines = serializedObject.FindProperty("dialogueLines");
        _speakerName = serializedObject.FindProperty("speakerName");
        _portrait = serializedObject.FindProperty("portrait");
        _portraitSide = serializedObject.FindProperty("portraitSide");
        _interactRange = serializedObject.FindProperty("interactRange");
        _interactKey = serializedObject.FindProperty("interactKey");
        _enableMouseClick = serializedObject.FindProperty("enableMouseClick");
        _showPrompt = serializedObject.FindProperty("showPrompt");
        _promptText = serializedObject.FindProperty("promptText");
        _promptHeight = serializedObject.FindProperty("promptHeight");
        _onInteractEnd = serializedObject.FindProperty("onInteractEnd");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Interactable target = (Interactable)this.target;

        // ------------------------------------------------------------------ 对话内容
        EditorGUILayout.Space(6);
        _showDialogueEditor = EditorGUILayout.Foldout(_showDialogueEditor, "对话内容", true, EditorStyles.foldoutHeader);
        if (_showDialogueEditor)
        {
            EditorGUI.indentLevel++;

            // 说话人
            EditorGUILayout.PropertyField(_speakerName, new GUIContent("说话人名字"));

            // 方式一：引用 DialogueAsset
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("方式一：引用对话资源（可复用）", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_dialogueAsset, new GUIContent("对话资源"));

            if (_dialogueAsset.objectReferenceValue != null)
            {
                if (GUILayout.Button("编辑对话资源", GUILayout.Height(28)))
                {
                    Selection.activeObject = _dialogueAsset.objectReferenceValue;
                }
                if (GUILayout.Button("取消引用（改用直接编辑）", GUILayout.Height(22)))
                {
                    _dialogueAsset.objectReferenceValue = null;
                }
            }

            // 方式二：直接编辑对话行
            if (_dialogueAsset.objectReferenceValue == null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("方式二：直接编辑对话（每行一句）", EditorStyles.miniBoldLabel);

                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(Mathf.Clamp(_dialogueLines.arraySize * 60f + 40f, 80f, 300f)));

                for (int i = 0; i < _dialogueLines.arraySize; i++)
                {
                    SerializedProperty line = _dialogueLines.GetArrayElementAtIndex(i);
                    SerializedProperty text = line.FindPropertyRelative("text");
                    SerializedProperty speaker = line.FindPropertyRelative("speakerName");

                    EditorGUILayout.BeginHorizontal();

                    // 序号
                    GUILayout.Label($"{i + 1}.", GUILayout.Width(24));

                    // 说话人（可选）
                    string speakerText = speaker.stringValue;
                    speakerText = EditorGUILayout.TextField(speakerText, GUILayout.Width(80));
                    if (speaker.stringValue != speakerText)
                        speaker.stringValue = speakerText;

                    // 对话内容
                    text.stringValue = EditorGUILayout.TextArea(text.stringValue, GUILayout.MinHeight(40));

                    // 删除按钮
                    if (GUILayout.Button("×", GUILayout.Width(24), GUILayout.Height(40)))
                    {
                        _dialogueLines.DeleteArrayElementAtIndex(i);
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }

                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("+ 添加一句对话", GUILayout.Height(28)))
                {
                    _dialogueLines.InsertArrayElementAtIndex(_dialogueLines.arraySize);
                }

                // 保存为对话资源
                if (_dialogueLines.arraySize > 0)
                {
                    if (GUILayout.Button("保存为对话资源（方便复用）", GUILayout.Height(26)))
                    {
                        SaveAsDialogueAsset(target);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        // ------------------------------------------------------------------ 人物立绘
        EditorGUILayout.Space(8);
        _showPortraitEditor = EditorGUILayout.Foldout(_showPortraitEditor, "人物立绘", true, EditorStyles.foldoutHeader);
        if (_showPortraitEditor)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(_portrait, new GUIContent("立绘图片"));
            if (_portrait.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("拖一张透明底 PNG 进来（512x512 起）", MessageType.Info);
            }

            EditorGUILayout.PropertyField(_portraitSide, new GUIContent("立绘位置"));

            if (_portrait.objectReferenceValue != null)
            {
                if (GUILayout.Button("应用立绘到所有对话行", GUILayout.Height(28)))
                {
                    target.ApplyPortraitToAllLines();
                    if (target.dialogueAsset != null)
                        target.ApplyPortraitToAsset();
                    EditorUtility.SetDirty(target);
                    if (target.dialogueAsset != null)
                        EditorUtility.SetDirty(target.dialogueAsset);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ------------------------------------------------------------------ 交互设置
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("交互设置", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_interactRange, new GUIContent("互动距离"));
        EditorGUILayout.PropertyField(_interactKey, new GUIContent("互动按键"));
        EditorGUILayout.PropertyField(_enableMouseClick, new GUIContent("鼠标左键互动"));
        EditorGUI.indentLevel--;

        // ------------------------------------------------------------------ 头顶提示
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("头顶提示", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_showPrompt, new GUIContent("显示提示"));
        if (_showPrompt.boolValue)
        {
            EditorGUILayout.PropertyField(_promptText, new GUIContent("提示文字"));
            EditorGUILayout.PropertyField(_promptHeight, new GUIContent("提示高度"));
        }
        EditorGUI.indentLevel--;

        // ------------------------------------------------------------------ 事件
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("事件", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_onInteractEnd, new GUIContent("互动结束事件"));
        EditorGUI.indentLevel--;

        // ------------------------------------------------------------------ 快捷操作
        EditorGUILayout.Space(8);
        if (GUILayout.Button("测试互动（在 Scene 视图中模拟）", GUILayout.Height(30)))
        {
            target.Interact();
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>把当前对话行保存为一个 DialogueAsset 资源</summary>
    private void SaveAsDialogueAsset(Interactable target)
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "保存对话资源", "Dialogue_" + target.name, "asset", "保存对话资源", "Assets");

        if (string.IsNullOrEmpty(path)) return;

        DialogueAsset asset = ScriptableObject.CreateInstance<DialogueAsset>();
        asset.defaultSpeaker = target.speakerName;

        foreach (DialogueLine line in target.dialogueLines)
        {
            asset.lines.Add(line);
        }

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        target.dialogueAsset = asset;
        target.dialogueLines.Clear();

        EditorUtility.DisplayDialog("保存成功", $"对话资源已保存到：\n{path}", "确定");
    }
}
