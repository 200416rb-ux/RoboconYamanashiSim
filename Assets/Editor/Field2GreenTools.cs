using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Field2GreenTools
{
    [MenuItem("Tools/Field2/Make Rounded Green")]
    static void MakeRoundedGreen()
    {
        var go = Selection.activeGameObject;
        if (go == null || go.scene.name != "Field2_Rebuild" ||
            go.name != "GreenSurface_WithHole")
        {
            Debug.LogError("Field2_Rebuildの有効なGreenSurface_WithHoleを選択してください。");
            return;
        }

        var filter = go.GetComponent<MeshFilter>();
        var hole = go.transform.root.GetComponentInChildren<MovableGolfHole>(true);
        if (filter == null || hole == null) return;

        const int n = 32;
        const float radius = 0.13f;
        var vertices = new Vector3[n * 2 + 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>();

        vertices[0] = new Vector3(0, 0.005f, 0);
        vertices[1] = new Vector3(0, -0.005f, 0);

        for (int i = 0; i < n; i++)
        {
            int corner = i / 8;
            float angle = (corner + (i % 8) / 8f) * Mathf.PI / 2;
            float cx = corner == 0 || corner == 3 ? 0.47f : -0.47f;
            float cz = corner < 2 ? 0.495f : -0.495f;
            float x = cx + radius * Mathf.Cos(angle);
            float z = cz + radius * Mathf.Sin(angle);

            vertices[2 + i] = new Vector3(x, 0.005f, z);
            vertices[2 + n + i] = new Vector3(x, -0.005f, z);

            int a = 2 + i, b = 2 + (i + 1) % n;
            int c = a + n, d = b + n;
            triangles.AddRange(new[] {
                0, b, a, 1, c, d,
                a, b, c, b, d, c
            });
        }

        for (int i = 0; i < vertices.Length; i++)
            uv[i] = new Vector2(vertices[i].x, vertices[i].z);

        var mesh = new Mesh {
            name = "Field2 Rounded Green",
            vertices = vertices,
            uv = uv,
            triangles = triangles.ToArray()
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        const string folder = "Assets/GeneratedGolfFields";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "GeneratedGolfFields");

        const string path = folder + "/Field2RoundedGreen.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);

        hole.enabled = false;

        if (saved == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            saved = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, saved);
            Object.DestroyImmediate(mesh);
        }

        go.transform.localScale = Vector3.one;
        go.transform.rotation = Quaternion.identity;
        go.transform.position = new Vector3(0.9f, 0.315f, 0);

        hole.solidGreen = saved;
        hole.green = filter;
        hole.enabled = true;
        hole.Rebuild();

        EditorUtility.SetDirty(saved);
        EditorUtility.SetDirty(hole);
        EditorSceneManager.MarkSceneDirty(go.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("グリーンを角丸の四角形に変更しました。");
    }
}