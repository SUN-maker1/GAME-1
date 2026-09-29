using System;
using UnityEngine;

/// <summary>
/// 退出游戏的统一入口。
///
/// 【为什么要有这个类】
/// 运行时脚本不能引用 UnityEditor（本项目 Assembly-CSharp 也没有这个先例），
/// 而在编辑器里 Application.Quit() 是没有任何效果的 —— 点「退出游戏」会看起来没反应。
/// 所以这里只负责「广播一个退出请求」，真正的编辑器逻辑
/// （EditorApplication.isPlaying = false）由 Assets/Editor 下的类订阅处理。
/// 打包成 exe 后没人订阅，Application.Quit() 自己就会生效。
/// </summary>
public static class AppQuit
{
    public static event Action Requested;

    public static void Request()
    {
        if (Requested != null) Requested();

        // 编辑器里这行会被忽略（不会真退出 Unity），打包后才起作用
        Application.Quit();
    }
}
