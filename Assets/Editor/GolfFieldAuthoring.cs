using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GolfFieldAuthoring
{
    const string Folder = "Assets/GeneratedGolfFields";
    public static string Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "GeneratedGolfFields");
        var first = SceneManager.GetSceneByPath("Assets/Scenes/Field1.unity");
        if (!first.isLoaded) first = EditorSceneManager.OpenScene("Assets/Scenes/Field1.unity", OpenSceneMode.Additive);
        var root = first.GetRootGameObjects().First(g => g.name == "FieldRoot");
        var green = root.transform.Find("GreenArea").Cast<Transform>().First(t => t.name == "GreenSurface_WithHole" && t.gameObject.activeSelf).GetComponent<MeshFilter>();
        var source = root.transform.Find("GreenArea/Greem_Outline_Backup").GetComponent<MeshFilter>();
        var mesh = UnityEngine.Object.Instantiate(source.sharedMesh);
        mesh.name = "Field1 solid green";
        mesh.vertices = mesh.vertices.Select(v => green.transform.InverseTransformPoint(source.transform.TransformPoint(v))).ToArray();
        mesh.RecalculateBounds();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Field1SolidGreen.asset");
        if (existing == null) AssetDatabase.CreateAsset(mesh, Folder + "/Field1SolidGreen.asset");
        else { UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
        var hole = root.transform.Find("HoleRoot");
        var movable = hole.GetComponent<MovableGolfHole>();
        if (movable == null) movable = Undo.AddComponent<MovableGolfHole>(hole.gameObject);
        Configure(movable, mesh, green, root.GetComponentInChildren<Terrain>(), hole.position);
        EditorSceneManager.MarkSceneDirty(first);
        EditorSceneManager.SaveScene(first);

        var second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(second);
        var r2 = new GameObject("FieldRoot");
        var rough = Material("Rough", new Color(.025f,.31f,.025f));
        var fairway = Material("Fairway", new Color(0,.62f,.24f));
        var lightGreen = Material("Green", new Color(.57f,.76f,.40f));
        var start = Material("Start", new Color(1,.9f,.62f));
        // Coordinates follow the supplied drawing: x right, z up; all dimensions in metres.
        Surface("Rough", r2.transform, new [] {new Vector2(0,0),new Vector2(3.6f,0),new Vector2(3.6f,1.8f),new Vector2(1.8f,1.8f),new Vector2(1.8f,.9f),new Vector2(0,.9f)}, .30f, rough);
        Surface("Fairway", r2.transform, Rounded(new [] {new Vector2(0,.15f),new Vector2(3.45f,.15f),new Vector2(3.45f,1.65f),new Vector2(1.95f,1.65f),new Vector2(1.95f,.75f),new Vector2(0,.75f)}, .12f), .302f, fairway);
        var g2 = Surface("GreenSurface_WithHole", r2.transform, Rounded(new [] {new Vector2(2.1f,.275f),new Vector2(3.3f,.275f),new Vector2(3.3f,1.525f),new Vector2(2.1f,1.525f)}, .13f), .32f, lightGreen);
        g2.gameObject.AddComponent<MeshCollider>().sharedMesh = g2.sharedMesh;
        var st = Surface("StartArea", r2.transform, new [] {new Vector2(-.5f,.2f),new Vector2(0,.2f),new Vector2(0,.7f),new Vector2(-.5f,.7f)}, .31f, start);
        st.gameObject.AddComponent<MeshCollider>().sharedMesh = st.sharedMesh;
        Surface("TeeGround", r2.transform, new [] {new Vector2(0,.2f),new Vector2(.5f,.2f),new Vector2(.5f,.7f),new Vector2(0,.7f)}, .304f, lightGreen);
        var data = new TerrainData { heightmapResolution = 257, size = new Vector3(3.6f,.4f,1.8f) };
        var heights = new float[257,257]; for(int z=0;z<257;z++) for(int x=0;x<257;x++) heights[z,x]=.75f;
        data.SetHeights(0,0,heights);
        int n=data.holesResolution; var mask=new bool[n,n];
        for(int z=0;z<n;z++) for(int x=0;x<n;x++) mask[z,x]=x>=n/2 || z<n/2;
        data.SetHoles(0,0,mask);
        AssetDatabase.CreateAsset(data, Folder+"/Field2Terrain.asset");
        var terrainObject=Terrain.CreateTerrainGameObject(data); terrainObject.name="TerrainSurface";terrainObject.transform.SetParent(r2.transform);
        var terrain=terrainObject.GetComponent<Terrain>(); terrain.drawHeightmap=false;
        var h2=UnityEngine.Object.Instantiate(hole.gameObject,r2.transform);h2.name="HoleRoot";
        var m2=h2.GetComponent<MovableGolfHole>();
        Configure(m2,g2.sharedMesh,g2,terrain,new Vector3(2.7f,.30f,.9f));
        m2.underlayFilters = new [] { r2.transform.Find("Rough").GetComponent<MeshFilter>(), r2.transform.Find("Fairway").GetComponent<MeshFilter>() };
        m2.underlaySources = m2.underlayFilters.Select(f => f.sharedMesh).ToArray();
        m2.Rebuild();
        var balls=UnityEngine.Object.Instantiate(root.transform.Find("SimulationObjects").gameObject,r2.transform);
        balls.name="SimulationObjects";
        foreach(Transform ball in balls.transform) ball.position=new Vector3(.25f,.335f,.45f);
        foreach(var item in first.GetRootGameObjects().Where(g=>g.name=="perorin" || g.name=="turtlebot3_burger"))
        {
            var robot=UnityEngine.Object.Instantiate(item);robot.name=item.name;
            robot.transform.position += new Vector3(1.8f,0,.45f);
        }
        var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";
        var camera=cameraObject.AddComponent<Camera>();cameraObject.AddComponent<AudioListener>();
        camera.transform.position=new Vector3(1.55f,5,.9f);camera.transform.rotation=Quaternion.Euler(90,0,0);
        camera.orthographic=true;camera.orthographicSize=1.6f;camera.nearClipPlane=.01f;camera.farClipPlane=30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);
        var lamp=new GameObject("Directional Light").AddComponent<Light>();lamp.type=LightType.Directional;lamp.intensity=1.2f;lamp.transform.rotation=Quaternion.Euler(55,-30,0);
        RenderSettings.ambientLight=Color.gray;
        EditorSceneManager.SaveScene(second,"Assets/Scenes/Field2.unity");
        AssetDatabase.SaveAssets();
        EditorSceneManager.CloseScene(first,true);
        Selection.activeGameObject=h2;
        return "Field1 movable cup configured; Field2 created.";
    }
    static void Configure(MovableGolfHole h,Mesh mesh,MeshFilter green,Terrain terrain,Vector3 position)
    {
        h.enabled=false;h.solidGreen=mesh;h.green=green;h.terrain=terrain;h.originalTerrain=terrain.terrainData;
        h.originalHole=new Vector2(position.x,position.z);h.positionXZ=h.originalHole;h.surfaceY=position.y;h.transform.position=position;
        h.enabled=true;h.Rebuild();
    }
    static Material Material(string name,Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m=new Material(shader);m.name=name;m.color=color;
        AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;
    }
    static Vector2[] Rounded(Vector2[] points,float radius)
    {
        var result=new List<Vector2>();
        for(int i=0;i<points.Length;i++)
        {
            var p=points[i];var a=p+(points[(i+points.Length-1)%points.Length]-p).normalized*radius;
            var b=p+(points[(i+1)%points.Length]-p).normalized*radius;
            for(int k=0;k<=8;k++) {float t=k/8f;result.Add((1-t)*(1-t)*a+2*(1-t)*t*p+t*t*b);}
        }
        return result.ToArray();
    }
    static MeshFilter Surface(string name,Transform parent,Vector2[] polygon,float y,Material material)
    {
        var mesh=new Mesh { name=name };
        mesh.vertices=polygon.Select(p=>new Vector3(p.x,y,p.y)).ToArray();
        var remaining=Enumerable.Range(0,polygon.Length).ToList();var triangles=new List<int>();
        int guard=0;
        while(remaining.Count>2 && guard++<10000)
        {
            bool cut=false;
            for(int i=0;i<remaining.Count;i++)
            {
                int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                if(Cross(polygon[b]-polygon[a],polygon[c]-polygon[b])<=1e-8f) continue;
                bool contains=remaining.Any(j=>j!=a&&j!=b&&j!=c&&Inside(polygon[j],polygon[a],polygon[b],polygon[c]));
                if(contains)continue;
                triangles.Add(a);triangles.Add(c);triangles.Add(b);remaining.RemoveAt(i);cut=true;break;
            }
            if(!cut)throw new InvalidOperationException("Cannot triangulate "+name);
        }
        mesh.triangles=triangles.ToArray();mesh.uv=polygon;mesh.RecalculateNormals();mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,Folder+"/Field2_"+name+".asset");
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshRenderer>().sharedMaterial=material;
        var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;return filter;
    }
    static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    static bool Inside(Vector2 p,Vector2 a,Vector2 b,Vector2 c)=>Cross(b-a,p-a)>=-1e-8f&&Cross(c-b,p-b)>=-1e-8f&&Cross(a-c,p-c)>=-1e-8f;
}
