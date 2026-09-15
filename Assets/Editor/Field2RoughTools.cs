using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Field2RoughTools
{
    [MenuItem("Tools/Field2/Round Rough Corner")]
    static void RoundCorner()
    {
        var selected = Selection.activeGameObject;
        if (selected == null ||
            selected.scene.name != "Field2_Rebuild" ||
            selected.name != "RoughAreas")
        {
            Debug.LogError("Field2_RebuildのRoughAreasを選択してください。");
            return;
        }

        var horizontal = selected.transform.Find("Rough_InnerHorizontal");
        var vertical = selected.transform.Find("Rough_InnerVertical");
        if (horizontal == null || vertical == null)
        {
            Debug.LogError("内側の横ラフと縦ラフが見つかりません。");
            return;
        }

        const int segments = 16;
        const int count = segments + 2;
        const float radius = 0.2f;
        var vertices = new Vector3[count * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>();

        vertices[0] = new Vector3(0, 0.005f, 0);
        vertices[count] = new Vector3(0, -0.005f, 0);

        for (int i = 0; i <= segments; i++)
        {
            float angle = -Mathf.PI / 2 + Mathf.PI / 2 * i / segments;
            float x = radius * Mathf.Cos(angle);
            float z = radius * Mathf.Sin(angle);
            int a = i + 1;

            vertices[a] = new Vector3(x, 0.005f, z);
            vertices[a + count] = new Vector3(x, -0.005f, z);

            if (i < segments)
                triangles.AddRange(new[] {
                    0, a + 1, a,
                    count, a + count, a + count + 1
                });
        }

        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            triangles.AddRange(new[] {
                i, j, i + count,
                j, j + count, i + count
            });
        }

        for (int i = 0; i < vertices.Length; i++)
            uv[i] = new Vector2(vertices[i].x, vertices[i].z) / radius;

        var mesh = new Mesh {
            name = "Field2 Rounded Rough Corner",
            vertices = vertices,
            uv = uv,
            triangles = triangles.ToArray()
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        const string folder = "Assets/GeneratedGolfFields";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "GeneratedGolfFields");

        const string path = folder + "/Field2RoughCorner.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            saved = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, saved);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(saved);
        }

        Undo.RecordObject(horizontal, "Adjust horizontal rough");
        horizontal.localPosition = new Vector3(-0.9f, 0.305f, -0.1f);
        horizontal.localScale = new Vector3(1.8f, 0.01f, 0.2f);

        Undo.RecordObject(vertical, "Adjust vertical rough");
        vertical.localPosition = new Vector3(0.1f, 0.305f, 0.45f);
        vertical.localScale = new Vector3(0.2f, 0.01f, 0.9f);

        var corner = selected.transform.Find("Rough_InnerCorner");
        GameObject go;
        if (corner == null)
        {
            go = new GameObject("Rough_InnerCorner");
            Undo.RegisterCreatedObjectUndo(go, "Create rounded rough");
            go.transform.SetParent(selected.transform, false);
        }
        else go = corner.gameObject;

        Undo.RecordObject(go.transform, "Position rounded rough");
        go.transform.localPosition = new Vector3(0, 0.305f, 0);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = Undo.AddComponent<MeshFilter>(go);
        filter.sharedMesh = saved;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<MeshRenderer>(go);
        renderer.sharedMaterial = horizontal.GetComponent<MeshRenderer>().sharedMaterial;

        var collider = go.GetComponent<MeshCollider>();
        if (collider == null) collider = Undo.AddComponent<MeshCollider>(go);
        collider.sharedMesh = null;
        collider.sharedMesh = saved;

        var sourceCollider = horizontal.GetComponent<Collider>();
        if (sourceCollider != null)
            collider.sharedMaterial = sourceCollider.sharedMaterial;

        EditorSceneManager.MarkSceneDirty(selected.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("L字のラフを半径200mmで丸く接続しました。");
    }
}