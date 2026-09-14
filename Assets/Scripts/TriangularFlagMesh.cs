using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class TriangularFlagMesh : MonoBehaviour
{
    [SerializeField] private float verticalBase = 0.100f;
    [SerializeField] private float horizontalHeight = 0.200f;
    [SerializeField] private float thickness = 0.002f;

    private const string MeshName = "Generated_ABS_TriangularFlag";

    private void OnEnable()
    {
        RebuildMesh();
    }

    private void OnValidate()
    {
        RebuildMesh();
    }

    private void RebuildMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshCollider meshCollider = GetComponent<MeshCollider>();

        Mesh mesh = meshFilter.sharedMesh;

        if (mesh == null || mesh.name != MeshName)
        {
            mesh = new Mesh();
            mesh.name = MeshName;
            meshFilter.sharedMesh = mesh;
        }

        mesh.Clear();

        float halfThickness = Mathf.Max(0.0001f, thickness * 0.5f);

        Vector3[] vertices =
        {
            new Vector3(0f, 0f, -halfThickness),
            new Vector3(0f, -verticalBase, -halfThickness),
            new Vector3(horizontalHeight, -verticalBase * 0.5f, -halfThickness),

            new Vector3(0f, 0f, halfThickness),
            new Vector3(0f, -verticalBase, halfThickness),
            new Vector3(horizontalHeight, -verticalBase * 0.5f, halfThickness)
        };

        int[] triangles =
        {
            0, 2, 1,
            3, 4, 5,

            0, 1, 4,
            0, 4, 3,

            0, 3, 5,
            0, 5, 2,

            1, 2, 5,
            1, 5, 4
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }
}