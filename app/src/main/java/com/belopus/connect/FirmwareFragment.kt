package com.belopus.connect

import android.graphics.Color
import android.graphics.Typeface
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.fragment.app.Fragment
import java.security.MessageDigest

class FirmwareFragment : Fragment() {
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    private var codeEdit: EditText? = null
    private var progress: ProgressBar? = null
    private var logView: TextView? = null
    private var binInfo: TextView? = null
    private val handler = Handler(Looper.getMainLooper())

    private var pendingBinName: String = ""
    private var pendingBinHash: String = ""
    private var pendingBinSize: Int = 0

    private val pickBin = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri: Uri? ->
        if (uri == null) return@registerForActivityResult
        try {
            val bytes = requireContext().contentResolver.openInputStream(uri)?.readBytes()
                ?: throw IllegalStateException("Пустой файл")
            val digest = MessageDigest.getInstance("SHA-256").digest(bytes)
            val hash = digest.joinToString("") { "%02x".format(it) }
            pendingBinName = uri.lastPathSegment?.substringAfterLast('/') ?: "firmware.bin"
            pendingBinHash = hash
            pendingBinSize = bytes.size
            AppState.project.firmwareBinName = pendingBinName
            binInfo?.text = "📦 Выбран .bin: $pendingBinName • ${bytes.size} байт • SHA-256: ${hash.take(24)}…"
            AppState.fwLogLine("Файл прошивки: $pendingBinName (${bytes.size} байт)")
            AppState.fwLogLine("SHA-256: $hash")
            AppState.fwLogLine("Нажмите «Залить прошивку» — файл будет записан в виртуальную флеш-память ESP32.")
            refreshLog()
            AppState.save(requireContext())
        } catch (e: Exception) {
            Toast.makeText(requireContext(), "Ошибка .bin: ${e.message}", Toast.LENGTH_LONG).show()
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
            text = "💻 Прошивка ESP32 DevKit"
            textSize = 19f; setTextColor(Color.parseColor("#0D47A1"))
        })
        root.addView(TextView(requireContext()).apply {
            text = "Напишите код (команды выполняются по кругу, как loop() в Arduino), выберите пример или загрузите готовый .bin — и нажмите «Залить прошивку». ESP на плате перезагрузится и начнёт выполнять код в симуляции."
            textSize = 13f; setPadding(0, dp(4), 0, dp(6))
        })

        root.addView(TextView(requireContext()).apply { text = "Готовые примеры:"; textSize = 13f })
        val exampleSpinner = Spinner(requireContext())
        exampleSpinner.adapter = ArrayAdapter(requireContext(), android.R.layout.simple_spinner_dropdown_item, FirmwareExamples.ALL.map { it.first })
        root.addView(exampleSpinner)
        root.addView(Button(requireContext()).apply {
            text = "Вставить пример в редактор"
            setOnClickListener {
                val code = FirmwareExamples.ALL[exampleSpinner.selectedItemPosition].second
                codeEdit?.setText(code)
                AppState.project.firmwareCode = code
                AppState.save(requireContext())
                Toast.makeText(requireContext(), "Пример вставлен. Теперь «Залить прошивку».", Toast.LENGTH_SHORT).show()
            }
        })

        codeEdit = EditText(requireContext()).apply {
            setText(AppState.project.firmwareCode)
            typeface = Typeface.MONOSPACE
            textSize = 13f
            minLines = 10
            setBackgroundColor(Color.WHITE)
            setPadding(dp(8), dp(8), dp(8), dp(8))
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(220))
        }
        root.addView(TextView(requireContext()).apply { text = "Код прошивки:"; textSize = 13f; setPadding(0, dp(8), 0, dp(2)) })
        root.addView(codeEdit)

        root.addView(Button(requireContext()).apply {
            text = "💾 Сохранить код"
            setOnClickListener {
                AppState.project.firmwareCode = codeEdit?.text?.toString() ?: ""
                AppState.save(requireContext())
                Toast.makeText(requireContext(), "Код сохранён", Toast.LENGTH_SHORT).show()
            }
        })

        binInfo = TextView(requireContext()).apply {
            textSize = 12f
            text = if (AppState.project.firmwareBinName.isNotEmpty()) "Последний .bin: ${AppState.project.firmwareBinName}" else "Файл .bin не выбран (необязательно — можно шить прямо код выше)."
            setPadding(0, dp(8), 0, dp(2))
        }
        root.addView(binInfo)
        root.addView(Button(requireContext()).apply {
            text = "📂 Выбрать готовый firmware.bin…"
            setOnClickListener { pickBin.launch(arrayOf("*/*")) }
        })

        progress = ProgressBar(requireContext(), null, android.R.attr.progressBarStyleHorizontal).apply {
            max = 100; progress = 0
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(24))
        }
        root.addView(progress)

        val flashBtn = Button(requireContext()).apply {
            text = "🚀 ЗАЛИТЬ ПРОШИВКУ В ESP32"
            textSize = 15f
            setBackgroundColor(Color.parseColor("#BBDEFB"))
        }
        flashBtn.setOnClickListener { flash(flashBtn) }
        root.addView(flashBtn)

        root.addView(Button(requireContext()).apply {
            text = if (AppState.running) "⏹ Остановить симуляцию" else "▶ Запустить симуляцию с этой прошивкой"
            setOnClickListener {
                if (AppState.running) AppState.stopSimulation() else {
                    AppState.project.firmwareCode = codeEdit?.text?.toString() ?: ""
                    AppState.startSimulation(requireContext())
                }
                (activity as? MainActivity)?.show("sim")
            }
        })

        logView = TextView(requireContext()).apply {
            textSize = 12f; typeface = Typeface.MONOSPACE
            setBackgroundColor(Color.parseColor("#102027")); setTextColor(Color.parseColor("#A5D6A7"))
            setPadding(dp(8), dp(8), dp(8), dp(8))
        }
        root.addView(TextView(requireContext()).apply { text = "Журнал прошивки (esptool):"; textSize = 13f; setPadding(0, dp(10), 0, dp(2)) })
        root.addView(logView)

        root.addView(TextView(requireContext()).apply {
            text = """
Поддерживаемые команды прошивки:
• pinMode(13, OUTPUT);  pinMode(32, INPUT_PULLUP);
• digitalWrite(13, HIGH); / LOW;
• if (digitalRead(32) == LOW) { ... } else { ... }
• delay(500);
• Serial.println("текст");
• oled.print("текст");  oled.clear();
• tft.print("текст");  tft.clear();   // TFT 1.8" ST7735 и TFT 2.4"
• nrf.send("пакет");
• servo.write(90);
Номер в коде = номер D-пина ESP32 (13 → D13, 32 → D32).
Пины ESP на плате: D13, D12, D14, D27, D26, D25, D33, D32, D23, D22, D21, D19, D18, D5.
В демо-схеме: LED через резистор на D13, кнопка — D32→GND, OLED — D21/D22, nRF24 — 3V3/GND.
""".trimIndent()
            textSize = 11f; setTextColor(Color.parseColor("#546E7A")); setPadding(0, dp(8), 0, dp(6))
        })

        if (AppState.fwLog.isEmpty()) {
            AppState.fwLogLine("esptool.py (виртуальный) готов. Порт: /dev/ESP32-USB")
            AppState.fwLogLine("Чип: ESP32-D0WD-V3, Flash: 4MB")
        }
        refreshLog()
        return scroll
    }

    private fun refreshLog() {
        logView?.text = AppState.fwLog.takeLast(60).joinToString("\n").ifEmpty { "—" }
    }

    private fun flash(btn: Button) {
        AppState.project.firmwareCode = codeEdit?.text?.toString() ?: ""
        btn.isEnabled = false
        progress?.progress = 0
        val srcName = if (pendingBinName.isNotEmpty()) pendingBinName else "sketch.ino (скомпилирован в firmware.bin)"
        val steps = listOf(
            5 to "Подключение к ESP32 по USB… OK",
            15 to "Чип: ESP32-D0WD-V3 • MAC: 24:6F:28:XX:XX:XX • Flash 4MB",
            30 to "Источник прошивки: $srcName" + if (pendingBinSize > 0) " ($pendingBinSize байт, SHA-256 ${pendingBinHash.take(16)}…)" else "",
            45 to "Стирание flash 0x00000–0x3FFFF… готово",
            65 to "Запись bootloader @ 0x1000… 100%",
            85 to "Запись firmware @ 0x10000… 100%",
            95 to "Проверка хэша… OK ✓",
            100 to "Hard reset через RTS… ESP32 перезагружена! Прошивка работает — смотрите вкладку «Симуляция» и плату (светодиоды, OLED)."
        )
        AppState.fwLogLine("—— Новая заливка ——")
        var idx = 0
        fun next() {
            if (idx >= steps.size) {
                btn.isEnabled = true
                AppState.project.flashed = true
                AppState.save(requireContext())
                AppState.fwLogLine("✅ Прошивка успешно залита!")
                AppState.log("🚀 Прошивка залита в ESP32, чип перезагружен")
                refreshLog()
                AppState.notifyChanged()
                Toast.makeText(requireContext(), "Прошивка залита! Запускаю симуляцию…", Toast.LENGTH_SHORT).show()
                AppState.startSimulation(requireContext())
                (activity as? MainActivity)?.show("sim")
                return
            }
            val (pct, msg) = steps[idx]
            progress?.progress = pct
            AppState.fwLogLine("[$pct%] $msg")
            refreshLog()
            idx++
            handler.postDelayed({ next() }, 420)
        }
        next()
    }
}
