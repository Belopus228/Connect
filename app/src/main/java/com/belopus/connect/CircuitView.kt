package com.belopus.connect

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View
import kotlin.math.hypot

class CircuitView @JvmOverloads constructor(
    context: Context, attrs: AttributeSet? = null
) : View(context, attrs) {

    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private var cell = 28f
    private var ox = 16f
    private var oy = 16f

    private var dragComp: PlacedComponent? = null
    private var dragDx = 0f
    private var dragDy = 0f

    private var pendingPin: Pair<PlacedComponent, PinDef>? = null

    var onPinTapped: ((PlacedComponent, PinDef) -> Unit)? = null
    var onComponentMoved: (() -> Unit)? = null

    private val wireColors = listOf(
        0xFFD32F2F.toInt(), 0xFFFF6F00.toInt(), 0xFFF9A825.toInt(),
        0xFF2E7D32.toInt(), 0xFF1565C0.toInt(), 0xFF6A1B9A.toInt(), 0xFF00838F.toInt()
    )

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val w = MeasureSpec.getSize(widthMeasureSpec)
        val proj = AppState.project
        cell = ((w - 32f) / proj.boardCols).coerceAtLeast(14f)
        val h = (proj.boardRows * cell + 32f).toInt()
        setMeasuredDimension(w, h)
    }

    fun gx(gridX: Int) = ox + gridX * cell
    fun gy(gridY: Int) = oy + gridY * cell

    fun pinXY(c: PlacedComponent, pin: PinDef): Pair<Float, Float> {
        val (px, py) = c.pinPos(pin)
        return Pair(gx(c.gridX + px) + cell / 2, gy(c.gridY + py) + cell / 2)
    }

    private fun compRect(c: PlacedComponent): RectF {
        return RectF(gx(c.gridX), gy(c.gridY), gx(c.gridX + c.effW()), gy(c.gridY + c.effH()))
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val proj = AppState.project
        val state = CircuitSim.compute()
        // плата
        paint.style = Paint.Style.FILL
        paint.color = Color.parseColor("#14532D")
        val board = RectF(ox - 8, oy - 8, gx(proj.boardCols) + 8, gy(proj.boardRows) + 8)
        canvas.drawRoundRect(board, 14f, 14f, paint)
        paint.color = Color.parseColor("#F5F5DC")
        paint.textSize = cell * 0.42f
        paint.textAlign = Paint.Align.LEFT
        canvas.drawText("МАКЕТНАЯ ПЛАТА ДЛЯ ПАЙКИ  •  шаг 2.54 мм  •  ${proj.boardCols}×${proj.boardRows}", ox, oy - 12f.coerceAtLeast(2f), paint)
        // отверстия
        paint.color = Color.parseColor("#0B3D20")
        for (y in 0 until proj.boardRows) for (x in 0 until proj.boardCols) {
            canvas.drawCircle(gx(x) + cell / 2, gy(y) + cell / 2, cell * 0.16f, paint)
        }
        paint.color = Color.parseColor("#9E9D24")
        for (y in 0 until proj.boardRows) for (x in 0 until proj.boardCols) {
            canvas.drawCircle(gx(x) + cell / 2, gy(y) + cell / 2, cell * 0.07f, paint)
        }

        // провода и пайка (под компонентами)
        for (w in proj.wires) {
            val a = AppState.compById(w.aComp) ?: continue
            val b = AppState.compById(w.bComp) ?: continue
            val pa = a.def().pins.firstOrNull { it.name == w.aPin } ?: continue
            val pb = b.def().pins.firstOrNull { it.name == w.bPin } ?: continue
            val (x1, y1) = pinXY(a, pa)
            val (x2, y2) = pinXY(b, pb)
            if (w.style == "solder") {
                paint.style = Paint.Style.STROKE
                paint.strokeWidth = cell * 0.22f
                paint.color = Color.parseColor("#C0C0C0")
                val path = Path()
                path.moveTo(x1, y1)
                path.lineTo(x2, y1)
                path.lineTo(x2, y2)
                canvas.drawPath(path, paint)
                paint.color = Color.parseColor("#78909C")
                paint.strokeWidth = cell * 0.08f
                canvas.drawPath(path, paint)
            } else {
                paint.style = Paint.Style.STROKE
                paint.strokeWidth = cell * 0.18f
                paint.color = w.color
                val path = Path()
                path.moveTo(x1, y1)
                val midY = maxOf(y1, y2) + cell * 1.2f
                path.cubicTo(x1, midY, x2, midY, x2, y2)
                canvas.drawPath(path, paint)
            }
            // точки пайки
            paint.style = Paint.Style.FILL
            paint.color = Color.parseColor("#ECEFF1")
            canvas.drawCircle(x1, y1, cell * 0.20f, paint)
            canvas.drawCircle(x2, y2, cell * 0.20f, paint)
            paint.color = Color.parseColor("#90A4AE")
            canvas.drawCircle(x1, y1, cell * 0.10f, paint)
            canvas.drawCircle(x2, y2, cell * 0.10f, paint)
        }

        // ожидающий пин
        pendingPin?.let { (c, p) ->
            val (x, y) = pinXY(c, p)
            paint.style = Paint.Style.STROKE
            paint.color = Color.YELLOW
            paint.strokeWidth = 3f
            canvas.drawCircle(x, y, cell * 0.55f, paint)
            paint.style = Paint.Style.FILL
        }

        // компоненты
        for (c in proj.components) drawComponent(canvas, c, state)

        // подпись режима
        paint.color = Color.WHITE
        paint.textSize = 30f
        paint.textAlign = Paint.Align.LEFT
    }

    private fun drawComponent(canvas: Canvas, c: PlacedComponent, state: CircuitState) {
        val def = c.def()
        val r = compRect(c)
        val selected = c.id == AppState.selectedId
        // тень
        paint.style = Paint.Style.FILL
        paint.color = Color.argb(70, 0, 0, 0)
        canvas.drawRoundRect(RectF(r.left + 4, r.top + 5, r.right + 4, r.bottom + 5), 8f, 8f, paint)
        // корпус
        paint.color = def.color
        canvas.drawRoundRect(r, 8f, 8f, paint)
        if (selected) {
            paint.style = Paint.Style.STROKE
            paint.color = Color.YELLOW
            paint.strokeWidth = 4f
            canvas.drawRoundRect(RectF(r.left - 5, r.top - 5, r.right + 5, r.bottom + 5), 10f, 10f, paint)
            paint.style = Paint.Style.FILL
        }

        // спец-отрисовка
        when (c.typeId) {
            "esp32" -> {
                paint.color = Color.parseColor("#37474F")
                val chip = RectF(r.left + r.width() * 0.28f, r.top + r.height() * 0.38f, r.right - r.width() * 0.28f, r.top + r.height() * 0.62f)
                canvas.drawRoundRect(chip, 6f, 6f, paint)
                paint.color = Color.parseColor("#90CAF9")
                canvas.drawRect(RectF(r.left + r.width() * 0.2f, r.top + 4f, r.right - r.width() * 0.2f, r.top + r.height() * 0.16f), paint)
                val powered = state.espPowered.contains(c.id)
                paint.color = if (powered && AppState.running) Color.parseColor("#69F0AE") else Color.parseColor("#546E7A")
                canvas.drawCircle(r.right - 14f, r.top + 14f, 7f, paint)
            }
            "oled", "tft", "lcd1602" -> {
                paint.color = if (c.typeId == "lcd1602") Color.parseColor("#9CCC65") else Color.BLACK
                val scr = RectF(r.left + 8f, r.top + cell * 1.2f, r.right - 8f, r.bottom - 8f)
                canvas.drawRoundRect(scr, 6f, 6f, paint)
                val powered = AppState.project.wires.any { (it.aComp == c.id && it.aPin == "VCC") || (it.bComp == c.id && it.bPin == "VCC") }
                paint.color = when {
                    c.typeId == "lcd1602" -> Color.parseColor("#1B5E20")
                    !powered -> Color.parseColor("#37474F")
                    else -> Color.parseColor("#80DEEA")
                }
                paint.textSize = (cell * 0.52f).coerceAtLeast(11f)
                paint.textAlign = Paint.Align.CENTER
                val txt = c.props["displayText"] ?: def.title
                val shown = if (txt.isEmpty()) "—" else txt
                // перенос по словам, макс 3 строки
                val words = shown.split(" ")
                val lines = mutableListOf<String>()
                var cur = ""
                for (w in words) {
                    if ((cur + " " + w).length > 16 && cur.isNotEmpty()) { lines.add(cur); cur = w } else cur = if (cur.isEmpty()) w else "$cur $w"
                }
                if (cur.isNotEmpty()) lines.add(cur)
                lines.take(3).forEachIndexed { i, ln ->
                    canvas.drawText(ln, scr.centerX(), scr.top + cell * (0.9f + i * 0.75f), paint)
                }
            }
            "led" -> {
                paint.color = Color.parseColor("#ECEFF1")
                canvas.drawRect(RectF(r.left + 3f, r.bottom - cell * 0.5f, r.right - 3f, r.bottom), paint)
                val st = state.leds[c.id]
                val baseColor = when (c.props["colorName"]) {
                    "Зелёный" -> Color.GREEN; "Синий" -> Color.BLUE; "Жёлтый" -> Color.YELLOW
                    "Белый" -> Color.WHITE; else -> Color.RED
                }
                if (st != null && st.burnt) {
                    paint.color = Color.DKGRAY
                    canvas.drawCircle(r.centerX(), r.centerY() - cell * 0.2f, cell * 0.62f, paint)
                    paint.color = Color.RED
                    paint.textSize = cell * 0.9f
                    paint.textAlign = Paint.Align.CENTER
                    canvas.drawText("✕", r.centerX(), r.centerY() + 2f, paint)
                } else {
                    // свечение
                    if (st != null && st.brightness > 0.02f && AppState.running) {
                        val glow = Paint(Paint.ANTI_ALIAS_FLAG)
                        glow.color = baseColor
                        glow.alpha = (140 * st.brightness).toInt()
                        canvas.drawCircle(r.centerX(), r.centerY() - cell * 0.2f, cell * (0.9f + st.brightness * 0.9f), glow)
                    }
                    paint.color = baseColor
                    paint.alpha = if (st != null && st.brightness > 0.02f && AppState.running) 255 else 120
                    canvas.drawCircle(r.centerX(), r.centerY() - cell * 0.2f, cell * 0.62f, paint)
                    paint.alpha = 255
                }
            }
            "resistor" -> {
                paint.color = Color.parseColor("#8D6E63")
                val stripes = listOf(Color.RED, Color.RED, Color.parseColor("#795548"), Color.parseColor("#FFD700"))
                stripes.forEachIndexed { i, col ->
                    paint.color = col
                    val x = r.left + r.width() * (0.3f + i * 0.13f)
                    canvas.drawRect(x, r.top + 2f, x + 4f, r.bottom - 2f, paint)
                }
            }
            "button" -> {
                val pressed = AppState.pressedButtons.contains(c.id)
                paint.color = if (pressed) Color.parseColor("#69F0AE") else Color.parseColor("#EF5350")
                canvas.drawCircle(r.centerX(), r.centerY(), cell * 0.62f, paint)
                paint.color = Color.WHITE
                paint.textSize = cell * 0.42f
                paint.textAlign = Paint.Align.CENTER
                canvas.drawText(if (pressed) "ON" else "○", r.centerX(), r.centerY() + cell * 0.16f, paint)
            }
            "nrf24" -> {
                paint.color = Color.parseColor("#90CAF9")
                paint.textSize = (cell * 0.45f).coerceAtLeast(10f)
                paint.textAlign = Paint.Align.CENTER
                canvas.drawText("((📡))", r.centerX(), r.top + cell * 1.1f, paint)
                canvas.drawText("ch${c.props["channel"] ?: "76"}", r.centerX(), r.top + cell * 1.9f, paint)
            }
            "servo" -> {
                paint.color = Color.WHITE
                paint.textSize = (cell * 0.5f).coerceAtLeast(10f)
                paint.textAlign = Paint.Align.CENTER
                canvas.drawText("${c.props["angle"] ?: "90"}°", r.centerX(), r.centerY(), paint)
            }
        }

        // заголовок
        paint.color = Color.WHITE
        paint.textSize = (cell * 0.46f).coerceAtLeast(10f)
        paint.textAlign = Paint.Align.CENTER
        val title = when (c.typeId) {
            "resistor" -> "R ${c.props["ohms"] ?: "220"}Ω"
            "bat18650", "bat9v", "lipo" -> "${def.title} ${c.props["voltage"] ?: ""}В"
            "led" -> "LED ${c.props["colorName"] ?: ""}"
            else -> def.title
        }
        canvas.drawText(title, r.centerX(), r.top + cell * 0.68f, paint)

        // пины
        for (pin in def.pins) {
            val (x, y) = pinXY(c, pin)
            val isPending = pendingPin?.let { it.first.id == c.id && it.second.name == pin.name } == true
            paint.color = if (isPending) Color.YELLOW else Color.parseColor("#FFD54F")
            canvas.drawCircle(x, y, cell * 0.23f, paint)
            paint.color = Color.parseColor("#4E342E")
            canvas.drawCircle(x, y, cell * 0.09f, paint)
            if (cell >= 20f) {
                paint.color = Color.WHITE
                paint.textSize = (cell * 0.34f).coerceAtLeast(8f)
                paint.textAlign = Paint.Align.CENTER
                canvas.drawText(pin.name, x, y - cell * 0.34f, paint)
            }
        }
    }

    fun hitPin(x: Float, y: Float): Pair<PlacedComponent, PinDef>? {
        var best: Pair<PlacedComponent, PinDef>? = null
        var bestD = cell * 0.9f
        for (c in AppState.project.components) {
            for (p in c.def().pins) {
                val (px, py) = pinXY(c, p)
                val d = hypot(x - px, y - py)
                if (d < bestD) { bestD = d; best = c to p }
            }
        }
        return best
    }

    fun hitComponent(x: Float, y: Float): PlacedComponent? {
        // сверху вниз — последние добавленные поверх
        for (c in AppState.project.components.asReversed()) {
            if (compRect(c).contains(x, y)) return c
        }
        return null
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val x = event.x; val y = event.y
        when (event.action) {
            MotionEvent.ACTION_DOWN -> {
                parent?.requestDisallowInterceptTouchEvent(true)
                val hitP = hitPin(x, y)
                val hitC = hitComponent(x, y)
                when (AppState.mode) {
                    "wire", "solder" -> {
                        if (hitP != null) {
                            val pending = pendingPin
                            if (pending == null) {
                                pendingPin = hitP
                                AppState.log("Паяльник: выбран ${hitP.first.def().title}.${hitP.second.name} — теперь коснитесь второго пина")
                            } else if (pending.first.id == hitP.first.id && pending.second.name == hitP.second.name) {
                                pendingPin = null
                            } else {
                                val color = wireColors[(AppState.project.wires.size) % wireColors.size]
                                AppState.project.wires.add(
                                    Wire(
                                        aComp = pending.first.id, aPin = pending.second.name,
                                        bComp = hitP.first.id, bPin = hitP.second.name,
                                        style = if (AppState.mode == "solder") "solder" else "wire",
                                        color = if (AppState.mode == "solder") 0xFF90A4AE.toInt() else color
                                    )
                                )
                                AppState.log(
                                    if (AppState.mode == "solder")
                                        "🔥 Припаяно: ${pending.first.def().title}.${pending.second.name} — ${hitP.first.def().title}.${hitP.second.name} (дорожка+припой)"
                                    else
                                        "🔌 Провод: ${pending.first.def().title}.${pending.second.name} — ${hitP.first.def().title}.${hitP.second.name}"
                                )
                                pendingPin = null
                                AppState.notifyChanged()
                            }
                            invalidate()
                            return true
                        }
                    }
                    "delete" -> {
                        if (hitC != null) {
                            // сначала провода компонента при повторном? удаляем компонент целиком
                            AppState.log("🗑 Удалено: ${hitC.def().title}")
                            AppState.deleteComponent(hitC.id)
                            invalidate()
                            return true
                        }
                    }
                    "sim" -> {
                        if (hitC != null && hitC.typeId == "button") {
                            if (AppState.pressedButtons.contains(hitC.id)) AppState.pressedButtons.remove(hitC.id)
                            else AppState.pressedButtons.add(hitC.id)
                            AppState.log(if (AppState.pressedButtons.contains(hitC.id)) "🔘 Кнопка НАЖАТА" else "🔘 Кнопка отпущена")
                            AppState.notifyChanged()
                            invalidate()
                            return true
                        }
                        if (hitC != null && hitC.typeId == "pot") {
                            val cur = hitC.props["value"]?.toIntOrNull() ?: 512
                            val next = (cur + 128) % 1024
                            hitC.props["value"] = next.toString()
                            AppState.log("🎛 Потенциометр: $next / 1023")
                            AppState.notifyChanged()
                            invalidate()
                            return true
                        }
                        if (hitC != null) { AppState.selectedId = hitC.id; AppState.notifyChanged(); invalidate(); return true }
                    }
                    else -> { // move
                        if (hitC != null) {
                            AppState.selectedId = hitC.id
                            dragComp = hitC
                            dragDx = x - gx(hitC.gridX)
                            dragDy = y - gy(hitC.gridY)
                            AppState.notifyChanged()
                            invalidate()
                            return true
                        } else {
                            AppState.selectedId = null
                            AppState.notifyChanged()
                            invalidate()
                        }
                    }
                }
            }
            MotionEvent.ACTION_MOVE -> {
                val dc = dragComp
                if (dc != null && AppState.mode == "move") {
                    val nx = (((x - dragDx - ox) / cell).toInt()).coerceIn(0, AppState.project.boardCols - dc.effW())
                    val ny = (((y - dragDy - oy) / cell).toInt()).coerceIn(0, AppState.project.boardRows - dc.effH())
                    if (nx != dc.gridX || ny != dc.gridY) {
                        dc.gridX = nx; dc.gridY = ny
                        invalidate()
                    }
                    return true
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (dragComp != null) {
                    dragComp = null
                    onComponentMoved?.invoke()
                    AppState.notifyChanged()
                }
                parent?.requestDisallowInterceptTouchEvent(false)
            }
        }
        return super.onTouchEvent(event)
    }

    fun clearPending() { pendingPin = null; invalidate() }
}
