var results = new System.Collections.Generic.List<string>();
foreach (var path in new [] { "Assets/Scenes/Field1.unity", "Assets/Scenes/Field2.unity" })
{
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
    if (!scene.isLoaded) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
    var root = scene.GetRootGameObjects().First(g => g.name == "FieldRoot");
    var hole = root.GetComponentInChildren<MovableGolfHole>();
    hole.SendMessage("Update");
    var initial = hole.positionXZ;
    var moved = initial + new UnityEngine.Vector2(0,.2f);
    if (!hole.ContainsCup(moved)) throw new System.Exception(path + ": destination invalid");
    hole.positionXZ = moved; hole.SendMessage("Update");
    UnityEngine.Physics.SyncTransforms();
    var collider = hole.green.GetComponent<UnityEngine.MeshCollider>();
    UnityEngine.RaycastHit hit;
    bool oldFilled = collider.Raycast(new UnityEngine.Ray(new UnityEngine.Vector3(initial.x+.03f,1,initial.y),UnityEngine.Vector3.down),out hit,2);
    bool newOpen = !collider.Raycast(new UnityEngine.Ray(new UnityEngine.Vector3(moved.x+.03f,1,moved.y),UnityEngine.Vector3.down),out hit,2);
    var terrainCollider=hole.terrain.GetComponent<UnityEngine.TerrainCollider>();
    bool terrainOpen=!terrainCollider.Raycast(new UnityEngine.Ray(new UnityEngine.Vector3(moved.x+.03f,1,moved.y),UnityEngine.Vector3.down),out hit,2);
    bool terrainRestored=terrainCollider.Raycast(new UnityEngine.Ray(new UnityEngine.Vector3(initial.x+.03f,1,initial.y),UnityEngine.Vector3.down),out hit,2);
    if(!oldFilled||!newOpen||!terrainOpen||!terrainRestored) throw new System.Exception(path+": movement collision failed "+oldFilled+","+newOpen+","+terrainOpen+","+terrainRestored);
    hole.positionXZ = new UnityEngine.Vector2(-99,-99); hole.SendMessage("Update");
    if(hole.positionXZ!=moved)throw new System.Exception("Invalid position not rejected");
    hole.transform.position=new UnityEngine.Vector3(initial.x,hole.surfaceY,initial.y);hole.SendMessage("Update");
    if(hole.positionXZ!=initial)throw new System.Exception("Transform movement not applied");
    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
    scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path,UnityEditor.SceneManagement.OpenSceneMode.Additive);
    root=scene.GetRootGameObjects().First(g=>g.name=="FieldRoot");hole=root.GetComponentInChildren<MovableGolfHole>();hole.SendMessage("Update");
    if(hole.green.sharedMesh==null||hole.terrain.terrainData==null||hole.positionXZ!=initial)throw new System.Exception("Reload failed");
    results.Add(path+": movement, old/new terrain holes, invalid position, transform drag, save/reload PASS");
    if(path.Contains("Field1"))UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
    else UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
}
return results;

