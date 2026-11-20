package com.belopus.connect

import android.graphics.Color
import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity

class MainActivity : AppCompatActivity() {

    private lateinit var navButtons: List<Button>
    private var currentTag = "board"

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        if (savedInstanceState == null) {
            AppState.load(this)
        }
        val header = findViewById<TextView>(R.id.headerStatus)
        fun refreshHeader() {
            header.text = "Деталей: ${AppState.project.components.size} • Проводов/паек: ${AppState.project.wires.size} • " +
                (if (AppState.running) "▶ СИМУЛЯЦИЯ ИДЁТ" else if (AppState.project.flashed) "Прошивка залита ✓" else "Прошивка не залита") +
                (AppState.caseModel?.let { " • Корпус: ${it.name}" } ?: "")
        }
        AppState.addListener { refreshHeader() }
        refreshHeader()

        val bBoard = findViewById<Button>(R.id.navBoard)
        val bParts = findViewById<Button>(R.id.navParts)
        val bCase = findViewById<Button>(R.id.navCase)
        val bCode = findViewById<Button>(R.id.navCode)
        val bSim = findViewById<Button>(R.id.navSim)
        navButtons = listOf(bBoard, bParts, bCase, bCode, bSim)

        bBoard.setOnClickListener { show("board") }
        bParts.setOnClickListener { show("parts") }
        bCase.setOnClickListener { show("case") }
        bCode.setOnClickListener { show("code") }
        bSim.setOnClickListener { show("sim") }

        if (savedInstanceState == null) show("board")
    }

    fun show(tag: String) {
        currentTag = tag
        val f = when (tag) {
            "parts" -> PartsFragment()
            "case" -> CaseFragment()
            "code" -> FirmwareFragment()
            "sim" -> SimFragment()
            else -> BoardFragment()
        }
        supportFragmentManager.beginTransaction()
            .replace(R.id.container, f, tag)
            .commit()
        navButtons.forEach { it.setBackgroundColor(Color.parseColor("#ECEFF1")) }
        val idx = listOf("board", "parts", "case", "code", "sim").indexOf(tag)
        if (idx >= 0) navButtons[idx].setBackgroundColor(Color.parseColor("#BBDEFB"))
    }

    override fun onPause() {
        super.onPause()
        AppState.save(this)
    }
}
