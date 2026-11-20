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
 * Главный экран (ландшафт):
 *  - центр: 3D/2D плата с реалистичными моделями деталей;
 *  - справа: СВОРАЧИВАЕМОЕ меню с вкладками «Детали / Инструменты / Свойства».
 *    Детали перетаскиваются из меню на плату (drag-and-drop).
 *    Во «Свойствах» — живые выходы ESP32 (HIGH/LOW по каждому D-пину).
 */
class BoardFragment : Fragment() {

    private var circuitView: CircuitView? = null
    private var sceneView: Scene3DView? = null
    private var infoText: TextView? = null
    private var hintOverlay: TextView? = null
    private var placedContainer: LinearLayout? = null
    private var espContainer: LinearLayout? = null
    private val modeButtons = mutableMapOf<String, Button>()
    private val tabButtons = mutableMapOf<String, Button>()
    private val tabPanels = mutableMapOf<String, LinearLayout>()
    private var currentTab = "parts"
    private var btn3dRef: Button? = null
    private var btn2dRef: Button? = null
    private var sceneRef: Scene3DView? = null
    private var scroll2dRef: ScrollView? = null
    private var sideScrollRef: ScrollView? = null
    private var expandBtnRef: Button? = null

    private val listener: () -> Unit = {
        refreshInfo()
        refreshPlaced()
        refreshEspOutputs()
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

    private fun showTab(name: String) {
        currentTab = name
        tabPanels.forEach { (k, p) -> p.visibility = if (k == name) View.VISIBLE else View.GONE }
        tabButtons.forEach { (k, b) ->
            b.setBackgroundColor(Color.parseColor(if (k == name) "#2563EB" else "#1E293B"))
        }
        if (name == "props") refreshEspOutputs()
    }

    private fun setMenuCollapsed(collapsed: Boolean) {
        sideScrollRef?.visibility = if (collapsed) View.GONE else View.VISIBLE
        expandBtnRef?.visibility = if (collapsed) View.VISIBLE else View.GONE
        sceneView?.invalidate()
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

        // кнопка разворачивания (видна, когда меню свёрнуто)
        val expandBtn = darkBtn("⟨ ☰ МЕНЮ", 12f).apply {
            setBackgroundColor(Color.parseColor("#2563EB"))
            val lp = FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(44))
            lp.gravity = android.view.Gravity.TOP or android.view.Gravity.END
            lp.topMargin = dp(10); lp.rightMargin = dp(10)
            layoutParams = lp
            visibility = View.GONE
            setOnClickListener { setMenuCollapsed(false) }
        }
        expandBtnRef = expandBtn
        stage.addView(expandBtn)
        root.addView(stage)

        // ---------- справа: сворачиваемое меню с вкладками ----------
        val sideScroll = ScrollView(requireContext()).apply {
            layoutParams = LinearLayout.LayoutParams(dp(322), ViewGroup.LayoutParams.MATCH_PARENT)
            setBackgroundColor(Color.parseColor("#0F1B2D"))
            isFillViewport = true
        }
        sideScrollRef = sideScroll
        val side = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(10), dp(8), dp(10), dp(14))
        }
        sideScroll.addView(side)
        root.addView(sideScroll)

        // шапка меню + свернуть
        val menuHeader = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, 0, 0, dp(6))
        }
        menuHeader.addView(TextView(requireContext()).apply {
            text = "☰ МЕНЮ"
            textSize = 15f; setTextColor(Color.WHITE)
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        })
        menuHeader.addView(darkBtn("Свернуть ⟩", 10f).apply {
            layoutParams = LinearLayout.LayoutParams(dp(104), dp(38))
            setOnClickListener { setMenuCollapsed(true) }
        })
        side.addView(menuHeader)

        // вкладки меню
        val tabsRow = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, 0, 0, dp(8))
        }
        for ((key, label) in listOf("parts" to "🧩 Детали", "tools" to "🛠 Инструм.", "props" to "🎯 Свойства")) {
            val b = darkBtn(label, 11f).apply {
                layoutParams = LinearLayout.LayoutParams(0, dp(42), 1f).apply {
                    if (key != "parts") leftMargin = dp(5)
                }
                setOnClickListener { showTab(key) }
            }
            tabButtons[key] = b
            tabsRow.addView(b)
        }
        side.addView(tabsRow)

        fun newTabPanel(): LinearLayout {
            val p = LinearLayout(requireContext()).apply { orientation = LinearLayout.VERTICAL }
            side.addView(p)
            return p
        }
        val partsTab = newTabPanel(); tabPanels["parts"] = partsTab
        val toolsTab = newTabPanel(); tabPanels["tools"] = toolsTab
        val propsTab = newTabPanel(); tabPanels["props"] = propsTab

        // ===== ВКЛАДКА «ДЕТАЛИ» =====
        val compCard = section(partsTab, "🧩 КОМПОНЕНТЫ — ПЕРЕТАЩИТЕ НА ПЛАТУ")
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
                text = "${def.wGrid}×${def.hGrid} отв. • пины: ${def.pins.take(5).joinToString(",") { it.name }}${if (def.pins.size > 5) "…" else ""}"
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

        val placedCard = section(partsTab, "📋 НА ПЛАТЕ — НАЖМИТЕ, ЧТОБЫ ВЫБРАТЬ")
        placedContainer = LinearLayout(requireContext()).apply { orientation = LinearLayout.VERTICAL }
        placedCard.addView(placedContainer)

        // ===== ВКЛАДКА «ИНСТРУМЕНТЫ» =====
        val toolsCard = section(toolsTab, "🛠 ИНСТРУМЕНТЫ")
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
            text = "Камера: палец — вращение, щипок — зум. Провод/Паять: коснитесь пина на модели → коснитесь второго. В «Нажимать» кнопки на плате живые."
            textSize = 10f; setTextColor(Color.parseColor("#94A3B8"))
        })

        val viewCard = section(toolsTab, "👁 ВИД, КАМЕРА И ПЛАТА")
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

        // ===== ВКЛАДКА «СВОЙСТВА» =====
        val selCard = section(propsTab, "🎯 ВЫБРАННАЯ ДЕТАЛЬ")
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

        val espCard = section(propsTab, "🔌 ВЫХОДЫ ESP32 (ЖИВЫЕ)")
        espContainer = LinearLayout(requireContext()).apply { orientation = LinearLayout.VERTICAL }
        espCard.addView(espContainer)

        val helpCard = section(propsTab, "💡 КАК СОБРАТЬ СХЕМУ")
        helpCard.addView(TextView(requireContext()).apply {
            text = "1) Вкладка «Детали»: перетащите детали на плату.\n" +
                "2) Вкладка «Инструменты» → «🔥 Паять»: батарея «+» → ESP VIN, «−» → GND (касайтесь золотых штырей на модели ESP — в этом режиме подписаны все пины).\n" +
                "3) LED: ESP D13 → резистор → LED(A), LED(K) → GND.\n" +
                "4) Слева 💻 КОД ESP → пример → «Залить прошивку».\n" +
                "5) Сверху ▶ ЗАПУСК. Выходы ESP в реальном времени — здесь, во вкладке «Свойства»: HIGH загорается зелёным и на 3D-модели.\n" +
                "Меню сворачивается кнопкой «Свернуть ⟩» — плата займёт весь экран."
            textSize = 10f; setTextColor(Color.parseColor("#94A3B8"))
        })

        applyStageVisibility()
        highlightModes()
        showTab("parts")
        AppState.addListener(listener)
        refreshInfo()
        refreshPlaced()
        refreshEspOutputs()
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
        Toast.makeText(requireContext(), "Сначала коснитесь детали на плате или выберите её во вкладке «Детали»", Toast.LENGTH_SHORT).show()
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
                val idx = vals.indexOf(cur)
                c.props["ohms"] = vals[if (idx < 0) 0 else (idx + 1) % vals.size]
                AppState.log("Резистор: ${c.props["ohms"]} Ω")
            }
            "led" -> {
                val vals = listOf("Красный", "Зелёный", "Синий", "Жёлтый", "Белый")
                val cur = c.props["colorName"] ?: "Красный"
                c.props["colorName"] = vals[(vals.indexOf(cur) + 1).coerceAtLeast(0) % vals.size]
            }
            "bat18650", "bat9v", "lipo" -> {
                val vals = listOf("3.7", "5", "9", "12")
                val cur = c.props["voltage"] ?: "3.7"
                c.props["voltage"] = vals[(vals.indexOf(cur) + 1).coerceAtLeast(0) % vals.size]
            }
            "nrf24" -> {
                val vals = listOf("76", "10", "40", "100", "125")
                val cur = c.props["channel"] ?: "76"
                c.props["channel"] = vals[(vals.indexOf(cur) + 1).coerceAtLeast(0) % vals.size]
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
            cont.addView(darkBtn(
                (if (c.id == AppState.selectedId) "▶ " else "") + c.def().title + "  (${c.gridX},${c.gridY})",
                10f
            ).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36)).apply { bottomMargin = dp(4) }
                setBackgroundColor(Color.parseColor(if (c.id == AppState.selectedId) "#2563EB" else "#24344E"))
                setOnClickListener {
                    AppState.selectedId = c.id
                    if (c.typeId == "esp32") showTab("props")
                    if (AppState.mode == "camera") AppState.mode = "move"
                    highlightModes()
                    AppState.notifyChanged()
                }
            })
        }
    }

    /** Живые выходы ESP: по строке на каждый пин, HIGH/LOW/INPUT + ручной переключатель. */
    private fun refreshEspOutputs() {
        val cont = espContainer ?: return
        if (!isAdded) return
        if (currentTab != "props") return // не дёргаем UI, пока вкладка скрыта
        cont.removeAllViews()
        val esp = (AppState.selected()?.takeIf { it.typeId == "esp32" })
            ?: AppState.project.components.firstOrNull { it.typeId == "esp32" }
        if (esp == null) {
            cont.addView(TextView(requireContext()).apply {
                text = "В схеме нет ESP32. Перетащите ESP32 DevKit из вкладки «Детали» — здесь появятся его выходы."
                textSize = 10f; setTextColor(Color.parseColor("#94A3B8"))
            })
            return
        }
        val state = CircuitSim.compute()
        val powered = state.espPowered.contains(esp.id)
        cont.addView(TextView(requireContext()).apply {
            text = (if (powered) "⚡ ESP32 запитан" else "❌ ESP32 без питания (+ → VIN, − → GND)") +
                (if (AppState.running) " • прошивка выполняется" else " • симуляция остановлена (▶ сверху)")
            textSize = 10f
            setTextColor(Color.parseColor(if (powered) "#86EFAC" else "#FCA5A5"))
            setPadding(0, 0, 0, dp(6))
        })
        for (pin in esp.def().pins) {
            if (pin.name == "GND2") continue
            val key = "${esp.id}:${pin.name}"
            val mode = AppState.pinModes[key]
            val outVal = AppState.pinOutputs[key]
            val stateText: String
            val stateColor: String
            when {
                mode == "OUTPUT" && outVal == 1 -> { stateText = "HIGH • 3.3 В"; stateColor = "#22C55E" }
                mode == "OUTPUT" -> { stateText = "LOW • 0 В"; stateColor = "#64748B" }
                mode != null && mode.startsWith("INPUT") -> {
                    val v = if (powered && AppState.running) CircuitSim.digitalRead(esp.id, pin.name) else -1
                    stateText = if (v >= 0) "IN • ${if (v == 1) "HIGH" else "LOW"}" else "IN • вход"
                    stateColor = "#38BDF8"
                }
                pin.name in listOf("VIN", "3V3") -> { stateText = "питание"; stateColor = "#FACC15" }
                pin.name.startsWith("GND") -> { stateText = "земля"; stateColor = "#94A3B8" }
                else -> { stateText = "не настроен"; stateColor = "#64748B" }
            }
            val rowLine = LinearLayout(requireContext()).apply {
                orientation = LinearLayout.HORIZONTAL
                setBackgroundColor(Color.parseColor("#24344E"))
                setPadding(dp(7), dp(3), dp(4), dp(3))
                val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                lp.bottomMargin = dp(3)
                layoutParams = lp
                gravity = android.view.Gravity.CENTER_VERTICAL
            }
            rowLine.addView(TextView(requireContext()).apply {
                text = pin.name; textSize = 11f; setTextColor(Color.WHITE)
                layoutParams = LinearLayout.LayoutParams(dp(46), ViewGroup.LayoutParams.WRAP_CONTENT)
            })
            rowLine.addView(TextView(requireContext()).apply {
                text = stateText; textSize = 10f; setTextColor(Color.parseColor(stateColor))
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            })
            if (pin.name.startsWith("D") || pin.name in listOf("TX", "RX")) {
                rowLine.addView(darkBtn(if (outVal == 1 && mode == "OUTPUT") "выкл" else "вкл", 9f).apply {
                    layoutParams = LinearLayout.LayoutParams(dp(52), dp(32))
                    setOnClickListener {
                        val newVal = if (AppState.pinOutputs[key] == 1 && AppState.pinModes[key] == "OUTPUT") 0 else 1
                        AppState.pinOutputs[key] = newVal
                        AppState.pinModes[key] = "OUTPUT"
                        AppState.log("🔌 Выход ESP ${pin.name} вручную: ${if (newVal == 1) "HIGH (3.3В)" else "LOW"}${if (!AppState.running) " — нажмите ▶ ЗАПУСК сверху, чтобы увидеть на схеме" else ""}")
                        AppState.notifyChanged()
                    }
                })
            }
            cont.addView(rowLine)
        }
        cont.addView(TextView(requireContext()).apply {
            text = "Выходы ставит прошивка (digitalWrite). Кнопка «вкл/выкл» — ручная проверка: действует до следующего digitalWrite. На 3D-модели HIGH-пины подсвечены зелёным."
            textSize = 9f; setTextColor(Color.parseColor("#64748B")); setPadding(0, dp(4), 0, 0)
        })
    }

    private fun refreshInfo() {
        if (!isAdded) return
        val t = infoText
        val c = AppState.selected()
        if (t != null) {
            if (c == null) {
                t.text = "Ничего не выбрано. Коснитесь детали на плате (ESP32 откроет вкладку с его выходами)."
            } else {
                val props = if (c.props.isEmpty()) "" else "\n" + c.props.entries.joinToString(", ") { "${it.key}=${it.value}" }
                val state = CircuitSim.compute()
                val extra = when (c.typeId) {
                    "led" -> state.leds[c.id]?.let {
                        "\nТок: ${"%.1f".format(it.currentMa)} мА" +
                            if (it.burnt) " 💥 СГОРЕЛ (нужен резистор!)" else if (it.brightness > 0) ", светится ${(it.brightness * 100).toInt()}%" else ", не горит"
                    } ?: ""
                    "esp32" -> if (state.espPowered.contains(c.id)) "\n⚡ ESP запитан — его выходы ниже в этой вкладке" else "\n❌ ESP без питания: нужны +→VIN и −→GND"
                    else -> ""
                }
                t.text = "${c.def().title}\nПозиция: (${c.gridX},${c.gridY}), поворот ${c.rotation}°${if (c.insideCase) ", в корпусе" else ""}$props$extra"
            }
        }
        hintOverlay?.text = when (AppState.mode) {
            "camera" -> "🎥 Камера: палец — вращение • щипок — зум • меню справа сворачивается кнопкой «Свернуть ⟩»"
            "wire" -> "🔌 Провод: коснитесь золотого штыря на модели → коснитесь второго штыря"
            "solder" -> "🔥 Пайка: штырь → штырь. У ESP подписаны все выходы: VIN, GND, D13, D32…"
            "delete" -> "🗑 Коснитесь детали, чтобы удалить её"
            "sim" -> "👆 Нажимайте кнопки на моделях, крутите потенциометр"
            else -> "✥ Двигать: тащите модель детали по плате • новые детали — перетаскиванием из меню «Детали»"
        }
        sceneView?.invalidate()
        circuitView?.invalidate()
    }

    override fun onDestroyView() {
        AppState.removeListener(listener)
        super.onDestroyView()
    }
}
