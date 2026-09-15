using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves the cup, cuts the green mesh, and opens the terrain below it.</summary>
[ExecuteAlways]
public class MovableGolfHole : MonoBehaviour
{
    [Tooltip("UnityのワールドX/Z座標（m）。SceneビューでHoleRootを移動しても更新されます。")]
    public Vector2 positionXZ;
    [HideInInspector] public Mesh solidGreen;
    [HideInInspector] public MeshFilter green;
    [HideInInspector] public Terrain terrain;
    public TerrainData originalTerrain;
    [HideInInspector] public Vector2 originalHole;
    [HideInInspector] public float surfaceY;
    const float Radius = 0.065f;
    [HideInInspector] public MeshFilter[] underlayFilters = new MeshFilter[0];
    [HideInInspector] public Mesh[] underlaySources = new Mesh[0];
    Mesh[] underlayMeshes;
    Mesh generated;
    TerrainData terrainCopy;
    Vector2 applied;
    bool initialized;

    void OnEnable() { initialized = false; }
    void Update()
    {
        if (solidGreen == null || green == null) return;
        var moved = new Vector2(transform.position.x, transform.position.z);
        var requested = !initialized || positionXZ != applied ? positionXZ : moved;
        if (initialized && requested == applied && Mathf.Abs(transform.position.y - surfaceY) < .00001f) return;
        if (!ContainsCup(requested)) requested = initialized ? applied : originalHole;
        positionXZ = requested;
        transform.position = new Vector3(requested.x, surfaceY, requested.y);
        Rebuild();
        applied = requested;
        initialized = true;
    }

    public bool ContainsCup(Vector2 point)
    {
        if (solidGreen == null || green == null) return false;
        var vertices = solidGreen.vertices;
        var triangles = solidGreen.triangles;
        for (int sample = 0; sample < 33; sample++)
        {
            float angle = sample * Mathf.PI * 2 / 32;
            var p = point + (sample == 32 ? Vector2.zero : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Radius + .008f));
            bool inside = false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = green.transform.TransformPoint(vertices[triangles[i]]);
                var b = green.transform.TransformPoint(vertices[triangles[i + 1]]);
                var c = green.transform.TransformPoint(vertices[triangles[i + 2]]);
                var aa = new Vector2(a.x, a.z); var bb = new Vector2(b.x, b.z); var cc = new Vector2(c.x, c.z);
                float area = Cross(bb - aa, cc - aa);
                if (Mathf.Abs(area) < 1e-9f) continue;
                float u = Cross(bb - p, cc - p) / area;
                float v = Cross(cc - p, aa - p) / area;
                if (u >= -.00001f && v >= -.00001f && u + v <= 1.00001f) { inside = true; break; }
            }
            if (!inside) return false;
        }
        return true;
    }
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    public void Rebuild()
    {
        BuildOne(solidGreen, green, ref generated);
        if (underlayMeshes == null || underlayMeshes.Length != underlayFilters.Length) underlayMeshes = new Mesh[underlayFilters.Length];
        for (int i = 0; i < underlayFilters.Length; i++) BuildOne(underlaySources[i], underlayFilters[i], ref underlayMeshes[i]);
        UpdateTerrain();
    }
    void BuildOne(Mesh solidGreen, MeshFilter green, ref Mesh generated)
    {
        if (solidGreen == null || green == null) return;
        if (generated == null) generated = new Mesh { name = "Movable cup green", hideFlags = HideFlags.DontSave };
        var vertices = new List<Vector3>(); var indices = new List<int>();
        var src = solidGreen.vertices; var tri = solidGreen.triangles;
        // Subtract a convex 64-sided cylinder from each original triangle.
        for (int i = 0; i < tri.Length; i += 3)
        {
            var remainder = new List<Vector3> { src[tri[i]], src[tri[i + 1]], src[tri[i + 2]] };
            for (int side = 0; side < 64 && remainder.Count >= 3; side++)
            {
                float angle = (side + .5f) * Mathf.PI * 2 / 64;
                var normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var outside = Clip(remainder, normal, false, green);
                for (int k = 1; k + 1 < outside.Count; k++)
                {
                    if (Vector3.Cross(outside[k] - outside[0], outside[k + 1] - outside[0]).sqrMagnitude < 1e-16f) continue;
                    int n = vertices.Count;
                    vertices.Add(outside[0]); vertices.Add(outside[k]); vertices.Add(outside[k + 1]);
                    indices.Add(n); indices.Add(n + 1); indices.Add(n + 2);
                }
                remainder = Clip(remainder, normal, true, green);
            }
        }
        generated.Clear(); generated.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        generated.SetVertices(vertices); generated.SetTriangles(indices, 0);
        var uv = new List<Vector2>(); foreach (var v in vertices) uv.Add(new Vector2(v.x, v.z));
        generated.SetUVs(0, uv); generated.RecalculateNormals(); generated.RecalculateBounds();
        green.sharedMesh = generated;
        var collider = green.GetComponent<MeshCollider>();
        if (collider != null) { collider.sharedMesh = null; collider.sharedMesh = generated; }
    }
    List<Vector3> Clip(List<Vector3> polygon, Vector2 normal, bool inside, MeshFilter green)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            var wa = green.transform.TransformPoint(a); var wb = green.transform.TransformPoint(b);
            float da = Vector2.Dot(new Vector2(wa.x, wa.z) - positionXZ, normal) - Radius * Mathf.Cos(Mathf.PI / 64);
            float db = Vector2.Dot(new Vector2(wb.x, wb.z) - positionXZ, normal) - Radius * Mathf.Cos(Mathf.PI / 64);
            bool keepA = inside ? da <= 0 : da >= 0; bool keepB = inside ? db <= 0 : db >= 0;
            if (keepA) result.Add(a);
            if (keepA != keepB) result.Add(Vector3.LerpUnclamped(a, b, da / (da - db)));
        }
        return result;
    }
    void UpdateTerrain()
    {
        if (terrain == null || originalTerrain == null) return;
        if (terrainCopy == null)
        {
            terrainCopy = Instantiate(originalTerrain); terrainCopy.hideFlags = HideFlags.DontSave;
            terrain.terrainData = terrainCopy;
            terrain.GetComponent<TerrainCollider>().terrainData = terrainCopy;
        }
        int n = terrainCopy.holesResolution;
        var holes = originalTerrain.GetHoles(0, 0, n, n);
        float dx = terrainCopy.size.x / n, dz = terrainCopy.size.z / n;
        float margin = Mathf.Sqrt(dx * dx + dz * dz);
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            var p = new Vector2(terrain.transform.position.x + (x + .5f) * dx, terrain.transform.position.z + (z + .5f) * dz);
            if (Vector2.Distance(p, originalHole) < Radius + margin) holes[z, x] = true;
            if (Vector2.Distance(p, positionXZ) < Radius + margin) holes[z, x] = false;
        }
        terrainCopy.SetHoles(0, 0, holes);
    }
    void OnDisable()
    {
        for (int i = 0; i < underlayFilters.Length; i++) if (underlayFilters[i] != null) underlayFilters[i].sharedMesh = underlaySources[i];
        if (underlayMeshes != null) foreach (var m in underlayMeshes) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        underlayMeshes = null;
        if (green != null && generated != null)
        {
            green.sharedMesh = solidGreen;
            var c = green.GetComponent<MeshCollider>(); if (c != null) c.sharedMesh = solidGreen;
        }
        if (terrain != null && originalTerrain != null)
        {
            terrain.terrainData = originalTerrain;
            terrain.GetComponent<TerrainCollider>().terrainData = originalTerrain;
        }
        if (generated != null) { if (Application.isPlaying) Destroy(generated); else DestroyImmediate(generated); }
        if (terrainCopy != null) { if (Application.isPlaying) Destroy(terrainCopy); else DestroyImmediate(terrainCopy); }
    }
}

