# -*- coding: utf-8 -*-
"""
Connect 3D — ПК-версия симулятора электроники (Windows / Linux / macOS).
Запуск:  python ConnectPC.py
Сборка .exe (Windows):  build_windows.bat  (PyInstaller)

Возможности (как в Android-версии 1.4):
 - ландшафтная 3D (изометрическая) макетная плата, реалистичные модели деталей;
 - детали перетаскиваются мышью из правого сворачиваемого меню с вкладками;
 - реалистичная пайка: температура паяльника 200–480°C, флюс, качество
   (блестящая / холодная / перегрев) влияет на внешний вид соединений;
 - ESP32: живые выходы, прошивка (Blink / кнопка / OLED / TFT 1.8 / nRF24),
   дисплеи OLED и TFT 1.8" ST7735, nRF24L01, кнопки, LED с расчётом тока;
 - импорт STL-корпуса (границы и каркас поверх платы), сохранение проекта JSON.
"""
import json
import math
import os
import re
import struct
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

# ---------------------------------------------------------------- каталог
# id -> (название, ширина отв., высота отв., высота мм, цвет, пины [(имя, dx, dy)], свойства)
CATALOG = {
    "esp32": ("ESP32 DevKit V1", 11, 20, 12, "#1a237e",
              [("3V3", 0, 1), ("GND", 0, 3), ("D13", 0, 5), ("D12", 0, 7), ("D14", 0, 9),
               ("D27", 0, 11), ("D26", 0, 13), ("D25", 0, 15), ("D33", 0, 17), ("D32", 0, 19),
               ("VIN", 10, 1), ("GND2", 10, 3), ("D23", 10, 5), ("D22", 10, 7), ("D21", 10, 9),
               ("D19", 10, 11), ("D18", 10, 13), ("D5", 10, 15), ("TX", 10, 17), ("RX", 10, 19)], {}),
    "oled": ("OLED SSD1306 128x64", 12, 7, 10, "#102027",
             [("VCC", 2, 0), ("GND", 4, 0), ("SCL", 7, 0), ("SDA", 9, 0)], {"displayText": "Connect OS"}),
    "tft18": ("TFT 1.8\" ST7735 128x160", 13, 20, 11, "#b71c1c",
              [("VCC", 1, 19), ("GND", 3, 19), ("SCK", 5, 19), ("SDA", 6, 19),
               ("RES", 8, 19), ("DC", 9, 19), ("CS", 11, 19), ("BL", 12, 19)], {"displayText": "TFT 1.8 128x160"}),
    "tft": ("TFT ILI9341 2.4\"", 12, 16, 11, "#4a148c",
            [("VCC", 0, 1), ("GND", 0, 3), ("CS", 0, 5), ("RST", 0, 7), ("DC", 0, 9),
             ("MOSI", 0, 11), ("SCK", 0, 13), ("LED", 0, 15)], {"displayText": "TFT 320x240"}),
    "lcd1602": ("LCD 1602", 18, 6, 12, "#1b5e20",
                [("VSS", 1, 0), ("VDD", 3, 0), ("RS", 5, 0), ("E", 7, 0), ("D4", 9, 0),
                 ("D5", 11, 0), ("D6", 13, 0), ("D7", 15, 0)], {"displayText": "LCD 1602 ready"}),
    "nrf24": ("nRF24L01", 7, 7, 14, "#0d47a1",
              [("VCC", 1, 0), ("GND", 3, 0), ("CE", 5, 0), ("CSN", 5, 6), ("SCK", 3, 6),
               ("MOSI", 1, 6), ("MISO", 5, 3)], {"channel": "76", "lastPacket": "—"}),
    "led": ("Светодиод", 3, 4, 9, "#b71c1c",
            [("A", 0, 3), ("K", 2, 3)], {"colorName": "Красный"}),
    "resistor": ("Резистор", 6, 2, 5, "#d7a86e",
                 [("1", 0, 0), ("2", 5, 0)], {"ohms": "220"}),
    "button": ("Тактовая кнопка", 5, 5, 7, "#37474f",
               [("1", 0, 0), ("2", 4, 0), ("3", 0, 4), ("4", 4, 4)], {}),
    "bat18650": ("Батарея 18650 3.7В", 9, 4, 19, "#2e7d32",
                 [("+", 8, 1), ("-", 0, 1)], {"voltage": "3.7"}),
    "bat9v": ("Батарея 9В Крона", 5, 7, 16, "#212121",
              [("+", 1, 0), ("-", 3, 0)], {"voltage": "9"}),
    "lipo": ("Li-Po 3.7В", 8, 5, 6, "#f9a825",
             [("+", 7, 2), ("-", 0, 2)], {"voltage": "3.7"}),
    "buzzer": ("Пьезо-зуммер", 4, 4, 9, "#455a64", [("+", 0, 3), ("-", 3, 3)], {}),
    "dht22": ("Датчик DHT22", 7, 5, 12, "#0277bd",
              [("VCC", 0, 2), ("DATA", 2, 2), ("GND", 6, 2)], {"temp": "24.5", "hum": "48"}),
    "pot": ("Потенциометр 10k", 6, 6, 12, "#6a1b9a",
            [("1", 0, 5), ("SIG", 3, 5), ("2", 5, 5)], {"value": "512"}),
    "servo": ("Сервопривод SG90", 6, 9, 22, "#ef6c00",
              [("VCC", 1, 8), ("SIG", 3, 8), ("GND", 5, 8)], {"angle": "90"}),
    "relay": ("Реле 5В", 9, 7, 16, "#1565c0",
              [("VCC", 0, 1), ("GND", 0, 3), ("IN", 0, 5), ("COM", 8, 1), ("NO", 8, 3), ("NC", 8, 5)], {}),
    "cap": ("Конденсатор 100uF", 3, 4, 10, "#283593", [("+", 0, 3), ("-", 2, 3)], {}),
}

EXAMPLES = {
    "Blink D13": "// Blink\npinMode(13, OUTPUT);\ndigitalWrite(13, HIGH);\ndelay(500);\ndigitalWrite(13, LOW);\ndelay(500);\n",
    "Кнопка D32 -> LED": "// Кнопка на D32\npinMode(13, OUTPUT);\npinMode(32, INPUT_PULLUP);\nif (digitalRead(32) == LOW) {\n  digitalWrite(13, HIGH);\n  Serial.println(\"Кнопка нажата\");\n} else {\n  digitalWrite(13, LOW);\n}\ndelay(80);\n",
    "OLED": "// OLED\npinMode(13, OUTPUT);\noled.clear();\noled.print(\"Привет, Connect!\");\ndigitalWrite(13, HIGH);\ndelay(800);\noled.print(\"ESP32 в 3D :)\");\ndigitalWrite(13, LOW);\ndelay(800);\n",
    "TFT 1.8 ST7735": "// TFT 1.8\npinMode(13, OUTPUT);\ntft.clear();\ntft.print(\"TFT 1.8 OK!\");\nSerial.println(\"TFT кадр\");\ndigitalWrite(13, HIGH);\ndelay(900);\ntft.print(\"Connect PC :)\");\ndigitalWrite(13, LOW);\ndelay(900);\n",
    "nRF24": "// nRF24\npinMode(13, OUTPUT);\nnrf.send(\"PING от ESP32\");\nSerial.println(\"nRF отправлено\");\ndigitalWrite(13, HIGH);\ndelay(300);\ndigitalWrite(13, LOW);\ndelay(700);\n",
}

WIRE_COLORS = ["#d32f2f", "#ff6f00", "#f9a825", "#2e7d32", "#1565c0", "#6a1b9a", "#00838f"]


def shade(hex_color, f):
    hex_color = hex_color.lstrip("#")
    r, g, b = (int(hex_color[i:i + 2], 16) for i in (0, 2, 4))
    r = max(0, min(255, int(r * f))); g = max(0, min(255, int(g * f))); b = max(0, min(255, int(b * f)))
    return f"#{r:02x}{g:02x}{b:02x}"


_next_id = [1]


def new_id():
    _next_id[0] += 1
    return str(_next_id[0])


# ---------------------------------------------------------------- STL
def parse_stl(path):
    data = open(path, "rb").read()
    tris = []
    if len(data) >= 84:
        count = struct.unpack_from("<I", data, 80)[0]
        if 0 < count < 500000 and 84 + count * 50 == len(data):
            off = 84
            for _ in range(count):
                vals = struct.unpack_from("<12fH", data, off)
                tris.append(vals[3:12])
                off += 50
            return tris, os.path.basename(path)
    text = data.decode("utf-8", "ignore")
    verts = re.findall(r"vertex\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", text)
    verts = [tuple(float(x) for x in v) for v in verts]
    for i in range(0, len(verts) - 2, 3):
        tris.append(verts[i] + verts[i + 1] + verts[i + 2])
    if not tris:
        raise ValueError("Не удалось распознать STL")
    return tris, os.path.basename(path)


# ---------------------------------------------------------------- цепи
class Circuit:
    """Упрощённая, но честная модель: сети проводов, питание ESP, токи LED."""

    def __init__(self, app):
        self.app = app

    def _union_find(self):
        parent = {}

        def find(x):
            parent.setdefault(x, x)
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        def union(a, b):
            ra, rb = find(a), find(b)
            if ra != rb:
                parent[ra] = rb

        for c in self.app.components:
            for (name, _, _) in CATALOG[c["type"]][5]:
                find((c["id"], name))
        for w in self.app.wires:
            union((w["a"][0], w["a"][1]), (w["b"][0], w["b"][1]))
        for c in self.app.components:
            if c["type"] == "button" and c["id"] in self.app.pressed:
                union((c["id"], "1"), (c["id"], "2"))
                union((c["id"], "3"), (c["id"], "4"))
        return find

    def compute(self):
        app = self.app
        find = self._union_find()
        sources = {}   # root -> вольты
        grounds = set()
        for c in app.components:
            if c["type"] in ("bat18650", "bat9v", "lipo"):
                try:
                    v = float(c["props"].get("voltage", "3.7"))
                except ValueError:
                    v = 3.7
                sources[find((c["id"], "+"))] = v
                grounds.add(find((c["id"], "-")))
        esp_powered = set()
        for c in app.components:
            if c["type"] != "esp32":
                continue
            gnd = find((c["id"], "GND"))
            vin = sources.get(find((c["id"], "VIN")), 0)
            v33 = sources.get(find((c["id"], "3V3")), 0)
            if gnd in grounds and (vin >= 3.3 or 3.0 <= v33 <= 3.8):
                esp_powered.add(c["id"])
                sources[find((c["id"], "3V3"))] = 3.3
                if app.running:
                    for (name, _, _) in CATALOG["esp32"][5]:
                        if app.pin_outputs.get((c["id"], name)) == 1:
                            sources[find((c["id"], name))] = 3.3
        # резисторы — рёбра между сетями
        redges = []
        for c in app.components:
            if c["type"] == "resistor":
                try:
                    ohms = float(c["props"].get("ohms", "220"))
                except ValueError:
                    ohms = 220.0
                redges.append((find((c["id"], "1")), find((c["id"], "2")), ohms))

        def through_resistors(start, targets):
            """BFS через резисторы: (цель, суммарные Ом) или None."""
            if start in targets:
                return start, 0.0
            frontier = [(start, 0.0)]
            seen = {start: 0.0}
            for _ in range(3):
                nxt = []
                for root, acc in frontier:
                    for (a, b, ohms) in redges:
                        other = b if a == root else (a if b == root else None)
                        if other is None:
                            continue
                        na = acc + ohms
                        if other in targets:
                            return other, na
                        if seen.get(other, 1e18) > na:
                            seen[other] = na
                            nxt.append((other, na))
                frontier = nxt
            return None

        leds = {}
        low_roots = set(grounds)
        if app.running:
            for c in app.components:
                if c["type"] == "esp32" and c["id"] in esp_powered:
                    for (name, _, _) in CATALOG["esp32"][5]:
                        if app.pin_modes.get((c["id"], name)) == "OUTPUT" and app.pin_outputs.get((c["id"], name)) == 0:
                            low_roots.add(find((c["id"], name)))
        for c in app.components:
            if c["type"] != "led":
                continue
            a_root = find((c["id"], "A")); k_root = find((c["id"], "K"))
            sup = through_resistors(a_root, set(sources.keys()))
            gnd = through_resistors(k_root, low_roots)
            if sup and gnd:
                volts = sources[sup[0]]
                total_r = sup[1] + gnd[1]
                if volts > 2.0:
                    if total_r < 1.0:
                        leds[c["id"]] = (0.0, 999.0, True)
                    else:
                        ma = (volts - 2.0) / total_r * 1000.0
                        leds[c["id"]] = (min(1.0, max(0.05, ma / 20.0)), ma, ma > 60)
                    continue
            leds[c["id"]] = (0.0, 0.0, False)
        return {"esp_powered": esp_powered, "leds": leds, "find": find, "sources": sources, "grounds": grounds}

    def digital_read(self, esp_id, pin_name):
        find = self._union_find()
        root = find((esp_id, pin_name))
        for c in self.app.components:
            if c["type"] in ("bat18650", "bat9v", "lipo"):
                if find((c["id"], "-")) == root:
                    return 0
                if find((c["id"], "+")) == root:
                    return 1
            if c["type"] == "esp32":
                if find((c["id"], "GND")) == root:
                    return 0
                if find((c["id"], "3V3")) == root:
                    return 1
        return 1 if self.app.pin_modes.get((esp_id, pin_name)) == "INPUT_PULLUP" else 0


# ---------------------------------------------------------------- прошивка
class Runner:
    def __init__(self, app, code):
        self.app = app
        self.instrs = self.parse(code)
        self.pc = 0
        self.cond = []
        self.job = None

    @staticmethod
    def parse(code):
        out = []
        for raw in code.splitlines():
            line = raw.strip()
            if not line or line.startswith("//") or line.startswith("#"):
                continue
            if line.startswith("} else") or line.startswith("else"):
                out.append(("ELSE",)); continue
            if line in ("}", "};"):
                out.append(("ENDIF",)); continue
            if line == "{" or line.startswith("void "):
                continue
            line = line.rstrip(";")
            m = re.search(r"if\s*\(\s*digitalRead\s*\(\s*(\w+)\s*\)\s*==\s*(\w+)\s*\)", line)
            if m:
                out.append(("IF", m.group(1), m.group(2))); continue
            m = re.search(r"pinMode\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)", line)
            if m:
                out.append(("PINMODE", m.group(1), m.group(2))); continue
            m = re.search(r"digitalWrite\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)", line)
            if m:
                out.append(("WRITE", m.group(1), m.group(2))); continue
            m = re.search(r"delay\s*\(\s*(\d+)\s*\)", line)
            if m:
                out.append(("DELAY", int(m.group(1)))); continue
            m = re.search(r'Serial\.println\s*\(\s*"([^"]*)"\s*\)', line)
            if m:
                out.append(("SERIAL", m.group(1))); continue
            m = re.search(r'tft\.print(?:ln)?\s*\(\s*"([^"]*)"\s*\)', line)
            if m:
                out.append(("TFT", m.group(1))); continue
            if re.search(r"tft\.clear\s*\(\s*\)", line):
                out.append(("TFT_CLEAR",)); continue
            m = re.search(r'(?:oled|display|lcd)\.print(?:ln)?\s*\(\s*"([^"]*)"\s*\)', line)
            if m:
                out.append(("OLED", m.group(1))); continue
            if re.search(r"(?:oled|display|lcd)\.clear\s*\(\s*\)", line):
                out.append(("OLED_CLEAR",)); continue
            m = re.search(r'nrf\.send\s*\(\s*"([^"]*)"\s*\)', line)
            if m:
                out.append(("NRF", m.group(1))); continue
            m = re.search(r"servo\.write\s*\(\s*(\d+)\s*\)", line)
            if m:
                out.append(("SERVO", int(m.group(1)))); continue
        return out

    def start(self):
        self.pc = 0
        self.cond = []
        self.job = self.app.root.after(80, self.step)

    def stop(self):
        if self.job:
            self.app.root.after_cancel(self.job)
            self.job = None

    def _pin(self, raw):
        return "D" + raw if raw.isdigit() else raw

    def _active(self):
        return all(self.cond)

    def step(self):
        app = self.app
        if not app.running:
            return
        esp = next((c for c in app.components if c["type"] == "esp32"), None)
        if esp is None:
            self.job = app.root.after(800, self.step)
            return
        guard = 0
        while guard < 300:
            guard += 1
            if self.pc >= len(self.instrs):
                self.pc = 0
                self.cond = []
                break
            ins = self.instrs[self.pc]
            self.pc += 1
            kind = ins[0]
            if kind == "IF":
                v = app.circuit.digital_read(esp["id"], self._pin(ins[1]))
                expect = 1 if ins[2] in ("HIGH", "1") else 0
                self.cond.append(self._active() and v == expect)
            elif kind == "ELSE":
                if self.cond:
                    parent = all(self.cond[:-1])
                    self.cond[-1] = parent and not self.cond[-1]
            elif kind == "ENDIF":
                if self.cond:
                    self.cond.pop()
            else:
                if not self._active():
                    continue
                if kind == "PINMODE":
                    app.pin_modes[(esp["id"], self._pin(ins[1]))] = ins[2]
                elif kind == "WRITE":
                    key = (esp["id"], self._pin(ins[1]))
                    val = 1 if ins[2] in ("HIGH", "1") else 0
                    if app.pin_outputs.get(key) != val:
                        app.pin_outputs[key] = val
                        app.pin_modes[key] = "OUTPUT"
                        app.refresh_all()
                elif kind == "DELAY":
                    self.job = app.root.after(max(10, min(5000, ins[1])), self.step)
                    return
                elif kind == "SERIAL":
                    app.log("Serial: " + ins[1])
                elif kind == "OLED":
                    for c in app.components:
                        if c["type"] in ("oled", "tft", "lcd1602"):
                            c["props"]["displayText"] = ins[1]
                    app.log("OLED: «" + ins[1] + "»")
                    app.refresh_all()
                elif kind == "OLED_CLEAR":
                    for c in app.components:
                        if c["type"] in ("oled", "tft", "lcd1602"):
                            c["props"]["displayText"] = ""
                    app.refresh_all()
                elif kind == "TFT":
                    tfts = [c for c in app.components if c["type"] in ("tft", "tft18")]
                    if not tfts:
                        app.log("tft.print: TFT-дисплея нет на плате!")
                    for c in tfts:
                        c["props"]["displayText"] = ins[1]
                    if tfts:
                        app.log("TFT: «" + ins[1] + "»")
                    app.refresh_all()
                elif kind == "TFT_CLEAR":
                    for c in app.components:
                        if c["type"] in ("tft", "tft18"):
                            c["props"]["displayText"] = ""
                    app.refresh_all()
                elif kind == "NRF":
                    nrfs = [c for c in app.components if c["type"] == "nrf24"]
                    if not nrfs:
                        app.log("nrf.send: модуля nRF24 нет на плате!")
                    for c in nrfs:
                        c["props"]["lastPacket"] = ins[1]
                        app.log(f"nRF24 [канал {c['props'].get('channel', '76')}] TX -> «{ins[1]}» доставлено")
                elif kind == "SERVO":
                    for c in app.components:
                        if c["type"] == "servo":
                            c["props"]["angle"] = str(ins[1])
                    app.refresh_all()
        self.job = app.root.after(30, self.step)


# ---------------------------------------------------------------- приложение
class App:
    COLS, ROWS = 34, 22
    CELL = 11.0

    def __init__(self, root):
        self.root = root
        root.title("Connect 3D — симулятор электроники (ПК)")
        root.geometry("1380x780")
        root.minsize(1024, 640)
        root.configure(bg="#0b1220")

        self.components = []   # {id,type,x,y,rot,props}
        self.wires = []        # {a:(id,pin), b:(id,pin), style, color, quality, tempC, flux}
        self.selected = None
        self.mode = "camera"
        self.running = False
        self.pressed = set()
        self.pin_outputs = {}
        self.pin_modes = {}
        self.solder_temp = tk.IntVar(value=350)
        self.use_flux = tk.BooleanVar(value=True)
        self.angle = tk.DoubleVar(value=-28.0)
        self.zoom = tk.DoubleVar(value=1.0)
        self.runner = None
        self.circuit = Circuit(self)
        self.stl = None          # (triangles, name)
        self.show_stl = tk.BooleanVar(value=False)
        self.placing_type = None
        self.drag_comp = None
        self.drag_start = None
        self.pending_pin = None
        self.list_drag_type = None
        self._build_ui()
        self._demo()
        self.refresh_all()

    # ---------- UI ----------
    def _build_ui(self):
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except tk.TclError:
            pass
        style.configure("TFrame", background="#0b1220")
        style.configure("Side.TFrame", background="#0f1b2d")
        style.configure("TLabel", background="#0f1b2d", foreground="#e2e8f0")
        style.configure("Title.TLabel", background="#0f1b2d", foreground="#7dd3fc", font=("Segoe UI", 10, "bold"))
        style.configure("TButton", padding=4)
        style.configure("TNotebook", background="#0f1b2d")
        style.configure("TNotebook.Tab", padding=(8, 4))

        top = tk.Frame(self.root, bg="#111c2e", height=46)
        top.pack(side="top", fill="x")
        tk.Label(top, text="⚡ CONNECT 3D", bg="#111c2e", fg="white",
                 font=("Segoe UI", 13, "bold")).pack(side="left", padx=10)
        self.status = tk.Label(top, text="", bg="#111c2e", fg="#93c5fd", font=("Segoe UI", 9))
        self.status.pack(side="left", padx=6)
        self.run_btn = tk.Button(top, text="▶ ЗАПУСК", bg="#14532d", fg="white",
                                 font=("Segoe UI", 10, "bold"), command=self.toggle_run)
        self.run_btn.pack(side="right", padx=8, pady=6)
        tk.Button(top, text="💾 Сохранить", command=self.save_project).pack(side="right", pady=6)
        tk.Button(top, text="📂 Открыть", command=self.load_project).pack(side="right", pady=6)
        self.menu_btn = tk.Button(top, text="☰ Меню: вкл", command=self.toggle_sidebar)
        self.menu_btn.pack(side="right", pady=6)

        main = tk.Frame(self.root, bg="#0b1220")
        main.pack(side="top", fill="both", expand=True)

        left = tk.Frame(main, bg="#0b1220")
        left.pack(side="left", fill="both", expand=True)
        self.canvas = tk.Canvas(left, bg="#0f1b24", highlightthickness=0)
        self.canvas.pack(side="top", fill="both", expand=True)
        self.canvas.bind("<Button-1>", self.on_canvas_down)
        self.canvas.bind("<B1-Motion>", self.on_canvas_move)
        self.canvas.bind("<ButtonRelease-1>", self.on_canvas_up)
        self.canvas.bind("<MouseWheel>", self.on_wheel)
        self.canvas.bind("<Configure>", lambda e: self.draw())
        self.log_text = tk.Text(left, height=6, bg="#0a1622", fg="#a5d6a7",
                                font=("Consolas", 9), state="disabled", wrap="word")
        self.log_text.pack(side="bottom", fill="x")

        # правая сворачиваемая панель с вкладками
        self.sidebar = tk.Frame(main, bg="#0f1b2d", width=352)
        self.sidebar.pack(side="right", fill="y")
        self.sidebar.pack_propagate(False)
        self.tabs = ttk.Notebook(self.sidebar)
        self.tabs.pack(fill="both", expand=True, padx=6, pady=6)
        self._tab_components()
        self._tab_tools()
        self._tab_firmware()

    def _tab_components(self):
        tab = ttk.Frame(self.tabs, style="Side.TFrame")
        self.tabs.add(tab, text="🧩 Детали")
        ttk.Label(tab, text="ПЕРЕТАЩИТЕ ДЕТАЛЬ НА ПЛАТУ", style="Title.TLabel").pack(anchor="w", pady=(6, 2))
        ttk.Label(tab, text="Зажмите деталь в списке и тащите на плату.\nИли: клик по детали → клик по плате.",
                  style="TLabel", font=("Segoe UI", 8)).pack(anchor="w")
        self.comp_list = tk.Listbox(tab, bg="#16233a", fg="white", selectbackground="#2563eb",
                                    font=("Segoe UI", 10), activestyle="none", exportselection=False)
        for tid, (title, *_rest) in CATALOG.items():
            self.comp_list.insert("end", title)
        self.comp_list.pack(fill="both", expand=True, pady=6)
        self.comp_list.bind("<ButtonPress-1>", self.on_list_press)
        self.comp_list.bind("<Double-Button-1>", self.on_list_double)
        self.root.bind_all("<B1-Motion>", self.on_list_drag_motion)
        self.root.bind_all("<ButtonRelease-1>", self.on_list_drag_release)
        ttk.Label(tab, text="НА ПЛАТЕ:", style="Title.TLabel").pack(anchor="w", pady=(4, 2))
        self.placed_list = tk.Listbox(tab, bg="#16233a", fg="white", selectbackground="#2563eb",
                                      font=("Segoe UI", 9), height=7, activestyle="none", exportselection=False)
        self.placed_list.pack(fill="x")
        self.placed_list.bind("<<ListboxSelect>>", self.on_placed_select)

    def _tab_tools(self):
        tab = ttk.Frame(self.tabs, style="Side.TFrame")
        self.tabs.add(tab, text="🛠 Инструменты")
        ttk.Label(tab, text="РЕЖИМ", style="Title.TLabel").pack(anchor="w", pady=(6, 2))
        modes = [("camera", "🎥 Камера"), ("move", "✥ Двигать"), ("wire", "🔌 Провод"),
                 ("solder", "🔥 Паять"), ("delete", "🗑 Удалить"), ("sim", "👆 Нажимать")]
        self.mode_buttons = {}
        row = None
        for i, (key, label) in enumerate(modes):
            if i % 3 == 0:
                row = tk.Frame(tab, bg="#0f1b2d")
                row.pack(fill="x", pady=2)
            b = tk.Button(row, text=label, font=("Segoe UI", 8), command=lambda k=key: self.set_mode(k))
            b.pack(side="left", fill="x", expand=True, padx=2)
            self.mode_buttons[key] = b
        ttk.Label(tab, text="ПАЯЛЬНИК", style="Title.TLabel").pack(anchor="w", pady=(10, 2))
        self.temp_label = ttk.Label(tab, text="", style="TLabel")
        self.temp_label.pack(anchor="w")
        tk.Scale(tab, from_=200, to=480, orient="horizontal", variable=self.solder_temp,
                 command=lambda _v: self.refresh_solder_label(), bg="#0f1b2d", fg="white",
                 troughcolor="#16233a", highlightthickness=0).pack(fill="x")
        tk.Checkbutton(tab, text="Использовать флюс", variable=self.use_flux,
                       command=self.refresh_solder_label, bg="#0f1b2d", fg="white",
                       selectcolor="#16233a", activebackground="#0f1b2d").pack(anchor="w")
        self.quality_label = ttk.Label(tab, text="", style="TLabel", font=("Segoe UI", 8), wraplength=320)
        self.quality_label.pack(anchor="w")
        ttk.Label(tab, text="КАМЕРА", style="Title.TLabel").pack(anchor="w", pady=(10, 2))
        tk.Scale(tab, from_=-80, to=80, orient="horizontal", variable=self.angle,
                 command=lambda _v: self.draw(), bg="#0f1b2d", fg="white",
                 troughcolor="#16233a", highlightthickness=0, label="Поворот сцены").pack(fill="x")
        tk.Scale(tab, from_=60, to=180, orient="horizontal", variable=self.zoom,
                 command=lambda _v: self.draw(), bg="#0f1b2d", fg="white",
                 troughcolor="#16233a", highlightthickness=0, label="Масштаб %").pack(fill="x")
        zoom_row = tk.Frame(tab, bg="#0f1b2d")
        zoom_row.pack(fill="x", pady=2)
        for label, ang in (("Изометрия", -28), ("Сверху", 0), ("Спереди", 80), ("Сбоку", -80)):
            tk.Button(zoom_row, text=label, font=("Segoe UI", 8),
                      command=lambda a=ang: (self.angle.set(a), self.draw())).pack(side="left", fill="x", expand=True, padx=2)
        ttk.Label(tab, text="ВЫБРАННАЯ ДЕТАЛЬ", style="Title.TLabel").pack(anchor="w", pady=(10, 2))
        self.sel_label = ttk.Label(tab, text="—", style="TLabel", font=("Segoe UI", 8), wraplength=320)
        self.sel_label.pack(anchor="w")
        act = tk.Frame(tab, bg="#0f1b2d")
        act.pack(fill="x", pady=4)
        tk.Button(act, text="↻ Повернуть", command=self.rotate_selected).pack(side="left", fill="x", expand=True, padx=2)
        tk.Button(act, text="⚙ Свойства", command=self.cycle_props).pack(side="left", fill="x", expand=True, padx=2)
        tk.Button(act, text="🗑 Удалить", command=self.delete_selected).pack(side="left", fill="x", expand=True, padx=2)
        ttk.Label(tab, text="КОРПУС STL", style="Title.TLabel").pack(anchor="w", pady=(10, 2))
        tk.Button(tab, text="📂 Импорт своего .STL…", command=self.import_stl).pack(fill="x", pady=2)
        tk.Checkbutton(tab, text="Показать STL в сцене", variable=self.show_stl,
                       command=self.draw, bg="#0f1b2d", fg="white",
                       selectcolor="#16233a", activebackground="#0f1b2d").pack(anchor="w")
        self.stl_label = ttk.Label(tab, text="Корпус не выбран", style="TLabel", font=("Segoe UI", 8), wraplength=320)
        self.stl_label.pack(anchor="w")
        ttk.Label(tab, text="ВЫХОДЫ ESP32 (живые)", style="Title.TLabel").pack(anchor="w", pady=(10, 2))
        self.esp_text = tk.Text(tab, height=10, bg="#16233a", fg="#e2e8f0", font=("Consolas", 8),
                                state="disabled", wrap="none")
        self.esp_text.pack(fill="x")
        tk.Button(tab, text="Переключить выбранный в списке пин", command=self.toggle_esp_pin).pack(fill="x", pady=4)
        self.esp_pin_list = tk.Listbox(tab, bg="#16233a", fg="white", selectbackground="#2563eb",
                                       font=("Consolas", 8), height=4, activestyle="none", exportselection=False)
        self.esp_pin_list.pack(fill="x")
        self.refresh_solder_label()
        self._highlight_modes()

    def _tab_firmware(self):
        tab = ttk.Frame(self.tabs, style="Side.TFrame")
        self.tabs.add(tab, text="💻 Код ESP")
        ttk.Label(tab, text="ПРИМЕРЫ ПРОШИВКИ", style="Title.TLabel").pack(anchor="w", pady=(6, 2))
        ex = tk.Frame(tab, bg="#0f1b2d")
        ex.pack(fill="x")
        for name in EXAMPLES:
            tk.Button(ex, text=name, font=("Segoe UI", 7),
                      command=lambda n=name: self.load_example(n)).pack(side="left", padx=1)
        self.code_text = tk.Text(tab, bg="#0d1526", fg="#d6deeb", font=("Consolas", 9),
                                 insertbackground="white", wrap="none", undo=True)
        self.code_text.pack(fill="both", expand=True, pady=6)
        self.code_text.insert("1.0", EXAMPLES["Blink D13"])
        tk.Button(tab, text="🚀 ЗАЛИТЬ ПРОШИВКУ И ЗАПУСТИТЬ", bg="#2563eb", fg="white",
                  font=("Segoe UI", 9, "bold"), command=self.flash_and_run).pack(fill="x", pady=2)
        ttk.Label(tab, text="Команды: pinMode, digitalWrite, digitalRead (if/else),\ndelay, Serial.println, oled.print/clear, tft.print/clear\n(TFT 1.8 и 2.4), nrf.send, servo.write.",
                  style="TLabel", font=("Segoe UI", 8), wraplength=320).pack(anchor="w")

    # ---------- состояние ----------
    def solder_quality(self):
        t = int(self.solder_temp.get())
        if t > 430:
            return "hot"
        if t < 280:
            return "cold"
        if t < 315 and not self.use_flux.get():
            return "cold"
        return "good"

    def refresh_solder_label(self):
        q = self.solder_quality()
        txt = {"good": "✨ Хорошая пайка: блестящий наплыв.",
               "cold": "🥶 Холодная пайка: тусклая, ненадёжная. Надо 320–380°C + флюс.",
               "hot": "🔥 Перегрев: поджог платы. Снизьте до 320–380°C."}[q]
        self.temp_label.config(text=f"Температура жала: {int(self.solder_temp.get())}°C")
        self.quality_label.config(text=txt)

    def toggle_sidebar(self):
        if self.sidebar.winfo_ismapped():
            self.sidebar.pack_forget()
            self.menu_btn.config(text="☰ Меню: показать")
        else:
            self.sidebar.pack(side="right", fill="y")
            self.menu_btn.config(text="☰ Меню: вкл")
        self.root.after(50, self.draw)

    def set_mode(self, key):
        self.mode = key
        self.pending_pin = None
        self._highlight_modes()
        self.draw()

    def _highlight_modes(self):
        for k, b in self.mode_buttons.items():
            b.config(bg="#2563eb" if k == self.mode else "#334155", fg="white")

    def log(self, text):
        self.log_text.config(state="normal")
        self.log_text.insert("end", "▸ " + text + "\n")
        self.log_text.see("end")
        self.log_text.config(state="disabled")

    def selected_comp(self):
        return next((c for c in self.components if c["id"] == self.selected), None)

    # ---------- демо ----------
    def _demo(self):
        def add(t, x, y):
            c = {"id": new_id(), "type": t, "x": x, "y": y, "rot": 0, "props": dict(CATALOG[t][6])}
            self.components.append(c)
            return c

        bat = add("bat18650", 1, 1)
        esp = add("esp32", 20, 1)
        res = add("resistor", 8, 12); res["props"]["ohms"] = "220"
        led = add("led", 8, 16)
        btn = add("button", 14, 14)
        oled = add("oled", 2, 6)
        nrf = add("nrf24", 27, 15)

        def wire(a, ap, b, bp, color, style="wire"):
            self.wires.append({"a": (a["id"], ap), "b": (b["id"], bp), "style": style,
                               "color": color, "quality": "good", "tempC": 350, "flux": True})

        wire(bat, "+", esp, "VIN", "#d32f2f")
        wire(bat, "-", esp, "GND", "#212121")
        wire(esp, "D13", res, "1", "#ff6f00")
        wire(res, "2", led, "A", "#ff6f00", "solder")
        wire(led, "K", esp, "GND", "#212121")
        wire(btn, "1", esp, "D32", "#1565c0")
        wire(btn, "2", esp, "GND", "#212121")
        wire(oled, "VCC", esp, "3V3", "#d32f2f")
        wire(oled, "GND", esp, "GND", "#212121")
        wire(oled, "SCL", esp, "D22", "#6a1b9a")
        wire(oled, "SDA", esp, "D21", "#00838f")
        wire(nrf, "VCC", esp, "3V3", "#d32f2f")
        wire(nrf, "GND", esp, "GND", "#212121")
        self.log("Демо-схема: ESP32 + LED + кнопка + OLED + nRF24. Перетащите TFT 1.8 из вкладки «Детали»!")
        self.log("Паять: режим «Паять», температура — во вкладке «Инструменты».")

    def add_component(self, type_id, gx, gy):
        title, w, h, _hm, _col, _pins, props = CATALOG[type_id]
        c = {"id": new_id(), "type": type_id,
             "x": max(0, min(self.COLS - w, int(gx))), "y": max(0, min(self.ROWS - h, int(gy))),
             "rot": 0, "props": dict(props)}
        self.components.append(c)
        self.selected = c["id"]
        self.log(f"Добавлено: {title} ({c['x']},{c['y']})")
        return c

    def eff_size(self, c):
        _t, w, h, *_ = CATALOG[c["type"]]
        return (h, w) if c["rot"] % 180 == 90 else (w, h)

    def pin_local_pos(self, c, pin):
        _t, w, h, *_ = CATALOG[c["type"]]
        _n, dx, dy = pin
        rot = c["rot"] % 360
        if rot == 90:
            return h - 1 - dy, dx
        if rot == 180:
            return w - 1 - dx, h - 1 - dy
        if rot == 270:
            return dy, w - 1 - dx
        return dx, dy

    def pin_world(self, c, pin):
        px, py = self.pin_local_pos(c, pin)
        return c["x"] + px + 0.5, c["y"] + py + 0.5

    def pin_height(self, c):
        t = c["type"]
        if t == "esp32":
            return 2.1
        if t in ("oled", "tft", "tft18", "lcd1602", "nrf24"):
            return 1.9
        if t in ("led", "resistor", "cap"):
            return 0.5
        return 1.2

    # ---------- проекция ----------
    def project(self, wx, wy, wz=0.0):
        cx = self.canvas.winfo_width() / 2.0
        cy = self.canvas.winfo_height() / 2.0
        ang = math.radians(float(self.angle.get()))
        zoom = float(self.zoom.get()) / 100.0
        # вращение вокруг центра платы
        bx, by = self.COLS / 2.0, self.ROWS / 2.0
        dx, dy = wx - bx, wy - by
        xr = dx * math.cos(ang) - dy * math.sin(ang)
        yr = dx * math.sin(ang) + dy * math.cos(ang)
        sx = cx + (xr - yr) * self.CELL * zoom
        sy = cy + (xr + yr) * self.CELL * 0.52 * zoom - wz * self.CELL * zoom
        return sx, sy

    def unproject(self, sx, sy):
        cx = self.canvas.winfo_width() / 2.0
        cy = self.canvas.winfo_height() / 2.0
        ang = math.radians(float(self.angle.get()))
        zoom = float(self.zoom.get()) / 100.0
        a = (sx - cx) / (self.CELL * zoom)
        b = (sy - cy) / (self.CELL * 0.52 * zoom)
        xr = (a + b) / 2.0
        yr = (b - a) / 2.0
        # обратное вращение
        dx = xr * math.cos(ang) + yr * math.sin(ang)
        dy = -xr * math.sin(ang) + yr * math.cos(ang)
        return dx + self.COLS / 2.0, dy + self.ROWS / 2.0

    def project_local(self, c, lx, ly, wz=0.0):
        _t, w, h, *_ = CATALOG[c["type"]]
        rot = c["rot"] % 360
        if rot == 90:
            rx, ry = h - ly, lx
        elif rot == 180:
            rx, ry = w - lx, h - ly
        elif rot == 270:
            rx, ry = ly, w - lx
        else:
            rx, ry = lx, ly
        return self.project(c["x"] + rx, c["y"] + ry, wz)

    # ---------- отрисовка ----------
    def draw(self):
        cv = self.canvas
        cv.delete("all")
        state = self.circuit.compute()
        # плата
        corners = [self.project(0, 0, 0), self.project(self.COLS, 0, 0),
                   self.project(self.COLS, self.ROWS, 0), self.project(0, self.ROWS, 0)]
        cv.create_polygon(corners, fill="#14532d", outline="#0b3d20", width=2)
        # отверстия
        occupied = set()
        for c in self.components:
            ew, eh = self.eff_size(c)
            for yy in range(c["y"], c["y"] + eh):
                for xx in range(c["x"], c["x"] + ew):
                    occupied.add((xx, yy))
        for gy in range(self.ROWS):
            for gx in range(self.COLS):
                if (gx, gy) in occupied:
                    continue
                sx, sy = self.project(gx + 0.5, gy + 0.5, 0.02)
                cv.create_oval(sx - 1.6, sy - 1.2, sx + 1.6, sy + 1.2, fill="#0b3d20", outline="")
        cv.create_text(*self.project(1, self.ROWS - 0.5, 0.05), anchor="w",
                       text="CONNECT PCB • REV 1.4 • PC", fill="#cfe8d4", font=("Segoe UI", 8))
        # паяные дорожки под компонентами
        for w in self.wires:
            if w["style"] == "solder":
                self._draw_wire(cv, w, state)
        # компоненты по глубине
        def depth_key(c):
            ew, eh = self.eff_size(c)
            p = self.project(c["x"] + ew / 2, c["y"] + eh / 2, 0)
            return p[1]
        for c in sorted(self.components, key=depth_key):
            self._draw_component(cv, c, state)
        # провода дугами поверх
        for w in self.wires:
            if w["style"] != "solder":
                self._draw_wire(cv, w, state)
        # штыри и подписи
        show_names = self.mode in ("wire", "solder")
        for c in self.components:
            for pin in CATALOG[c["type"]][5]:
                wx, wy = self.pin_world(c, pin)
                sx, sy = self.project(wx, wy, self.pin_height(c))
                key = (c["id"], pin[0])
                is_out = c["type"] == "esp32" and self.pin_modes.get(key) == "OUTPUT"
                high = is_out and self.pin_outputs.get(key) == 1 and self.running
                color = "#22c55e" if high else ("#475569" if is_out else "#ffd54f")
                r = 4 if not high else 5
                if high:
                    cv.create_oval(sx - 9, sy - 7, sx + 9, sy + 7, outline="#22c55e", width=2)
                cv.create_oval(sx - r, sy - r * 0.8, sx + r, sy + r * 0.8, fill=color, outline="#4e342e")
                if show_names or c["id"] == self.selected:
                    cv.create_text(sx, sy - 10, text=pin[0], fill="white", font=("Segoe UI", 7))
        # STL-корпус каркасом
        if self.show_stl.get() and self.stl:
            tris, _name = self.stl
            if tris:
                xs = [v for t in tris for v in t[0::3]]; ys = [v for t in tris for v in t[1::3]]
                minx, miny = min(xs), min(ys)
                wmm = max(xs) - minx or 1
                scale = (self.COLS * 0.9) / wmm
                stride = max(1, len(tris) // 700)
                for t in tris[::stride]:
                    pts = []
                    for k in range(3):
                        wx = (t[k * 3] - minx) * scale + self.COLS * 0.05
                        wy = (t[k * 3 + 1] - miny) * scale + 2
                        wz = t[k * 3 + 2] * scale / 2.54
                        pts.append(self.project(wx, wy, wz))
                    cv.create_polygon(pts, outline="#90caf9", fill="", stipple="gray25")
        # подсказка режима
        hints = {
            "camera": "🎥 Камера: тяните мышью по пустому месту — поворот; колесо — масштаб. Меню справа сворачивается сверху.",
            "move": "✥ Двигать: тяните деталь мышью. Детали добавляются перетаскиванием из вкладки «Детали».",
            "wire": "🔌 Провод: клик по штырю → клик по второму штырю.",
            "solder": f"🔥 Пайка {int(self.solder_temp.get())}°C: штырь → штырь. Качество зависит от температуры и флюса (вкладка «Инструменты»).",
            "delete": "🗑 Клик по детали удаляет её.",
            "sim": "👆 Кликайте тактовые кнопки на плате, потенциометр крутится кликами.",
        }
        cv.create_text(10, self.canvas.winfo_height() - 12, anchor="sw",
                       text=hints.get(self.mode, ""), fill="#b0bec5", font=("Segoe UI", 9))

    def _box(self, cv, c, lx1, ly1, lx2, ly2, z1, z2, color):
        top = [self.project_local(c, lx1, ly1, z2), self.project_local(c, lx2, ly1, z2),
               self.project_local(c, lx2, ly2, z2), self.project_local(c, lx1, ly2, z2)]
        sides = [
            ([self.project_local(c, lx1, ly2, z1), self.project_local(c, lx2, ly2, z1),
              self.project_local(c, lx2, ly2, z2), self.project_local(c, lx1, ly2, z2)], 0.62),
            ([self.project_local(c, lx2, ly1, z1), self.project_local(c, lx2, ly2, z1),
              self.project_local(c, lx2, ly2, z2), self.project_local(c, lx2, ly1, z2)], 0.78),
            ([self.project_local(c, lx1, ly1, z1), self.project_local(c, lx2, ly1, z1),
              self.project_local(c, lx2, ly1, z2), self.project_local(c, lx1, ly1, z2)], 0.5),
            ([self.project_local(c, lx1, ly1, z1), self.project_local(c, lx1, ly2, z1),
              self.project_local(c, lx1, ly2, z2), self.project_local(c, lx1, ly1, z2)], 0.7),
        ]
        for pts, f in sides:
            cv.create_polygon(pts, fill=shade(color, f), outline=shade(color, 0.35))
        cv.create_polygon(top, fill=color, outline=shade(color, 0.4))

    def _draw_component(self, cv, c, state):
        t = c["type"]
        title, w, h, hmm, color, _pins, _props = CATALOG[t]
        ztop = hmm / 2.54
        sel = c["id"] == self.selected
        if t == "esp32":
            self._box(cv, c, 0, 0, w, h, 0, 0.65, "#1a237e")
            self._box(cv, c, w * 0.22, h * 0.30, w * 0.78, h * 0.72, 0.65, 2.0, "#c0c6ce")  # экран WROOM
            self._box(cv, c, w * 0.22, h * 0.05, w * 0.78, h * 0.25, 0.65, 0.95, "#3949ab")  # антенна
            self._box(cv, c, w * 0.30, -0.3, w * 0.70, 1.8, 0.65, 1.9, "#9aa0a6")  # USB
            self._box(cv, c, 0.2, 0.5, 1.5, 1.8, 0.65, 1.5, "#212121")
            self._box(cv, c, w - 1.5, 0.5, w - 0.2, 1.8, 0.65, 1.5, "#212121")
            powered = c["id"] in state["esp_powered"]
            cx, cy = self.project_local(c, w - 0.8, 2.6, 1.6)
            cv.create_oval(cx - 4, cy - 4, cx + 4, cy + 4,
                           fill="#69f0ae" if (powered and self.running) else "#546e7a", outline="")
        elif t in ("oled", "tft", "tft18", "lcd1602"):
            self._box(cv, c, 0, 0, w, h, 0, 0.65, color)
            scr = "#9ccc65" if t == "lcd1602" else "#05131a"
            poly = [self.project_local(c, w * 0.07, h * 0.08, 0.75), self.project_local(c, w * 0.93, h * 0.08, 0.75),
                    self.project_local(c, w * 0.93, h * 0.92, 0.75), self.project_local(c, w * 0.07, h * 0.92, 0.75)]
            cv.create_polygon(poly, fill=scr, outline="#90a4ae")
            txt = c["props"].get("displayText", "")
            if txt:
                cx, cy = self.project_local(c, w / 2, h / 2, 0.8)
                cv.create_text(cx, cy, text=txt[:20], fill="#1b5e20" if t == "lcd1602" else "#80deea",
                               font=("Segoe UI", 9, "bold"))
        elif t == "nrf24":
            self._box(cv, c, 0, 0, w, h, 0, 0.65, "#0d47a1")
            self._box(cv, c, w * 0.3, h * 0.38, w * 0.7, h * 0.68, 0.65, 1.3, "#111111")
            for i in range(3):
                yy = h * (0.06 + i * 0.11)
                cv.create_line(self.project_local(c, w * 0.08, yy, 0.8), self.project_local(c, w * 0.92, yy, 0.8),
                               fill="#ffd54f", width=2)
        elif t == "led":
            led_colors = {"Красный": "#ff2222", "Зелёный": "#22ff66", "Синий": "#2266ff",
                          "Жёлтый": "#ffee22", "Белый": "#ffffff"}
            base = led_colors.get(c["props"].get("colorName", "Красный"), "#ff2222")
            cx, cy = self.project_local(c, w / 2, h / 2, 3.4)
            st = state["leds"].get(c["id"], (0, 0, False))
            if st[2]:
                cv.create_oval(cx - 11, cy - 11, cx + 11, cy + 11, fill="#333333", outline="#ff2222", width=2)
                cv.create_text(cx, cy, text="✕", fill="red", font=("Segoe UI", 11, "bold"))
            else:
                if st[0] > 0.02 and self.running:
                    r = 14 + 16 * st[0]
                    cv.create_oval(cx - r, cy - r, cx + r, cy + r, outline=base, width=3)
                cv.create_oval(cx - 9, cy - 12, cx + 9, cy + 8, fill=base, outline=shade(base if base.startswith("#") else "#ff2222", 0.6))
                cv.create_oval(cx - 4, cy - 9, cx + 1, cy - 4, fill="white", outline="")
        elif t == "resistor":
            p1 = self.project_local(c, 0.3, h / 2, 0.8)
            p2 = self.project_local(c, w * 0.28, h / 2, 0.8)
            p3 = self.project_local(c, w * 0.72, h / 2, 0.8)
            p4 = self.project_local(c, w - 0.3, h / 2, 0.8)
            cv.create_line(p1, p2, fill="#c0c6ce", width=2)
            cv.create_line(p3, p4, fill="#c0c6ce", width=2)
            cv.create_line(p2, p3, fill="#d7a86e", width=9)
            for i, col in enumerate(("#ff2222", "#ff2222", "#795548", "#ffd700")):
                tt = 0.36 + i * 0.10
                q1 = self.project_local(c, w * tt, h / 2 - 0.5, 0.9)
                q2 = self.project_local(c, w * tt, h / 2 + 0.5, 0.9)
                cv.create_line(q1, q2, fill=col, width=3)
        elif t == "button":
            self._box(cv, c, 0, 0, w, h, 0, 0.9, "#263238")
            cx, cy = self.project_local(c, w / 2, h / 2, 1.8)
            cv.create_oval(cx - 9, cy - 8, cx + 9, cy + 8,
                           fill="#69f0ae" if c["id"] in self.pressed else "#ef5350", outline="white")
        elif t == "bat18650":
            self._box(cv, c, 0, 0, w, h, 0, 1.2, "#1b5e20")
            cx, cy = self.project_local(c, w / 2, h / 2, 2.6)
            cv.create_oval(cx - 26, cy - 10, cx + 26, cy + 10, fill="#2e7d32", outline="#a5d6a7", width=2)
            cv.create_text(cx, cy, text="18650", fill="white", font=("Segoe UI", 7))
        elif t == "pot":
            self._box(cv, c, 0, 0, w, h, 0, 1.8, "#263238")
            cx, cy = self.project_local(c, w / 2, h / 2, 3.6)
            cv.create_oval(cx - 8, cy - 8, cx + 8, cy + 8, fill="#b0bec5", outline="white")
        elif t == "servo":
            self._box(cv, c, 0, 0, w, h, 0, ztop * 0.6, "#1565c0")
            cx, cy = self.project_local(c, w / 2, h / 2, ztop * 0.75)
            cv.create_line(cx - 14, cy, cx + 14, cy, fill="white", width=4)
        elif t == "cap" or t == "buzzer":
            self._box(cv, c, 0, 0, w, h, 0, 0.5, color)
            cx, cy = self.project_local(c, w / 2, h / 2, ztop * 0.8)
            cv.create_oval(cx - 9, cy - 9, cx + 9, cy + 9, fill=color, outline="#c0c6ce", width=2)
        else:
            self._box(cv, c, 0, 0, w, h, 0, ztop * 0.7, color)
        # подпись и выделение
        ew, eh = self.eff_size(c)
        cx, cy = self.project(c["x"] + ew / 2, c["y"] + eh / 2, ztop + 0.4)
        label = {"resistor": f"R {c['props'].get('ohms', '220')}Ω", "tft18": "TFT 1.8\""}.get(t, title)
        cv.create_text(cx, cy, text=label[:20], fill="white", font=("Segoe UI", 8))
        if sel:
            poly = [self.project(c["x"], c["y"], ztop + 0.5), self.project(c["x"] + ew, c["y"], ztop + 0.5),
                    self.project(c["x"] + ew, c["y"] + eh, ztop + 0.5), self.project(c["x"], c["y"] + eh, ztop + 0.5)]
            cv.create_polygon(poly, outline="#ffff00", fill="", width=2)

    def _draw_wire(self, cv, w, state):
        a = next((c for c in self.components if c["id"] == w["a"][0]), None)
        b = next((c for c in self.components if c["id"] == w["b"][0]), None)
        if not a or not b:
            return
        pa = next(p for p in CATALOG[a["type"]][5] if p[0] == w["a"][1])
        pb = next(p for p in CATALOG[b["type"]][5] if p[0] == w["b"][1])
        ax, ay = self.pin_world(a, pa); bx, by = self.pin_world(b, pb)
        az, bz = self.pin_height(a), self.pin_height(b)
        if w["style"] == "solder":
            pts3 = [(ax, ay, 0.12), (bx, ay, 0.12), (bx, by, 0.12)]
            pts = [self.project(*p) for p in pts3]
            if w["quality"] == "hot":
                cv.create_line(pts, fill="#3e2723", width=7)
            col = {"cold": "#7d838a", "hot": "#5f564e"}.get(w["quality"], "#b9c2c9")
            cv.create_line(pts, fill=col, width=4)
            if w["quality"] == "good":
                cv.create_line(pts, fill="#f4f8fa", width=1)
        else:
            topz = max(az, bz) + 2.6
            pts = []
            for i in range(17):
                tt = i / 16.0
                mt = 1 - tt
                x = mt**3 * ax + 3 * mt * mt * tt * ax + 3 * mt * tt * tt * bx + tt**3 * bx
                y = mt**3 * ay + 3 * mt * mt * tt * ay + 3 * mt * tt * tt * by + tt**3 * by
                z = mt**3 * az + 3 * mt * mt * tt * topz + 3 * mt * tt * tt * topz + tt**3 * bz
                pts.append(self.project(x, y, z))
            cv.create_line(pts, fill=w["color"], width=3, smooth=True)
        # наплывы припоя на концах
        for (wx, wy) in ((ax, ay), (bx, by)):
            sx, sy = self.project(wx, wy, 0.35)
            if w.get("flux"):
                cv.create_oval(sx - 9, sy - 6, sx + 9, sy + 6, fill="#d9a441", outline="", stipple="gray50")
            col = {"cold": "#878e96", "hot": "#6e6259"}.get(w["quality"], "#dde5ea")
            cv.create_oval(sx - 6, sy - 5, sx + 6, sy + 5, fill=col, outline="#5f6b73")
            if w["quality"] == "cold":
                cv.create_line(sx - 4, sy - 2, sx + 3, sy + 3, fill="#37474f", width=1)
                cv.create_line(sx + 4, sy - 3, sx - 3, sy + 1, fill="#37474f", width=1)
            elif w["quality"] == "good":
                cv.create_oval(sx - 3, sy - 3, sx, sy - 1, fill="white", outline="")

    # ---------- мышь ----------
    def hit_pin(self, sx, sy):
        best, bestd = None, 20
        for c in self.components:
            for pin in CATALOG[c["type"]][5]:
                wx, wy = self.pin_world(c, pin)
                px, py = self.project(wx, wy, self.pin_height(c))
                d = math.hypot(sx - px, sy - py)
                if d < bestd:
                    bestd, best = d, (c, pin[0])
        return best

    def hit_comp(self, sx, sy):
        for c in reversed(self.components):
            ew, eh = self.eff_size(c)
            z = CATALOG[c["type"]][3] / 2.54
            poly = [self.project(c["x"], c["y"], z), self.project(c["x"] + ew, c["y"], z),
                    self.project(c["x"] + ew, c["y"] + eh, z), self.project(c["x"], c["y"] + eh, z)]
            # точка в полигоне
            inside = False
            j = len(poly) - 1
            for i in range(len(poly)):
                xi, yi = poly[i]; xj, yj = poly[j]
                if ((yi > sy) != (yj > sy)) and (sx < (xj - xi) * (sy - yi) / (yj - yi) + xi):
                    inside = not inside
                j = i
            if inside:
                return c
        return None

    def on_canvas_down(self, e):
        if self.placing_type:
            wx, wy = self.unproject(e.x, e.y)
            ew, eh = CATALOG[self.placing_type][1], CATALOG[self.placing_type][2]
            self.add_component(self.placing_type, int(wx - ew / 2), int(wy - eh / 2))
            self.placing_type = None
            self.mode = "move"
            self._highlight_modes()
            self.refresh_all()
            return
        hp = self.hit_pin(e.x, e.y)
        hc = self.hit_comp(e.x, e.y)
        if self.mode in ("wire", "solder") and hp:
            comp, pin_name = hp
            if self.pending_pin is None:
                self.pending_pin = (comp["id"], pin_name)
                self.log(f"{'Паяльник' if self.mode == 'solder' else 'Провод'}: выбран {CATALOG[comp['type']][0]}.{pin_name} — кликните второй штырь")
            elif self.pending_pin == (comp["id"], pin_name):
                self.pending_pin = None
            else:
                q = self.solder_quality() if self.mode == "solder" else "good"
                self.wires.append({
                    "a": self.pending_pin, "b": (comp["id"], pin_name),
                    "style": "solder" if self.mode == "solder" else "wire",
                    "color": WIRE_COLORS[len(self.wires) % len(WIRE_COLORS)] if self.mode == "wire" else "#90a4ae",
                    "quality": q, "tempC": int(self.solder_temp.get()), "flux": bool(self.use_flux.get())})
                a_name = next(c for c in self.components if c["id"] == self.pending_pin[0])
                if self.mode == "solder":
                    verdict = {"good": "✨ блестящий наплыв", "cold": "🥶 ХОЛОДНАЯ пайка — перепаяйте при 320–380°C с флюсом",
                               "hot": "🔥 ПЕРЕГРЕВ — поджог платы, перепаяйте при 320–380°C"}[q]
                    self.log(f"Припаяно при {int(self.solder_temp.get())}°C: {CATALOG[a_name['type']][0]}.{self.pending_pin[1]} — {CATALOG[comp['type']][0]}.{pin_name}. {verdict}")
                else:
                    self.log(f"Провод: {CATALOG[a_name['type']][0]}.{self.pending_pin[1]} — {CATALOG[comp['type']][0]}.{pin_name}")
                self.pending_pin = None
            self.refresh_all()
            return
        if self.mode == "delete" and hc:
            self.components.remove(hc)
            self.wires = [w for w in self.wires if w["a"][0] != hc["id"] and w["b"][0] != hc["id"]]
            if self.selected == hc["id"]:
                self.selected = None
            self.refresh_all()
            return
        if self.mode == "sim" and hc:
            if hc["type"] == "button":
                if hc["id"] in self.pressed:
                    self.pressed.discard(hc["id"])
                else:
                    self.pressed.add(hc["id"])
                self.log("Кнопка " + ("НАЖАТА" if hc["id"] in self.pressed else "отпущена"))
            elif hc["type"] == "pot":
                v = (int(hc["props"].get("value", "512")) + 128) % 1024
                hc["props"]["value"] = str(v)
                self.log(f"Потенциометр: {v}/1023")
            else:
                self.selected = hc["id"]
            self.refresh_all()
            return
        if self.mode == "camera":
            self.drag_start = (e.x, e.y, float(self.angle.get()))
            return
        # move
        if hc:
            self.selected = hc["id"]
            wx, wy = self.unproject(e.x, e.y)
            self.drag_comp = (hc, wx - hc["x"], wy - hc["y"])
        else:
            self.selected = None
        self.refresh_all()

    def on_canvas_move(self, e):
        if self.mode == "camera" and self.drag_start:
            x0, _y0, a0 = self.drag_start
            self.angle.set(max(-80, min(80, a0 + (e.x - x0) * 0.15)))
            self.draw()
            return
        if self.drag_comp:
            hc, offx, offy = self.drag_comp
            wx, wy = self.unproject(e.x, e.y)
            ew, eh = self.eff_size(hc)
            hc["x"] = max(0, min(self.COLS - ew, int(round(wx - offx))))
            hc["y"] = max(0, min(self.ROWS - eh, int(round(wy - offy))))
            self.draw()

    def on_canvas_up(self, e):
        self.drag_comp = None
        self.drag_start = None
        self.refresh_placed()
        self.draw()

    def on_wheel(self, e):
        z = float(self.zoom.get()) + (10 if e.delta > 0 else -10)
        self.zoom.set(max(50, min(220, z)))
        self.draw()

    # список деталей: перетаскивание
    def on_list_press(self, e):
        idx = self.comp_list.nearest(e.y)
        if idx >= 0:
            self.comp_list.selection_clear(0, "end")
            self.comp_list.selection_set(idx)
            self.list_drag_type = list(CATALOG.keys())[idx]
        else:
            self.list_drag_type = None

    def on_list_double(self, _e):
        if self.list_drag_type:
            self.add_component(self.list_drag_type, 4, 4)
            self.mode = "move"
            self._highlight_modes()
            self.refresh_all()

    def on_list_drag_motion(self, e):
        if not self.list_drag_type:
            return
        # указатель над холстом?
        px, py = self.root.winfo_pointerxy()
        cx = self.canvas.winfo_rootx(); cy = self.canvas.winfo_rooty()
        if cx <= px <= cx + self.canvas.winfo_width() and cy <= py <= cy + self.canvas.winfo_height():
            self.canvas.config(cursor="crosshair")
        else:
            self.canvas.config(cursor="")

    def on_list_drag_release(self, e):
        if not self.list_drag_type:
            return
        type_id = self.list_drag_type
        self.list_drag_type = None
        self.canvas.config(cursor="")
        px, py = self.root.winfo_pointerxy()
        cx = self.canvas.winfo_rootx(); cy = self.canvas.winfo_rooty()
        if cx <= px <= cx + self.canvas.winfo_width() and cy <= py <= cy + self.canvas.winfo_height():
            wx, wy = self.unproject(px - cx, py - cy)
            ew, eh = CATALOG[type_id][1], CATALOG[type_id][2]
            self.add_component(type_id, int(wx - ew / 2), int(wy - eh / 2))
            self.mode = "move"
            self._highlight_modes()
            self.refresh_all()
        else:
            # не дотянули до платы — режим «клик-клик»
            self.placing_type = type_id
            self.log("Кликните по плате, чтобы поставить: " + CATALOG[type_id][0])

    def on_placed_select(self, _e):
        sel = self.placed_list.curselection()
        if not sel:
            return
        idx = sel[0]
        if 0 <= idx < len(self.components):
            self.selected = self.components[idx]["id"]
            if self.components[idx]["type"] == "esp32":
                try:
                    self.tabs.select(1)  # Инструменты: там выходы ESP
                except tk.TclError:
                    pass
            self.refresh_all()

    # ---------- действия ----------
    def rotate_selected(self):
        c = self.selected_comp()
        if c:
            c["rot"] = (c["rot"] + 90) % 360
            ew, eh = self.eff_size(c)
            c["x"] = max(0, min(self.COLS - ew, c["x"]))
            c["y"] = max(0, min(self.ROWS - eh, c["y"]))
            self.refresh_all()

    def delete_selected(self):
        c = self.selected_comp()
        if c:
            self.components.remove(c)
            self.wires = [w for w in self.wires if w["a"][0] != c["id"] and w["b"][0] != c["id"]]
            self.selected = None
            self.refresh_all()

    def cycle_props(self):
        c = self.selected_comp()
        if not c:
            return
        t = c["type"]
        if t == "resistor":
            vals = ["10", "47", "100", "220", "470", "1000", "4700", "10000"]
            cur = c["props"].get("ohms", "220")
            c["props"]["ohms"] = vals[(vals.index(cur) + 1) % len(vals)] if cur in vals else "220"
        elif t == "led":
            vals = ["Красный", "Зелёный", "Синий", "Жёлтый", "Белый"]
            cur = c["props"].get("colorName", "Красный")
            c["props"]["colorName"] = vals[(vals.index(cur) + 1) % len(vals)]
        elif t in ("bat18650", "bat9v", "lipo"):
            vals = ["3.7", "5", "9", "12"]
            cur = c["props"].get("voltage", "3.7")
            c["props"]["voltage"] = vals[(vals.index(cur) + 1) % len(vals)]
        elif t == "nrf24":
            vals = ["76", "10", "40", "100", "125"]
            cur = c["props"].get("channel", "76")
            c["props"]["channel"] = vals[(vals.index(cur) + 1) % len(vals)]
        elif t == "pot":
            c["props"]["value"] = str((int(c["props"].get("value", "512")) + 255) % 1024)
        self.refresh_all()

    def load_example(self, name):
        self.code_text.delete("1.0", "end")
        self.code_text.insert("1.0", EXAMPLES[name])

    def flash_and_run(self):
        if self.runner:
            self.runner.stop()
        bad = [w for w in self.wires if w["style"] == "solder" and w["quality"] != "good"]
        for w in bad:
            self.log(("🥶 Холодная" if w["quality"] == "cold" else "🔥 Перегретая") +
                     f" пайка ({w['tempC']}°C) — контакт ненадёжен, перепаяйте при 320–380°C.")
        self.log("esptool: erase → write @0x10000 → verify OK → hard reset. Прошивка запущена!")
        self.running = True
        self.run_btn.config(text="⏹ СТОП", bg="#7f1d1d")
        self.runner = Runner(self, self.code_text.get("1.0", "end"))
        self.runner.start()
        self.refresh_all()

    def toggle_run(self):
        if self.running:
            self.running = False
            if self.runner:
                self.runner.stop()
            self.pin_outputs.clear()
            self.run_btn.config(text="▶ ЗАПУСК", bg="#14532d")
            self.log("Симуляция остановлена")
            self.refresh_all()
        else:
            self.flash_and_run()

    def toggle_esp_pin(self):
        sel = self.esp_pin_list.curselection()
        esp = next((c for c in self.components if c["type"] == "esp32"), None)
        if not sel or not esp:
            return
        pin_name = self.esp_pin_list.get(sel[0]).split()[0]
        key = (esp["id"], pin_name)
        new_val = 0 if (self.pin_outputs.get(key) == 1 and self.pin_modes.get(key) == "OUTPUT") else 1
        self.pin_outputs[key] = new_val
        self.pin_modes[key] = "OUTPUT"
        self.log(f"Выход ESP {pin_name} вручную: {'HIGH (3.3В)' if new_val else 'LOW'}")
        self.refresh_all()

    def import_stl(self):
        path = filedialog.askopenfilename(title="Выберите STL-корпус", filetypes=[("STL", "*.stl"), ("Все файлы", "*.*")])
        if not path:
            return
        try:
            tris, name = parse_stl(path)
            pts = [v for t in tris for v in t]
            xs = pts[0::3]; ys = pts[1::3]; zs = pts[2::3]
            self.stl = (tris, name)
            self.show_stl.set(True)
            self.stl_label.config(
                text=f"{name}: {len(tris)} треугольников, {max(xs)-min(xs):.1f}×{max(ys)-min(ys):.1f}×{max(zs)-min(zs):.1f} мм")
            self.log(f"STL-корпус загружен: {name} ({len(tris)} треуг.) — каркас поверх платы.")
            self.draw()
        except Exception as exc:
            messagebox.showerror("STL", f"Не удалось загрузить STL:\n{exc}")

    # ---------- сохранение ----------
    def save_project(self):
        path = filedialog.asksaveasfilename(title="Сохранить проект", defaultextension=".json",
                                            filetypes=[("JSON проект", "*.json")])
        if not path:
            return
        data = {
            "components": self.components,
            "wires": [{"a": list(w["a"]), "b": list(w["b"]), "style": w["style"], "color": w["color"],
                       "quality": w["quality"], "tempC": w["tempC"], "flux": w["flux"]} for w in self.wires],
            "code": self.code_text.get("1.0", "end"),
        }
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
        self.log("Проект сохранён: " + path)

    def load_project(self):
        path = filedialog.askopenfilename(title="Открыть проект", filetypes=[("JSON проект", "*.json")])
        if not path:
            return
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        self.components = data.get("components", [])
        self.wires = [{"a": tuple(w["a"]), "b": tuple(w["b"]), "style": w["style"], "color": w["color"],
                       "quality": w.get("quality", "good"), "tempC": w.get("tempC", 350), "flux": w.get("flux", True)}
                      for w in data.get("wires", [])]
        self.code_text.delete("1.0", "end")
        self.code_text.insert("1.0", data.get("code", ""))
        self.selected = None
        self.log("Проект открыт: " + path)
        self.refresh_all()

    # ---------- обновление панелей ----------
    def refresh_placed(self):
        self.placed_list.delete(0, "end")
        for c in self.components:
            mark = "▶ " if c["id"] == self.selected else ""
            self.placed_list.insert("end", f"{mark}{CATALOG[c['type']][0]} ({c['x']},{c['y']})")

    def refresh_esp(self):
        esp = next((c for c in self.components if c["type"] == "esp32"), None)
        self.esp_text.config(state="normal")
        self.esp_text.delete("1.0", "end")
        self.esp_pin_list.delete(0, "end")
        if not esp:
            self.esp_text.insert("1.0", "ESP32 нет на плате.\nПеретащите его из вкладки «Детали».")
        else:
            state = self.circuit.compute()
            powered = esp["id"] in state["esp_powered"]
            lines = ["⚡ ESP запитан" if powered else "❌ ESP без питания (+→VIN, −→GND)"]
            for (name, _dx, _dy) in CATALOG["esp32"][5]:
                if name == "GND2":
                    continue
                key = (esp["id"], name)
                mode = self.pin_modes.get(key)
                out = self.pin_outputs.get(key)
                if mode == "OUTPUT":
                    txt = f"{name:<5} OUT {'HIGH 3.3В' if out == 1 else 'LOW  0В'}"
                elif mode and mode.startswith("INPUT"):
                    v = self.circuit.digital_read(esp["id"], name) if (powered and self.running) else -1
                    txt = f"{name:<5} IN  {('HIGH' if v == 1 else 'LOW') if v >= 0 else '—'}"
                elif name in ("VIN", "3V3"):
                    txt = f"{name:<5} питание"
                elif name.startswith("GND"):
                    txt = f"{name:<5} земля"
                else:
                    txt = f"{name:<5} не настроен"
                lines.append(txt)
                if name.startswith("D") or name in ("TX", "RX"):
                    self.esp_pin_list.insert("end", txt)
            self.esp_text.insert("1.0", "\n".join(lines))
        self.esp_text.config(state="disabled")

    def refresh_all(self):
        self.draw()
        self.refresh_placed()
        self.refresh_esp()
        c = self.selected_comp()
        state = self.circuit.compute()
        n_esp = sum(1 for x in self.components if x["type"] == "esp32")
        self.status.config(
            text=f"Деталей: {len(self.components)} • Соединений: {len(self.wires)} • ESP: {n_esp} • "
                 + ("▶ симуляция идёт" if self.running else "симуляция остановлена"))
        self.run_btn.config(text="⏹ СТОП" if self.running else "▶ ЗАПУСК",
                            bg="#7f1d1d" if self.running else "#14532d")
        if c:
            extra = ""
            if c["type"] == "led":
                br, ma, burnt = state["leds"].get(c["id"], (0, 0, False))
                extra = f"\nТок: {ma:.1f} мА" + (" 💥 СГОРЕЛ" if burnt else (f", свет {int(br*100)}%" if br > 0 else ", не горит"))
            if c["type"] == "esp32":
                extra = "\n⚡ запитан" if c["id"] in state["esp_powered"] else "\n❌ без питания: +→VIN, −→GND"
            props = ", ".join(f"{k}={v}" for k, v in c["props"].items())
            self.sel_label.config(text=f"{CATALOG[c['type']][0]}\nПозиция ({c['x']},{c['y']}), поворот {c['rot']}°\n{props}{extra}")
        else:
            self.sel_label.config(text="Ничего не выбрано. Кликните деталь на плате или в списке.")


def main():
    root = tk.Tk()
    App(root)
    root.mainloop()


if __name__ == "__main__":
    main()
