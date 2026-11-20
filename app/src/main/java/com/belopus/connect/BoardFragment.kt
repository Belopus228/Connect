package com.belopus.connect

import android.app.AlertDialog
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.view.Gravity
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.fragment.app.Fragment

class BoardFragment : Fragment() {

    private var circuitView: CircuitView? = null
    private var sceneView: Scene3DView? = null
    private var infoText: TextView? = null
    private var modeButtons = mutableListOf<Button>()
    private val listener: () -> Unit = { refreshInfo() }

    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        val root = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#ECEFF1"))
        }

        // режимы
        val modesRow = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(dp(6), dp(6), dp(6), dp(2))
        }
        val modes = listOf(
            "camera" to "🎥 Камера",
            "move" to "✥ Двигать",
            "wire" to "🔌 Провод",
            "solder" to "🔥 Паять",
            "delete" to "🗑 Удалить",
            "sim" to "👆 Нажимать"
        )
        modeButtons.clear()
        for ((key, label) in modes) {
            val b = Button(requireContext()).apply {
                text = label
                textSize = 11f
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                setOnClickListener {
                    AppState.mode = key
                    circuitView?.clearPending()
                    sceneView?.clearPending()
                    highlightModes()
                    Toast.makeText(
                        requireContext(), when (key) {
                            "camera" -> "Камера 3D: ведите палец — вращение, щипок двумя пальцами — зум"
                            "wire" -> "Провод: коснитесь пина → затем второго пина"
                            "solder" -> "Пайка: коснитесь пина → затем второго (дорожка + припой)"
                            "delete" -> "Коснитесь детали, чтобы удалить"
                            "sim" -> "Симуляция: нажимайте кнопки на плате, крутите потенциометр"
                            else -> "Перетаскивайте детали по плате (в 3D — пальцем по детали)"
                        }, Toast.LENGTH_SHORT
                    ).show()
                }
            }
            modeButtons.add(b)
            modesRow.addView(b)
        }
        root.addView(modesRow)
        highlightModes()

        // плата размера
        val boardRow = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(8), dp(2), dp(8), dp(2))
        }
        boardRow.addView(TextView(requireContext()).apply { text = "Плата: "; textSize = 13f })
        val boardSpinner = Spinner(requireContext())
        val boardOptions = listOf("Перфорация 24×16", "Перфорация 30×22 (стандарт)", "Перфорация 40×28 (большая)", "Мини 18×12")
        val boardSizes = listOf(24 to 16, 30 to 22, 40 to 28, 18 to 12)
        boardSpinner.adapter = ArrayAdapter(requireContext(), android.R.layout.simple_spinner_dropdown_item, boardOptions)
        val curIdx = boardSizes.indexOfFirst { it.first == AppState.project.boardCols }.let { if (it < 0) 1 else it }
        boardSpinner.setSelection(curIdx)
        boardRow.addView(boardSpinner)
        val applyBoard = Button(requireContext()).apply {
            text = "Применить"
            textSize = 11f
            setOnClickListener {
                val (c, r) = boardSizes[boardSpinner.selectedItemPosition]
                AppState.project.boardCols = c; AppState.project.boardRows = r
                AppState.log("Макетная плата изменена: $c×$r отверстий")
                AppState.save(requireContext())
                // пересоздать вью для перемерки
                circuitView?.requestLayout()
                circuitView?.invalidate()
                AppState.notifyChanged()
            }
        }
        boardRow.addView(applyBoard)
        val clearWires = Button(requireContext()).apply {
            text = "Снять все провода"
            textSize = 11f
            setOnClickListener {
                AlertDialog.Builder(requireContext())
                    .setTitle("Удалить все провода и пайку?")
                    .setPositiveButton("Да") { _, _ ->
                        AppState.project.wires.clear()
                        AppState.log("Все провода/пайки удалены")
                        AppState.notifyChanged(); circuitView?.invalidate()
                    }
                    .setNegativeButton("Отмена", null).show()
            }
        }
        boardRow.addView(clearWires)
        root.addView(boardRow)

        // переключатель 3D / 2D и ракурсы камеры
        val viewRow = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(dp(6), dp(4), dp(6), dp(2))
        }
        val btn3d = Button(requireContext()).apply {
            text = "🧊 3D"
            textSize = 12f
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }
        val btn2d = Button(requireContext()).apply {
            text = "▦ 2D"
            textSize = 12f
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }
        viewRow.addView(btn3d); viewRow.addView(btn2d)
        for ((label, viewName) in listOf("Изометрия" to "iso", "Сверху" to "top", "Спереди" to "front", "Сбоку" to "side")) {
            viewRow.addView(Button(requireContext()).apply {
                text = label; textSize = 10f
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                setOnClickListener {
                    btn3d.performClick()
                    sceneView?.setView(viewName)
                    AppState.mode = "camera"
                    highlightModes()
                }
            })
        }
        root.addView(viewRow)

        val caseCb = CheckBox(requireContext()).apply {
            text = "📦 Показать STL-корпус в 3D-сцене"
            textSize = 12f
            isChecked = AppState.showCaseIn3D
        }
        root.addView(caseCb)

        // область платы: 3D-сцена (основная) или 2D-плата
        val stage = FrameLayout(requireContext()).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        val scene = Scene3DView(requireContext()).apply {
            layoutParams = FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        }
        sceneView = scene
        scene.onComponentMoved = { AppState.save(requireContext()) }
        stage.addView(scene)

        val scroll = ScrollView(requireContext()).apply {
            layoutParams = FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        }
        val cv = CircuitView(requireContext())
        circuitView = cv
        cv.onComponentMoved = { AppState.save(requireContext()) }
        scroll.addView(cv)
        stage.addView(scroll)
        root.addView(stage)

        fun applyStageVisibility() {
            scene.visibility = if (AppState.board3d) View.VISIBLE else View.GONE
            scroll.visibility = if (AppState.board3d) View.GONE else View.VISIBLE
            btn3d.setBackgroundColor(Color.parseColor(if (AppState.board3d) "#BBDEFB" else "#ECEFF1"))
            btn2d.setBackgroundColor(Color.parseColor(if (AppState.board3d) "#ECEFF1" else "#BBDEFB"))
            scene.invalidate(); cv.invalidate()
        }
        btn3d.setOnClickListener {
            AppState.board3d = true
            if (AppState.mode == "move") AppState.mode = "camera"
            highlightModes()
            applyStageVisibility()
            Toast.makeText(requireContext(), "3D-вид: режим «Камера» — вращение пальцем, зум щипком", Toast.LENGTH_SHORT).show()
        }
        btn2d.setOnClickListener {
            AppState.board3d = false
            if (AppState.mode == "camera") AppState.mode = "move"
            highlightModes()
            applyStageVisibility()
        }
        caseCb.setOnCheckedChangeListener { _, checked ->
            AppState.showCaseIn3D = checked
            if (checked && AppState.caseModel == null) {
                Toast.makeText(requireContext(), "Сначала выберите корпус во вкладке «Корпус» (импорт STL или готовый)", Toast.LENGTH_LONG).show()
            }
            if (checked) AppState.board3d = true
            applyStageVisibility()
            scene.invalidate()
        }
        applyStageVisibility()

        // палитра быстрых деталей
        root.addView(TextView(requireContext()).apply {
            text = "  Быстро добавить деталь (полный каталог — вкладка «Детали»):"
            textSize = 12f; setPadding(dp(6), dp(4), dp(6), 0)
        })
        val paletteScroll = HorizontalScrollView(requireContext())
        val palette = LinearLayout(requireContext()).apply { orientation = LinearLayout.HORIZONTAL; setPadding(dp(4), dp(2), dp(4), dp(2)) }
        for (def in Catalog.defs) {
            val b = Button(requireContext()).apply {
                text = def.title
                textSize = 11f
                setOnClickListener {
                    val c = AppState.addComponent(def.id)
                    AppState.mode = "move"
                    highlightModes()
                    circuitView?.invalidate()
                    AppState.save(requireContext())
                    Toast.makeText(requireContext(), "Добавлено: ${def.title}. Двигайте её по плате.", Toast.LENGTH_SHORT).show()
                }
            }
            palette.addView(b)
        }
        paletteScroll.addView(palette)
        root.addView(paletteScroll)

        // панель выбранного
        val panel = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.WHITE)
            setPadding(dp(8), dp(6), dp(8), dp(8))
        }
        infoText = TextView(requireContext()).apply { textSize = 13f }
        panel.addView(infoText)
        val actions = LinearLayout(requireContext()).apply { orientation = LinearLayout.HORIZONTAL }
        fun actBtn(label: String, action: () -> Unit): Button {
            val b = Button(requireContext()).apply {
                text = label; textSize = 11f
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                setOnClickListener { action() }
            }
            actions.addView(b); return b
        }
        actBtn("↻ Повернуть") {
            val c = AppState.selected() ?: return@actBtn toastSel()
            c.rotation = (c.rotation + 90) % 360
            AppState.notifyChanged(); circuitView?.invalidate(); AppState.save(requireContext())
        }
        actBtn("⚙ Свойства") { cycleProps() }
        actBtn("🗑 Удалить") {
            val c = AppState.selected() ?: return@actBtn toastSel()
            AppState.deleteComponent(c.id); circuitView?.invalidate(); AppState.save(requireContext())
        }
        actBtn("📦 В корпус") {
            val c = AppState.selected() ?: return@actBtn toastSel()
            c.insideCase = !c.insideCase
            AppState.log(if (c.insideCase) "📦 ${c.def().title} помещён в корпус (координаты — вкладка «Корпус»)" else "📦 ${c.def().title} убран из корпуса")
            AppState.notifyChanged(); AppState.save(requireContext())
        }
        panel.addView(actions)
        val hint = TextView(requireContext()).apply {
            text = "💡 3D (основной вид): «🎥 Камера» — вращение/зум, детали — объёмные, провода — дугами, включите ☑ выше, чтобы видеть STL-корпус в сцене. Паять: режим «Паять» → пин батареи «+» → ESP «VIN», «−» → «GND». LED: D13 → резистор → LED(A), LED(K) → GND."
            textSize = 11f; setTextColor(Color.parseColor("#546E7A"))
        }
        panel.addView(hint)
        root.addView(panel)

        AppState.addListener(listener)
        refreshInfo()
        return root
    }

    private fun toastSel() = Toast.makeText(requireContext(), "Сначала коснитесь детали на плате", Toast.LENGTH_SHORT).show()

    private fun highlightModes() {
        val modes = listOf("camera", "move", "wire", "solder", "delete", "sim")
        modeButtons.forEachIndexed { i, b ->
            b.setBackgroundColor(if (AppState.mode == modes[i]) Color.parseColor("#BBDEFB") else Color.parseColor("#ECEFF1"))
        }
    }

    private fun cycleProps() {
        val c = AppState.selected() ?: return toastSel()
        when (c.typeId) {
            "resistor" -> {
                val vals = listOf("10", "47", "100", "220", "470", "1000", "4700", "10000")
                val cur = c.props["ohms"] ?: "220"
                val next = vals[(vals.indexOf(cur) + 1).coerceAtLeast(0) % vals.size]
                c.props["ohms"] = next
                AppState.log("Резистор: $next Ω — ток LED пересчитан")
            }
            "led" -> {
                val vals = listOf("Красный", "Зелёный", "Синий", "Жёлтый", "Белый")
                val cur = c.props["colorName"] ?: "Красный"
                c.props["colorName"] = vals[(vals.indexOf(cur) + 1) % vals.size]
            }
            "bat18650", "bat9v", "lipo" -> {
                val vals = listOf("3.7", "5", "9", "12")
                val cur = c.props["voltage"] ?: "3.7"
                c.props["voltage"] = vals[(vals.indexOf(cur) + 1) % vals.size]
            }
            "nrf24" -> {
                val vals = listOf("76", "10", "40", "100", "125")
                val cur = c.props["channel"] ?: "76"
                c.props["channel"] = vals[(vals.indexOf(cur) + 1) % vals.size]
                AppState.log("nRF24: канал ${c.props["channel"]}")
            }
            "pot" -> {
                val cur = c.props["value"]?.toIntOrNull() ?: 512
                c.props["value"] = ((cur + 255) % 1024).toString()
            }
            else -> {
                Toast.makeText(requireContext(), "У «${c.def().title}» свойства задаются прошивкой/симуляцией", Toast.LENGTH_SHORT).show()
                return
            }
        }
        AppState.notifyChanged(); circuitView?.invalidate(); AppState.save(requireContext())
        refreshInfo()
    }

    private fun refreshInfo() {
        val t = infoText ?: return
        val c = AppState.selected()
        if (!isAdded) return
        if (c == null) {
            t.text = "Ничего не выбрано. Коснитесь детали. Деталей на плате: ${AppState.project.components.size}, проводов/паек: ${AppState.project.wires.size}"
        } else {
            val props = if (c.props.isEmpty()) "" else " • " + c.props.entries.joinToString(", ") { "${it.key}=${it.value}" }
            val state = CircuitSim.compute()
            val extra = when (c.typeId) {
                "led" -> state.leds[c.id]?.let { " • ток ${"%.1f".format(it.currentMa)} мА" + if (it.burnt) " 💥 СГОРЕЛ" else if (it.brightness > 0) ", светится ${(it.brightness * 100).toInt()}%" else ", не горит" } ?: ""
                "esp32" -> if (state.espPowered.contains(c.id)) " • ⚡ запитан" else " • ❌ нет питания (нужны +→VIN, −→GND)"
                else -> ""
            }
            t.typeface = Typeface.DEFAULT
            t.text = "Выбрано: ${c.def().title} (${c.gridX},${c.gridY}) пов.${c.rotation}°${if (c.insideCase) " • в корпусе" else ""}$props$extra"
        }
        circuitView?.invalidate()
        sceneView?.invalidate()
    }

    override fun onDestroyView() {
        AppState.removeListener(listener)
        super.onDestroyView()
    }
}
