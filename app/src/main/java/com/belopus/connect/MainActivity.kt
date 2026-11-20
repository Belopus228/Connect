package com.belopus.connect

import android.graphics.Color
import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity

class MainActivity : AppCompatActivity() {

    private lateinit var navButtons: Map<String, Button>
    private lateinit var topRun: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        if (savedInstanceState == null) {
            AppState.load(this)
        }

        val header = findViewById<TextView>(R.id.headerStatus)
        topRun = findViewById(R.id.topRun)
        fun refreshHeader() {
            header.text =
                "Деталей: ${AppState.project.components.size} • Соединений: ${AppState.project.wires.size} • " +
                    (if (AppState.running) "▶ симуляция идёт" else if (AppState.project.flashed) "прошивка залита ✓" else "прошивка не залита") +
                    (AppState.caseModel?.let { " • 📦 ${it.name}" } ?: "") +
                    " • 📱 горизонтальный режим"
            topRun.text = if (AppState.running) "⏹ СТОП" else "▶ ЗАПУСК"
            topRun.setBackgroundColor(Color.parseColor(if (AppState.running) "#7F1D1D" else "#14532D"))
        }
        AppState.addListener { refreshHeader() }
        refreshHeader()

        findViewById<Button>(R.id.topSave).setOnClickListener {
            AppState.save(this)
            Toast.makeText(this, "Проект сохранён", Toast.LENGTH_SHORT).show()
        }
        topRun.setOnClickListener {
            val wasRunning = AppState.running
            if (wasRunning) AppState.stopSimulation() else AppState.startSimulation(this)
            refreshHeader()
            Toast.makeText(
                this,
                if (wasRunning) "Симуляция остановлена" else "Симуляция запущена — плата слева, журнал во вкладке «Симул.»",
                Toast.LENGTH_SHORT
            ).show()
        }

        val bBoard = findViewById<Button>(R.id.navBoard)
        val bCase = findViewById<Button>(R.id.navCase)
        val bCode = findViewById<Button>(R.id.navCode)
        val bSim = findViewById<Button>(R.id.navSim)
        navButtons = mapOf("board" to bBoard, "case" to bCase, "code" to bCode, "sim" to bSim)

        bBoard.setOnClickListener { show("board") }
        bCase.setOnClickListener { show("case") }
        bCode.setOnClickListener { show("code") }
        bSim.setOnClickListener { show("sim") }

        if (savedInstanceState == null) show("board")
    }

    fun show(tag: String) {
        // «Детали» теперь живут в правой панели платы, отдельный экран не нужен
        val realTag = if (tag == "parts") "board" else tag
        val f = when (realTag) {
            "case" -> CaseFragment()
            "code" -> FirmwareFragment()
            "sim" -> SimFragment()
            else -> BoardFragment()
        }
        supportFragmentManager.beginTransaction()
            .replace(R.id.container, f, realTag)
            .commit()
        navButtons.forEach { (k, b) ->
            b.setBackgroundColor(Color.parseColor(if (k == realTag) "#2563EB" else "#1E293B"))
            b.setTextColor(Color.WHITE)
        }
    }

    override fun onPause() {
        super.onPause()
        AppState.save(this)
    }
}
