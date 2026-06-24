using UnityEngine;
using UnityEditor;

namespace DungeonShooter.Dungeon.Editor
{
    /// <summary>
    /// RoomTemplate 的自定义 Inspector 编辑器。
    /// 在 Inspector 面板中绘制可视化网格，点击格子即可切换 空白/地板。
    /// </summary>
    [CustomEditor(typeof(RoomTemplate))]
    public class RoomTemplateEditor : UnityEditor.Editor
    {
        private RoomTemplate template;
        private bool showGrid = true;
        private const int CellSize = 24;

        private void OnEnable()
        {
            template = (RoomTemplate)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ---- 基础信息 ----
            EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("templateName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("width"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("height"));
            if (EditorGUI.EndChangeCheck())
            {
                // 宽高变化 → 应用数值后调整 grid 数组大小
                serializedObject.ApplyModifiedProperties();
                template.EnsureGrid();
                EditorUtility.SetDirty(template);
            }

            EditorGUILayout.Space();

            // ---- 瓦片网格可视化编辑器 ----
            showGrid = EditorGUILayout.Foldout(showGrid, "瓦片网格编辑器", true);
            if (showGrid)
            {
                EditorGUI.indentLevel++;

                // 图例
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("图例：", GUILayout.Width(40));
                GUI.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
                GUILayout.Button("· = 空白", GUILayout.Width(90));
                GUI.backgroundColor = new Color(0.15f, 0.45f, 0.15f);
                GUILayout.Button("F = 地板", GUILayout.Width(90));
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                // 快捷按钮
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("全部填充地板", GUILayout.Width(100)))
                {
                    Undo.RecordObject(template, "Fill All Floor");
                    template.FillAllFloor();
                    EditorUtility.SetDirty(template);
                }
                if (GUILayout.Button("生成默认矩形", GUILayout.Width(100)))
                {
                    Undo.RecordObject(template, "Generate Default");
                    template.GenerateDefaultRectangle();
                    EditorUtility.SetDirty(template);
                }
                if (GUILayout.Button("全部清空", GUILayout.Width(100)))
                {
                    Undo.RecordObject(template, "Clear All");
                    template.FillGrid(0);
                    EditorUtility.SetDirty(template);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                // 网格
                DrawGrid();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // ---- 生成参数 ----
            EditorGUILayout.LabelField("生成参数", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("allowedTypes"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("weight"));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGrid()
        {
            template.EnsureGrid();
            int w = template.width;
            int h = template.height;

            // 请求一块固定大小的 GUI 区域
            Rect gridRect = GUILayoutUtility.GetRect(w * CellSize, h * CellSize);
            gridRect.width = w * CellSize;
            gridRect.height = h * CellSize;

            // 深色背景
            EditorGUI.DrawRect(gridRect, new Color(0.15f, 0.15f, 0.15f));

            // 遍历每个格子
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Y 轴翻转：让第 0 行在网格底部（更直观）
                    int cellValue = template.GetCell(x, h - 1 - y);
                    bool isFloor = cellValue == 1;

                    Rect cellRect = new Rect(
                        gridRect.x + x * CellSize,
                        gridRect.y + y * CellSize,
                        CellSize,
                        CellSize
                    );

                    // 背景颜色
                    Color bg = isFloor
                        ? new Color(0.15f, 0.45f, 0.15f)
                        : new Color(0.3f, 0.3f, 0.3f);
                    EditorGUI.DrawRect(cellRect, bg);

                    // 文字标签
                    string label = isFloor ? "F" : "·";
                    GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 11,
                        fontStyle = FontStyle.Bold,
                    };
                    labelStyle.normal.textColor = Color.white;
                    EditorGUI.LabelField(cellRect, label, labelStyle);

                    // 鼠标点击检测
                    Event evt = Event.current;
                    if (evt.type == EventType.MouseDown && cellRect.Contains(evt.mousePosition))
                    {
                        Undo.RecordObject(template, "Toggle Cell");
                        template.SetCell(x, h - 1 - y, isFloor ? 0 : 1);
                        EditorUtility.SetDirty(template);
                        evt.Use();    // 消费事件，防止其他控件响应
                        Repaint();    // 立即重绘面板
                    }
                }
            }
        }
    }
}
