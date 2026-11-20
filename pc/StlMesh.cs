using System.Globalization;
using System.IO;

namespace ConnectPC;

/// <summary>Вершина STL-модели (координаты как в файле, обычно миллиметры).</summary>
public readonly record struct StlV(double X, double Y, double Z);

/// <summary>
/// Загрузчик STL: бинарный и ASCII форматы. Никаких внешних библиотек.
/// </summary>
public sealed class StlMesh
{
    public List<(StlV A, StlV B, StlV C)> Triangles { get; } = new();
    public double MinX { get; private set; }
    public double MinY { get; private set; }
    public double MinZ { get; private set; }
    public double MaxX { get; private set; }
    public double MaxY { get; private set; }
    public double MaxZ { get; private set; }
    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public double SizeZ => MaxZ - MinZ;

    public static StlMesh Load(string path)
    {
        var mesh = new StlMesh();
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) throw new InvalidDataException("Файл STL пустой.");

        bool binary = false;
        if (bytes.Length >= 84)
        {
            uint count = BitConverter.ToUInt32(bytes, 80);
            // точное совпадение размера — надёжный признак бинарного STL
            if (count > 0 && 84L + 50L * count == bytes.Length) binary = true;
        }

        if (binary)
        {
            uint count = BitConverter.ToUInt32(bytes, 80);
            int off = 84;
            for (uint i = 0; i < count; i++)
            {
                // нормаль пропускаем (12 байт), берём 3 вершины
                var a = ReadV(bytes, off + 12);
                var b = ReadV(bytes, off + 24);
                var c = ReadV(bytes, off + 36);
                mesh.Triangles.Add((a, b, c));
                off += 50;
            }
        }
        else
        {
            var verts = new List<StlV>();
            foreach (var line in File.ReadLines(path))
            {
                var t = line.Trim();
                if (!t.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var y) &&
                    double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var z))
                    verts.Add(new StlV(x, y, z));
            }
            for (int i = 0; i + 2 < verts.Count; i += 3)
                mesh.Triangles.Add((verts[i], verts[i + 1], verts[i + 2]));
        }

        if (mesh.Triangles.Count == 0)
            throw new InvalidDataException("В файле STL не найдено ни одного треугольника.");

        mesh.MinX = mesh.Triangles.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)));
        mesh.MaxX = mesh.Triangles.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
        mesh.MinY = mesh.Triangles.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)));
        mesh.MaxY = mesh.Triangles.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        mesh.MinZ = mesh.Triangles.Min(t => Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z)));
        mesh.MaxZ = mesh.Triangles.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
        return mesh;
    }

    private static StlV ReadV(byte[] b, int off) => new(
        BitConverter.ToSingle(b, off),
        BitConverter.ToSingle(b, off + 4),
        BitConverter.ToSingle(b, off + 8));
}
