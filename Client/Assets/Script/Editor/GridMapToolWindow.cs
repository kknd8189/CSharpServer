using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;

public class PlaneMapToolWindow : EditorWindow
{
    public int xMin = 0, xMax = 6, zMin = 0, zMax = 4;
    public int blockCount = 5;

    private Dictionary<Vector3Int, CellType> gridData = new Dictionary<Vector3Int, CellType>();
    private Dictionary<Vector3Int, GameObject> cellObjects = new Dictionary<Vector3Int, GameObject>();

    private Grid grid;

    [MenuItem("MapTool/Open Plane Map Tool")]
    public static void ShowWindow()
    {
        GetWindow<PlaneMapToolWindow>("MapTool");
    }

    private void OnGUI()
    {
        GUILayout.Label("Plane Map Tool Settings", EditorStyles.boldLabel);

        xMin = EditorGUILayout.IntField("X 최소값", xMin);
        xMax = EditorGUILayout.IntField("X 최대값", xMax);
        zMin = EditorGUILayout.IntField("Z 최소값", zMin);
        zMax = EditorGUILayout.IntField("Z 최대값", zMax);
        blockCount = EditorGUILayout.IntField("Block Count", blockCount);

        if (GUILayout.Button("Generate Grid")) GenerateGrid();

        if (GUILayout.Button("Save Map")) SaveMap();

        if (GUILayout.Button("Load Map")) LoadMap();
    }

    // ---------------- 저장 ----------------
    private void SaveMap()
    {
        string path = EditorUtility.SaveFilePanel("Save Map", "", "map.txt", "txt");
        if (string.IsNullOrEmpty(path)) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"{xMax}");
        sb.AppendLine($"{xMin}");
        sb.AppendLine($"{zMax}");
        sb.AppendLine($"{zMin}");
        sb.AppendLine("0");

        for (int z = zMax; z >= zMin; z--)
        {
            string line = "";
            for (int x = xMin; x <= xMax; x++)
            {
                Vector3Int pos = new Vector3Int(x, 0, z);
                line += (gridData.ContainsKey(pos) && gridData[pos] == CellType.Block) ? "1" : "0";
            }
            sb.AppendLine(line);
        }

        File.WriteAllText(path, sb.ToString());
        Debug.Log($"맵 저장 완료: {path}");
    }

    // ---------------- 불러오기 ----------------
    private void LoadMap()
    {
        string path = EditorUtility.OpenFilePanel("Load Map", "", "txt");
        if (string.IsNullOrEmpty(path)) return;

        string[] lines = File.ReadAllLines(path);
        xMax = int.Parse(lines[0]);
        xMin = int.Parse(lines[1]);
        zMax = int.Parse(lines[2]);
        zMin = int.Parse(lines[3]);

        gridData.Clear();
        int width = xMax - xMin + 1;
        int height = zMax - zMin + 1;

        for (int row = 0; row < height; row++)
        {
            string line = lines[5 + row];
            int z = zMax - row;
            for (int col = 0; col < width; col++)
            {
                int x = xMin + col;
                CellType type = (line[col] == '1') ? CellType.Block : CellType.Empty;
                gridData[new Vector3Int(x, 0, z)] = type;
            }
        }

        // 씬에 다시 반영
        CreateSceneObjects();
        Debug.Log($"맵 불러오기 완료: {path}");
    }

    // ---------------- 씬에 생성 ----------------
    private void GenerateGrid()
    {
        // 기존 Grid 삭제
        GameObject oldRoot = GameObject.Find("GeneratedGrid");
        if (oldRoot != null) DestroyImmediate(oldRoot);

        // 새 Grid 생성
        GameObject root = new GameObject("GeneratedGrid");
        grid = root.AddComponent<Grid>();
        grid.cellSize = new Vector3(1, 0, 1);

        gridData.Clear();
        cellObjects.Clear();

        // 모든 셀 Empty 초기화
        for (int x = xMin; x <= xMax; x++)
        {
            for (int z = zMin; z <= zMax; z++)
            {
                Vector3Int cellPos = new Vector3Int(x, 0, z);
                gridData[cellPos] = CellType.Empty;

                // Quad 생성
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.SetParent(root.transform);
                quad.transform.position = grid.CellToWorld(cellPos);
                quad.transform.rotation = Quaternion.Euler(90, 0, 0); // 위로 눕히기

                var renderer = quad.GetComponent<Renderer>();
                renderer.material.color = Color.green;

                cellObjects[cellPos] = quad;
            }
        }

        // 랜덤 Block 배치
        List<Vector3Int> allCells = new List<Vector3Int>(gridData.Keys);
        for (int i = 0; i < blockCount && allCells.Count > 0; i++)
        {
            int idx = Random.Range(0, allCells.Count);
            Vector3Int pos = allCells[idx];
            SetCellType(pos, CellType.Block);
            allCells.RemoveAt(idx);
        }
    }
    private void SetCellType(Vector3Int pos, CellType type)
    {
        if (!gridData.ContainsKey(pos)) return;

        gridData[pos] = type;
        var renderer = cellObjects[pos].GetComponent<Renderer>();
        renderer.material.color = (type == CellType.Block) ? Color.red : Color.green;
    }
    private void CreateSceneObjects()
    {
        GameObject oldRoot = GameObject.Find("GeneratedGrid");
        if (oldRoot != null) DestroyImmediate(oldRoot);

        GameObject root = new GameObject("GeneratedGrid");
        grid = root.AddComponent<Grid>();
        grid.cellSize = new Vector3(1, 0, 1);

        cellObjects.Clear();

        foreach (var kvp in gridData)
        {
            Vector3Int pos = kvp.Key;
            CellType type = kvp.Value;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(root.transform);
            quad.transform.position = grid.CellToWorld(pos);
            quad.transform.rotation = Quaternion.Euler(90, 0, 0);

            var renderer = quad.GetComponent<Renderer>();
            renderer.material.color = (type == CellType.Block) ? Color.red : Color.green;

            cellObjects[pos] = quad;
        }
    }

}