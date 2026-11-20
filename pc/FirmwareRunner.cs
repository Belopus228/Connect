using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace ConnectPC;

public sealed class FirmwareRunner
{
    private sealed record Instr(string Kind, string A = "", string B = "", long N = 0);

    private readonly ProjectData _data;
    private List<Instr> _instrs = new();
    private readonly DispatcherTimer _timer = new();
    private int _pc;
    private readonly List<bool> _cond = new();

    /// <summary>Куда писать вывод прошивки (Serial, дисплеи, nRF).</summary>
    public Action<string> OnOutput { get; set; }
    /// <summary>Вызывается, когда прошивка изменила состояние платы.</summary>
    public event Action StateChanged;

    private void _log(string msg) => OnOutput?.Invoke(msg);
    private void _changed() => StateChanged?.Invoke();

    public FirmwareRunner(ProjectData data)
    {
        _data = data;
        _timer.Tick += (_, _) => Step();
    }

    private static List<Instr> Parse(string src)
    {
        var outp = new List<Instr>();
        foreach (var raw in src.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#include")) continue;
            if (line.StartsWith("} else") || line.StartsWith("else")) { outp.Add(new Instr("ELSE")); continue; }
            if (line is "}" or "};") { outp.Add(new Instr("ENDIF")); continue; }
            if (line == "{" || line.StartsWith("void ")) continue;
            line = line.TrimEnd(';').Trim();
            Match m;
            if ((m = Regex.Match(line, @"if\s*\(\s*digitalRead\s*\(\s*(\w+)\s*\)\s*==\s*(\w+)\s*\)")).Success)
                outp.Add(new Instr("IF", m.Groups[1].Value, m.Groups[2].Value));
            else if ((m = Regex.Match(line, @"pinMode\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)")).Success)
                outp.Add(new Instr("PINMODE", m.Groups[1].Value, m.Groups[2].Value));
            else if ((m = Regex.Match(line, @"digitalWrite\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)")).Success)
                outp.Add(new Instr("WRITE", m.Groups[1].Value, m.Groups[2].Value));
            else if ((m = Regex.Match(line, @"delay\s*\(\s*(\d+)\s*\)")).Success)
                outp.Add(new Instr("DELAY", N: long.Parse(m.Groups[1].Value)));
            else if ((m = Regex.Match(line, "Serial\\.println\\s*\\(\\s*\"([^\"]*)\"\\s*\\)")).Success)
                outp.Add(new Instr("SERIAL", m.Groups[1].Value));
            else if (Regex.IsMatch(line, @"tft\.clear\s*\(\s*\)"))
                outp.Add(new Instr("TFT_CLEAR"));
            else if ((m = Regex.Match(line, "tft\\.print(?:ln)?\\s*\\(\\s*\"([^\"]*)\"\\s*\\)")).Success)
                outp.Add(new Instr("TFT", m.Groups[1].Value));
            else if (Regex.IsMatch(line, @"(?:oled|display|lcd)\.clear\s*\(\s*\)"))
                outp.Add(new Instr("OLED_CLEAR"));
            else if ((m = Regex.Match(line, "(?:oled|display|lcd)\\.print(?:ln)?\\s*\\(\\s*\"([^\"]*)\"\\s*\\)")).Success)
                outp.Add(new Instr("OLED", m.Groups[1].Value));
            else if ((m = Regex.Match(line, "nrf\\.send\\s*\\(\\s*\"([^\"]*)\"\\s*\\)")).Success)
                outp.Add(new Instr("NRF", m.Groups[1].Value));
            else if ((m = Regex.Match(line, @"servo\.write\s*\(\s*(\d+)\s*\)")).Success)
                outp.Add(new Instr("SERVO", N: long.Parse(m.Groups[1].Value)));
        }
        return outp;
    }

    public void Start(string code)
    {
        _instrs = Parse(code ?? "");
        _pc = 0;
        _cond.Clear();
        _timer.Interval = TimeSpan.FromMilliseconds(60);
        _timer.Start();
        _log("🚀 Прошивка запущена на ESP32 (виртуально)");
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private static string PinName(string raw) => int.TryParse(raw, out _) ? "D" + raw : raw;
    private bool Active() => _cond.All(x => x);

    private void Step()
    {
        if (!_data.Running) return;
        var esp = _data.Components.FirstOrDefault(c => c.Type == "esp32");
        if (esp == null)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(900);
            return;
        }
        int guard = 0;
        while (guard++ < 300)
        {
            if (_pc >= _instrs.Count)
            {
                _pc = 0;
                _cond.Clear();
                break;
            }
            var ins = _instrs[_pc++];
            switch (ins.Kind)
            {
                case "IF":
                {
                    int v = Circuit.DigitalRead(_data, esp.Id, PinName(ins.A));
                    int expect = ins.B is "HIGH" or "1" ? 1 : 0;
                    _cond.Add(Active() && v == expect);
                    break;
                }
                case "ELSE":
                    if (_cond.Count > 0)
                    {
                        bool parent = _cond.Take(_cond.Count - 1).All(x => x);
                        _cond[^1] = parent && !_cond[^1];
                    }
                    break;
                case "ENDIF":
                    if (_cond.Count > 0) _cond.RemoveAt(_cond.Count - 1);
                    break;
                default:
                {
                    if (!Active()) break;
                    switch (ins.Kind)
                    {
                        case "PINMODE":
                            _data.PinModes[esp.Id + ":" + PinName(ins.A)] = ins.B;
                            break;
                        case "WRITE":
                        {
                            var key = esp.Id + ":" + PinName(ins.A);
                            int v = ins.B is "HIGH" or "1" ? 1 : 0;
                            if (!_data.PinOutputs.TryGetValue(key, out var old) || old != v)
                            {
                                _data.PinOutputs[key] = v;
                                _data.PinModes[key] = "OUTPUT";
                                _changed();
                            }
                            break;
                        }
                        case "DELAY":
                            _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(ins.N, 10, 5000));
                            return;
                        case "SERIAL":
                            _log("Serial: " + ins.A);
                            break;
                        case "OLED":
                            foreach (var c in _data.Components.Where(c => c.Type is "oled" or "tft" or "lcd1602"))
                                c.Props["displayText"] = ins.A;
                            _log("🖥 Дисплей: «" + ins.A + "»");
                            _changed();
                            break;
                        case "OLED_CLEAR":
                            foreach (var c in _data.Components.Where(c => c.Type is "oled" or "tft" or "lcd1602"))
                                c.Props["displayText"] = "";
                            _changed();
                            break;
                        case "TFT":
                        {
                            var tfts = _data.Components.Where(c => c.Type is "tft" or "tft18").ToList();
                            if (tfts.Count == 0) _log("📺 tft.print: TFT-дисплея нет на плате!");
                            foreach (var c in tfts) c.Props["displayText"] = ins.A;
                            if (tfts.Count > 0) _log("📺 TFT: «" + ins.A + "»");
                            _changed();
                            break;
                        }
                        case "TFT_CLEAR":
                            foreach (var c in _data.Components.Where(c => c.Type is "tft" or "tft18"))
                                c.Props["displayText"] = "";
                            _changed();
                            break;
                        case "NRF":
                        {
                            var nrfs = _data.Components.Where(c => c.Type == "nrf24").ToList();
                            if (nrfs.Count == 0) _log("📡 nrf.send: модуля nRF24 нет на плате!");
                            foreach (var c in nrfs)
                            {
                                c.Props["lastPacket"] = ins.A;
                                _log($"📡 nRF24 [канал {c.Prop("channel", "76")}] TX → «{ins.A}» доставлено");
                            }
                            break;
                        }
                        case "SERVO":
                            foreach (var c in _data.Components.Where(c => c.Type == "servo"))
                                c.Props["angle"] = ins.N.ToString();
                            _changed();
                            break;
                    }
                    break;
                }
            }
        }
        _timer.Interval = TimeSpan.FromMilliseconds(30);
    }
}
