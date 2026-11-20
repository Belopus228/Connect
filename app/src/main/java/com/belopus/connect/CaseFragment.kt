package com.belopus.connect

import android.graphics.Color
import android.net.Uri
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.fragment.app.Fragment
import java.io.File

class CaseFragment : Fragment() {
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    private var infoText: TextView? = null
    private var fitText: TextView? = null
    private var view3d: Case3DView? = null
    private var topView: CaseTopView? = null

    private val pickStl = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri: Uri? ->
        if (uri == null) return@registerForActivityResult
        try {
            val bytes = requireContext().contentResolver.openInputStream(uri)?.readBytes()
                ?: throw IllegalStateException("Пустой файл")
            if (bytes.size > 25_000_000) throw IllegalStateException("Файл больше 25 МБ — слишком большой для телефона")
            val name = uri.lastPathSegment?.substringAfterLast('/') ?: "case.stl"
            val model = StlParser.parse(bytes, name)
            // копия внутрь приложения
            File(requireContext().filesDir, "case.stl").writeBytes(bytes)
            AppState.caseModel = model
            AppState.casePresetName = ""
            AppState.project.stlName = name
            AppState.log("📦 Загружен STL-корпус «$name»: ${model.triangles.size} треугольников, ${"%.1f×%.1f×%.1f".format(model.sizeX(), model.sizeY(), model.sizeZ())} мм")
            AppState.save(requireContext())
            refresh()
            Toast.makeText(requireContext(), "STL загружен: ${model.triangles.size} треуг.", Toast.LENGTH_LONG).show()
        } catch (e: Exception) {
            Toast.makeText(requireContext(), "Ошибка STL: ${e.message}", Toast.LENGTH_LONG).show()
        }
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        val scroll = ScrollView(requireContext())
        val root = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(10), dp(10), dp(10), dp(10))
            setBackgroundColor(Color.parseColor("#ECEFF1"))
        }
        scroll.addView(root)

        root.addView(TextView(requireContext()).apply {
            text = "📦 Корпус из STL"
            textSize = 19f; setTextColor(Color.parseColor("#0D47A1"))
        })
        root.addView(TextView(requireContext()).apply {
            text = "Загрузите свой .stl (корпус с 3D-принтера / из Fusion, Tinkercad, FreeCAD), посмотрите его в 3D и расставьте детали внутри: отметьте деталь, двигайте ползунки X/Y/Z — приложение проверит, влезает ли она."
            textSize = 13f; setPadding(0, dp(4), 0, dp(8))
        })

        val btnRow = LinearLayout(requireContext()).apply { orientation = LinearLayout.HORIZONTAL }
        btnRow.addView(Button(requireContext()).apply {
            text = "📂 Импорт своего .STL"
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            setOnClickListener { pickStl.launch(arrayOf("*/*")) }
        })
        root.addView(btnRow)

        root.addView(TextView(requireContext()).apply { text = "Или готовый корпус:"; textSize = 13f; setPadding(0, dp(8), 0, dp(2)) })
        val presetSpinner = Spinner(requireContext())
        presetSpinner.adapter = ArrayAdapter(requireContext(), android.R.layout.simple_spinner_dropdown_item, StlPresets.ALL.map { it.name })
        root.addView(presetSpinner)
        root.addView(Button(requireContext()).apply {
            text = "Использовать выбранный корпус"
            setOnClickListener {
                val m = StlPresets.ALL[presetSpinner.selectedItemPosition]
                AppState.caseModel = m
                AppState.casePresetName = m.name
                AppState.project.stlName = m.name
                // снести импортированный, чтобы пресет не перекрывался при загрузке
                File(requireContext().filesDir, "case.stl").delete()
                AppState.log("📦 Выбран готовый корпус «${m.name}»")
                AppState.save(requireContext())
                refresh()
            }
        })

        infoText = TextView(requireContext()).apply { textSize = 13f; setPadding(0, dp(8), 0, dp(4)) }
        root.addView(infoText)

        view3d = Case3DView(requireContext()).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(250))
        }
        root.addView(view3d)

        topView = CaseTopView(requireContext()).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(230))
            onFitText = { txt, ok ->
                fitText?.text = if (txt.isEmpty()) "" else "Проверка влезания:\n$txt"
                fitText?.setTextColor(if (ok) Color.parseColor("#2E7D32") else Color.parseColor("#C62828"))
            }
        }
        root.addView(TextView(requireContext()).apply { text = "Расстановка деталей внутри корпуса:"; textSize = 15f; setPadding(0, dp(10), 0, dp(4)) })
        root.addView(topView)
        fitText = TextView(requireContext()).apply { textSize = 12f }
        root.addView(fitText)

        // выбор детали и ползунки
        root.addView(TextView(requireContext()).apply { text = "Деталь для расстановки:"; textSize = 13f; setPadding(0, dp(8), 0, dp(2)) })
        val compSpinner = Spinner(requireContext())
        val comps = AppState.project.components
        compSpinner.adapter = ArrayAdapter(
            requireContext(), android.R.layout.simple_spinner_dropdown_item,
            if (comps.isEmpty()) listOf("— на плате пока пусто —") else comps.map { "${it.def().title} (${it.gridX},${it.gridY})" }
        )
        root.addView(compSpinner)

        fun selectedComp(): PlacedComponent? = comps.getOrNull(compSpinner.selectedItemPosition)

        val inCaseCb = CheckBox(requireContext()).apply { text = "Поместить эту деталь в корпус" }
        root.addView(inCaseCb)

        val sliders = mutableListOf<SeekBar>()
        val sliderLabels = mutableListOf<TextView>()
        val axes = listOf("X, мм →", "Y, мм ↓", "Z, мм ↑ (высота от дна)")
        for (a in 0..2) {
            sliderLabels.add(TextView(requireContext()).apply { text = axes[a]; textSize = 12f })
            root.addView(sliderLabels.last())
            val sb = SeekBar(requireContext()).apply { max = 100 }
            sliders.add(sb)
            root.addView(sb)
        }

        fun syncFromComp() {
            val m = AppState.caseModel
            val c = selectedComp() ?: return
            inCaseCb.isChecked = c.insideCase
            val maxX = (m?.sizeX() ?: 100f).toInt().coerceAtLeast(10)
            val maxY = (m?.sizeY() ?: 70f).toInt().coerceAtLeast(10)
            val maxZ = (m?.sizeZ() ?: 25f).toInt().coerceAtLeast(5)
            sliders[0].max = maxX; sliders[1].max = maxY; sliders[2].max = maxZ
            sliders[0].progress = c.caseXmm.toInt().coerceIn(0, maxX)
            sliders[1].progress = c.caseYmm.toInt().coerceIn(0, maxY)
            sliders[2].progress = c.caseZmm.toInt().coerceIn(0, maxZ)
            sliderLabels[0].text = "X: ${c.caseXmm.toInt()} мм (ширина детали ${"%.1f".format(c.widthMm())} мм)"
            sliderLabels[1].text = "Y: ${c.caseYmm.toInt()} мм (глубина ${"%.1f".format(c.depthMm())} мм)"
            sliderLabels[2].text = "Z: ${c.caseZmm.toInt()} мм (высота детали ${c.def().heightMm} мм)"
        }
        compSpinner.onItemSelectedListener = object : android.widget.AdapterView.OnItemSelectedListener {
            override fun onItemSelected(p: android.widget.AdapterView<*>?, v: View?, pos: Int, id: Long) { syncFromComp() }
            override fun onNothingSelected(p: android.widget.AdapterView<*>?) {}
        }
        inCaseCb.setOnCheckedChangeListener { _, checked ->
            val c = selectedComp() ?: return@setOnCheckedChangeListener
            c.insideCase = checked
            AppState.save(requireContext()); topView?.invalidate(); AppState.notifyChanged()
        }
        sliders.forEachIndexed { idx, sb ->
            sb.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(s: SeekBar?, progress: Int, fromUser: Boolean) {
                    if (!fromUser) return
                    val c = selectedComp() ?: return
                    when (idx) { 0 -> c.caseXmm = progress.toFloat(); 1 -> c.caseYmm = progress.toFloat(); 2 -> c.caseZmm = progress.toFloat() }
                    c.insideCase = true; inCaseCb.isChecked = true
                    sliderLabels[0].text = "X: ${c.caseXmm.toInt()} мм (ширина детали ${"%.1f".format(c.widthMm())} мм)"
                    sliderLabels[1].text = "Y: ${c.caseYmm.toInt()} мм (глубина ${"%.1f".format(c.depthMm())} мм)"
                    sliderLabels[2].text = "Z: ${c.caseZmm.toInt()} мм (высота детали ${c.def().heightMm} мм)"
                    topView?.invalidate()
                }
                override fun onStartTrackingTouch(s: SeekBar?) {}
                override fun onStopTrackingTouch(s: SeekBar?) { AppState.save(requireContext()); AppState.notifyChanged() }
            })
        }

        root.addView(TextView(requireContext()).apply {
            text = "💡 Экспорт для печати: свой STL можно нарезать в Cura/PrusaSlicer. Размеры деталей считаются по шагу платы 2.54 мм. Если деталь подсвечена красным на виде сверху — она не влезает: сдвинь её или возьмите корпус побольше."
            textSize = 11f; setTextColor(Color.parseColor("#546E7A")); setPadding(0, dp(10), 0, dp(6))
        })

        refresh()
        syncFromComp()
        return scroll
    }

    private fun refresh() {
        val m = AppState.caseModel
        if (m == null) {
            infoText?.text = "Корпус не выбран."
        } else {
            infoText?.text = "Файл: ${m.name}\n" +
                "Треугольников: ${m.triangles.size} • Габариты: ${"%.1f".format(m.sizeX())} × ${"%.1f".format(m.sizeY())} × ${"%.1f".format(m.sizeZ())} мм\n" +
                "Оценка объёма: ${"%.1f".format(m.volumeMm3() / 1000.0)} см³ • Деталей в корпусе: ${AppState.project.components.count { it.insideCase }} из ${AppState.project.components.size}"
        }
        view3d?.invalidate()
        topView?.invalidate()
    }
}
