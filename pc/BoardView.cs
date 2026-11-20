using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ConnectPC;

/// <summary>
/// 3D (изометрическая) плата. Режимы: Camera, MoveBoard, Move, Wire, Solder,
/// Delete, Sim, Measure. Плата поворачивается (BoardRotDeg), двигается (Pan),
/// смотрится сверху и снизу (ViewSide), провода ведутся по обеим сторонам.
/// Подписи — специально мелкие и только там, где нужны.
/// </summary>
public sealed class BoardView : FrameworkElement
{
    public ProjectData Data { get; set; } = ProjectData.CreateDemo();
    public string Mode { get; set; } = "Camera";
    public string ViewSide { get; set; } = "Top";   // Top | Bottom
    public string WireSide { get; set; } = "Top";   // куда кладём новые провода
    public int SolderTempC { get; set; } = 350;
    public bool UseFlux { get; set; } = true;
    public double CameraAngle { get; set; } = -28;
    public double Tilt { get; set; } = 0.52;
    public double Zoom { get; set; } = 1.0;
    public int BoardRotDeg { get; set; } = 0;
    public PlacedComponent Selected { get; set; }

    public Point Pan = new(0, 0);

    public event Action<string> LogMessage;
    public event Action StateChanged;

    private const double Cell = 11.0;
    private readonly List<FreePoint> _chain = new();
    private bool _chainActive;
    private FreePoint _chainCursor = new(0, 0);
    /// <summary>Длина следующего звена провода в клетках платы (колесо мыши).</summary>
    public double LinkLengthCells { get; set; } = 3.0;
    private PlacedComponent _dragComp;
    private (double, double) _dragOffset;
    private Point _lastMouse;
    private bool _panning, _rotatingCam;
    private (double, double)? _measureStart;
    private (double, double)? _hoverBoard;

    private static readonly string[] WireColors = { "#d32f2f", "#ff6f00", "#f9a825", "#2e7d32", "#1565c0", "#6a1b9a", "#00838f" };

    public string SolderQuality() =>
        SolderTempC > 430 ? "hot" : (SolderTempC < 280 || (SolderTempC < 315 && !UseFlux)) ? "cold" : "good";

    public void ResetView()
    {
        CameraAngle = -28; Tilt = 0.52; Zoom = 1.0; Pan = new Point(0, 0);
        BoardRotDeg = 0; ViewSide = "Top";
        InvalidateVisual();
    }

    public void RotateBoard(int dir)
    {
        BoardRotDeg = ((BoardRotDeg + dir * 90) % 360 + 360) % 360;
        LogMessage?.Invoke($"Плата повёрнута: {BoardRotDeg}°");
        InvalidateVisual();
    }

    // ---------------- проекция ----------------
    private (double, double) RotateBoardPoint(double wx, double wy)
    {
        double cx = Data.Cols / 2.0, cy = Data.Rows / 2.0;
        double a = BoardRotDeg * Math.PI / 180.0;
        double dx = wx - cx, dy = wy - cy;
        double rx = dx * Math.Cos(a) - dy * Math.Sin(a);
        double ry = dx * Math.Sin(a) + dy * Math.Cos(a);
        double x = cx + rx, y = cy + ry;
        if (ViewSide == "Bottom") x = Data.Cols - x;
        return (x, y);
    }

    public Point Project(double wx, double wy, double wz = 0) => ProjectD(wx, wy, wz).Pt;

    public (Point Pt, double Depth) ProjectD(double wx, double wy, double wz = 0)
    {
        var (ux, uy) = RotateBoardPoint(wx, wy);
        double cx = Data.Cols / 2.0, cy = Data.Rows / 2.0;
        double a = CameraAngle * Math.PI / 180.0;
        double dx = ux - cx, dy = uy - cy;
        double xr = dx * Math.Cos(a) - dy * Math.Sin(a);
        double yr = dx * Math.Sin(a) + dy * Math.Cos(a);
        double sx = ActualWidth / 2 + Pan.X + (xr - yr) * Cell * Zoom;
        double sy = ActualHeight / 2 + Pan.Y + (xr + yr) * Cell * Tilt * Zoom - wz * Cell * Zoom;
        return (new Point(sx, sy), (xr + yr) + wz * 1.5);
    }

    public (double, double) Unproject(Point s)
    {
        double a = CameraAngle * Math.PI / 180.0;
        double aa = (s.X - ActualWidth / 2 - Pan.X) / (Cell * Zoom);
        double bb = (s.Y - ActualHeight / 2 - Pan.Y) / (Cell * Tilt * Zoom);
        double xr = (aa + bb) / 2, yr = (bb - aa) / 2;
        double dx = xr * Math.Cos(a) + yr * Math.Sin(a);
        double dy = -xr * Math.Sin(a) + yr * Math.Cos(a);
        double ux = dx + Data.Cols / 2.0, uy = dy + Data.Rows / 2.0;
        if (ViewSide == "Bottom") ux = Data.Cols - ux;
        // обратный поворот платы
        double cx = Data.Cols / 2.0, cy = Data.Rows / 2.0;
        double ra = -BoardRotDeg * Math.PI / 180.0;
        double ddx = ux - cx, ddy = uy - cy;
        double wx = cx + ddx * Math.Cos(ra) - ddy * Math.Sin(ra);
        double wy = cy + ddx * Math.Sin(ra) + ddy * Math.Cos(ra);
        return (wx, wy);
    }

    private Point ProjectLocal(PlacedComponent c, double lx, double ly, double wz = 0)
    {
        var d = c.Def;
        double rx, ry;
        switch (((c.Rot % 360) + 360) % 360)
        {
            case 90: rx = d.H - ly; ry = lx; break;
            case 180: rx = d.W - lx; ry = d.H - ly; break;
            case 270: rx = ly; ry = d.W - lx; break;
            default: rx = lx; ry = ly; break;
        }
        return Project(c.X + rx, c.Y + ry, wz);
    }

    private (double, double) PinWorld(PlacedComponent c, PinDef pin)
    {
        var (px, py) = c.PinPos(pin);
        return (c.X + px + 0.5, c.Y + py + 0.5);
    }

    private static double PinZ(PlacedComponent c) => c.Type switch
    {
        "esp32" => 2.1,
        "oled" or "tft" or "tft18" or "lcd1602" or "nrf24" => 1.9,
        "led" or "resistor" or "cap" => 0.5,
        _ => 1.2
    };

    // ---------------- утилиты рисования ----------------
    private static Color Hex(string hex, byte alpha = 255)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = alpha;
        return c;
    }
    private static SolidColorBrush Brush(string hex, byte alpha = 255) => new(Hex(hex, alpha));
    private static SolidColorBrush Shade(string hex, double f, byte alpha = 255)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(Color.FromArgb(alpha,
            (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255)));
    }
    private static Pen PenOf(string hex, double w, byte alpha = 255) => new(Brush(hex, alpha), w);

    private void Text(DrawingContext dc, string text, Point at, double size, string color, bool bold = false, bool center = true)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var p = center ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at;
        dc.DrawText(ft, p);
    }

    private void Poly(DrawingContext dc, Point[] pts, Brush fill, Pen pen = null)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], fill != null, true);
            g.PolyLineTo(pts.Skip(1).ToArray(), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, pen, geo);
    }

    private void DrawBox(DrawingContext dc, PlacedComponent c, double lx1, double ly1, double lx2, double ly2,
        double z1, double z2, string color)
    {
        var top = new[]
        {
            ProjectLocal(c, lx1, ly1, z2), ProjectLocal(c, lx2, ly1, z2),
            ProjectLocal(c, lx2, ly2, z2), ProjectLocal(c, lx1, ly2, z2)
        };
        var sides = new (Point[], double)[]
        {
            (new[] { ProjectLocal(c, lx1, ly2, z1), ProjectLocal(c, lx2, ly2, z1), ProjectLocal(c, lx2, ly2, z2), ProjectLocal(c, lx1, ly2, z2) }, 0.62),
            (new[] { ProjectLocal(c, lx2, ly1, z1), ProjectLocal(c, lx2, ly2, z1), ProjectLocal(c, lx2, ly2, z2), ProjectLocal(c, lx2, ly1, z2) }, 0.78),
            (new[] { ProjectLocal(c, lx1, ly1, z1), ProjectLocal(c, lx2, ly1, z1), ProjectLocal(c, lx2, ly1, z2), ProjectLocal(c, lx1, ly1, z2) }, 0.5),
            (new[] { ProjectLocal(c, lx1, ly1, z1), ProjectLocal(c, lx1, ly2, z1), ProjectLocal(c, lx1, ly2, z2), ProjectLocal(c, lx1, ly1, z2) }, 0.7),
        };
        foreach (var (pts, f) in sides) Poly(dc, pts, Shade(color, f), PenOf("#000000", 0.5, 60));
        Poly(dc, top, Brush(color), PenOf("#000000", 0.6, 80));
    }

    // ---------------- рендер ----------------
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brush("#0f1b24"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Data == null) return;
        var state = Circuit.Compute(Data);
        bool bottom = ViewSide == "Bottom";
        DrawCaseBack(dc);

        // плата
        var bc = new[] { Project(0, 0), Project(Data.Cols, 0), Project(Data.Cols, Data.Rows), Project(0, Data.Rows) };
        var bcLow = bc.Select(p => new Point(p.X, p.Y + 7 * Zoom)).ToArray();
        Poly(dc, new[] { bcLow[3], bcLow[2], bc[2], bc[3] }, Brush("#0a3520"));
        Poly(dc, new[] { bcLow[1], bcLow[2], bc[2], bc[1] }, Brush("#0d4426"));
        Poly(dc, bc, Brush(bottom ? "#6d4c2f" : "#14532d"), PenOf("#0b3d20", 2));
        dc.DrawRectangle(Brush("#ffffff", 22), null, new Rect(bc[0].X - 2, bc[0].Y - 14, 1, 1)); // noop-сохранитель

        // отверстия
        var occupied = new HashSet<(int, int)>();
        foreach (var c in Data.Components)
        {
            var (ew, eh) = c.EffSize;
            for (int yy = c.Y; yy < c.Y + eh; yy++)
                for (int xx = c.X; xx < c.X + ew; xx++) occupied.Add((xx, yy));
        }
        for (int gy = 0; gy < Data.Rows; gy++)
            for (int gx = 0; gx < Data.Cols; gx++)
            {
                if (occupied.Contains((gx, gy))) continue;
                var p = Project(gx + 0.5, gy + 0.5, 0.02);
                dc.DrawEllipse(Brush(bottom ? "#4e342e" : "#0b3d20"), null, p, 1.7 * Zoom, 1.3 * Zoom);
            }
        DrawBoardTraces(dc, occupied);
        var silk = Project(1, Data.Rows - 0.6, 0.05);
        Text(dc, bottom ? "СОЛДЕРНАЯ СТОРОНА (BOTTOM)" : "CONNECT PCB • REV 2.0 • TOP", silk, 7, "#cfe8d4", center: false);

        // паяные дорожки активной стороны + чужие пунктиром
        foreach (var w in Data.Wires.Where(w => w.Style == "solder"))
            DrawWire(dc, w, state, bottom);
        foreach (var fw in Data.FreeWires.Where(f => f.Style == "solder"))
            DrawFreeWire(dc, fw, bottom);
        // компоненты: настоящие 3D-меши (полигоны + освещение)
        if (bottom)
        {
            foreach (var c in Data.Components) DrawComponentBottom(dc, c);
        }
        else
        {
            var meshFaces = new List<(Face3, PlacedComponent)>();
            foreach (var c in Data.Components) BuildComponentMesh(c, state, meshFaces);
            DrawMeshFaces(dc, meshFaces);
            DrawDynamicOverlays(dc, state);
        }
        // провода-дуги
        foreach (var w in Data.Wires.Where(w => w.Style != "solder"))
            DrawWire(dc, w, state, bottom);
        foreach (var fw in Data.FreeWires.Where(f => f.Style != "solder"))
            DrawFreeWire(dc, fw, bottom);
        // текущая укладка по звеньям: поставленные узлы + следующее звено к курсору
        if (_chainActive && _chain.Count >= 1)
        {
            var end = PreviewEnd();
            var pts = new List<FreePoint>(_chain) { end };
            DrawFreeWire(dc, new FreeWire
            {
                Points = pts,
                Style = Mode == "Solder" ? "solder" : "wire",
                Color = "#facc15", Side = WireSide, Quality = SolderQuality(),
                TempC = SolderTempC, Flux = UseFlux
            }, bottom, preview: true);
            foreach (var np in _chain)
            {
                var sp = Project(np.X, np.Y, 0.5);
                dc.DrawEllipse(Brush("#facc15"), PenOf("#111827", 1), sp, 3.4, 3.4);
            }
            var ep = Project(end.X, end.Y, 0.5);
            dc.DrawEllipse(null, PenOf("#facc15", 1.6), ep, 5.5, 5.5);
            Text(dc, $"звено {LinkLengthCells:F1} кл. = {LinkLengthCells * 2.54:F1} мм", new Point(ep.X, ep.Y - 13), 8, "#facc15", bold: true);
        }
        DrawCaseFront(dc);
        DrawContactLabels(dc, bottom);
        // подписи: маленькие, только выбранное и экраны
        DrawLabels(dc, state, bottom);
        // замеры
        DrawMeasurements(dc);
    }

    // ================= 3D-меши компонентов =================
    private readonly record struct V3(double X, double Y, double Z);

    private sealed class Face3
    {
        public V3[] P = Array.Empty<V3>();
        public string Color = "#888888";
    }

    private sealed class Mesh
    {
        public readonly List<Face3> Faces = new();
        public void Add(V3[] pts, string col) => Faces.Add(new Face3 { P = pts, Color = col });

        public void Box(double x1, double y1, double z1, double x2, double y2, double z2, string col)
        {
            var a = new V3(x1, y1, z1); var b = new V3(x2, y1, z1); var c = new V3(x2, y2, z1); var d = new V3(x1, y2, z1);
            var e = new V3(x1, y1, z2); var f = new V3(x2, y1, z2); var g = new V3(x2, y2, z2); var h = new V3(x1, y2, z2);
            Add(new[] { e, f, g, h }, col);       // верх
            Add(new[] { d, c, b, a }, col);       // низ
            Add(new[] { a, b, f, e }, col);       // стороны
            Add(new[] { b, c, g, f }, col);
            Add(new[] { c, d, h, g }, col);
            Add(new[] { d, a, e, h }, col);
        }

        public void BevelBox(double x1, double y1, double z1, double x2, double y2, double z2, double b, string col)
        {
            // корпус со скошенной верхней кромкой: низ -> вертикальные стенки -> фаска -> крышка
            double zm = z2 - b;
            var bot = new[] { new V3(x1, y1, z1), new V3(x2, y1, z1), new V3(x2, y2, z1), new V3(x1, y2, z1) };
            var mid = new[] { new V3(x1, y1, zm), new V3(x2, y1, zm), new V3(x2, y2, zm), new V3(x1, y2, zm) };
            var top = new[] { new V3(x1 + b, y1 + b, z2), new V3(x2 - b, y1 + b, z2), new V3(x2 - b, y2 - b, z2), new V3(x1 + b, y2 - b, z2) };
            Add(new[] { bot[3], bot[2], bot[1], bot[0] }, col);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Add(new[] { bot[i], bot[j], mid[j], mid[i] }, col);   // стенка
                Add(new[] { mid[i], mid[j], top[j], top[i] }, col);   // фаска
            }
            Add(top, col);
        }

        public void CylZ(double cx, double cy, double z1, double z2, double r, string col, int seg = 16)
        {
            var top = new V3[seg];
            var bot = new V3[seg];
            for (int i = 0; i < seg; i++)
            {
                double a = 2 * Math.PI * i / seg;
                top[i] = new V3(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z2);
                bot[i] = new V3(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z1);
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                Add(new[] { bot[i], bot[j], top[j], top[i] }, col);
            }
            Add(top, col);
        }

        public void CylX(double x1, double x2, double cy, double cz, double r, string col, int seg = 16)
        {
            var ra = new V3[seg];
            var rb = new V3[seg];
            for (int i = 0; i < seg; i++)
            {
                double a = 2 * Math.PI * i / seg;
                double oy = r * Math.Cos(a), oz = r * Math.Sin(a);
                ra[i] = new V3(x1, cy + oy, cz + oz);
                rb[i] = new V3(x2, cy + oy, cz + oz);
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                Add(new[] { ra[i], ra[j], rb[j], rb[i] }, col);
            }
            Add(rb, col);
        }

        public void RotBox(double cx, double cy, double z1, double z2, double len, double wid, double angDeg, string col)
        {
            double a = angDeg * Math.PI / 180.0;
            double dx = Math.Cos(a), dy = Math.Sin(a), px = -dy, py = dx;
            V3 Corner(double sl, double sw, double z) => new(cx + dx * sl + px * sw, cy + dy * sl + py * sw, z);
            var t = new[] { Corner(-len / 2, -wid / 2, z2), Corner(len / 2, -wid / 2, z2), Corner(len / 2, wid / 2, z2), Corner(-len / 2, wid / 2, z2) };
            var b = new[] { Corner(-len / 2, -wid / 2, z1), Corner(len / 2, -wid / 2, z1), Corner(len / 2, wid / 2, z1), Corner(-len / 2, wid / 2, z1) };
            Add(t, col);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Add(new[] { b[i], b[j], t[j], t[i] }, col);
            }
        }
    }

    private (double, double) LocalToWorld(PlacedComponent c, double lx, double ly)
    {
        var d = c.Def;
        double rx, ry;
        switch (((c.Rot % 360) + 360) % 360)
        {
            case 90: rx = d.H - ly; ry = lx; break;
            case 180: rx = d.W - lx; ry = d.H - ly; break;
            case 270: rx = ly; ry = d.W - lx; break;
            default: rx = lx; ry = ly; break;
        }
        return (c.X + rx, c.Y + ry);
    }

    private void DrawBoardTraces(DrawingContext dc, HashSet<(int, int)> occupied)
    {
        // декоративные печатные дорожки текстолита между отверстиями
        var pen = PenOf("#1e7a46", 2.0, 70);
        for (int gy = 2; gy < Data.Rows - 3; gy += 4)
            for (int gx = 2; gx < Data.Cols - 4; gx += 5)
            {
                if (occupied.Contains((gx, gy)) || occupied.Contains((gx + 3, gy + 2))) continue;
                DrawPolyline(dc, new[]
                {
                    Project(gx + 0.5, gy + 0.5, 0.03), Project(gx + 3.5, gy + 0.5, 0.03), Project(gx + 3.5, gy + 2.5, 0.03)
                }, pen);
            }
    }

    private void DrawContactLabels(DrawingContext dc, bool bottom)
    {
        // Подписи контактов прямо на текстолите (как заводская шелкография):
        // мелкий шрифт у каждой площадки, наружу от центра детали.
        foreach (var c in Data.Components)
        {
            var (ew, eh) = c.EffSize;
            double cx = c.X + ew / 2.0, cy = c.Y + eh / 2.0;
            foreach (var pin in c.Def.Pins)
            {
                var (px, py) = PinWorld(c, pin);
                double dx = px - cx, dy = py - cy;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.01) { dx = 0; dy = -1; len = 1; }
                var sp = Project(px + dx / len * 0.70, py + dy / len * 0.70, 0.10);
                Text(dc, pin.Name, sp, Zoom < 0.85 ? 5.2 : 6.0, bottom ? "#e8d9b0" : "#d9ead3");
            }
        }
    }

    private void DrawMeshFaces(DrawingContext dc, List<(Face3, PlacedComponent)> items)
    {
        var rendered = new List<(Point[] Pts, string Color, double Depth, double Shade)>();
        foreach (var (face, c) in items)
        {
            if (face.P.Length < 3) continue;
            var wv = face.P.Select(v =>
            {
                var (wx, wy) = LocalToWorld(c, v.X, v.Y);
                return (wx, wy, v.Z);
            }).ToArray();
            var proj = wv.Select(v => ProjectD(v.wx, v.wy, v.Z)).ToArray();
            double depth = proj.Average(q => q.Depth);
            var a = wv[0]; var b = wv[1]; var cc = wv[2];
            double ux = b.wx - a.wx, uy = b.wy - a.wy, uz = b.Z - a.Z;
            double vx = cc.wx - a.wx, vy = cc.wy - a.wy, vz = cc.Z - a.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-9) { nx = 0; ny = 0; nz = 1; nl = 1; }
            double dot = (nx * -0.45 + ny * -0.55 + nz * 0.75) / (nl * 1.03);
            double shade = Math.Clamp(0.48 + 0.62 * Math.Max(0, dot), 0.30, 1.08);
            rendered.Add((proj.Select(q => q.Pt).ToArray(), face.Color, depth, shade));
        }
        foreach (var r in rendered.OrderBy(r => r.Depth))
            Poly(dc, r.Pts, Shade(r.Color, r.Shade), PenOf("#000000", 0.5, 45));
    }

    private static string LedColorHex(PlacedComponent c) => c.Prop("colorName", "Красный") switch
    {
        "Зелёный" => "#22ff66",
        "Синий" => "#2266ff",
        "Жёлтый" => "#ffee22",
        "Белый" => "#ffffff",
        _ => "#ff2222"
    };

    private void BuildComponentMesh(PlacedComponent c, CircuitResult state, List<(Face3, PlacedComponent)> outp)
    {
        var m = new Mesh();
        var d = c.Def;
        var (w, h) = (d.W, d.H);
        double zt = d.HeightMm / 2.54;
        // ножки-выводы — часть модели
        foreach (var pin in d.Pins)
            m.Box(pin.Dx + 0.27, pin.Dy + 0.27, 0, pin.Dx + 0.73, pin.Dy + 0.73, 0.30, "#d4af37");
        switch (c.Type)
        {
            case "esp32":
            {
                bool powered = state.EspPowered.Contains(c.Id) && Data.Running;
                m.BevelBox(0, 0, 0.25, w, h, 0.85, 0.16, "#1a237e");
                m.Box(0, 0, 0.30, 0.55, h, 0.85, "#101418");
                m.Box(w - 0.55, 0, 0.30, w, h, 0.85, "#101418");
                m.Box(w * 0.24, h * 0.30, 0.85, w * 0.76, h * 0.74, 2.0, "#c7ccd4");   // экран WROOM
                m.Box(w * 0.26, h * 0.32, 2.0, w * 0.74, h * 0.72, 2.12, "#dde3ea");  // крышка экрана
                m.Box(w * 0.24, h * 0.04, 0.85, w * 0.76, h * 0.26, 1.02, "#283593"); // антенная зона
                for (int i = 0; i < 6; i++)
                    m.Box(w * 0.27 + i * w * 0.075, h * 0.07, 1.03, w * 0.27 + i * w * 0.075 + 0.20, h * 0.23, 1.10, "#d4af37");
                m.Box(w * 0.33, -0.4, 0.85, w * 0.67, 1.5, 1.9, "#9aa0a6");          // USB-C
                m.Box(w * 0.38, -0.45, 1.05, w * 0.62, 0.2, 1.7, "#20242a");
                m.CylZ(0.85, 1.15, 0.85, 1.5, 0.55, "#263238", 12);                    // EN
                m.CylZ(w - 0.85, 1.15, 0.85, 1.5, 0.55, "#263238", 12);               // BOOT
                m.Box(w * 0.30, h * 0.78, 0.85, w * 0.48, h * 0.90, 1.25, "#8f979e"); // кварц
                // SMD-компоненты вокруг модуля: конденсаторы/резисторы 0402
                var smd = new[] { (0.10, 0.35), (0.10, 0.50), (0.10, 0.65), (0.86, 0.40), (0.86, 0.58), (0.40, 0.82), (0.60, 0.82) };
                foreach (var (fx, fy) in smd)
                    m.Box(w * fx, h * fy, 0.85, w * fx + 0.55, h * fy + 0.38, 1.12, (fx * 10 + fy) % 2 < 1 ? "#37474f" : "#6d4c2f");
                // винты крепления модуля
                m.CylZ(0.55, 0.55, 0.85, 1.05, 0.26, "#90a4ae", 10);
                m.CylZ(w - 0.55, h - 0.55, 0.85, 1.05, 0.26, "#90a4ae", 10);
                m.Box(w - 1.3, 2.2, 0.9, w - 0.7, 2.8, 1.25, powered ? "#69f0ae" : "#546e7a"); // PWR LED
                break;
            }
            case "oled":
            case "tft":
            case "tft18":
            case "lcd1602":
                m.BevelBox(0, 0, 0.25, w, h, 0.8, 0.14, d.Color);
                m.Box(w * 0.06, h * 0.07, 0.8, w * 0.94, h * 0.93, 1.08, c.Type == "lcd1602" ? "#9ccc65" : "#07141d");
                m.Box(w * 0.05, h * 0.05, 1.08, w * 0.95, h * 0.10, 1.2, "#607d8b");
                m.Box(w * 0.05, h * 0.90, 1.08, w * 0.95, h * 0.95, 1.2, "#607d8b");
                m.Box(w * 0.05, h * 0.05, 1.08, w * 0.08, h * 0.95, 1.2, "#607d8b");
                m.Box(w * 0.92, h * 0.05, 1.08, w * 0.95, h * 0.95, 1.2, "#607d8b");
                foreach (var (sx, sy) in new[] { (0.55, 0.55), (w - 0.55, 0.55), (w - 0.55, h - 0.55), (0.55, h - 0.55) })
                    m.CylZ(sx, sy, 0.8, 1.05, 0.30, "#37474f", 10);
                break;
            case "nrf24":
                m.Box(0, 0, 0.25, w, h, 0.8, "#0d47a1");
                m.Box(w * 0.3, h * 0.38, 0.8, w * 0.7, h * 0.68, 1.35, "#111111");
                m.Box(w * 0.38, h * 0.72, 0.8, w * 0.62, h * 0.92, 1.2, "#c0c6ce");
                for (int i = 0; i < 8; i++)
                    m.Box(w * 0.06 + i * w * 0.11, h * (i % 2 == 0 ? 0.08 : 0.20), 0.82,
                          w * 0.06 + i * w * 0.11 + 0.17, h * (i % 2 == 0 ? 0.08 : 0.20) + 0.15, 0.92, "#d4af37");
                break;
            case "led":
            {
                string col = LedColorHex(c);
                m.Box(0.45, h / 2.0 - 0.18, 0, 0.85, h / 2.0 + 0.18, 1.0, "#c0c6ce");
                m.Box(w - 0.85, h / 2.0 - 0.18, 0, w - 0.45, h / 2.0 + 0.18, 1.0, "#c0c6ce");
                m.CylZ(w / 2.0, h / 2.0, 0.95, 1.25, 1.12, col, 18);
                for (int i = 0; i < 5; i++)
                    m.CylZ(w / 2.0, h / 2.0, 1.25 + i * 0.42, 1.67 + i * 0.42, 1.05 - 0.19 * i, col, 18);
                break;
            }
            case "resistor":
                m.CylX(0, w, h / 2.0, 0.85, 0.10, "#c0c6ce", 8);
                m.CylX(w * 0.27, w * 0.73, h / 2.0, 0.85, 0.60, "#d7a86e", 18);
                var bands = new[] { "#ff2222", "#ff2222", "#795548", "#ffd700" };
                for (int i = 0; i < bands.Length; i++)
                {
                    double bx = w * (0.36 + i * 0.10);
                    m.CylX(bx, bx + 0.24, h / 2.0, 0.85, 0.63, bands[i], 18);
                }
                break;
            case "button":
                m.BevelBox(0, 0, 0.2, w, h, 0.95, 0.18, "#263238");
                m.CylZ(w / 2.0, h / 2.0, 0.95, 1.8, 1.05, Data.Pressed.Contains(c.Id) ? "#69f0ae" : "#ef5350", 18);
                break;
            case "bat18650":
                m.BevelBox(0, 0, 0.1, w, h, 0.75, 0.15, "#37474f");
                m.CylX(0.35, w - 0.35, h / 2.0, 1.6, 1.5, "#2e7d32", 22);
                m.CylX(w * 0.42, w * 0.58, h / 2.0, 1.6, 1.53, "#a5d6a7", 22);
                m.CylX(w - 0.6, w - 0.32, h / 2.0, 1.6, 1.52, "#9aa0a6", 22);
                m.CylX(w - 0.32, w + 0.02, h / 2.0, 1.6, 0.45, "#e8eaed", 12);
                break;
            case "bat9v":
                m.Box(0, 0, 0.2, w, h, zt * 0.85, "#212121");
                m.Box(0.2, 0.2, zt * 0.85, w - 0.2, h - 0.2, zt, "#37474f");
                m.CylZ(1.5, 0.5, zt, zt + 0.5, 0.4, "#c0c6ce", 12);
                m.CylZ(3.5, 0.5, zt, zt + 0.5, 0.4, "#616161", 12);
                break;
            case "lipo":
                m.Box(0, 0, 0.2, w, h, zt * 0.7, "#f9a825");
                m.Box(w * 0.08, h * 0.1, zt * 0.7, w * 0.92, h * 0.9, zt * 0.85, "#f57f17");
                break;
            case "buzzer":
                m.CylZ(w / 2.0, h / 2.0, 0.2, zt * 0.85, 1.35, "#263238", 20);
                m.CylZ(w / 2.0, h / 2.0, zt * 0.85, zt * 0.92, 0.42, "#0b1220", 12);
                break;
            case "dht22":
                m.BevelBox(0, 0, 0.2, w, h, zt * 0.8, 0.16, "#eceff1");
                for (int i = 0; i < 4; i++)
                    m.Box(w * 0.1, h * (0.18 + i * 0.16), zt * 0.8, w * 0.9, h * (0.18 + i * 0.16) + 0.18, zt * 0.86, "#90a4ae");
                break;
            case "pot":
            {
                m.BevelBox(0, 0, 0.2, w, h, 1.8, 0.16, "#263238");
                m.CylZ(w / 2.0, h / 2.0, 1.8, 3.3, 1.35, "#78909c", 20);
                int val = int.TryParse(c.Prop("value", "512"), out var vv) ? vv : 512;
                double ang = val / 1023.0 * 270.0 - 135.0;
                m.RotBox(w / 2.0, h / 2.0, 3.3, 3.52, 2.3, 0.42, ang, "#111827");
                break;
            }
            case "servo":
            {
                m.Box(-0.5, h * 0.3, 0.2, 0, h * 0.7, 0.7, "#0d47a1");
                m.Box(w, h * 0.3, 0.2, w + 0.5, h * 0.7, 0.7, "#0d47a1");
                m.BevelBox(0, 0, 0.2, w, h, zt * 0.55, 0.16, "#1565c0");
                m.CylZ(w / 2.0, h / 2.0, zt * 0.55, zt * 0.78, 0.95, "#eceff1", 16);
                int angDeg = int.TryParse(c.Prop("angle", "90"), out var av) ? av : 90;
                m.RotBox(w / 2.0, h / 2.0, zt * 0.78, zt * 0.92, 4.6, 0.95, angDeg - 90, "#ffffff");
                break;
            }
            case "relay":
                m.BevelBox(0, 0, 0.2, w, h, zt * 0.65, 0.18, d.Color);
                m.Box(w * 0.12, h * 0.14, zt * 0.65, w * 0.88, h * 0.86, zt * 0.78, "#1e6fd9");
                break;
            case "cap":
                m.CylZ(w / 2.0, h / 2.0, 0.2, zt * 0.9, 1.15, "#1e2a6e", 20);
                m.CylZ(w / 2.0, h / 2.0, zt * 0.9, zt * 0.98, 1.16, "#283593", 20);
                break;
            default:
                m.Box(0, 0, 0.2, w, h, zt * 0.7, d.Color);
                m.Box(w * 0.15, h * 0.15, zt * 0.7, w * 0.85, h * 0.85, zt * 0.82, d.Color);
                break;
        }
        foreach (var f in m.Faces) outp.Add((f, c));
    }

    private void DrawComponentBottom(DrawingContext dc, PlacedComponent c)
    {
        var (ew, eh) = c.EffSize;
        var poly = new[]
        {
            Project(c.X, c.Y, 0.1), Project(c.X + ew, c.Y, 0.1),
            Project(c.X + ew, c.Y + eh, 0.1), Project(c.X, c.Y + eh, 0.1)
        };
        Poly(dc, poly, Brush("#90a4ae", 45), PenOf("#cfd8dc", 1, 120));
    }

    private void DrawDynamicOverlays(DrawingContext dc, CircuitResult state)
    {
        foreach (var c in Data.Components.Where(c => c.Type == "esp32"))
        {
            var tp = ProjectLocal(c, c.Def.W / 2.0, c.Def.H * 0.52, 2.20);
            Text(dc, "ESP32-WROOM", tp, 6.5, "#37474f", bold: true);
        }
        foreach (var c in Data.Components.Where(c => c.Type == "bat18650"))
        {
            var (w, h) = (c.Def.W, c.Def.H);
            Text(dc, "+", ProjectLocal(c, w - 0.45, h / 2.0, 3.30), 10, "#ffffff", bold: true);
            Text(dc, "18650", ProjectLocal(c, w / 2.0, h / 2.0, 3.25), 6, "#e8f5e9", bold: true);
        }
        foreach (var c in Data.Components.Where(c => c.Type == "led"))
        {
            var (w, h) = (c.Def.W, c.Def.H);
            var ctr = ProjectLocal(c, w / 2.0, h / 2.0, 3.6);
            state.Leds.TryGetValue(c.Id, out var st);
            if (st is { Burnt: true })
            {
                dc.DrawEllipse(Brush("#333333", 160), PenOf("#ff2222", 2), ctr, 10, 10);
                Text(dc, "✕", ctr, 10, "#ff2222", bold: true);
            }
            else if (st is { Brightness: > 0.02 } && Data.Running)
            {
                double r = 14 + 16 * st.Brightness;
                dc.DrawEllipse(Brush(LedColorHex(c), 40), null, ctr, r, r);
                dc.DrawEllipse(null, new Pen(Brush(LedColorHex(c), 170), 3), ctr, r * 0.7, r * 0.7);
            }
        }
    }

    // ---------------- корпус платы ----------------
    private void DrawCaseBack(DrawingContext dc)
    {
        if (Data.CaseType is null or "None") return;
        var tray = new[]
        {
            Project(-1.2, -1.2, -0.05), Project(Data.Cols + 1.2, -1.2, -0.05),
            Project(Data.Cols + 1.2, Data.Rows + 1.2, -0.05), Project(-1.2, Data.Rows + 1.2, -0.05)
        };
        var (fill, alpha) = Data.CaseType switch
        {
            "Acrylic" => ("#bfe9ff", (byte)40),
            "BlueMetal" => ("#1e3a5f", (byte)235),
            _ => ("#111827", (byte)235)
        };
        Poly(dc, tray, Brush(fill, alpha), PenOf("#000000", 1, 90));
    }

    private void DrawCaseFront(DrawingContext dc)
    {
        if (Data.CaseType is null or "None") return;
        const double hz = 4.6;
        double x0 = -1.2, y0 = -1.2, x1 = Data.Cols + 1.2, y1 = Data.Rows + 1.2;
        var (col, alpha) = Data.CaseType switch
        {
            "Acrylic" => ("#dff6ff", (byte)60),
            "BlueMetal" => ("#2563eb", (byte)205),
            _ => ("#0b1220", (byte)185)
        };
        var walls = new[]
        {
            new[] { Project(x0, y1, 0), Project(x1, y1, 0), Project(x1, y1, hz), Project(x0, y1, hz) },
            new[] { Project(x1, y0, 0), Project(x1, y1, 0), Project(x1, y1, hz), Project(x1, y0, hz) },
            new[] { Project(x0, y0, 0), Project(x1, y0, 0), Project(x1, y0, hz), Project(x0, y0, hz) },
            new[] { Project(x0, y0, 0), Project(x0, y1, 0), Project(x0, y1, hz), Project(x0, y0, hz) },
        };
        foreach (var wall in walls) Poly(dc, wall, Brush(col, alpha), PenOf("#000000", 0.8, 80));
        // винты по углам
        foreach (var (sx, sy) in new[] { (x0 + 0.55, y0 + 0.55), (x1 - 0.55, y0 + 0.55), (x1 - 0.55, y1 - 0.55), (x0 + 0.55, y1 - 0.55) })
        {
            var sp = Project(sx, sy, hz);
            dc.DrawEllipse(Brush("#c0c6ce"), PenOf("#37474f", 1), sp, 4.6, 3.6);
            dc.DrawLine(PenOf("#37474f", 1.2), new Point(sp.X - 3, sp.Y), new Point(sp.X + 3, sp.Y));
        }
        // прозрачная крышка — только у акрилового корпуса
        if (Data.CaseType == "Acrylic")
        {
            var lid = new[] { Project(x0, y0, hz), Project(x1, y0, hz), Project(x1, y1, hz), Project(x0, y1, hz) };
            Poly(dc, lid, Brush("#e0f2fe", 26), PenOf("#bae6fd", 1.2, 110));
        }
    }

    // ---------------- свободные линии (без пинов и снапа) ----------------
    private void DrawFreeWire(DrawingContext dc, FreeWire fw, bool bottom, bool preview = false)
    {
        if (fw.Points.Count < 2) return;
        bool activeSide = (fw.Side == "Top") != bottom;
        byte alpha = preview ? (byte)230 : activeSide ? (byte)255 : (byte)70;
        double z = fw.Style == "solder" ? 0.14 : (fw.Side == "Bottom" ? 0.22 : 0.95);
        var pts = fw.Points.Select(p => Project(p.X, p.Y, z)).ToArray();
        if (fw.Style == "solder")
        {
            if (fw.Quality == "hot")
                DrawPolyline(dc, pts, PenOf("#3e2723", 6.5, (byte)(alpha * 0.55)));
            string col = fw.Quality switch { "cold" => "#7d838a", "hot" => "#5f564e", _ => "#b9c2c9" };
            var pen = PenOf(col, 3.6, alpha);
            if (!activeSide && !preview) pen.DashStyle = DashStyles.Dash;
            DrawPolyline(dc, pts, pen);
            if (fw.Quality == "good" && activeSide)
                DrawPolyline(dc, pts, PenOf("#f4f8fa", 1.0, alpha));
            // наплывы припоя: концы + редкие точки вдоль линии
            for (int i = 0; i < fw.Points.Count; i += 5)
                DrawSolderBlob(dc, Project(fw.Points[i].X, fw.Points[i].Y, 0.35), fw.Quality, fw.Flux, activeSide, alpha);
            var last = fw.Points[^1];
            DrawSolderBlob(dc, Project(last.X, last.Y, 0.35), fw.Quality, fw.Flux, activeSide, alpha);
        }
        else
        {
            DrawPolyline(dc, pts, PenOf("#000000", 4.4, (byte)(alpha * 0.35)));
            var pen = PenOf(fw.Color, 2.7, alpha);
            if (!activeSide && !preview) pen.DashStyle = DashStyles.Dash;
            DrawPolyline(dc, pts, pen);
            DrawPolyline(dc, pts, PenOf("#ffffff", 0.8, (byte)(alpha * 0.45)));
        }
    }

    private void DrawSolderBlob(DrawingContext dc, Point s, string quality, bool flux, bool activeSide, byte alpha)
    {
        if (flux) dc.DrawEllipse(Brush("#d9a441", (byte)(activeSide ? 55 : 22)), null, s, 8.5, 6);
        if (quality == "hot") dc.DrawEllipse(Brush("#3e2723", (byte)(activeSide ? 120 : 45)), null, s, 7, 5);
        string col = quality switch { "cold" => "#878e96", "hot" => "#6e6259", _ => "#dde5ea" };
        dc.DrawEllipse(Brush(col, alpha), PenOf("#5f6b73", 0.8, alpha), s, 5.6, 4.6);
        if (quality == "cold" && activeSide)
        {
            dc.DrawLine(PenOf("#37474f", 1.2), new Point(s.X - 3.6, s.Y - 1.8), new Point(s.X + 2.8, s.Y + 2.8));
            dc.DrawLine(PenOf("#37474f", 1.2), new Point(s.X + 3.6, s.Y - 2.6), new Point(s.X - 2.6, s.Y + 0.8));
        }
        else if (quality == "good" && activeSide)
        {
            dc.DrawEllipse(Brush("#ffffff", 230), null, new Point(s.X - 1.8, s.Y - 2.0), 1.7, 1.4);
        }
    }

    private void DrawWire(DrawingContext dc, WireConn w, CircuitResult state, bool bottom)
    {
        var a = Data.ById(w.AId);
        var b = Data.ById(w.BId);
        if (a == null || b == null) return;
        var pa = a.Def.Pins.FirstOrDefault(p => p.Name == w.APin);
        var pb = b.Def.Pins.FirstOrDefault(p => p.Name == w.BPin);
        if (pa == null || pb == null) return;
        var (ax, ay) = PinWorld(a, pa);
        var (bx, by) = PinWorld(b, pb);
        bool activeSide = (w.Side == "Top") != bottom;
        byte alpha = activeSide ? (byte)255 : (byte)70;

        if (w.Style == "solder")
        {
            var pts = new[] { Project(ax, ay, 0.12), Project(bx, ay, 0.12), Project(bx, by, 0.12) };
            if (w.Quality == "hot")
                DrawPolyline(dc, pts, PenOf("#3e2723", 6.5, (byte)(alpha * 0.55)));
            string col = w.Quality switch { "cold" => "#7d838a", "hot" => "#5f564e", _ => "#b9c2c9" };
            var pen = PenOf(col, 3.6, alpha);
            if (!activeSide) pen.DashStyle = DashStyles.Dash;
            DrawPolyline(dc, pts, pen);
            if (w.Quality == "good" && activeSide)
                DrawPolyline(dc, pts, PenOf("#f4f8fa", 1.0, alpha));
        }
        else
        {
            double topz = Math.Max(PinZ(a), PinZ(b)) + 2.6;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                var start = Project(ax, ay, PinZ(a));
                g.BeginFigure(start, false, false);
                var pts = new List<Point>();
                for (int i = 1; i <= 16; i++)
                {
                    double t = i / 16.0, mt = 1 - t;
                    double x = mt * mt * mt * ax + 3 * mt * mt * t * ax + 3 * mt * t * t * bx + t * t * t * bx;
                    double y = mt * mt * mt * ay + 3 * mt * mt * t * ay + 3 * mt * t * t * by + t * t * t * by;
                    double z = mt * mt * mt * PinZ(a) + 3 * mt * mt * t * topz + 3 * mt * t * t * topz + t * t * t * PinZ(b);
                    pts.Add(Project(x, y, z));
                }
                g.PolyLineTo(pts, true, false);
            }
            geo.Freeze();
            var pen = PenOf(w.Color, 2.6, alpha);
            if (!activeSide) pen.DashStyle = DashStyles.Dash;
            dc.DrawGeometry(null, pen, geo);
        }
        // наплывы припоя по концам
        foreach (var (wx, wy) in new[] { (ax, ay), (bx, by) })
        {
            var s = Project(wx, wy, 0.35);
            if (w.Flux) dc.DrawEllipse(Brush("#d9a441", (byte)(activeSide ? 55 : 22)), null, s, 8.5, 6);
            if (w.Quality == "hot") dc.DrawEllipse(Brush("#3e2723", (byte)(activeSide ? 120 : 45)), null, s, 7, 5);
            string col = w.Quality switch { "cold" => "#878e96", "hot" => "#6e6259", _ => "#dde5ea" };
            dc.DrawEllipse(Brush(col, alpha), PenOf("#5f6b73", 0.8, alpha), s, 5.6, 4.6);
            if (w.Quality == "cold" && activeSide)
            {
                dc.DrawLine(PenOf("#37474f", 1.2), new Point(s.X - 3.6, s.Y - 1.8), new Point(s.X + 2.8, s.Y + 2.8));
                dc.DrawLine(PenOf("#37474f", 1.2), new Point(s.X + 3.6, s.Y - 2.6), new Point(s.X - 2.6, s.Y + 0.8));
            }
            else if (w.Quality == "good" && activeSide)
            {
                dc.DrawEllipse(Brush("#ffffff", 230), null, new Point(s.X - 1.8, s.Y - 2.0), 1.7, 1.4);
            }
        }
    }

    private void DrawPolyline(DrawingContext dc, Point[] pts, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], false, false);
            g.PolyLineTo(pts.Skip(1).ToArray(), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private void DrawLabels(DrawingContext dc, CircuitResult state, bool bottom)
    {
        // экраны — текст нужен всегда, но мелкий
        foreach (var c in Data.Components.Where(c => c.Type is "oled" or "tft" or "tft18" or "lcd1602"))
        {
            if (bottom) continue;
            string txt = c.Prop("displayText");
            if (txt.Length == 0) continue;
            var (ew, eh) = c.EffSize;
            var ctr = Project(c.X + ew / 2.0, c.Y + eh / 2.0, 1.0);
            Text(dc, txt.Length > 18 ? txt[..18] + "…" : txt, ctr, 7.5, c.Type == "lcd1602" ? "#1b5e20" : "#80deea", bold: true);
        }
        // подпись только у выбранной детали (маленькая)
        if (Selected != null && !bottom)
        {
            var c = Selected;
            var (ew, eh) = c.EffSize;
            double z = c.Def.HeightMm / 2.54 + 0.8;
            var at = Project(c.X + ew / 2.0, c.Y + eh / 2.0, z);
            string label = c.Type == "resistor" ? $"R {c.Prop("ohms", "220")}Ω" : c.Def.Short;
            Text(dc, label, at, 7.5, "#e2e8f0");
            var poly = new[]
            {
                Project(c.X, c.Y, z - 0.3), Project(c.X + ew, c.Y, z - 0.3),
                Project(c.X + ew, c.Y + eh, z - 0.3), Project(c.X, c.Y + eh, z - 0.3)
            };
            Poly(dc, poly, null, PenOf("#ffff00", 1.6));
        }
    }

    private void DrawMeasurements(DrawingContext dc)
    {
        foreach (var m in Data.Measurements)
        {
            var a = Project(m.AX, m.AY, 0.25);
            var b = Project(m.BX, m.BY, 0.25);
            dc.DrawLine(PenOf("#facc15", 1.8), a, b);
            // засечки по отверстиям вдоль линии
            double lenHoles = Math.Sqrt((m.BX - m.AX) * (m.BX - m.AX) + (m.BY - m.AY) * (m.BY - m.AY));
            int steps = (int)Math.Floor(lenHoles);
            for (int i = 1; i < steps; i++)
            {
                double t = i / lenHoles;
                var p = Project(m.AX + (m.BX - m.AX) * t, m.AY + (m.BY - m.AY) * t, 0.25);
                dc.DrawLine(PenOf("#facc15", 1.2), new Point(p.X, p.Y - 3.5), new Point(p.X, p.Y + 3.5));
            }
            foreach (var p in new[] { a, b })
            {
                dc.DrawLine(PenOf("#facc15", 1.8), new Point(p.X - 5, p.Y), new Point(p.X + 5, p.Y));
                dc.DrawLine(PenOf("#facc15", 1.8), new Point(p.X, p.Y - 5), new Point(p.X, p.Y + 5));
            }
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 10);
            string label = $"{m.DistanceMm:F1} мм";
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 8, Brush("#111827"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRoundedRectangle(Brush("#facc15"), null,
                new Rect(mid.X - ft.Width / 2 - 5, mid.Y - ft.Height / 2 - 2.5, ft.Width + 10, ft.Height + 5), 3, 3);
            dc.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
        }
        if (_measureStart is { } ms && _hoverBoard is { } hb)
        {
            var a = Project(ms.Item1, ms.Item2, 0.25);
            var b = Project(hb.Item1, hb.Item2, 0.25);
            var dashPen = PenOf("#facc15", 1.4);
            dashPen.DashStyle = DashStyles.Dash;
            dc.DrawLine(dashPen, a, b);
            var tmp = new Measurement { AX = ms.Item1, AY = ms.Item2, BX = hb.Item1, BY = hb.Item2 };
            Text(dc, $"{tmp.DistanceMm:F1} мм", new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 10), 8, "#facc15", bold: true);
            dc.DrawEllipse(null, PenOf("#facc15", 2), a, 5, 5);
        }
    }

    // ---------------- hit-testing ----------------
    private FreePoint ClampToBoard((double, double) w) =>
        new(Math.Clamp(w.Item1, 0, Data.Cols), Math.Clamp(w.Item2, 0, Data.Rows));

    private FreePoint PreviewEnd()
    {
        var last = _chain[^1];
        double dx = _chainCursor.X - last.X, dy = _chainCursor.Y - last.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= LinkLengthCells || len < 1e-9) return _chainCursor;
        double k = LinkLengthCells / len;
        return new FreePoint(last.X + dx * k, last.Y + dy * k);
    }

    private void FinishChain()
    {
        _chainActive = false;
        double len = 0;
        for (int i = 0; i + 1 < _chain.Count; i++)
        {
            double dx = _chain[i + 1].X - _chain[i].X;
            double dy = _chain[i + 1].Y - _chain[i].Y;
            len += Math.Sqrt(dx * dx + dy * dy);
        }
        if (_chain.Count >= 2 && len >= 0.4)
        {
            bool solder = Mode == "Solder";
            var fw = new FreeWire
            {
                Points = new List<FreePoint>(_chain),
                Style = solder ? "solder" : "wire",
                Color = solder ? "#90a4ae" : WireColors[Data.FreeWires.Count % WireColors.Length],
                Side = WireSide,
                Quality = solder ? SolderQuality() : "good",
                TempC = SolderTempC,
                Flux = UseFlux
            };
            Data.FreeWires.Add(fw);
            if (solder)
            {
                string verdict = fw.Quality switch
                {
                    "cold" => "🥶 ХОЛОДНАЯ пайка — перепаяйте при 320–380°C с флюсом",
                    "hot" => "🔥 ПЕРЕГРЕВ — поджог платы, перепаяйте при 320–380°C",
                    _ => "✨ блестящие наплывы припоя"
                };
                LogMessage?.Invoke($"Припаяно при {SolderTempC}°C [{WireSide}]: линия из {fw.Points.Count} звеньев. {verdict}");
            }
            else
            {
                LogMessage?.Invoke($"✒ Провод готов [{WireSide}]: {fw.Points.Count} звеньев, длина звена {LinkLengthCells:F1} кл.");
            }
            StateChanged?.Invoke();
        }
        else
        {
            LogMessage?.Invoke("✒ Линия отменена (нужно минимум 2 звена)");
        }
        _chain.Clear();
    }

    private FreeWire HitFreeWire(Point s)
    {
        foreach (var fw in Data.FreeWires.AsEnumerable().Reverse())
        {
            if (fw.Points.Count < 2) continue;
            var pts = fw.Points.Select(p => Project(p.X, p.Y, 0.4)).ToArray();
            for (int i = 0; i + 1 < pts.Length; i++)
                if (PointSegmentDistance(s, pts[i], pts[i + 1]) < 10) return fw;
        }
        return null;
    }

    private static double PointSegmentDistance(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }

    private PlacedComponent HitComponent(Point s)
    {
        foreach (var c in Data.Components.AsEnumerable().Reverse())
        {
            var (ew, eh) = c.EffSize;
            double z = c.Def.HeightMm / 2.54;
            var poly = new[]
            {
                Project(c.X, c.Y, z), Project(c.X + ew, c.Y, z),
                Project(c.X + ew, c.Y + eh, z), Project(c.X, c.Y + eh, z)
            };
            if (PointInPoly(s, poly)) return c;
        }
        return null;
    }

    private static bool PointInPoly(Point p, Point[] poly)
    {
        bool inside = false;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            var (xi, yi) = (poly[i].X, poly[i].Y);
            var (xj, yj) = (poly[j].X, poly[j].Y);
            if ((yi > p.Y) != (yj > p.Y) && p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi) inside = !inside;
            j = i;
        }
        return inside;
    }

    private (double, double) SnapToHole(Point s)
    {
        var (wx, wy) = Unproject(s);
        double sx = Math.Clamp(Math.Floor(wx) + 0.5, 0.5, Data.Cols - 0.5);
        double sy = Math.Clamp(Math.Floor(wy) + 0.5, 0.5, Data.Rows - 0.5);
        return (sx, sy);
    }

    // ---------------- drag&drop из меню ----------------
    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.Text) is string typeId && Catalog.All.ContainsKey(typeId))
        {
            var pos = e.GetPosition(this);
            var (wx, wy) = Unproject(pos);
            var def = Catalog.ById(typeId);
            var c = Data.AddAt(typeId, (int)Math.Round(wx - def.W / 2.0), (int)Math.Round(wy - def.H / 2.0));
            Selected = c;
            Mode = "Move";
            LogMessage?.Invoke($"🧩 Перетащено из меню: {def.Title} → плата ({c.X},{c.Y})");
            StateChanged?.Invoke();
            InvalidateVisual();
        }
        e.Handled = true;
    }

    // ---------------- мышь ----------------
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        _lastMouse = pos;
        CaptureMouse();

        if (Mode == "Measure")
        {
            var snap = SnapToHole(pos);
            if (_measureStart == null)
            {
                _measureStart = snap;
                LogMessage?.Invoke("📏 Замер: первая точка поставлена — кликните вторую.");
            }
            else
            {
                var m = new Measurement { AX = _measureStart.Value.Item1, AY = _measureStart.Value.Item2, BX = snap.Item1, BY = snap.Item2 };
                Data.Measurements.Add(m);
                LogMessage?.Invoke($"📏 Замер: {m}");
                _measureStart = null;
                StateChanged?.Invoke();
            }
            InvalidateVisual();
            return;
        }
        if (Mode is "Wire" or "Solder")
        {
            // Укладка по звеньям: ЛКМ ставит звено, ПКМ завершает. Без снапа.
            var pt = ClampToBoard(Unproject(pos));
            _chainCursor = pt;
            if (!_chainActive)
            {
                _chain.Clear();
                _chain.Add(pt);
                _chainActive = true;
                LogMessage?.Invoke("✒ Начало линии: ЛКМ — поставить звено, колесо — длина звена, ПКМ — завершить");
            }
            else
            {
                var endPt = PreviewEnd();
                var last = _chain[^1];
                double d = Math.Abs(endPt.X - last.X) + Math.Abs(endPt.Y - last.Y);
                if (d >= 0.12) _chain.Add(endPt);
            }
            InvalidateVisual();
            return;
        }
        var hc = HitComponent(pos);
        switch (Mode)
        {
            case "Camera":
                _rotatingCam = true;
                break;
            case "MoveBoard":
                _panning = true;
                break;
            case "Delete":
            {
                var fwHit = HitFreeWire(pos);
                if (fwHit != null)
                {
                    Data.FreeWires.Remove(fwHit);
                    LogMessage?.Invoke("🗑 Линия удалена");
                    StateChanged?.Invoke();
                }
                else if (hc != null)
                {
                    Data.Components.Remove(hc);
                    Data.Wires.RemoveAll(w => w.AId == hc.Id || w.BId == hc.Id);
                    if (Selected == hc) Selected = null;
                    StateChanged?.Invoke();
                }
                break;
            }
            case "Sim":
                if (hc != null)
                {
                    if (hc.Type == "button")
                    {
                        if (!Data.Pressed.Add(hc.Id)) Data.Pressed.Remove(hc.Id);
                        LogMessage?.Invoke(Data.Pressed.Contains(hc.Id) ? "🔘 Кнопка НАЖАТА" : "🔘 Кнопка отпущена");
                    }
                    else if (hc.Type == "pot")
                    {
                        int v = (int.Parse(hc.Prop("value", "512")) + 128) % 1024;
                        hc.Props["value"] = v.ToString();
                        LogMessage?.Invoke($"🎛 Потенциометр: {v}/1023");
                    }
                    else Selected = hc;
                    StateChanged?.Invoke();
                }
                break;
            case "Move":
                if (hc != null)
                {
                    Selected = hc;
                    var (wx, wy) = Unproject(pos);
                    _dragComp = hc;
                    _dragOffset = (wx - hc.X, wy - hc.Y);
                    StateChanged?.Invoke();
                }
                else
                {
                    Selected = null;
                    StateChanged?.Invoke();
                }
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        _hoverBoard = Mode == "Measure" ? SnapToHole(pos) : null;

        _chainCursor = ClampToBoard(Unproject(pos));
        if (_chainActive)
        {
            InvalidateVisual();
        }
        else if (_rotatingCam && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
        {
            // Вращение «по захвату»: модель следует ЗА курсором.
            CameraAngle -= (pos.X - _lastMouse.X) * 0.35;
            while (CameraAngle > 180) CameraAngle -= 360;
            while (CameraAngle < -180) CameraAngle += 360;
            Tilt = Math.Clamp(Tilt + (pos.Y - _lastMouse.Y) * 0.003, 0.08, 1.30);
            InvalidateVisual();
        }
        else if (_panning && e.LeftButton == MouseButtonState.Pressed)
        {
            Pan = new Point(Pan.X + pos.X - _lastMouse.X, Pan.Y + pos.Y - _lastMouse.Y);
            InvalidateVisual();
        }
        else if (_dragComp != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var (wx, wy) = Unproject(pos);
            var (ew, eh) = _dragComp.EffSize;
            _dragComp.X = Math.Clamp((int)Math.Round(wx - _dragOffset.Item1), 0, Data.Cols - ew);
            _dragComp.Y = Math.Clamp((int)Math.Round(wy - _dragOffset.Item2), 0, Data.Rows - eh);
            InvalidateVisual();
        }
        else if (Mode == "Measure" && _measureStart != null)
        {
            InvalidateVisual();
        }
        _lastMouse = pos;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        bool wasDragging = _dragComp != null;
        _dragComp = null;
        _panning = false;
        _rotatingCam = false;
        ReleaseMouseCapture();
        if (wasDragging) StateChanged?.Invoke();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        // правый клик: отменить текущее действие
        if (_chainActive) { FinishChain(); InvalidateVisual(); return; }
        if (_measureStart != null) { _measureStart = null; LogMessage?.Invoke("📏 Замер отменён"); }
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        // Вращение камеры — зажатым колёсиком (средней кнопкой), в любом режиме.
        if (e.ChangedButton == MouseButton.Middle)
        {
            _rotatingCam = true;
            _lastMouse = e.GetPosition(this);
            CaptureMouse();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            _rotatingCam = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Mode is "Wire" or "Solder")
        {
            // Во время укладки колесо регулирует дальность следующего звена.
            LinkLengthCells = Math.Clamp(LinkLengthCells + (e.Delta > 0 ? 0.25 : -0.25), 0.5, 10.0);
            StateChanged?.Invoke();
        }
        else
        {
            Zoom = Math.Clamp(Zoom + (e.Delta > 0 ? 0.08 : -0.08), 0.55, 2.4);
        }
        InvalidateVisual();
    }
}
