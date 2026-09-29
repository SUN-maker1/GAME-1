#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 碰撞区域体检 —— 「画了区域但人还是走得过去」时，点一次这个菜单，
/// Console 会打印完整报告：区域有没有碰撞体、是不是被勾成了触发器、
/// 层级碰撞矩阵有没有关掉、当前场景的 Player 有没有刚体……
/// 缺什么、该怎么修，报告里直接写。
/// </summary>
public class ObstacleShapeDoctor
{
    [MenuItem("Tools/农场RPG/碰撞/诊断：为什么挡不住人？", false, 220)]
    private static void Diagnose()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("════════ 碰撞区域体检 ════════");

        // —— 1. 玩家 ——
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        if (players.Length == 0)
            sb.AppendLine("【严重】场景里没有标签为 Player 的物体！");
        else
        {
            sb.AppendLine($"玩家数量：{players.Length}");
            foreach (GameObject p in players)
            {
                sb.AppendLine($"  · {p.name}（场景 {p.scene.name}，层 {LayerMask.LayerToName(p.gameObject.layer)}）");

                Rigidbody2D rb = p.GetComponent<Rigidbody2D>();
                if (rb == null)
                    sb.AppendLine("    ✗ 没有 Rigidbody2D —— 人物会退化成直接改坐标，任何碰撞都挡不住！"
                                  + "\n      修法：给 Player 加 Rigidbody2D，Body Type=Dynamic，Gravity Scale=0。");
                else
                    sb.AppendLine($"    ✓ Rigidbody2D：{(rb.bodyType == RigidbodyType2D.Dynamic ? "Dynamic（正常）" : rb.bodyType + "（非 Dynamic，静态墙挡不住！）")}"
                                  + $"，Gravity={rb.gravityScale}，Simulated={rb.simulated}");

                Collider2D col = p.GetComponent<Collider2D>();
                if (col == null)
                    sb.AppendLine("    ✗ 没有任何 2D 碰撞体，人物无法与墙产生碰撞！");
                else
                    sb.AppendLine($"    ✓ 碰撞体：{col.GetType().Name}，尺寸 {col.bounds.size.x:F2}×{col.bounds.size.y:F2}，Trigger={col.isTrigger}");
            }
        }

        // —— 2. 区域 ——
        ObstacleShape2D[] areas = Object.FindObjectsOfType<ObstacleShape2D>(true);
        sb.AppendLine($"\n碰撞区域数量：{areas.Length}");
        if (areas.Length == 0)
            sb.AppendLine("  （一个都没有 —— 先用菜单「绘制碰撞区域…」画一块）");

        int bad = 0;
        foreach (ObstacleShape2D a in areas)
        {
            Collider2D col = a.GetComponent<Collider2D>();
            string layerName = LayerMask.LayerToName(a.gameObject.layer);
            sb.AppendLine($"  · {a.name}（层 {layerName}，形状 {a.shape}，尺寸 {a.size.x:F2}×{a.size.y:F2}，世界中心 {CenterOf(col, a)}）");

            if (col == null)
            {
                bad++;
                sb.AppendLine("    ✗ 没有碰撞体！点「一键修复」重建，或者直接改一下 Inspector 里的尺寸触发重建。");
                continue;
            }

            if (!col.enabled) { bad++; sb.AppendLine("    ✗ 碰撞体被禁用了（enabled=false）。"); }
            if (col.isTrigger)
                sb.AppendLine("    ⚠ 勾了「只做触发器」—— 这种本来就不挡人，只做感应。要挡人请把 Is Trigger 取消。");
            else
                sb.AppendLine($"    ✓ {col.GetType().Name} 实心，能挡人");

            if (a.size.x < 0.2f || a.size.y < 0.2f)
                sb.AppendLine("    ⚠ 区域太小了（不足 0.2 单位），可能是鼠标点一下没拖动造成的，重新拖一块大点的。");

            // 层级碰撞矩阵
            foreach (GameObject p in players)
            {
                if (p == null) continue;
                if (Physics2D.GetIgnoreLayerCollision(p.layer, a.gameObject.layer))
                {
                    bad++;
                    sb.AppendLine($"    ✗ Project Settings ▸ Physics 2D 的碰撞矩阵里，层「{LayerMask.LayerToName(p.layer)}」和「{layerName}」被设为互不碰撞！");
                }
            }
        }

        // —— 3. 全局开关 ——
        sb.AppendLine($"\n物理自动模拟：{Physics2D.autoSimulation}（应为 True）");
        sb.AppendLine($"重力：{Physics2D.gravity}（俯视 RPG 一般为 0）");

        sb.AppendLine("═══════════════════════════════");
        sb.AppendLine(bad == 0
            ? "结论：没查到硬伤。如果人物还是穿过去，请确认：① 区域是否画在人物实际所在的坐标上（Play 时切到 Scene 视图看橙色框）② 人物是不是从区域的透明边角绕过去了。"
            : $"结论：发现 {bad} 处问题，见上面 ✗ 开头的行。");

        Debug.Log(sb.ToString());
        Debug.Log("提示：Play 运行时切到 Scene 视图，勾选右上角 Gizmos，能直接看到碰撞体线框——人物能不能被挡住一眼就看得出来。");
    }

    private static string CenterOf(Collider2D col, ObstacleShape2D a)
    {
        Vector3 c = col != null ? col.bounds.center : a.transform.position + (Vector3)a.offset;
        return $"({c.x:F2}, {c.y:F2})";
    }

    /// <summary>
    /// 「桌子后面有一大块空气墙」这类问题的专用雷达：
    /// 把场景里【所有实心 2D 碰撞体】一个个列出来（Play 模式下也能用，连自动生成的边界墙一起看）。
    /// 来历不明的（既不是碰撞区域、不是边界墙、也不是传送条）就是空气墙本尊，直接删它所在的物体。
    /// </summary>
    [MenuItem("Tools/农场RPG/碰撞/诊断：找出多余的空气墙", false, 222)]
    private static void FindStrayWalls()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("════════ 空气墙雷达：当前场景所有实心碰撞体 ════════");
        sb.AppendLine("（触发器不挡人，不列；Play 模式下运行也能查，含自动边界墙）");

        Collider2D[] cols = Object.FindObjectsOfType<Collider2D>();
        int suspicious = 0;
        foreach (Collider2D col in cols)
        {
            if (col == null || col.isTrigger) continue;

            GameObject go = col.gameObject;
            Bounds b = col.bounds;
            string role;
            if (col.GetComponent<ObstacleShape2D>() != null)
                role = "✓ 碰撞区域（ObstacleShape2D）";
            else if (go.name.StartsWith("Wall_"))
                role = "✓ 地图自动边界墙";
            else if (col.GetComponent<ScenePortal>() != null)
                role = "✓ 传送条";
            else if (go.CompareTag("Player"))
                role = "✓ 玩家自己";
            else if (go.GetComponent<SpriteRenderer>() != null)
                role = "⚠ 挂在带贴图的物体上（可能是给家具加的，确认一下）";
            else
            {
                role = "✗ 来历不明 —— 很可能就是你看到的空气墙，直接删掉这个物体";
                suspicious++;
            }

            sb.AppendLine($"  · {go.name}（{col.GetType().Name} {b.size.x:F2}×{b.size.y:F2}，" +
                          $"中心 ({b.center.x:F2}, {b.center.y:F2})）\n      {role}");
        }

        sb.AppendLine("═══════════════════════════════");
        sb.AppendLine(suspicious == 0
            ? "结论：没有来历不明的碰撞体。若人还是被莫名的墙挡住，对照上面 ✓ 项的坐标，看是哪一块位置不对。"
            : $"结论：发现 {suspicious} 个来历不明的碰撞体（✗ 行）。在 Hierarchy 里按名字找到并删除，然后 Ctrl+S 保存场景。");

        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/农场RPG/碰撞/一键修复：重建所有区域的碰撞体", false, 221)]
    private static void FixAll()
    {
        ObstacleShape2D[] areas = Object.FindObjectsOfType<ObstacleShape2D>(true);
        int n = 0;
        foreach (ObstacleShape2D a in areas)
        {
            Undo.RecordObject(a, "重建碰撞区域");
            a.Rebuild();
            EditorUtility.SetDirty(a);
            n++;
        }

        // 玩家缺刚体的话顺手补上（按农场 RPG 的通用配置）
        int fixedPlayers = 0;
        foreach (GameObject p in GameObject.FindGameObjectsWithTag("Player"))
        {
            if (p.GetComponent<Rigidbody2D>() != null) continue;
            Rigidbody2D rb = Undo.AddComponent<Rigidbody2D>(p);
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            EditorUtility.SetDirty(p);
            fixedPlayers++;
        }

        Debug.Log($"[碰撞区域] 已重建 {n} 块区域的碰撞体；给 {fixedPlayers} 个缺刚体的 Player 补上了 Rigidbody2D。" +
                  (fixedPlayers > 0 ? "（补完记得 Ctrl+S 保存场景）" : ""));
    }
}
#endif
