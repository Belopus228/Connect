package com.belopus.connect

import android.content.ClipData
import android.content.ClipDescription
import android.graphics.Color
import android.os.Bundle
import android.view.DragEvent
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.fragment.app.Fragment

/**
 * Главный рабочий экран (ландшафт):
 * слева — 3D/2D плата, справа — панель: компоненты (drag-and-drop), инструменты,
 * вид/камера, выбранная деталь, список деталей на плате.
 */
class BoardFragment : Fragment() {

    private var circuitView: CircuitView? = null
    private var sceneView: Scene3DView? = null
    private var infoText: TextView? = null
    private var hintOverlay: TextView? = null
    private var placedContainer: LinearLayout? = null
    private val modeButtons = mutableMapOf<String, Button>()
    private var btn3dRef: Button? = null
    private var btn2dRef: Button? = null
    private var sceneRef: Scene3DView? = null
    private var scroll2dRef: ScrollView? = null

    private val listener: () -> Unit = {
        refreshInfo()
        refreshPlaced()
    }

    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    private fun darkBtn(label: String, size: Float = 11f): Button {
        return Button(requireContext()).apply {
            text = label
            textSize = size
            isAllCaps = false
            setTextColor(Color.WHITE)
            setBackgroundColor(Color.parseColor("#334155"))
            setPadding(dp(4), 0, dp(4), 0)
        }
    }

    private fun section(parent: LinearLayout, title: String): LinearLayout {
        val card = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#16233A"))
            setPadding(dp(10), dp(8), dp(10), dp(10))
            val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            lp.bottomMargin = dp(10)
            layoutParams = lp
        }
        card.addView(TextView(requireContext()).apply {
            text = title
            textSize = 13f
            setTextColor(Color.parseColor("#7DD3FC"))
            setPadding(0, 0, 0, dp(6))
        })
        parent.addView(card)
        return card
    }

    private fun selectMode(key: String) {
        AppState.mode = key
        circuitView?.clearPending()
        sceneView?.clearPending()
        highlightModes()
        refreshInfo()
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        if (AppState.board3d) AppState.mode = "camera"

        val root = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL
            setBackgroundColor(Color.parseColor("#0B1220"))
        }

        // ---------- центр: сцена ----------
        val stage = FrameLayout(requireContext()).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f)
            setBackgroundColor(Color.parseColor("#0F1B24"))
        }
        val scene = Scene3DView(requireContext()).apply {
            layoutParams = FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        }
        sceneView = scene; sceneRef = scene
        scene.onComponentMoved = { AppState.save(requireContext()) }
        stage.addView(scene)

        val scroll2d = ScrollView(requireContext()).apply {
            layoutParams = FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        }
        val cv = CircuitView(requireContext())
        circuitView = cv
        cv.onComponentMoved = { AppState.save(requireContext()) }
        scroll2d.addView(cv)
        stage.addView(scroll2d)
        scroll2dRef = scroll2d

        hintOverlay = TextView(requireContext()).apply {
            textSize = 11f
            setTextColor(Color.WHITE)
            setBackgroundColor(Color.argb(170, 2, 6, 23))
            setPadding(dp(10), dp(6), dp(10), dp(6))
            val lp = FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            lp.gravity = android.view.Gravity.BOTTOM or android.view.Gravity.START
            lp.leftMargin = dp(10); lp.bottomMargin = dp(10)
            layoutParams = lp
        }
        stage.addView(hintOverlay)
        root.addView(stage)

        // ---------- справа: панель ----------
        val sideScroll = ScrollView(requireContext()).apply {
            layoutParams = LinearLayout.LayoutParams(dp(318), ViewGroup.LayoutParams.MATCH_PARENT)
            setBackgroundColor(Color.parseColor("#0F1B2D"))
            isFillViewport = true
        }
        val side = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(10), dp(10), dp(10), dp(14))
        }
        sideScroll.addView(side)
        root.addView(sideScroll)

        // --- компоненты (drag-and-drop) ---
        val compCard = section(side, "🧩 КОМПОНЕНТЫ — ПЕРЕТАЩИТЕ НА ПЛАТУ")
        compCard.addView(TextView(requireContext()).apply {
            text = "Зажмите деталь и тащите влево на плату. Или просто нажмите — встанет в свободное место."
            textSize = 10f; setTextColor(Color.parseColor("#94A3B8")); setPadding(0, 0, 0, dp(6))
        })
        for (def in Catalog.defs) {
            val item = LinearLayout(requireContext()).apply {
                orientation = LinearLayout.HORIZONTAL
                setBackgroundColor(Color.parseColor("#24344E"))
                setPadding(dp(8), dp(6), dp(8), dp(6))
                val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                lp.bottomMargin = dp(6)
                layoutParams = lp
                contentDescription = def.title
            }
            item.addView(View(requireContext()).apply {
                setBackgroundColor(def.color)
                layoutParams = LinearLayout.LayoutParams(dp(10), dp(36))
            })
            val texts = LinearLayout(requireContext()).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                setPadding(dp(8), 0, 0, 0)
            }
            texts.addView(TextView(requireContext()).apply {
                text = def.title; textSize = 12f; setTextColor(Color.WHITE); maxLines = 1
            })
            texts.addView(TextView(requireContext()).apply {
                text = "${def.wGrid}×${def.hGrid} отв. • ${"%.0f".format(def.wGrid * 2.54f)}×${"%.0f".format(def.hGrid * 2.54f)} мм • пины: ${def.pins.take(4).joinToString(",") { it.name }}${if (def.pins.size > 4) "…" else ""}"
                textSize = 9f; setTextColor(Color.parseColor("#94A3B8")); maxLines = 2
            })
            item.addView(texts)
            item.addView(TextView(requireContext()).apply {
                text = "⠿"; textSize = 18f; setTextColor(Color.parseColor("#64748B"))
            })
            item.setOnClickListener {
                AppState.addComponent(def.id)
                AppState.mode = "move"
                highlightModes()
                AppState.save(requireContext())
                Toast.makeText(requireContext(), "«${def.title}» на плате — двигайте её пальцем.", Toast.LENGTH_SHORT).show()
            }
            item.setOnLongClickListener { v ->
                val data = ClipData.newPlainText("component", def.id)
                val shadow = View.DragShadowBuilder(v)
                v.startDragAndDrop(data, shadow, null, 0)
                true
            }
            compCard.addView(item)
        }

        // drag-and-drop на сцены
        val dragListener = View.OnDragListener { view, event ->
            when (event.action) {
                DragEvent.ACTION_DRAG_STARTED ->
                    event.clipDescription?.hasMimeType(ClipDescription.MIMETYPE_TEXT_PLAIN) == true
                DragEvent.ACTION_DRAG_LOCATION, DragEvent.ACTION_DRAG_ENTERED, DragEvent.ACTION_DRAG_EXITED -> true
                DragEvent.ACTION_DROP -> {
                    val typeId = event.clipData?.getItemAt(0)?.text?.toString()
                    if (typeId.isNullOrEmpty()) {
                        false
                    } else {
                        val ok = when (view) {
                            scene -> scene.dropComponent(typeId, event.x, event.y)
                            cv -> cv.dropComponent(typeId, event.x, event.y)
                            else -> false
                        }
                        if (ok) {
                            highlightModes()
                            AppState.save(requireContext())
                            Toast.makeText(requireContext(), "Деталь на плате! Режим «Двигать» — можно подвинуть точнее.", Toast.LENGTH_SHORT).show()
                        }
                        ok
                    }
                }
                DragEvent.ACTION_DRAG_ENDED -> true
                else -> false
            }
        }
        scene.setOnDragListener(dragListener)
        cv.setOnDragListener(dragListener)

        // --- инструменты ---
        val toolsCard = section(side, "🛠 ИНСТРУМЕНТЫ")
        val modes = listOf(
            "camera" to "🎥 Камера", "move" to "✥ Двигать", "wire" to "🔌 Провод",
            "solder" to "🔥 Паять", "delete" to "🗑 Удалить", "sim" to "👆 Нажимать"
        )
        modeButtons.clear()
        var row: LinearLayout? = null
        modes.forEachIndexed { index, (key, label) ->
            if (index % 3 == 0) {
                row = LinearLayout(requireContext()).apply { orientation = LinearLayout.HORIZONTAL }
                toolsCard.addView(row)
            }
            val b = darkBtn(label, 10f).apply {
                layoutParams = LinearLayout.LayoutParams(0, dp(42), 1f).apply {
                    if (index % 3 != 0) leftMargin = dp(5)
                    bottomMargin = dp(5)
                }
                setOnClickListener { selectMode(key) }
            }
            modeButtons[key] = b
            row?.addView(b)
        }
        toolsCard.addView(TextView(requireContext()).apply {
            text = "Камера: палец — вращение, щипок — зум. Провод/Паять: пин → пин. Нажимать: кнопки и потенциометр — в симуляции."
            textSize = 10f; setTextColor(Color.parseColor("#94A3B8"))
        })

        // --- вид и плата ---
        val viewCard = section(side, "👁 ВИД И ПЛАТА")
        val viewRow = LinearLayout(requireContext()).apply { orientation = LinearLayout.HORIZONTAL }
        val btn3d = darkBtn("🧊 3D", 12f).apply { layoutParams = LinearLayout.LayoutParams(0, dp(42), 1f) }
        val btn2d = darkBtn("▦ 2D", 12f).apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(42), 1f).apply { leftMargin = dp(6) }
        }
        btn3dRef = btn3d; btn2dRef = btn2d
        viewRow.addView(btn3d); viewRow.addView(btn2d)
        viewCard.addView(viewRow)

        val presetRow = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, dp(6), 0, 0)
        }
        for ((label, viewName) in listOf("Изометрия" to "iso", "Сверху" to "top", "Спереди" to "front", "Сбоку" to "side")) {
            presetRow.addView(darkBtn(label, 9f).apply {
                layoutParams = LinearLayout.LayoutParams(0, dp(36), 1f).apply { if (label != "Изометрия") leftMargin = dp(4) }
                setOnClickListener {
                    AppState.board3d = true
                    applyStageVisibility()
                    scene.setView(viewName)
                    selectMode("camera")
                }
            })
        }
        viewCard.addView(presetRow)

        val caseCb = CheckBox(requireContext()).apply {
            text = "STL-корпус в 3D-сцене"
            textSize = 11f; setTextColor(Color.WHITE)
            isChecked = AppState.showCaseIn3D
            setPadding(0, dp(6), 0, 0)
            setOnCheckedChangeListener { _, checked ->
                AppState.showCaseIn3D = checked
                if (checked && AppState.caseModel == null) {
                    Toast.makeText(requireContext(), "Сначала выберите корпус: слева 📦 КОРПУС", Toast.LENGTH_LONG).show()
                }
                if (checked) { AppState.board3d = true; applyStageVisibility() }
                scene.invalidate()
            }
        }
        viewCard.addView(caseCb)

        viewCard.addView(TextView(requireContext()).apply {
            text = "Размер макетной платы:"
            textSize = 11f; setTextColor(Color.parseColor("#94A3B8")); setPadding(0, dp(6), 0, dp(2))
        })
        val boardSpinner = Spinner(requireContext())
        val boardOptions = listOf("Перфорация 24×16", "Перфорация 30×22 (стандарт)", "Перфорация 40×28 (большая)", "Мини 18×12")
        val boardSizes = listOf(24 to 16, 30 to 22, 40 to 28, 18 to 12)
        boardSpinner.adapter = ArrayAdapter(requireContext(), android.R.layout.simple_spinner_dropdown_item, boardOptions)
        boardSpinner.setSelection(boardSizes.indexOfFirst { it.first == AppState.project.boardCols }.let { if (it < 0) 1 else it })
        viewCard.addView(boardSpinner)
        val boardRow2 = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL; setPadding(0, dp(6), 0, 0)
        }
        boardRow2.addView(darkBtn("Применить плату", 10f).apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(40), 1f)
            setOnClickListener {
                val (c, r) = boardSizes[boardSpinner.selectedItemPosition]
                AppState.project.boardCols = c; AppState.project.boardRows = r
                AppState.log("Макетная плата изменена: $c×$r отверстий")
                AppState.save(requireContext())
                cv.requestLayout()
                AppState.notifyChanged()
            }
        })
        boardRow2.addView(darkBtn("Снять провода", 10f).apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(40), 1f).apply { leftMargin = dp(6) }
            setOnClickListener {
                AppState.project.wires.clear()
                AppState.log("Все провода/пайки удалены")
                AppState.save(requireContext())
                AppState.notifyChanged()
            }
        })
        viewCard.addView(boardRow2)

        btn3d.setOnClickListener {
            AppState.board3d = true
            applyStageVisibility()
            selectMode("camera")
        }
        btn2d.setOnClickListener {
            AppState.board3d = false
            applyStageVisibility()
            if (AppState.mode == "camera") selectMode("move")
        }

        // --- выбранная деталь ---
        val selCard = section(side, "🎯 ВЫБРАННАЯ ДЕТАЛЬ")
        infoText = TextView(requireContext()).apply {
            textSize = 11f; setTextColor(Color.parseColor("#E2E8F0"))
        }
        selCard.addView(infoText)
        val actRow1 = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL; setPadding(0, dp(6), 0, 0)
        }
        actRow1.addView(darkBtn("↻ Повернуть", 10f).apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(40), 1f)
            setOnClickListener {
                val c = AppState.selected() ?: return@setOnClickListener noSel()
                c.rotation = (c.rotation + 90) % 360
                AppState.notifyChanged(); AppState.save(requireContext())
            }
        })
        actRow1.addView(darkBtn("⚙ Свойства", 10f).apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(40), 1f).apply { leftMargin = dp(6) }
            setOnClickListener { cycleProps() }
        })
        selCard.addView(actRow1)
        val actRow2 = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL; setPadding(0, dp(6), 0, 0)
        }
        actRow2.addView(darkBtn("📦 В корпус", 10f).apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(40), 1f)
            setOnClickListener {
                val c = AppState.selected() ?: return@setOnClickListener noSel()
                c.insideCase = !c.insideCase
                AppState.log(if (c.insideCase) "📦 ${c.def().title} — в корпус (координаты: 📦 КОРПУС слева)" else "📦 ${c.def().title} убран из корпуса")
                AppState.notifyChanged(); AppState.save(requireContext())
            }
        })
        actRow2.addView(darkBtn("🗑 Удалить", 10f).apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(40), 1f).apply { leftMargin = dp(6) }
            setOnClickListener {
                val c = AppState.selected() ?: return@setOnClickListener noSel()
                AppState.deleteComponent(c.id)
                AppState.save(requireContext())
            }
        })
        selCard.addView(actRow2)

        // --- на плате ---
        val placedCard = section(side, "📋 НА ПЛАТЕ — НАЖМИТЕ, ЧТОБЫ ВЫБРАТЬ")
        placedContainer = LinearLayout(requireContext()).apply { orientation = LinearLayout.VERTICAL }
        placedCard.addView(placedContainer)

        // --- помощь ---
        val helpCard = section(side, "💡 КАК СОБРАТЬ СХЕМУ")
        helpCard.addView(TextView(requireContext()).apply {
            text = "1) Перетащите детали из меню выше на плату.\n" +
                "2) «🔥 Паять»: батарея «+» → ESP VIN, «−» → GND.\n" +
                "3) LED: ESP D13 → резистор → LED(A), LED(K) → GND.\n" +
                "4) Слева 💻 КОД ESP → пример → «Залить прошивку».\n" +
                "5) Сверху ▶ ЗАПУСК — и схема оживает в 3D!"
            textSize = 10f; setTextColor(Color.parseColor("#94A3B8"))
        })

        applyStageVisibility()
        highlightModes()
        AppState.addListener(listener)
        refreshInfo()
        refreshPlaced()
        return root
    }

    private fun applyStageVisibility() {
        val scene = sceneRef ?: return
        val scroll = scroll2dRef ?: return
        scene.visibility = if (AppState.board3d) View.VISIBLE else View.GONE
        scroll.visibility = if (AppState.board3d) View.GONE else View.VISIBLE
        btn3dRef?.setBackgroundColor(Color.parseColor(if (AppState.board3d) "#2563EB" else "#334155"))
        btn2dRef?.setBackgroundColor(Color.parseColor(if (AppState.board3d) "#334155" else "#2563EB"))
        scene.invalidate()
        circuitView?.invalidate()
    }

    private fun noSel() {
        Toast.makeText(requireContext(), "Сначала коснитесь детали на плате или в списке «На плате»", Toast.LENGTH_SHORT).show()
    }

    private fun highlightModes() {
        modeButtons.forEach { (k, b) ->
            b.setBackgroundColor(Color.parseColor(if (AppState.mode == k) "#2563EB" else "#334155"))
        }
    }

    private fun cycleProps() {
        val c = AppState.selected() ?: return noSel()
        when (c.typeId) {
            "resistor" -> {
                val vals = listOf("10", "47", "100", "220", "470", "1000", "4700", "10000")
                val cur = c.props["ohms"] ?: "220"
                c.props["ohms"] = vals[(vals.indexOf(cur) + 1).let { if (it < 0) 0 else it } % vals.size]
                AppState.log("Резистор: ${c.props["ohms"]} Ω")
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
                Toast.makeText(requireContext(), "У «${c.def().title}» свойства задаёт прошивка/симуляция", Toast.LENGTH_SHORT).show()
                return
            }
        }
        AppState.notifyChanged()
        AppState.save(requireContext())
    }

    private fun refreshPlaced() {
        val cont = placedContainer ?: return
        if (!isAdded) return
        cont.removeAllViews()
        if (AppState.project.components.isEmpty()) {
            cont.addView(TextView(requireContext()).apply {
                text = "Пока пусто — перетащите первую деталь из меню выше."
                textSize = 10f; setTextColor(Color.parseColor("#94A3B8"))
            })
            return
        }
        for (c in AppState.project.components) {
            val rowBtn = darkBtn(
                (if (c.id == AppState.selectedId) "▶ " else "") + c.def().title + "  (${c.gridX},${c.gridY})",
                10f
            ).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36)).apply { bottomMargin = dp(4) }
                setBackgroundColor(Color.parseColor(if (c.id == AppState.selectedId) "#2563EB" else "#24344E"))
                setOnClickListener {
                    AppState.selectedId = c.id
                    if (AppState.mode == "camera") AppState.mode = "move"
                    highlightModes()
                    AppState.notifyChanged()
                }
            }
            cont.addView(rowBtn)
        }
    }

    private fun refreshInfo() {
        if (!isAdded) return
        val t = infoText
        val c = AppState.selected()
        if (t != null) {
            if (c == null) {
                t.text = "Ничего не выбрано. Перетащите деталь из меню или коснитесь детали на плате."
            } else {
                val props = if (c.props.isEmpty()) "" else "\n" + c.props.entries.joinToString(", ") { "${it.key}=${it.value}" }
                val state = CircuitSim.compute()
                val extra = when (c.typeId) {
                    "led" -> state.leds[c.id]?.let {
                        "\nТок: ${"%.1f".format(it.currentMa)} мА" +
                            if (it.burnt) " 💥 СГОРЕЛ (нужен резистор!)" else if (it.brightness > 0) ", светится ${(it.brightness * 100).toInt()}%" else ", не горит"
                    } ?: ""
                    "esp32" -> if (state.espPowered.contains(c.id)) "\n⚡ ESP запитан" else "\n❌ ESP без питания: нужны +→VIN и −→GND"
                    else -> ""
                }
                t.text = "${c.def().title}\nПозиция: (${c.gridX},${c.gridY}), поворот ${c.rotation}°${if (c.insideCase) ", в корпусе" else ""}$props$extra"
            }
        }
        hintOverlay?.text = when (AppState.mode) {
            "camera" -> "🎥 Камера: палец — вращение • щипок — зум • детали таскаются из меню справа ⠿"
            "wire" -> "🔌 Провод: коснитесь пина → коснитесь второго пина"
            "solder" -> "🔥 Пайка: коснитесь пина → коснитесь второго пина (дорожка + припой)"
            "delete" -> "🗑 Коснитесь детали, чтобы удалить её"
            "sim" -> "👆 Нажимайте кнопки на плате, крутите потенциометр"
            else -> "✥ Двигать: тащите деталь по плате • детали добавляются перетаскиванием справа"
        }
        sceneView?.invalidate()
        circuitView?.invalidate()
    }

    override fun onDestroyView() {
        AppState.removeListener(listener)
        super.onDestroyView()
    }
}
