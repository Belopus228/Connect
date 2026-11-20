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

    /// <summary>Сколько треугольников максимум реально рисуем (остальное — прореживание).</summary>
    public const int MaxRenderTriangles = 12000;
    /// <summary>Ступеней освещения: грани группируются по оттенку в общие геометрии.</summary>
    public const int ShadeBuckets = 20;

    // ---- оптимизированные данные для рендера: сваренные вершины + индексы + готовый оттенок ----
    public double[] RenderX { get; private set; } = Array.Empty<double>();
    public double[] RenderY { get; private set; } = Array.Empty<double>();
    public double[] RenderZ { get; private set; } = Array.Empty<double>();
    public int[] RenderIndices { get; private set; } = Array.Empty<int>();
    public byte[] RenderShade { get; private set; } = Array.Empty<byte>();
    public int RenderTriangleCount => RenderIndices.Length / 3;
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

        mesh.BuildRenderData();
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

    /// <summary>
    /// Готовит лёгкую копию модели для отрисовки: сваривает совпадающие вершины
    /// (в STL каждая вершина повторяется до 6 раз), а если треугольников всё ещё
    /// слишком много — кластерное прореживание: близкие вершины сливаются в одну.
    /// Для корпуса 100 мм ячейка ~1 мм — внешне неотличимо, а граней в разы меньше.
    /// Освещение тоже считается здесь один раз: STL в сцене не вращается, только камера.
    /// </summary>
    private void BuildRenderData()
    {
        double minX = Triangles.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)));
        double minY = Triangles.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)));
        double minZ = Triangles.Min(t => Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z)));
        double maxX = Triangles.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
        double maxY = Triangles.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        double maxZ = Triangles.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
        double maxSize = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));

        BuildFromClusters(0, minX, minY, minZ);
        if (RenderTriangleCount > MaxRenderTriangles && maxSize > 0)
        {
            foreach (int div in new[] { 192, 144, 108, 80, 60, 44, 32, 22 })
            {
                BuildFromClusters(maxSize / div, minX, minY, minZ);
                if (RenderTriangleCount <= MaxRenderTriangles) break;
            }
        }

        RenderShade = new byte[RenderTriangleCount];
        for (int f = 0; f < RenderTriangleCount; f++)
        {
            int i0 = RenderIndices[f * 3], i1 = RenderIndices[f * 3 + 1], i2 = RenderIndices[f * 3 + 2];
            double ux = RenderX[i1] - RenderX[i0], uy = RenderY[i1] - RenderY[i0], uz = RenderZ[i1] - RenderZ[i0];
            double vx = RenderX[i2] - RenderX[i0], vy = RenderY[i2] - RenderY[i0], vz = RenderZ[i2] - RenderZ[i0];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-9) { nx = 0; ny = 0; nz = 1; nl = 1; }
            double dot = (nx * -0.45 + ny * -0.55 + nz * 0.75) / (nl * 1.03);
            double shade = Math.Clamp(0.48 + 0.62 * Math.Max(0, dot), 0.30, 1.08);
            int bucket = (int)Math.Round((shade - 0.30) / 0.78 * (ShadeBuckets - 1));
            RenderShade[f] = (byte)Math.Clamp(bucket, 0, ShadeBuckets - 1);
        }
    }

    /// <summary>cell=0 — точная сварка вершин; cell>0 — кластеры указанного размера.</summary>
    private void BuildFromClusters(double cell, double minX, double minY, double minZ)
    {
        var map = new Dictionary<(long, long, long), int>();
        var sx = new List<double>();
        var sy = new List<double>();
        var sz = new List<double>();
        var cnt = new List<int>();

        int VertexIndex(StlV v)
        {
            (long, long, long) key = cell > 0
                ? ((long)Math.Floor((v.X - minX) / cell), (long)Math.Floor((v.Y - minY) / cell), (long)Math.Floor((v.Z - minZ) / cell))
                : ((long)Math.Round(v.X * 10000), (long)Math.Round(v.Y * 10000), (long)Math.Round(v.Z * 10000));
            if (map.TryGetValue(key, out int idx))
            {
                sx[idx] += v.X; sy[idx] += v.Y; sz[idx] += v.Z; cnt[idx]++;
                return idx;
            }
            idx = sx.Count;
            map[key] = idx;
            sx.Add(v.X); sy.Add(v.Y); sz.Add(v.Z); cnt.Add(1);
            return idx;
        }

        var idx = new List<int>(Triangles.Count * 3);
        var seen = new HashSet<(int, int, int)>();
        foreach (var t in Triangles)
        {
            int a = VertexIndex(t.A), b = VertexIndex(t.B), c = VertexIndex(t.C);
            if (a == b || b == c || a == c) continue;              // вырожденный после слияния
            if (!seen.Add((a, b, c))) continue;                    // дубль грани
            idx.Add(a); idx.Add(b); idx.Add(c);
        }

        RenderX = new double[sx.Count];
        RenderY = new double[sx.Count];
        RenderZ = new double[sx.Count];
        for (int i = 0; i < sx.Count; i++)
        {
            RenderX[i] = sx[i] / cnt[i];
            RenderY[i] = sy[i] / cnt[i];
            RenderZ[i] = sz[i] / cnt[i];
        }
        RenderIndices = idx.ToArray();
    }
}
