using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class UrdfFbxConverter
{
    private const string SourceAsset = "Assets/Robots/IndependentSteerRobot/source/independent_steer_robot.fbx";
    private const string OutputAssetDirectory = "Assets/Robots/IndependentSteerRobot";

    public static void ConvertFromCommandLine()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceAsset);
            if (prefab == null)
                throw new FileNotFoundException("Unity could not import the FBX asset.", SourceAsset);

            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "independent_steer_robot_source";

            try
            {
                Export(instance);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("URDF conversion completed successfully.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Export(GameObject root)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string outputDirectory = Path.Combine(projectRoot, OutputAssetDirectory);
        string meshDirectory = Path.Combine(outputDirectory, "meshes");
        Directory.CreateDirectory(meshDirectory);

        var parts = new List<Part>();
        var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (var meshFilter in meshFilters)
        {
            var renderer = meshFilter.GetComponent<MeshRenderer>();
            if (meshFilter.sharedMesh == null || renderer == null)
                continue;
            parts.Add(CreatePart(root.transform, meshFilter.transform, renderer, meshFilter.sharedMesh, null, parts.Count));
        }

        var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in skinnedRenderers)
        {
            var bakedMesh = new Mesh();
            renderer.BakeMesh(bakedMesh);
            if (bakedMesh.vertexCount == 0)
            {
                UnityEngine.Object.DestroyImmediate(bakedMesh);
                continue;
            }
            parts.Add(CreatePart(root.transform, renderer.transform, renderer, bakedMesh, bakedMesh, parts.Count));
        }

        if (parts.Count == 0)
            throw new InvalidOperationException("The FBX contains no renderable meshes.");

        foreach (var part in parts)
        {
            WriteObj(Path.Combine(meshDirectory, part.MeshFile), part.Vertices, part.Triangles);
            if (part.OwnedMesh != null)
                UnityEngine.Object.DestroyImmediate(part.OwnedMesh);
        }

        File.WriteAllText(
            Path.Combine(outputDirectory, "independent_steer_robot.urdf"),
            BuildUrdf(parts),
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(outputDirectory, "conversion_report.txt"),
            BuildReport(root, parts),
            new UTF8Encoding(false));
    }

    private static Part CreatePart(
        Transform root,
        Transform transform,
        Renderer renderer,
        Mesh mesh,
        Mesh ownedMesh,
        int index)
    {
        var vertices = mesh.vertices.Select(vertex => UnityToRos(transform.TransformPoint(vertex))).ToArray();
        var triangles = mesh.triangles.ToArray();
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            // Unity and ROS use opposite handedness, so preserve face orientation.
            (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }

        string safeName = SanitizeName(transform.name);
        string linkName = $"part_{index:D3}_{safeName}";
        Color color = ReadColor(renderer);

        return new Part
        {
            LinkName = linkName,
            JointName = $"base_to_{linkName}",
            MeshFile = $"part_{index:D3}.obj",
            SourcePath = GetPath(root, transform),
            Vertices = vertices,
            Triangles = triangles,
            Color = color,
            Bounds = renderer.bounds,
            OwnedMesh = ownedMesh
        };
    }

    private static Vector3 UnityToRos(Vector3 unity)
    {
        // Unity: left-handed X-right/Y-up/Z-forward. ROS: right-handed X-forward/Y-left/Z-up.
        return new Vector3(unity.z, -unity.x, unity.y);
    }

    private static Color ReadColor(Renderer renderer)
    {
        var material = renderer.sharedMaterials.FirstOrDefault(item => item != null);
        if (material == null)
            return new Color(0.7f, 0.7f, 0.7f, 1f);
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");
        return new Color(0.7f, 0.7f, 0.7f, 1f);
    }

    private static void WriteObj(string path, IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles)
    {
        var text = new StringBuilder(vertices.Count * 48 + triangles.Count * 8);
        text.AppendLine("# Generated from the supplied FBX for URDF use; units are metres.");
        text.AppendLine("o mesh");
        foreach (var vertex in vertices)
            text.AppendFormat(CultureInfo.InvariantCulture, "v {0:R} {1:R} {2:R}\n", vertex.x, vertex.y, vertex.z);
        for (int i = 0; i + 2 < triangles.Count; i += 3)
            text.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\n", triangles[i] + 1, triangles[i + 1] + 1, triangles[i + 2] + 1);
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static string BuildUrdf(IReadOnlyList<Part> parts)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\"?>");
        xml.AppendLine("<!-- Generated from the supplied FBX. Each visible component is retained as a fixed link. -->");
        xml.AppendLine("<robot name=\"independent_steer_robot\">");
        xml.AppendLine("  <link name=\"base_link\">");
        xml.AppendLine("    <inertial>");
        xml.AppendLine("      <mass value=\"1.0\"/>");
        xml.AppendLine("      <inertia ixx=\"0.001\" ixy=\"0\" ixz=\"0\" iyy=\"0.001\" iyz=\"0\" izz=\"0.001\"/>");
        xml.AppendLine("    </inertial>");
        xml.AppendLine("  </link>");

        foreach (var part in parts)
        {
            string rgba = string.Format(CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R} {3:R}", part.Color.r, part.Color.g, part.Color.b, part.Color.a);
            xml.AppendLine($"  <link name=\"{part.LinkName}\">");
            xml.AppendLine("    <visual>");
            xml.AppendLine("      <origin xyz=\"0 0 0\" rpy=\"0 0 0\"/>");
            xml.AppendLine($"      <geometry><mesh filename=\"meshes/{part.MeshFile}\" scale=\"1 1 1\"/></geometry>");
            xml.AppendLine($"      <material name=\"material_{part.LinkName}\"><color rgba=\"{rgba}\"/></material>");
            xml.AppendLine("    </visual>");
            xml.AppendLine("    <collision>");
            xml.AppendLine("      <origin xyz=\"0 0 0\" rpy=\"0 0 0\"/>");
            xml.AppendLine($"      <geometry><mesh filename=\"meshes/{part.MeshFile}\" scale=\"1 1 1\"/></geometry>");
            xml.AppendLine("    </collision>");
            xml.AppendLine("  </link>");
            xml.AppendLine($"  <joint name=\"{part.JointName}\" type=\"fixed\">");
            xml.AppendLine("    <parent link=\"base_link\"/>");
            xml.AppendLine($"    <child link=\"{part.LinkName}\"/>");
            xml.AppendLine("    <origin xyz=\"0 0 0\" rpy=\"0 0 0\"/>");
            xml.AppendLine("  </joint>");
        }

        xml.AppendLine("</robot>");
        return xml.ToString();
    }

    private static string BuildReport(GameObject root, IReadOnlyList<Part> parts)
    {
        Bounds combined = parts[0].Bounds;
        foreach (var part in parts.Skip(1))
            combined.Encapsulate(part.Bounds);

        Vector3 rosSize = new Vector3(combined.size.z, combined.size.x, combined.size.y);
        var report = new StringBuilder();
        report.AppendLine("FBX to URDF conversion report");
        report.AppendLine($"Source asset: {SourceAsset}");
        report.AppendLine($"Renderable parts: {parts.Count}");
        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Approximate ROS bounding size (x y z metres): {0:R} {1:R} {2:R}", rosSize.x, rosSize.y, rosSize.z));
        report.AppendLine("Coordinate conversion: Unity (x,y,z) -> ROS (z,-x,y)");
        report.AppendLine("All FBX transforms are baked into each OBJ mesh. URDF joints are fixed at the origin to preserve the supplied placement.");
        report.AppendLine("Mass/inertia are placeholders and should be replaced with measured or CAD-derived values.");
        report.AppendLine();
        foreach (var part in parts)
            report.AppendLine($"{part.LinkName}\t{part.MeshFile}\t{part.Vertices.Length} vertices\t{part.Triangles.Length / 3} triangles\t{part.SourcePath}");
        return report.ToString();
    }

    private static string GetPath(Transform root, Transform target)
    {
        var names = new Stack<string>();
        Transform current = target;
        while (current != null)
        {
            names.Push(current.name);
            if (current == root)
                break;
            current = current.parent;
        }
        return string.Join("/", names);
    }

    private static string SanitizeName(string value)
    {
        string normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9_]+", "_").Trim('_');
        return string.IsNullOrEmpty(normalized) ? "mesh" : normalized;
    }

    private sealed class Part
    {
        public string LinkName;
        public string JointName;
        public string MeshFile;
        public string SourcePath;
        public Vector3[] Vertices;
        public int[] Triangles;
        public Color Color;
        public Bounds Bounds;
        public Mesh OwnedMesh;
    }
}
