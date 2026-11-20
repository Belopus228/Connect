using System.IO;
using System.Text.Json;

namespace ConnectPC;

public sealed class PinDef
{
    public string Name { get; set; } = "";
    public int Dx { get; set; }
    public int Dy { get; set; }
    public PinDef() { }
    public PinDef(string n, int x, int y) { Name = n; Dx = x; Dy = y; }
}

public sealed class CompDef
{
    public string Id = "", Title = "", Short = "";
    public int W, H;
    public double HeightMm;
    public string Color = "#334155";
    public List<PinDef> Pins = new();
    public Dictionary<string, string> DefaultProps = new();
}

public static class Catalog
{
    public static readonly Dictionary<string, CompDef> All = new();
    private static void Add(string id, string title, string shortName, int w, int h, double hm, string color,
        (string, int, int)[] pins, Dictionary<string, string> props = null)
    {
        All[id] = new CompDef
        {
            Id = id, Title = title, Short = shortName, W = w, H = h, HeightMm = hm, Color = color,
            Pins = pins.Select(p => new PinDef(p.Item1, p.Item2, p.Item3)).ToList(),
            DefaultProps = props ?? new Dictionary<string, string>()
        };
    }

    static Catalog()
    {
        // Настоящий DOIT ESP32 DevKit V1: 30 контактов, 15 + 15, шаг 2.54 мм
        Add("esp32", "ESP32 DevKit V1 (30 pin)", "ESP32", 11, 20, 12, "#1a237e",
            new[] { ("EN",0,1),("VP",0,2),("VN",0,3),("D34",0,4),("D35",0,5),("D32",0,6),("D33",0,7),("D25",0,8),("D26",0,9),("D27",0,10),("D14",0,11),("D12",0,12),("D13",0,13),("GND",0,14),("VIN",0,15),
                    ("D23",10,1),("D22",10,2),("D21",10,3),("D19",10,4),("D18",10,5),("D5",10,6),("D17",10,7),("D16",10,8),("D4",10,9),("D2",10,10),("D15",10,11),("GND2",10,12),("3V3",10,13),("TX",10,14),("RX",10,15) });
        Add("oled", "OLED SSD1306 128x64", "OLED", 12, 7, 10, "#102027",
            new[] { ("VCC",2,0),("GND",4,0),("SCL",7,0),("SDA",9,0) },
            new() { ["displayText"] = "Connect OS" });
        Add("tft18", "TFT 1.8\" ST7735 128x160", "TFT1.8", 13, 20, 11, "#b71c1c",
            new[] { ("VCC",1,19),("GND",3,19),("SCK",5,19),("SDA",6,19),("RES",8,19),("DC",9,19),("CS",11,19),("BL",12,19) },
            new() { ["displayText"] = "TFT 1.8 128x160" });
        Add("tft", "TFT ILI9341 2.4\"", "TFT2.4", 12, 16, 11, "#4a148c",
            new[] { ("VCC",0,1),("GND",0,3),("CS",0,5),("RST",0,7),("DC",0,9),("MOSI",0,11),("SCK",0,13),("LED",0,15) },
            new() { ["displayText"] = "TFT 320x240" });
        Add("lcd1602", "LCD 1602", "LCD", 18, 6, 12, "#1b5e20",
            new[] { ("VSS",1,0),("VDD",3,0),("RS",5,0),("E",7,0),("D4",9,0),("D5",11,0),("D6",13,0),("D7",15,0) },
            new() { ["displayText"] = "LCD 1602 ready" });
        Add("nrf24", "nRF24L01", "nRF24", 7, 7, 14, "#0d47a1",
            new[] { ("VCC",1,0),("GND",3,0),("CE",5,0),("CSN",5,6),("SCK",3,6),("MOSI",1,6),("MISO",5,3) },
            new() { ["channel"] = "76", ["lastPacket"] = "—" });
        Add("led", "Светодиод", "LED", 3, 4, 9, "#b71c1c",
            new[] { ("A",0,3),("K",2,3) }, new() { ["colorName"] = "Красный" });
        Add("resistor", "Резистор", "R", 6, 2, 5, "#d7a86e",
            new[] { ("1",0,0),("2",5,0) }, new() { ["ohms"] = "220" });
        Add("button", "Тактовая кнопка", "BTN", 5, 5, 7, "#37474f",
            new[] { ("1",0,0),("2",4,0),("3",0,4),("4",4,4) });
        Add("bat18650", "Батарея 18650 3.7В", "18650", 9, 4, 19, "#2e7d32",
            new[] { ("+",8,1),("-",0,1) }, new() { ["voltage"] = "3.7" });
        Add("bat9v", "Батарея 9В Крона", "9V", 5, 7, 16, "#212121",
            new[] { ("+",1,0),("-",3,0) }, new() { ["voltage"] = "9" });
        Add("lipo", "Li-Po 3.7В", "LiPo", 8, 5, 6, "#f9a825",
            new[] { ("+",7,2),("-",0,2) }, new() { ["voltage"] = "3.7" });
        Add("lipo1000", "Li-Po 1000 мАч 3.7В", "LiPo·1000", 8, 5, 6, "#f9a825",
            new[] { ("+",7,2),("-",0,2) }, new() { ["voltage"] = "3.7", ["mah"] = "1000" });
        Add("lipo1800", "Li-Po 1800 мАч 3.7В", "LiPo·1800", 10, 6, 7, "#fb8c00",
            new[] { ("+",9,2),("-",0,2) }, new() { ["voltage"] = "3.7", ["mah"] = "1800" });
        Add("buzzer", "Пьезо-зуммер", "BUZ", 4, 4, 9, "#455a64", new[] { ("+",0,3),("-",3,3) });
        Add("dht22", "Датчик DHT22", "DHT22", 7, 5, 12, "#0277bd",
            new[] { ("VCC",0,2),("DATA",2,2),("GND",6,2) }, new() { ["temp"] = "24.5", ["hum"] = "48" });
        Add("pot", "Потенциометр 10k", "POT", 6, 6, 12, "#6a1b9a",
            new[] { ("1",0,5),("SIG",3,5),("2",5,5) }, new() { ["value"] = "512" });
        Add("servo", "Сервопривод SG90", "SERVO", 6, 9, 22, "#ef6c00",
            new[] { ("VCC",1,8),("SIG",3,8),("GND",5,8) }, new() { ["angle"] = "90" });
        Add("relay", "Реле 5В", "RELAY", 9, 7, 16, "#1565c0",
            new[] { ("VCC",0,1),("GND",0,3),("IN",0,5),("COM",8,1),("NO",8,3),("NC",8,5) });
        Add("cap", "Конденсатор 100uF", "C", 3, 4, 10, "#283593", new[] { ("+",0,3),("-",2,3) });
    }

    public static CompDef ById(string id) => All.TryGetValue(id, out var d) ? d : All["esp32"];
}

public sealed class PlacedComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "led";
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Поворот вокруг вертикальной оси Z (yaw), градусы, любой угол.</summary>
    public int Rot { get; set; }
    /// <summary>Высота детали над платой в клетках (ось Z, движение вверх/вниз).</summary>
    public double Z { get; set; }
    /// <summary>Наклон вокруг оси X, градусы.</summary>
    public double RotX { get; set; }
    /// <summary>Наклон вокруг оси Y, градусы.</summary>
    public double RotY { get; set; }
    public Dictionary<string, string> Props { get; set; } = new();
    public CompDef Def => Catalog.ById(Type);

    /// <summary>Габариты проекции детали на плату при текущем повороте вокруг Z.</summary>
    public (double W, double H) Footprint
    {
        get
        {
            double a = Rot * Math.PI / 180.0;
            double co = Math.Abs(Math.Cos(a)), si = Math.Abs(Math.Sin(a));
            return (Def.W * co + Def.H * si, Def.W * si + Def.H * co);
        }
    }
    public (int, int) EffSize => (Math.Max(1, (int)Math.Ceiling(Footprint.W - 1e-6)),
                                  Math.Max(1, (int)Math.Ceiling(Footprint.H - 1e-6)));

    /// <summary>
    /// Полное 3D-преобразование локальной точки детали в мировые координаты платы:
    /// наклон X -> наклон Y -> поворот Z -> перенос (X, Y, Z). Вращение — вокруг
    /// центра объёма детали, поэтому деталь не проваливается под плату при наклоне.
    /// </summary>
    public (double X, double Y, double Z) Transform(double lx, double ly, double lz)
    {
        double zt = Def.HeightMm / 2.54;
        double px = lx - Def.W / 2.0, py = ly - Def.H / 2.0, pz = lz - zt / 2.0;
        double ax = RotX * Math.PI / 180.0, ay = RotY * Math.PI / 180.0, az = Rot * Math.PI / 180.0;
        (py, pz) = (py * Math.Cos(ax) - pz * Math.Sin(ax), py * Math.Sin(ax) + pz * Math.Cos(ax));
        (px, pz) = (px * Math.Cos(ay) + pz * Math.Sin(ay), -px * Math.Sin(ay) + pz * Math.Cos(ay));
        (px, py) = (px * Math.Cos(az) - py * Math.Sin(az), px * Math.Sin(az) + py * Math.Cos(az));
        var (fw, fh) = Footprint;
        return (X + fw / 2.0 + px, Y + fh / 2.0 + py, Z + zt / 2.0 + pz);
    }

    /// <summary>Мировые координаты контакта на плате (XY).</summary>
    public (double, double) PinPos(PinDef p)
    {
        var t = Transform(p.Dx + 0.5, p.Dy + 0.5, 0);
        return (t.X, t.Y);
    }
    /// <summary>Мировые координаты контакта в 3D (с высотой).</summary>
    public (double X, double Y, double Z) PinPos3(PinDef p) => Transform(p.Dx + 0.5, p.Dy + 0.5, 0);
    public string Prop(string k, string def = "") => Props.TryGetValue(k, out var v) ? v : def;
}

public sealed class WireConn
{
    public string AId { get; set; } = "";
    public string APin { get; set; } = "";
    public string BId { get; set; } = "";
    public string BPin { get; set; } = "";
    public string Style { get; set; } = "wire"; // wire | solder
    public string Color { get; set; } = "#d32f2f";
    public string Quality { get; set; } = "good"; // good | cold | hot
    public int TempC { get; set; } = 350;
    public bool Flux { get; set; } = true;
    public string Side { get; set; } = "Top"; // Top | Bottom — сторона платы
}

/// <summary>Точка свободной линии на плате (координаты в отверстиях, НЕ привязана к пину).</summary>
public sealed class FreePoint
{
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Высота звена над платой в клетках (регулируется колесом мыши).</summary>
    public double Z { get; set; }
    public FreePoint() { }
    public FreePoint(double x, double y) { X = x; Y = y; }
}

/// <summary>
/// Свободно проложенный провод/дорожка: пользователь сам ведёт линию мышью.
/// Никакого автоснапа к пинам: путь хранится ровно как нарисован.
/// </summary>
public sealed class FreeWire
{
    public List<FreePoint> Points { get; set; } = new();
    public string Style { get; set; } = "wire"; // wire | solder
    public string Color { get; set; } = "#d32f2f";
    public double Thickness { get; set; } = 1.0;
    public string Side { get; set; } = "Top";   // Top | Bottom
    public string Quality { get; set; } = "good";
    public int TempC { get; set; } = 350;
    public bool Flux { get; set; } = true;
}

public sealed class Measurement
{
    public double AX { get; set; }
    public double AY { get; set; }
    public double BX { get; set; }
    public double BY { get; set; }
    public double DistanceMm => Math.Sqrt((BX - AX) * (BX - AX) + (BY - AY) * (BY - AY)) * 2.54;
    public double DxMm => (BX - AX) * 2.54;
    public double DyMm => (BY - AY) * 2.54;
    public double AngleDeg => Math.Atan2(-(BY - AY), BX - AX) * 180.0 / Math.PI;
    public override string ToString() =>
        $"{DistanceMm:F1} мм  (ΔX {DxMm:+0.0;-0.0} мм, ΔY {DyMm:+0.0;-0.0} мм, ∠{AngleDeg:F0}°)";
}

public sealed class ProjectData
{
    public List<PlacedComponent> Components { get; set; } = new();
    public List<WireConn> Wires { get; set; } = new();
    public List<FreeWire> FreeWires { get; set; } = new();
    public List<Measurement> Measurements { get; set; } = new();
    /// <summary>Корпус платы: None | Acrylic | Black | BlueMetal.</summary>
    public string CaseType { get; set; } = "None";
    /// <summary>Отступ корпуса от платы в клетках.</summary>
    public double CaseMargin { get; set; } = 1.2;
    /// <summary>Высота стенок корпуса в клетках.</summary>
    public double CaseHeight { get; set; } = 4.6;
    /// <summary>Макетная плата необязательна: можно строить схему прямо в пространстве.</summary>
    public bool ShowBoard { get; set; } = true;
    /// <summary>Импортированный STL-корпус: путь к файлу (модель не копируется в проект).</summary>
    public string CaseStlPath { get; set; } = "";
    public string CaseStlName { get; set; } = "";
    /// <summary>Масштаб STL-модели (1 = как есть, единицы файла — миллиметры).</summary>
    public double CaseStlScale { get; set; } = 1.0;
    /// <summary>Сдвиги STL-корпуса в клетках платы.</summary>
    public double CaseStlX { get; set; }
    public double CaseStlY { get; set; }
    public double CaseStlZ { get; set; }
    public string Code { get; set; } = FirmwareExamples.All[0].Code;
    // runtime (не сериализуются целиком, но Json не повредит)
    public Dictionary<string, int> PinOutputs { get; set; } = new();
    public Dictionary<string, string> PinModes { get; set; } = new();
    public HashSet<string> Pressed { get; set; } = new();
    public bool Running { get; set; }
    public int Cols { get; set; } = 34;
    public int Rows { get; set; } = 22;

    public PlacedComponent ById(string id) => Components.FirstOrDefault(c => c.Id == id);

    public string PinMode(PlacedComponent c, string pin) =>
        PinModes.TryGetValue(c.Id + ":" + pin, out var m) ? m : "INPUT";
    public int PinOutput(PlacedComponent c, string pin) =>
        PinOutputs.TryGetValue(c.Id + ":" + pin, out var v) ? v : 0;

    public List<object> WireRows()
    {
        var rows = Wires.Select(w => (object)new
        {
            A = $"{ById(w.AId)?.Def.Title ?? "?"}.{w.APin}",
            B = $"{ById(w.BId)?.Def.Title ?? "?"}.{w.BPin}",
            Style = w.Style == "solder" ? "пайка" : "провод",
            Side = w.Side,
            Quality = w.Quality switch { "cold" => "холодная 🥶", "hot" => "перегрев 🔥", _ => "хорошая ✨" }
        }).ToList();
        rows.AddRange(FreeWires.Select(f => (object)new
        {
            A = "Свободная линия",
            B = $"{f.Points.Count} точек",
            Style = f.Style == "solder" ? "пайка (ручная)" : "провод (ручной)",
            Side = f.Side,
            Quality = f.Quality switch { "cold" => "холодная 🥶", "hot" => "перегрев 🔥", _ => "хорошая ✨" }
        }));
        return rows;
    }

    public List<object> MeasurementRows() => Measurements.Select(m => (object)new
    {
        DistanceMm = $"{m.DistanceMm:F1}",
        DXmm = $"{m.DxMm:+0.0;-0.0}",
        DYmm = $"{m.DyMm:+0.0;-0.0}",
        AngleDeg = $"{m.AngleDeg:F0}"
    }).ToList();
    public PlacedComponent AddAt(string type, int gx, int gy)
    {
        var def = Catalog.ById(type);
        var c = new PlacedComponent
        {
            Type = type,
            X = Math.Clamp(gx, 0, Math.Max(0, Cols - def.W)),
            Y = Math.Clamp(gy, 0, Math.Max(0, Rows - def.H)),
            Props = new Dictionary<string, string>(def.DefaultProps)
        };
        Components.Add(c);
        return c;
    }
    public PlacedComponent AddFree(string type)
    {
        var def = Catalog.ById(type);
        for (int y = 1; y < Rows - def.H; y++)
            for (int x = 1; x < Cols - def.W; x++)
            {
                bool clash = Components.Any(c =>
                {
                    var (ew, eh) = c.EffSize;
                    return x < c.X + ew && x + def.W > c.X && y < c.Y + eh && y + def.H > c.Y;
                });
                if (!clash) return AddAt(type, x, y);
            }
        return AddAt(type, 2, 2);
    }

    public static ProjectData CreateDemo()
    {
        var p = new ProjectData();
        PlacedComponent Add(string t, int x, int y) { var c = p.AddAt(t, x, y); return c; }
        var bat = Add("bat18650", 1, 1);
        var esp = Add("esp32", 20, 1);
        var res = Add("resistor", 8, 12); res.Props["ohms"] = "220";
        var led = Add("led", 8, 16);
        var btn = Add("button", 14, 14);
        var oled = Add("oled", 2, 6);
        var nrf = Add("nrf24", 27, 15);
        void Wire(PlacedComponent a, string ap, PlacedComponent b, string bp, string col, string style = "wire", string side = "Top")
        {
            // Демо тоже рисуется свободными линиями: путь ровно такой, как проведён.
            var pa = a.Def.Pins.First(x => x.Name == ap);
            var pb = b.Def.Pins.First(x => x.Name == bp);
            var (ax, ay) = (a.X + pa.Dx + 0.5, a.Y + pa.Dy + 0.5);
            var (bx, by) = (b.X + pb.Dx + 0.5, b.Y + pb.Dy + 0.5);
            var pts = new List<FreePoint> { new(ax, ay), new((ax + bx) / 2.0, ay), new((ax + bx) / 2.0, by), new(bx, by) };
            p.FreeWires.Add(new FreeWire
            {
                Points = pts, Color = col, Style = style, Side = side,
                Quality = "good", TempC = 350, Flux = true
            });
        }
        Wire(bat, "+", esp, "VIN", "#d32f2f");
        Wire(bat, "-", esp, "GND", "#212121", "wire", "Bottom");
        Wire(esp, "D13", res, "1", "#ff6f00");
        Wire(res, "2", led, "A", "#ff6f00", "solder");
        Wire(led, "K", esp, "GND", "#212121");
        Wire(btn, "1", esp, "D32", "#1565c0");
        Wire(btn, "2", esp, "GND", "#212121", "wire", "Bottom");
        Wire(oled, "VCC", esp, "3V3", "#d32f2f");
        Wire(oled, "GND", esp, "GND", "#212121");
        Wire(oled, "SCL", esp, "D22", "#6a1b9a");
        Wire(oled, "SDA", esp, "D21", "#00838f");
        Wire(nrf, "VCC", esp, "3V3", "#d32f2f");
        Wire(nrf, "GND", esp, "GND", "#212121");
        return p;
    }

    public void Save(string path)
    {
        var dto = new ProjectData
        {
            Components = Components, Wires = Wires, FreeWires = FreeWires, Measurements = Measurements,
            Code = Code, CaseType = CaseType, CaseMargin = CaseMargin, CaseHeight = CaseHeight, ShowBoard = ShowBoard,
            CaseStlPath = CaseStlPath, CaseStlName = CaseStlName, CaseStlScale = CaseStlScale,
            CaseStlX = CaseStlX, CaseStlY = CaseStlY, CaseStlZ = CaseStlZ,
            Cols = Cols, Rows = Rows
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static ProjectData Load(string path)
    {
        var p = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path)) ?? CreateDemo();
        p.FreeWires ??= new();
        p.CaseType ??= "None";
        p.CaseStlPath ??= "";
        p.CaseStlName ??= "";
        if (p.CaseStlScale <= 0) p.CaseStlScale = 1.0;
        p.PinOutputs = new(); p.PinModes = new(); p.Pressed = new();
        return p;
    }
}

public sealed class LedState
{
    public double Brightness, CurrentMa;
    public bool Burnt;
}

public sealed class CircuitResult
{
    public HashSet<string> EspPowered = new();
    public Dictionary<string, LedState> Leds = new();
}

public static class Circuit
{
    private sealed class UF
    {
        private readonly Dictionary<string, string> _p = new();
        public string Find(string x)
        {
            if (!_p.TryGetValue(x, out var v)) { _p[x] = x; return x; }
            if (v == x) return x;
            return _p[x] = Find(v);
        }
        public void Union(string a, string b)
        {
            var ra = Find(a); var rb = Find(b);
            if (ra != rb) _p[ra] = rb;
        }
    }

    private static string Node(PlacedComponent c, string pin) => c.Id + "|" + pin;

    private static (double, double) PinCenter(PlacedComponent c, PinDef pin) => c.PinPos(pin);

    private static double PointSegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double len2 = dx * dx + dy * dy;
        double t = len2 < 1e-9 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        double cx = ax + t * dx, cy = ay + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    private static UF BuildUF(ProjectData p)
    {
        var uf = new UF();
        foreach (var c in p.Components)
            foreach (var pin in c.Def.Pins) uf.Find(Node(c, pin.Name));
        foreach (var w in p.Wires) uf.Union(w.AId + "|" + w.APin, w.BId + "|" + w.BPin);
        // Свободные линии: контакт только там, где линия реально прошла по площадке.
        foreach (var fw in p.FreeWires)
        {
            if (fw.Points.Count < 2) continue;
            var touched = new List<string>();
            foreach (var c in p.Components)
                foreach (var pin in c.Def.Pins)
                {
                    var (px, py) = PinCenter(c, pin);
                    for (int i = 0; i + 1 < fw.Points.Count; i++)
                    {
                        var a = fw.Points[i];
                        var b = fw.Points[i + 1];
                        if (PointSegmentDistance(px, py, a.X, a.Y, b.X, b.Y) <= 0.62)
                        {
                            touched.Add(Node(c, pin.Name));
                            break;
                        }
                    }
                }
            for (int i = 1; i < touched.Count; i++) uf.Union(touched[0], touched[i]);
        }
        foreach (var c in p.Components.Where(c => c.Type == "button" && p.Pressed.Contains(c.Id)))
        {
            uf.Union(Node(c, "1"), Node(c, "2"));
            uf.Union(Node(c, "3"), Node(c, "4"));
        }
        return uf;
    }

    public static CircuitResult Compute(ProjectData p)
    {
        var uf = BuildUF(p);
        var sources = new Dictionary<string, double>();
        var grounds = new HashSet<string>();
        foreach (var c in p.Components.Where(c => c.Type is "bat18650" or "bat9v" or "lipo" or "lipo1000" or "lipo1800"))
        {
            double v = double.TryParse(c.Prop("voltage", "3.7"), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var vv) ? vv : 3.7;
            sources[uf.Find(Node(c, "+"))] = v;
            grounds.Add(uf.Find(Node(c, "-")));
        }
        var result = new CircuitResult();
        foreach (var esp in p.Components.Where(c => c.Type == "esp32"))
        {
            var gnd = uf.Find(Node(esp, "GND"));
            double vin = sources.TryGetValue(uf.Find(Node(esp, "VIN")), out var a) ? a : 0;
            double v33 = sources.TryGetValue(uf.Find(Node(esp, "3V3")), out var b) ? b : 0;
            if (grounds.Contains(gnd) && (vin >= 3.3 || (v33 >= 3.0 && v33 <= 3.8)))
            {
                result.EspPowered.Add(esp.Id);
                sources[uf.Find(Node(esp, "3V3"))] = 3.3;
                if (p.Running)
                    foreach (var pin in esp.Def.Pins)
                        if (p.PinOutputs.TryGetValue(esp.Id + ":" + pin.Name, out var o) && o == 1)
                            sources[uf.Find(Node(esp, pin.Name))] = 3.3;
            }
        }
        var redges = new List<(string, string, double)>();
        foreach (var r in p.Components.Where(c => c.Type == "resistor"))
        {
            double ohms = double.TryParse(r.Prop("ohms", "220"), out var o) ? o : 220;
            redges.Add((uf.Find(Node(r, "1")), uf.Find(Node(r, "2")), ohms));
        }
        (string, double)? Through(string start, HashSet<string> targets)
        {
            if (targets.Contains(start)) return (start, 0);
            var frontier = new List<(string, double)> { (start, 0) };
            var seen = new Dictionary<string, double> { [start] = 0 };
            for (int depth = 0; depth < 3; depth++)
            {
                var next = new List<(string, double)>();
                foreach (var (root, acc) in frontier)
                    foreach (var (ea, eb, ohms) in redges)
                    {
                        string other = ea == root ? eb : (eb == root ? ea : null);
                        if (other == null) continue;
                        double na = acc + ohms;
                        if (targets.Contains(other)) return (other, na);
                        if (!seen.TryGetValue(other, out var sv) || sv > na) { seen[other] = na; next.Add((other, na)); }
                    }
                frontier = next;
            }
            return null;
        }
        var lowRoots = new HashSet<string>(grounds);
        if (p.Running)
            foreach (var esp in p.Components.Where(c => c.Type == "esp32" && result.EspPowered.Contains(c.Id)))
                foreach (var pin in esp.Def.Pins)
                    if (p.PinModes.TryGetValue(esp.Id + ":" + pin.Name, out var m) && m == "OUTPUT" &&
                        p.PinOutputs.TryGetValue(esp.Id + ":" + pin.Name, out var o) && o == 0)
                        lowRoots.Add(uf.Find(Node(esp, pin.Name)));
        foreach (var led in p.Components.Where(c => c.Type == "led"))
        {
            var st = new LedState();
            var sup = Through(uf.Find(Node(led, "A")), new HashSet<string>(sources.Keys));
            var gnd = Through(uf.Find(Node(led, "K")), lowRoots);
            if (sup != null && gnd != null)
            {
                double volts = sources[sup.Value.Item1];
                double totalR = sup.Value.Item2 + gnd.Value.Item2;
                if (volts > 2.0)
                {
                    if (totalR < 1.0) { st.CurrentMa = 999; st.Burnt = true; }
                    else
                    {
                        st.CurrentMa = (volts - 2.0) / totalR * 1000.0;
                        st.Brightness = Math.Clamp(st.CurrentMa / 20.0, 0.05, 1.0);
                        st.Burnt = st.CurrentMa > 60;
                    }
                }
            }
            result.Leds[led.Id] = st;
        }
        return result;
    }

    public static int DigitalRead(ProjectData p, string espId, string pinName)
    {
        var uf = BuildUF(p);
        var esp = p.ById(espId);
        if (esp == null) return 0;
        string root = uf.Find(espId + "|" + pinName);
        foreach (var c in p.Components)
        {
            if (c.Type is "bat18650" or "bat9v" or "lipo" or "lipo1000" or "lipo1800")
            {
                if (uf.Find(Node(c, "-")) == root) return 0;
                if (uf.Find(Node(c, "+")) == root) return 1;
            }
            if (c.Type == "esp32")
            {
                if (uf.Find(Node(c, "GND")) == root) return 0;
                if (uf.Find(Node(c, "3V3")) == root) return 1;
            }
        }
        return p.PinModes.TryGetValue(espId + ":" + pinName, out var m) && m == "INPUT_PULLUP" ? 1 : 0;
    }
}

public sealed record FirmwareExample(string Name, string Code);

public static class FirmwareExamples
{
    public static readonly List<FirmwareExample> All = new()
    {
        new("💡 Blink D13", "// Blink\npinMode(13, OUTPUT);\ndigitalWrite(13, HIGH);\ndelay(500);\ndigitalWrite(13, LOW);\ndelay(500);\n"),
        new("🔘 Кнопка D32 → LED", "// Кнопка на D32\npinMode(13, OUTPUT);\npinMode(32, INPUT_PULLUP);\nif (digitalRead(32) == LOW) {\n  digitalWrite(13, HIGH);\n  Serial.println(\"Кнопка нажата\");\n} else {\n  digitalWrite(13, LOW);\n}\ndelay(80);\n"),
        new("🖥 OLED", "// OLED\npinMode(13, OUTPUT);\noled.clear();\noled.print(\"Привет, Connect!\");\ndigitalWrite(13, HIGH);\ndelay(800);\noled.print(\"ESP32 на ПК :)\");\ndigitalWrite(13, LOW);\ndelay(800);\n"),
        new("📺 TFT 1.8 ST7735", "// TFT 1.8\npinMode(13, OUTPUT);\ntft.clear();\ntft.print(\"TFT 1.8 OK!\");\nSerial.println(\"TFT кадр\");\ndigitalWrite(13, HIGH);\ndelay(900);\ntft.print(\"Connect PC :)\");\ndigitalWrite(13, LOW);\ndelay(900);\n"),
        new("📡 nRF24", "// nRF24\npinMode(13, OUTPUT);\nnrf.send(\"PING от ESP32\");\nSerial.println(\"nRF отправлено\");\ndigitalWrite(13, HIGH);\ndelay(300);\ndigitalWrite(13, LOW);\ndelay(700);\n"),
    };
}
