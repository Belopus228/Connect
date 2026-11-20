package com.belopus.connect

import android.content.Context
import android.os.Handler
import android.os.Looper
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** Глобальное состояние приложения */
object AppState {
    var project: Project = Project()
    var selectedId: String? = null
    var running: Boolean = false
    var mode: String = "move" // move | wire | solder | delete | sim
    var caseModel: StlModel? = null
    var casePresetName: String = ""

    val pressedButtons: MutableSet<String> = mutableSetOf()
    /** выходы ESP: "espId:D13" -> 1/0 */
    val pinOutputs: MutableMap<String, Int> = mutableMapOf()
    val pinModes: MutableMap<String, String> = mutableMapOf()

    val simLog: MutableList<String> = mutableListOf()
    val fwLog: MutableList<String> = mutableListOf()

    var runner: FirmwareRunner? = null

    private val listeners = mutableListOf<() -> Unit>()
    fun addListener(l: () -> Unit) { listeners.add(l) }
    fun removeListener(l: () -> Unit) { listeners.remove(l) }
    fun notifyChanged() {
        // копия — листенеры могут отписываться внутри
        listeners.toList().forEach { try { it() } catch (_: Exception) {} }
    }

    fun log(s: String) {
        val line = "▸ $s"
        simLog.add(line)
        if (simLog.size > 300) simLog.removeAt(0)
        notifyChanged()
    }

    fun fwLogLine(s: String) {
        fwLog.add(s)
        if (fwLog.size > 300) fwLog.removeAt(0)
        notifyChanged()
    }

    fun selected(): PlacedComponent? = project.components.firstOrNull { it.id == selectedId }

    fun compById(id: String): PlacedComponent? = project.components.firstOrNull { it.id == id }

    fun addComponent(typeId: String): PlacedComponent {
        val def = Catalog.byId(typeId)
        // ищем свободное место
        var px = 2; var py = 2
        outer@ for (y in 1..(project.boardRows - def.hGrid)) {
            for (x in 1..(project.boardCols - def.wGrid)) {
                val clash = project.components.any { c ->
                    x < c.gridX + c.effW() && x + def.wGrid > c.gridX &&
                        y < c.gridY + c.effH() && y + def.hGrid > c.gridY
                }
                if (!clash) { px = x; py = y; break@outer }
            }
        }
        val c = PlacedComponent(
            typeId = typeId, gridX = px, gridY = py,
            label = def.title
        )
        c.props.putAll(def.defaultProps)
        project.components.add(c)
        selectedId = c.id
        log("Добавлено: ${def.title} на плату (${px},${py})")
        notifyChanged()
        return c
    }

    fun deleteComponent(id: String) {
        project.components.removeAll { it.id == id }
        project.wires.removeAll { it.aComp == id || it.bComp == id }
        pressedButtons.remove(id)
        if (selectedId == id) selectedId = null
        notifyChanged()
    }

    fun startSimulation(context: Context) {
        if (!project.flashed) {
            log("⚠ Прошивка ещё не залита! Но запускаю с текущей (вкладка «Код ESP» → «Залить прошивку»).")
            project.flashed = true
        }
        running = true
        runner?.stop()
        runner = FirmwareRunner(project.firmwareCode)
        val c = CircuitSim.compute()
        if (c.espPowered.isEmpty()) {
            log("⚠ ESP32 НЕ запитан! Подключите батарею: «+» → VIN (или 3V3), «−» → GND и припаяйте провода.")
        } else {
            log("⚡ ESP32 запитан (${c.espPowered.size} шт). Симуляция запущена ▶")
        }
        c.leds.forEach { (id, st) ->
            val comp = compById(id)
            if (st.burnt) log("💥 СГОРЕЛ светодиод ${comp?.label} — ток ${"%.0f".format(st.currentMa)} мА без резистора!")
            else if (st.currentMa > 25) log("⚠ Светодиод ${comp?.label}: ток ${"%.1f".format(st.currentMa)} мА — многовато, поставьте резистор побольше")
        }
        runner?.start()
        notifyChanged()
    }

    fun stopSimulation() {
        running = false
        runner?.stop()
        runner = null
        pinOutputs.clear()
        log("⏹ Симуляция остановлена")
        notifyChanged()
    }

    // ---------- сохранение ----------
    fun save(ctx: Context) {
        try {
            val o = JSONObject()
            o.put("name", project.name)
            o.put("cols", project.boardCols); o.put("rows", project.boardRows)
            o.put("code", project.firmwareCode)
            o.put("bin", project.firmwareBinName)
            o.put("stl", project.stlName)
            o.put("preset", casePresetName)
            o.put("flashed", project.flashed)
            val ca = JSONArray()
            project.components.forEach { c ->
                val j = JSONObject()
                j.put("id", c.id); j.put("t", c.typeId); j.put("x", c.gridX); j.put("y", c.gridY)
                j.put("r", c.rotation); j.put("label", c.label)
                j.put("inCase", c.insideCase); j.put("cx", c.caseXmm.toDouble())
                j.put("cy", c.caseYmm.toDouble()); j.put("cz", c.caseZmm.toDouble())
                val p = JSONObject(); c.props.forEach { (k, v) -> p.put(k, v) }
                j.put("props", p)
                ca.put(j)
            }
            o.put("components", ca)
            val wa = JSONArray()
            project.wires.forEach { w ->
                val j = JSONObject()
                j.put("id", w.id); j.put("a", w.aComp); j.put("ap", w.aPin)
                j.put("b", w.bComp); j.put("bp", w.bPin); j.put("s", w.style); j.put("c", w.color)
                wa.put(j)
            }
            o.put("wires", wa)
            File(ctx.filesDir, "project.json").writeText(o.toString())
            caseModel?.let {
                // исходный STL уже лежит в filesDir/case.stl при импорте; для пресетов сохраняем ASCII
                if (casePresetName.isNotEmpty()) {
                    File(ctx.filesDir, "case_preset.txt").writeText(casePresetName)
                }
            }
        } catch (e: Exception) {
            android.util.Log.e("Connect", "save", e)
        }
    }

    fun load(ctx: Context) {
        try {
            val f = File(ctx.filesDir, "project.json")
            if (!f.exists()) { loadDemo(); return }
            val o = JSONObject(f.readText())
            val p = Project()
            p.name = o.optString("name", "Мой проект")
            p.boardCols = o.optInt("cols", 30); p.boardRows = o.optInt("rows", 18)
            p.firmwareCode = o.optString("code", FirmwareExamples.BLINK)
            p.firmwareBinName = o.optString("bin", "")
            p.stlName = o.optString("stl", "")
            p.flashed = o.optBoolean("flashed", false)
            casePresetName = o.optString("preset", "")
            val ca = o.optJSONArray("components") ?: JSONArray()
            for (i in 0 until ca.length()) {
                val j = ca.getJSONObject(i)
                val c = PlacedComponent(
                    id = j.optString("id"), typeId = j.optString("t"),
                    gridX = j.optInt("x"), gridY = j.optInt("y"), rotation = j.optInt("r"),
                    label = j.optString("label")
                )
                c.insideCase = j.optBoolean("inCase", false)
                c.caseXmm = j.optDouble("cx", 5.0).toFloat()
                c.caseYmm = j.optDouble("cy", 5.0).toFloat()
                c.caseZmm = j.optDouble("cz", 2.0).toFloat()
                val pr = j.optJSONObject("props")
                if (pr != null) {
                    val keys = pr.keys()
                    while (keys.hasNext()) { val k = keys.next(); c.props[k] = pr.optString(k) }
                }
                p.components.add(c)
            }
            val wa = o.optJSONArray("wires") ?: JSONArray()
            for (i in 0 until wa.length()) {
                val j = wa.getJSONObject(i)
                p.wires.add(
                    Wire(
                        id = j.optString("id"), aComp = j.optString("a"), aPin = j.optString("ap"),
                        bComp = j.optString("b"), bPin = j.optString("bp"),
                        style = j.optString("s", "wire"), color = j.optInt("c", 0xFFD32F2F.toInt())
                    )
                )
            }
            project = p
            // STL
            val stlFile = File(ctx.filesDir, "case.stl")
            if (stlFile.exists() && p.stlName.isNotEmpty()) {
                try { caseModel = StlParser.parse(stlFile.readBytes(), p.stlName) } catch (_: Exception) {}
            } else if (casePresetName.isNotEmpty()) {
                caseModel = StlPresets.byName(casePresetName)
            }
        } catch (e: Exception) {
            android.util.Log.e("Connect", "load", e)
            loadDemo()
        }
    }

    fun loadDemo() {
        val p = Project()
        p.firmwareCode = FirmwareExamples.BLINK
        project = p
        fun add(type: String, x: Int, y: Int): PlacedComponent {
            val d = Catalog.byId(type)
            val c = PlacedComponent(typeId = type, gridX = x, gridY = y, label = d.title)
            c.props.putAll(d.defaultProps)
            p.components.add(c); return c
        }
        val bat = add("bat18650", 1, 1)
        val esp = add("esp32", 14, 1)
        // esp h=20 but board rows 18 -> move: rows default 22 for demo
        p.boardRows = 22; p.boardCols = 34
        esp.gridX = 20; esp.gridY = 1
        val res = add("resistor", 8, 12)
        res.props["ohms"] = "220"
        val led = add("led", 8, 16)
        val btn = add("button", 14, 14)
        val oled = add("oled", 2, 6)
        val nrf = add("nrf24", 27, 15)
        fun wire(a: PlacedComponent, ap: String, b: PlacedComponent, bp: String, color: Int, style: String = "wire") {
            p.wires.add(Wire(aComp = a.id, aPin = ap, bComp = b.id, bPin = bp, color = color, style = style))
        }
        wire(bat, "+", esp, "VIN", 0xFFD32F2F.toInt())
        wire(bat, "-", esp, "GND", 0xFF212121.toInt())
        wire(esp, "D13", res, "1", 0xFFFF6F00.toInt())
        wire(res, "2", led, "A", 0xFFFF6F00.toInt(), "solder")
        wire(led, "K", esp, "GND", 0xFF212121.toInt())
        wire(btn, "1", esp, "D32", 0xFF1565C0.toInt())
        wire(btn, "2", esp, "GND", 0xFF212121.toInt())
        wire(oled, "VCC", esp, "3V3", 0xFFD32F2F.toInt())
        wire(oled, "GND", esp, "GND", 0xFF212121.toInt())
        wire(oled, "SCL", esp, "D22", 0xFF6A1B9A.toInt())
        wire(oled, "SDA", esp, "D21", 0xFF00838F.toInt())
        wire(nrf, "VCC", esp, "3V3", 0xFFD32F2F.toInt())
        wire(nrf, "GND", esp, "GND", 0xFF212121.toInt())
        casePresetName = "Корпус 100×70×25 мм"
        caseModel = StlPresets.byName(casePresetName)
        listOf(bat, esp, led, res).forEach { it.insideCase = true }
        bat.caseXmm = 5f; bat.caseYmm = 5f
        esp.caseXmm = 35f; esp.caseYmm = 8f
        led.caseXmm = 12f; led.caseYmm = 45f
        res.caseXmm = 20f; res.caseYmm = 45f
        simLog.add("👋 Добро пожаловать в Connect! Загружена демо-схема: ESP32 + LED + кнопка + OLED + nRF24.")
        simLog.add("👉 Нажмите ▶ Симуляция → Запуск, или «Залить прошивку» во вкладке «Код ESP».")
    }
}

// ----------------- симуляция цепей -----------------

data class LedState(val brightness: Float, val currentMa: Float, val burnt: Boolean, val powered: Boolean)
data class CircuitState(
    val espPowered: Set<String>,
    val leds: Map<String, LedState>,
    val netVoltage: Map<String, Double>
)

object CircuitSim {

    private class UF {
        val parent = mutableMapOf<String, String>()
        fun find(x: String): String {
            var r = x
            while (parent[r] != null && parent[r] != r) r = parent[r]!!
            parent[x] = r
            return r
        }
        fun union(a: String, b: String) {
            val ra = find(a); val rb = find(b)
            if (ra != rb) parent[ra] = rb
        }
    }

    private fun node(c: String, p: String) = "$c|$p"

    fun compute(): CircuitState {
        val proj = AppState.project
        val uf = UF()
        proj.components.forEach { c ->
            c.def().pins.forEach { p -> uf.find(node(c.id, p.name)) }
        }
        proj.wires.forEach { w -> uf.union(node(w.aComp, w.aPin), node(w.bComp, w.bPin)) }
        // нажатые кнопки замыкают 1-2 и 3-4
        proj.components.filter { it.typeId == "button" && AppState.pressedButtons.contains(it.id) }
            .forEach { b ->
                uf.union(node(b.id, "1"), node(b.id, "2"))
                uf.union(node(b.id, "3"), node(b.id, "4"))
            }

        // источники напряжения на сетях (root -> вольты)
        val sourceVolts = mutableMapOf<String, Double>()
        val groundRoots = mutableSetOf<String>()
        proj.components.forEach { c ->
            when (c.typeId) {
                "bat18650", "bat9v", "lipo" -> {
                    val v = c.props["voltage"]?.toDoubleOrNull()
                        ?: if (c.typeId == "bat9v") 9.0 else 3.7
                    sourceVolts[uf.find(node(c.id, "+"))] = v
                    groundRoots.add(uf.find(node(c.id, "-")))
                }
            }
        }
        // ESP запитан?
        val espPowered = mutableSetOf<String>()
        proj.components.filter { it.typeId == "esp32" }.forEach { esp ->
            val gndRoot = uf.find(node(esp.id, "GND"))
            val vinRoot = uf.find(node(esp.id, "VIN"))
            val v33Root = uf.find(node(esp.id, "3V3"))
            val poweredGnd = groundRoots.contains(gndRoot)
            val poweredVin = (sourceVolts[vinRoot] ?: 0.0) >= 3.3
            val powered33 = (sourceVolts[v33Root] ?: 0.0) in 3.0..3.8
            if (poweredGnd && (poweredVin || powered33)) {
                espPowered.add(esp.id)
                // ESP выдаёт 3.3В на 3V3 и HIGH на выходах
                sourceVolts[v33Root] = 3.3
                if (AppState.running) {
                    esp.def().pins.forEach { pin ->
                        if (AppState.pinOutputs["${esp.id}:${pin.name}"] == 1) {
                            sourceVolts[uf.find(node(esp.id, pin.name))] = 3.3
                        }
                    }
                }
            }
        }

        // резисторные рёбра между сетями: rootA, rootB, ohms
        data class REdge(val a: String, val b: String, val ohms: Double, val compId: String)
        val rEdges = mutableListOf<REdge>()
        proj.components.filter { it.typeId == "resistor" }.forEach { r ->
            val a = uf.find(node(r.id, "1")); val b = uf.find(node(r.id, "2"))
            rEdges.add(REdge(a, b, r.props["ohms"]?.toDoubleOrNull() ?: 220.0, r.id))
        }

        /** поиск источника через резисторы: возвращает (вольты, суммарные Ом) */
        fun findSupply(startRoot: String): Pair<Double, Double>? {
            if (sourceVolts.containsKey(startRoot)) return Pair(sourceVolts[startRoot]!!, 0.0)
            // BFS по резисторам (глубина до 3)
            val visited = mutableMapOf<String, Double>()
            var frontier = listOf(startRoot to 0.0)
            visited[startRoot] = 0.0
            repeat(3) {
                val next = mutableListOf<Pair<String, Double>>()
                for ((root, acc) in frontier) {
                    for (e in rEdges) {
                        val other = when { e.a == root -> e.b; e.b == root -> e.a; else -> null } ?: continue
                        val na = acc + e.ohms
                        if (sourceVolts.containsKey(other)) return Pair(sourceVolts[other]!!, na)
                        if ((visited[other] ?: Double.MAX_VALUE) > na) { visited[other] = na; next.add(other to na) }
                    }
                }
                frontier = next
            }
            return null
        }
        fun findGround(startRoot: String): Double? {
            if (groundRoots.contains(startRoot)) return 0.0
            // ESP LOW выход = земля
            if (isEspLow(startRoot, uf)) return 0.0
            val visited = mutableMapOf<String, Double>()
            var frontier = listOf(startRoot to 0.0)
            visited[startRoot] = 0.0
            repeat(3) {
                val next = mutableListOf<Pair<String, Double>>()
                for ((root, acc) in frontier) {
                    for (e in rEdges) {
                        val other = when { e.a == root -> e.b; e.b == root -> e.a; else -> null } ?: continue
                        val na = acc + e.ohms
                        if (groundRoots.contains(other) || isEspLow(other, uf)) return na
                        if ((visited[other] ?: Double.MAX_VALUE) > na) { visited[other] = na; next.add(other to na) }
                    }
                }
                frontier = next
            }
            return null
        }

        val leds = mutableMapOf<String, LedState>()
        proj.components.filter { it.typeId == "led" }.forEach { led ->
            val aRoot = uf.find(node(led.id, "A"))
            val kRoot = uf.find(node(led.id, "K"))
            val supply = findSupply(aRoot)
            val gndR = findGround(kRoot)
            if (supply != null && gndR != null) {
                val (volts, rAcc) = supply
                val totalR = rAcc + gndR
                val vf = 2.0
                if (volts > vf) {
                    if (totalR < 1.0) {
                        leds[led.id] = LedState(0f, 999f, burnt = true, powered = true)
                    } else {
                        val ma = (volts - vf) / totalR * 1000.0
                        val br = (ma / 20.0).toFloat().coerceIn(0.05f, 1f)
                        leds[led.id] = LedState(br, ma.toFloat(), burnt = ma > 60, powered = true)
                    }
                } else leds[led.id] = LedState(0f, 0f, false, powered = false)
            } else leds[led.id] = LedState(0f, 0f, false, powered = false)
        }
        return CircuitState(espPowered, leds, sourceVolts.mapKeys { it.key })
    }

    fun isEspLow(root: String, ufRootOf: (String) -> String): Boolean = false // заглушка, LOW обрабатывается через ground ниже

    private fun isEspLow(root: String, uf: UF): Boolean {
        if (!AppState.running) return false
        var result = false
        AppState.project.components.filter { it.typeId == "esp32" }.forEach { esp ->
            esp.def().pins.forEach { pin ->
                if (AppState.pinOutputs["${esp.id}:${pin.name}"] == 0 &&
                    AppState.pinModes["${esp.id}:${pin.name}"] == "OUTPUT"
                ) {
                    if (uf.find(node(esp.id, pin.name)) == root) result = true
                }
            }
        }
        return result
    }

    /** чтение цифрового пина ESP для интерпретатора */
    fun digitalRead(espId: String, pinName: String): Int {
        val proj = AppState.project
        val uf = UF()
        proj.components.forEach { c -> c.def().pins.forEach { p -> uf.find(node(c.id, p.name)) } }
        proj.wires.forEach { w -> uf.union(node(w.aComp, w.aPin), node(w.bComp, w.bPin)) }
        proj.components.filter { it.typeId == "button" && AppState.pressedButtons.contains(it.id) }
            .forEach { b -> uf.union(node(b.id, "1"), node(b.id, "2")); uf.union(node(b.id, "3"), node(b.id, "4")) }
        val root = uf.find(node(espId, pinName))
        // земля?
        proj.components.forEach { c ->
            if (c.typeId in listOf("bat18650", "bat9v", "lipo")) {
                if (uf.find(node(c.id, "-")) == root) return 0
            }
            if (c.typeId == "esp32") {
                if (uf.find(node(c.id, "GND")) == root) return 0
                if (uf.find(node(c.id, "3V3")) == root) return 1
                if (uf.find(node(c.id, "VIN")) == root) return 1
            }
            if (c.typeId in listOf("bat18650", "bat9v", "lipo")) {
                if (uf.find(node(c.id, "+")) == root) return 1
            }
        }
        // подтяжка вверх по умолчанию для INPUT_PULLUP
        return if (AppState.pinModes["$espId:$pinName"] == "INPUT_PULLUP") 1 else 0
    }
}

// ----------------- интерпретатор прошивки -----------------

class FirmwareRunner(private val code: String) {
    private data class Instr(val kind: String, val a: String = "", val b: String = "", val n: Long = 0)

    private val instrs: List<Instr> = parse(code)
    private val handler = Handler(Looper.getMainLooper())
    private var pc = 0
    private var stopped = false
    private val condStack = mutableListOf<Boolean>()

    private fun parse(src: String): List<Instr> {
        val out = mutableListOf<Instr>()
        val pinModeRe = Regex("""pinMode\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)""")
        val writeRe = Regex("""digitalWrite\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)""")
        val delayRe = Regex("""delay\s*\(\s*(\d+)\s*\)""")
        val serialRe = Regex("""Serial\.println\s*\(\s*"([^"]*)"\s*\)""")
        val serialRe2 = Regex("""Serial\.print\s*\(\s*"([^"]*)"\s*\)""")
        val oledRe = Regex("""(?:oled|display|lcd)\.print(?:ln)?\s*\(\s*"([^"]*)"\s*\)""")
        val oledClearRe = Regex("""(?:oled|display|lcd)\.clear\s*\(\s*\)""")
        val nrfRe = Regex("""nrf\.send\s*\(\s*"([^"]*)"\s*\)""")
        val servoRe = Regex("""servo\.write\s*\(\s*(\d+)\s*\)""")
        val ifRe = Regex("""if\s*\(\s*digitalRead\s*\(\s*(\w+)\s*\)\s*==\s*(\w+)\s*\)""")
        for (raw in src.lines()) {
            var line = raw.trim()
            if (line.isEmpty() || line.startsWith("//") || line.startsWith("#include")) continue
            if (line.startsWith("void ") || line.startsWith("int ") && line.contains("(")) continue
            if (line == "{" ) continue
            if (line.startsWith("} else") || line.startsWith("else")) { out.add(Instr("ELSE")); continue }
            if (line == "}" || line == "};") { out.add(Instr("ENDIF")); continue }
            line = line.removeSuffix(";").trim()
            val mIf = ifRe.find(line)
            val mPin = if (mIf == null) pinModeRe.find(line) else null
            val mWrite = if (mIf == null && mPin == null) writeRe.find(line) else null
            val mDelay = if (mIf == null && mPin == null && mWrite == null) delayRe.find(line) else null
            val mSerial = if (mIf == null && mPin == null && mWrite == null && mDelay == null) (serialRe.find(line) ?: serialRe2.find(line)) else null
            val mOledClear = if (mSerial == null && mIf == null && mPin == null && mWrite == null && mDelay == null) oledClearRe.find(line) else null
            val mOled = if (mSerial == null && mOledClear == null && mIf == null && mPin == null && mWrite == null && mDelay == null) oledRe.find(line) else null
            val mNrf = if (mSerial == null && mOledClear == null && mOled == null && mIf == null && mPin == null && mWrite == null && mDelay == null) nrfRe.find(line) else null
            val mServo = if (mSerial == null && mOledClear == null && mOled == null && mNrf == null && mIf == null && mPin == null && mWrite == null && mDelay == null) servoRe.find(line) else null
            when {
                mIf != null -> out.add(Instr("IF", mIf.groupValues[1], mIf.groupValues[2]))
                mPin != null -> out.add(Instr("PINMODE", mPin.groupValues[1], mPin.groupValues[2]))
                mWrite != null -> out.add(Instr("WRITE", mWrite.groupValues[1], mWrite.groupValues[2]))
                mDelay != null -> out.add(Instr("DELAY", n = mDelay.groupValues[1].toLongOrNull() ?: 0))
                mSerial != null -> out.add(Instr("SERIAL", mSerial.groupValues[1]))
                mOledClear != null -> out.add(Instr("OLED_CLEAR"))
                mOled != null -> out.add(Instr("OLED", mOled.groupValues[1]))
                mNrf != null -> out.add(Instr("NRF", mNrf.groupValues[1]))
                mServo != null -> out.add(Instr("SERVO", n = mServo.groupValues[1].toLongOrNull() ?: 90))
                line.contains("tone(") -> out.add(Instr("SERIAL", "🔊 tone() — зуммер пищит"))
            }
            // неизвестные строки игнорируем (как компилятор бы ругнулся — мы мягче)
        }
        // убираем ENDIF от setup()/loop() — лишние ENDIF без IF безвредны в исполнителе
        return out
    }

    fun start() {
        stopped = false; pc = 0; condStack.clear()
        AppState.log("🚀 Прошивка запущена на ESP32 (виртуально)")
        schedule(60)
    }

    fun stop() {
        stopped = true
        handler.removeCallbacksAndMessages(null)
    }

    private fun schedule(ms: Long) {
        if (stopped) return
        handler.postDelayed({ step() }, ms)
    }

    private fun pinName(raw: String): String {
        val t = raw.trim()
        return if (t.all { it.isDigit() }) "D$t" else t
    }

    private fun esp(): PlacedComponent? = AppState.project.components.firstOrNull { it.typeId == "esp32" }

    private fun active(): Boolean = condStack.all { it }

    private fun step() {
        if (stopped || !AppState.running) return
        val esp = esp()
        if (esp == null) { AppState.log("⚠ В схеме нет ESP32 — прошивка не выполняется"); schedule(1000); return }
        var guard = 0
        while (guard < 200) {
            guard++
            if (pc >= instrs.size) {
                pc = 0; condStack.clear()
                schedule(30)
                return
            }
            val ins = instrs[pc]; pc++
            when (ins.kind) {
                "IF" -> {
                    val v = CircuitSim.digitalRead(esp.id, pinName(ins.a))
                    val expect = when (ins.b) { "HIGH", "1" -> 1; else -> 0 }
                    condStack.add(active() && (v == expect))
                }
                "ELSE" -> {
                    if (condStack.isNotEmpty()) {
                        val parent = condStack.dropLast(1).all { it }
                        condStack[condStack.lastIndex] = parent && !condStack.last()
                    }
                }
                "ENDIF" -> if (condStack.isNotEmpty()) condStack.removeAt(condStack.lastIndex)
                else -> {
                    if (!active()) continue
                    when (ins.kind) {
                        "PINMODE" -> AppState.pinModes["${esp.id}:${pinName(ins.a)}"] = ins.b
                        "WRITE" -> {
                            val v = if (ins.b == "HIGH" || ins.b == "1") 1 else 0
                            val key = "${esp.id}:${pinName(ins.a)}"
                            if (AppState.pinOutputs[key] != v) {
                                AppState.pinOutputs[key] = v
                                AppState.pinModes[key] = "OUTPUT"
                                AppState.notifyChanged()
                            }
                        }
                        "DELAY" -> { schedule(ins.n.coerceIn(10, 5000)); return }
                        "SERIAL" -> AppState.log("Serial: ${ins.a}")
                        "OLED" -> {
                            AppState.project.components.filter { it.typeId in listOf("oled", "tft", "lcd1602") }
                                .forEach { it.props["displayText"] = ins.a }
                            AppState.log("🖥 Дисплей: «${ins.a}»")
                            AppState.notifyChanged()
                        }
                        "OLED_CLEAR" -> {
                            AppState.project.components.filter { it.typeId in listOf("oled", "tft", "lcd1602") }
                                .forEach { it.props["displayText"] = "" }
                            AppState.notifyChanged()
                        }
                        "NRF" -> {
                            val nrfs = AppState.project.components.filter { it.typeId == "nrf24" }
                            if (nrfs.isEmpty()) AppState.log("📡 nrf.send: модуль nRF24 не найден на плате!")
                            else nrfs.forEach { n ->
                                n.props["lastPacket"] = ins.a
                                AppState.log("📡 nRF24 [канал ${n.props["channel"] ?: "76"}] TX → «${ins.a}» ✔ доставлено (виртуальный эфир)")
                            }
                        }
                        "SERVO" -> {
                            AppState.project.components.filter { it.typeId == "servo" }
                                .forEach { it.props["angle"] = ins.n.toString() }
                            AppState.log("🦾 Сервопривод: угол ${ins.n}°")
                            AppState.notifyChanged()
                        }
                    }
                }
            }
        }
        schedule(30)
    }
}
