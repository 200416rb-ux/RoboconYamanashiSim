using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Save durable source assets; regenerate transient meshes and TerrainData after loading.
[InitializeOnLoad]
public static class MovableGolfHolePersistence
{
    static MovableGolfHolePersistence()
    {
        EditorSceneManager.sceneSaving += BeforeSave;
        EditorSceneManager.sceneSaved += AfterSave;
    }
    static void BeforeSave(Scene scene, string path)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var hole in root.GetComponentsInChildren<MovableGolfHole>(true))
                if (hole.enabled) { hole.enabled = false; hole.enabled = true; }
    }
    static void AfterSave(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var hole in root.GetComponentsInChildren<MovableGolfHole>(true))
                if (hole.enabled) hole.Rebuild();
    }
}
