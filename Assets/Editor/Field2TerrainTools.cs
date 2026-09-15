using UnityEditor;
using UnityEngine;

public static class Field2TerrainTools
{
    [MenuItem("Tools/Field2/Cut L Shape")]
    static void CutLShape()
    {
        var selected = Selection.activeGameObject;
        var terrain = selected != null
            ? selected.GetComponent<Terrain>() : null;

        if (terrain == null ||
            selected.scene.name != "Field2_Rebuild" ||
            System.IO.Path.GetFileNameWithoutExtension(
                AssetDatabase.GetAssetPath(terrain.terrainData)) != "Field2_Terrain")
        {
            Debug.LogError(
                "Field2_RebuildのTerrainSurfaceを選択してください。" +
                "Terrain DataはField2_Terrainにしてください。");
            return;
        }

        var data = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(data, "Cut Field2 L Shape");

        int n = data.holesResolution;
        var holes = new bool[n, n];

        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                holes[z, x] = x >= n / 2 || z < n / 2;

        data.SetHoles(0, 0, holes);
        EditorUtility.SetDirty(data);
        Debug.Log("Field2の左上を切り抜きました。");
    }
}