package com.belopus.connect

import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.fragment.app.Fragment

class SimFragment : Fragment() {
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    private var statusText: TextView? = null
    private var stateText: TextView? = null
    private var logText: TextView? = null
    private val listener: () -> Unit = { refresh() }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        val scroll = ScrollView(requireContext())
        val root = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(10), dp(10), dp(10), dp(10))
            setBackgroundColor(Color.parseColor("#ECEFF1"))
        }
        scroll.addView(root)

        root.addView(TextView(requireContext()).apply {
            text = "▶ Симуляция работы схемы"
            textSize = 19f; setTextColor(Color.parseColor("#0D47A1"))
        })

        val runBtn = Button(requireContext()).apply {
            textSize = 16f
            setOnClickListener {
                if (AppState.running) AppState.stopSimulation()
                else AppState.startSimulation(requireContext())
                refresh()
            }
        }
        runBtn.id = 1001
        root.addView(runBtn)
        // сохраняем ссылку через тег
        runBtn.tag = "run"
        root.tag = runBtn

        statusText = TextView(requireContext()).apply { textSize = 14f; setPadding(0, dp(8), 0, dp(4)) }
        root.addView(statusText)

        stateText = TextView(requireContext()).apply {
            textSize = 13f; setBackgroundColor(Color.WHITE); setPadding(dp(8), dp(8), dp(8), dp(8))
        }
        root.addView(TextView(requireContext()).apply { text = "Состояние компонентов:"; textSize = 14f; setPadding(0, dp(8), 0, dp(4)) })
        root.addView(stateText)

        root.addView(TextView(requireContext()).apply {
            text = "Как взаимодействовать: на вкладке «Плата» включите режим «👆 Нажимать» и тапайте тактовые кнопки (зелёный = нажата), крутите потенциометр. OLED/TFT покажут текст от oled.print(), светодиоды загорятся по-настоящему с расчётом тока через резистор."
            textSize = 12f; setTextColor(Color.parseColor("#455A64")); setPadding(0, dp(8), 0, dp(4))
        })

        val clearBtn = Button(requireContext()).apply {
            text = "Очистить журнал"
            setOnClickListener { AppState.simLog.clear(); refresh() }
        }
        root.addView(clearBtn)

        root.addView(TextView(requireContext()).apply { text = "Serial Monitor / журнал:"; textSize = 14f; setPadding(0, dp(8), 0, dp(4)) })
        logText = TextView(requireContext()).apply {
            textSize = 12f; typeface = Typeface.MONOSPACE
            setBackgroundColor(Color.parseColor("#102027")); setTextColor(Color.parseColor("#C5E1A5"))
            setPadding(dp(8), dp(8), dp(8), dp(8))
        }
        root.addView(logText)

        AppState.addListener(listener)
        refresh()
        return scroll
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        refresh()
    }

    override fun onResume() {
        super.onResume()
        refresh()
    }

    private fun refresh() {
        if (!isAdded) return
        val view = view ?: return
        val runBtn = view.findViewById<Button>(1001)
        runBtn?.text = if (AppState.running) "⏹ СТОП — остановить симуляцию" else "▶ ЗАПУСК — включить питание и прошивку"
        runBtn?.setBackgroundColor(Color.parseColor(if (AppState.running) "#FFCDD2" else "#C8E6C9"))
        val state = CircuitSim.compute()
        statusText?.text = when {
            AppState.project.components.none { it.typeId == "esp32" } -> "⚠ В схеме нет ESP32 — добавьте из каталога."
            state.espPowered.isEmpty() -> "❌ ESP32 не запитан. Проверьте пайку: батарея «+» → ESP VIN (или 3V3), «−» → GND."
            AppState.running -> "⚡ ESP32 запитан, прошивка выполняется. Все системы в норме."
            else -> "⚡ ESP32 запитан, но симуляция остановлена — нажмите «ЗАПУСК»."
        }
        val sb = StringBuilder()
        for (c in AppState.project.components) {
            when (c.typeId) {
                "esp32" -> sb.append("• ESP32: ${if (state.espPowered.contains(c.id)) "⚡ запитан 3.3В" else "❌ без питания"} • прошивка ${if (AppState.project.flashed) "залита ✓" else "не залита"}\n")
                "led" -> {
                    val st = state.leds[c.id]
                    sb.append("• LED (${c.props["colorName"]}): ")
                    when {
                        st == null -> sb.append("нет данных\n")
                        st.burnt -> sb.append("💥 СГОРЕЛ (ток ${"%.0f".format(st.currentMa)} мА — нет резистора!)\n")
                        st.brightness > 0 && AppState.running -> sb.append("💡 светится ${(st.brightness * 100).toInt()}%, ток ${"%.1f".format(st.currentMa)} мА\n")
                        else -> sb.append("не горит (ток ${"%.1f".format(st?.currentMa ?: 0f)} мА)\n")
                    }
                }
                "oled", "tft", "tft18", "lcd1602" -> sb.append("• ${c.def().title}: на экране «${c.props["displayText"] ?: ""}»\n")
                "nrf24" -> sb.append("• nRF24: канал ${c.props["channel"]}, адрес ${c.props["address"]}, последний пакет: «${c.props["lastPacket"]}»\n")
                "button" -> sb.append("• Кнопка: ${if (AppState.pressedButtons.contains(c.id)) "НАЖАТА 🟢" else "отпущена"} (режим «Нажимать» на Плате)\n")
                "bat18650", "bat9v", "lipo" -> sb.append("• ${c.def().title}: ${c.props["voltage"]}В\n")
                "resistor" -> sb.append("• Резистор: ${c.props["ohms"]} Ω\n")
                "pot" -> sb.append("• Потенциометр: ${c.props["value"]}/1023\n")
                "servo" -> sb.append("• Серво SG90: угол ${c.props["angle"]}°\n")
                "dht22" -> sb.append("• DHT22: ${c.props["temp"]}°C, влажность ${c.props["hum"]}%\n")
                else -> sb.append("• ${c.def().title}\n")
            }
        }
        stateText?.text = if (sb.isEmpty()) "На плате пока нет деталей." else sb.toString()
        logText?.text = AppState.simLog.takeLast(80).joinToString("\n").ifEmpty { "Журнал пуст. Запустите симуляцию." }
        // скролл журнала вниз не делаем автоматически внутри ScrollView родителя — ок
    }

    override fun onDestroyView() {
        AppState.removeListener(listener)
        super.onDestroyView()
    }
}
