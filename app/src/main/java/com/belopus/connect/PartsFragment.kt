package com.belopus.connect

import android.graphics.Color
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import androidx.fragment.app.Fragment

class PartsFragment : Fragment() {
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        val scroll = ScrollView(requireContext())
        val root = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(10), dp(10), dp(10), dp(10))
            setBackgroundColor(Color.parseColor("#ECEFF1"))
        }
        scroll.addView(root)

        root.addView(TextView(requireContext()).apply {
            text = "🧩 Каталог компонентов"
            textSize = 19f; setTextColor(Color.parseColor("#0D47A1"))
        })
        root.addView(TextView(requireContext()).apply {
            text = "Нажмите «+ На плату» — деталь появится на макетной плате. Дальше её можно двигать, поворачивать, паять к ней провода и ставить в корпус."
            textSize = 13f; setPadding(0, dp(4), 0, dp(8))
        })

        for (def in Catalog.defs) {
            val card = LinearLayout(requireContext()).apply {
                orientation = LinearLayout.VERTICAL
                setBackgroundColor(Color.WHITE)
                setPadding(dp(10), dp(8), dp(10), dp(8))
                val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                lp.bottomMargin = dp(8)
                layoutParams = lp
            }
            card.addView(TextView(requireContext()).apply { text = def.title; textSize = 16f; setTextColor(Color.BLACK) })
            card.addView(TextView(requireContext()).apply { text = def.desc; textSize = 12f; setTextColor(Color.parseColor("#455A64")) })
            card.addView(TextView(requireContext()).apply {
                text = "Размер: ${def.wGrid}×${def.hGrid} отв. (${"%.1f".format(def.wGrid * 2.54f)}×${"%.1f".format(def.hGrid * 2.54f)} мм), высота ${def.heightMm} мм • Пины: ${def.pins.joinToString(", ") { it.name }}"
                textSize = 11f; setTextColor(Color.parseColor("#78909C"))
            })
            card.addView(Button(requireContext()).apply {
                text = "+ На плату"
                setOnClickListener {
                    AppState.addComponent(def.id)
                    AppState.save(requireContext())
                    Toast.makeText(requireContext(), "«${def.title}» — на плате! Перейдите во вкладку «Плата».", Toast.LENGTH_SHORT).show()
                    (activity as? MainActivity)?.show("board")
                }
            })
            root.addView(card)
        }

        root.addView(TextView(requireContext()).apply {
            text = "📋 Уже на плате (${AppState.project.components.size}):"
            textSize = 16f; setPadding(0, dp(10), 0, dp(6)); setTextColor(Color.parseColor("#0D47A1"))
        })
        if (AppState.project.components.isEmpty()) {
            root.addView(TextView(requireContext()).apply { text = "Пока пусто — добавьте первую деталь выше."; textSize = 13f })
        }
        for (c in AppState.project.components) {
            val row = LinearLayout(requireContext()).apply {
                orientation = LinearLayout.HORIZONTAL
                setBackgroundColor(Color.WHITE)
                setPadding(dp(8), dp(6), dp(8), dp(6))
                val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                lp.bottomMargin = dp(4); layoutParams = lp
            }
            row.addView(TextView(requireContext()).apply {
                text = "${c.def().title} • (${c.gridX},${c.gridY})${if (c.insideCase) " • 📦 в корпусе" else ""}"
                textSize = 13f
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            })
            row.addView(Button(requireContext()).apply {
                text = "🗑"
                setOnClickListener {
                    AppState.deleteComponent(c.id)
                    AppState.save(requireContext())
                    (activity as? MainActivity)?.show("parts")
                }
            })
            root.addView(row)
        }
        return scroll
    }
}
