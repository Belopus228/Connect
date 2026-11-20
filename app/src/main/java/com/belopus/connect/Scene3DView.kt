package com.belopus.connect

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * Настоящий 3D-вид платы: программный перспективный рендер.
 * Мир — в миллиметрах: X вправо по плате, Y вглубь, Z вверх.
 * Камера: орбита (азимут/высота), зум щипком, готовые ракурсы.
 */
class Scene3DView @JvmOverloads constructor(
    context: Context, attrs: AttributeSet? = null
) : View(context, attrs) {

    private data class Vec(val x: Float, val y: Float, val z: Float) {
        operator fun plus(o: Vec) = Vec(x + o.x, y + o.y, z + o.z)
        operator fun minus(o: Vec) = Vec(x - o.x, y - o.y, z - o.z)
        operator fun times(s: Float) = Vec(x * s, y * s, z * s)
    }

    private data class Scr(val x: Float, val y: Float, val scale: Float, val depth: Float)
    private data class Cam(val eye: Vec, val right: Vec, val up: Vec, val fwd: Vec, val focal: Float)
    private data class Face(val pts: List<Vec>, val color: Int, val alpha: Int, var depth: Float = 0f)

    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val density = resources.displayMetrics.density

    var camAzimuth = 0.95f
    var camElevation = 0.92f
    var zoom = 1.0f

    private var lastX = 0f
    private var lastY = 0f
    private var lastPinch = 0f

    private var dragComp: PlacedComponent? = null
    private var dragStartGridX = 0
    private var dragStartGridY = 0
    private var dragStartTouchX = 0f
    private var dragStartTouchY = 0f

    private var pendingPin: Pair<PlacedComponent, PinDef>? = null
    var onComponentMoved: (() -> Unit)? = null

    private fun dot(a: Vec, b: Vec) = a.x * b.x + a.y * b.y + a.z * b.z
    private fun cross(a: Vec, b: Vec) = Vec(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x
    )
    private fun norm(v: Vec): Vec {
        val l = sqrt(dot(v, v))
        return if (l < 0.0001f) Vec(0f, 0f, 1f) else Vec(v.x / l, v.y / l, v.z / l)
    }

    private fun boardW() = AppState.project.boardCols * 2.54f
    private fun boardD() = AppState.project.boardRows * 2.54f

    private fun camera(): Cam {
        val target = Vec(boardW() / 2f, boardD() / 2f, 0f)
        val dist = maxOf(boardW(), boardD()) * 2.1f + 60f
        val dir = Vec(cos(camElevation) * cos(camAzimuth), cos(camElevation) * sin(camAzimuth), sin(camElevation))
        val eye = target + dir * dist
        val fwd = norm(target - eye)
        val right = norm(cross(Vec(0f, 0f, 1f), fwd))
        val up = norm(cross(fwd, right))
        val focal = (minOf(if (width > 0) width else 900, if (height > 0) height else 900) * 1.28f * zoom)
        return Cam(eye, right, up, fwd, focal)
    }

    private fun project(v: Vec, cam: Cam): Scr {
        val rel = v - cam.eye
        val cx = dot(rel, cam.right)
        val cy = dot(rel, cam.up)
        var cz = dot(rel, cam.fwd)
        if (cz < 1f) cz = 1f
        return Scr(width / 2f + cx * cam.focal / cz, height / 2f - cy * cam.focal / cz, cam.focal / cz, cz)
    }

    fun setView(name: String) {
        when (name) {
            "top" -> { camElevation = 1.38f; camAzimuth = 0.95f }
            "front" -> { camElevation = 0.28f; camAzimuth = 1.57f }
            "side" -> { camElevation = 0.32f; camAzimuth = 0.05f }
            else -> { camElevation = 0.92f; camAzimuth = 0.95f }
        }
        invalidate()
    }

    fun clearPending() { pendingPin = null; invalidate() }

    /** Точный пересчёт точки экрана в точку на плоскости платы (Z=0) через луч камеры. */
    fun screenToBoard(sx: Float, sy: Float): Pair<Float, Float>? {
        val cam = camera()
        val cx = (sx - width / 2f) / cam.focal
        val cy = -(sy - height / 2f) / cam.focal
        // направление луча в мировых координатах
        val dir = cam.fwd + cam.right * cx + cam.up * cy
        if (kotlin.math.abs(dir.z) < 0.0005f) return null
        val t = -cam.eye.z / dir.z
        if (t <= 0f) return null
        val hit = cam.eye + dir * t
        return hit.x to hit.y
    }

    /** Сброс детали из меню: sx,sy — координаты DragEvent внутри этого View. */
    fun dropComponent(typeId: String, sx: Float, sy: Float): Boolean {
        val def = Catalog.byId(typeId)
        val world = screenToBoard(sx, sy) ?: return false
        val gx = Math.round(world.first / 2.54f - def.wGrid / 2f)
        val gy = Math.round(world.second / 2.54f - def.hGrid / 2f)
        AppState.addComponentAt(typeId, gx, gy)
        AppState.mode = "move"
        invalidate()
        return true
    }

    // ---------------- геометрия ----------------

    private fun shade(color: Int, f: Float): Int {
        val ff = f.coerceIn(0.25f, 1.05f)
        return Color.rgb(
            (Color.red(color) * ff).toInt().coerceIn(0, 255),
            (Color.green(color) * ff).toInt().coerceIn(0, 255),
            (Color.blue(color) * ff).toInt().coerceIn(0, 255)
        )
    }

    private fun addBox(faces: MutableList<Face>, x: Float, y: Float, z: Float, dx: Float, dy: Float, dz: Float, color: Int, alpha: Int = 255) {
        val c = listOf(
            Vec(x, y, z), Vec(x + dx, y, z), Vec(x + dx, y + dy, z), Vec(x, y + dy, z),
            Vec(x, y, z + dz), Vec(x + dx, y, z + dz), Vec(x + dx, y + dy, z + dz), Vec(x, y + dy, z + dz)
        )
        val idx = listOf(
            listOf(0, 1, 2, 3), listOf(4, 5, 6, 7),
            listOf(0, 1, 5, 4), listOf(1, 2, 6, 5), listOf(2, 3, 7, 6), listOf(3, 0, 4, 7)
        )
        for (f in idx) faces.add(Face(f.map { c[it] }, color, alpha))
    }

    private fun addQuad(faces: MutableList<Face>, pts: List<Vec>, color: Int, alpha: Int = 255) {
        faces.add(Face(pts, color, alpha))
    }

    /** Вертикальный цилиндр (Z вверх) — кнопки, конденсаторы, штыри, валы. */
    private fun addCylinderZ(faces: MutableList<Face>, cx: Float, cy: Float, z0: Float, r: Float, h: Float, color: Int, alpha: Int = 255, seg: Int = 14) {
        val ring = (0 until seg).map { i ->
            val a = (2.0 * Math.PI * i / seg).toFloat()
            Vec(cx + r * cos(a), cy + r * sin(a), z0)
        }
        for (i in 0 until seg) {
            val j = (i + 1) % seg
            val a = ring[i]; val b = ring[j]
            faces.add(Face(listOf(a, b, Vec(b.x, b.y, z0 + h), Vec(a.x, a.y, z0 + h)), color, alpha))
        }
        faces.add(Face(ring.map { Vec(it.x, it.y, z0 + h) }, color, alpha))
    }

    /** Горизонтальный цилиндр вдоль X — тело резистора, батарея 18650. */
    private fun addCylinderX(faces: MutableList<Face>, x1: Float, x2: Float, cy: Float, cz: Float, r: Float, color: Int, alpha: Int = 255, seg: Int = 14) {
        for (i in 0 until seg) {
            val a1 = (2.0 * Math.PI * i / seg).toFloat()
            val a2 = (2.0 * Math.PI * (i + 1) / seg).toFloat()
            val p1 = Vec(x1, cy + r * cos(a1), cz + r * sin(a1))
            val p2 = Vec(x1, cy + r * cos(a2), cz + r * sin(a2))
            val p3 = Vec(x2, cy + r * cos(a2), cz + r * sin(a2))
            val p4 = Vec(x2, cy + r * cos(a1), cz + r * sin(a1))
            faces.add(Face(listOf(p1, p2, p3, p4), color, alpha))
        }
        val cap = (0 until seg).map { i ->
            val a = (2.0 * Math.PI * i / seg).toFloat()
            Vec(x2, cy + r * cos(a), cz + r * sin(a))
        }
        faces.add(Face(cap, color, alpha))
    }

    /** Полусфера — купол светодиода. cz — центр сферы (основание купола). */
    private fun addDome(faces: MutableList<Face>, cx: Float, cy: Float, cz: Float, r: Float, color: Int, alpha: Int = 200) {
        val rings = 5
        val seg = 14
        var prev: List<Vec> = (0 until seg).map { i ->
            val a = (2.0 * Math.PI * i / seg).toFloat()
            Vec(cx + r * cos(a), cy + r * sin(a), cz)
        }
        for (k in 1..rings) {
            val phi = (Math.PI / 2.0 * k / rings).toFloat() // от экватора к вершине
            val rr = r * cos(phi)
            val zz = cz + r * sin(phi)
            val cur: List<Vec> = if (k == rings) {
                List(seg) { Vec(cx, cy, cz + r) }
            } else {
                (0 until seg).map { i ->
                    val a = (2.0 * Math.PI * i / seg).toFloat()
                    Vec(cx + rr * cos(a), cy + rr * sin(a), zz)
                }
            }
            for (i in 0 until seg) {
                val j = (i + 1) % seg
                faces.add(Face(listOf(prev[i], prev[j], cur[j], cur[i]), color, alpha))
            }
            prev = cur
        }
    }

    /** Реальная высота верха 3D-модели детали, мм (для подписей и выделения). */
    private fun visualTop(c: PlacedComponent): Float = when (c.typeId) {
        "esp32" -> 7.2f
        "oled", "tft", "tft18", "lcd1602" -> 2.2f
        "nrf24" -> 3.4f
        "led" -> 8.6f
        "resistor" -> 3.8f
        "button" -> 4.8f
        "bat18650" -> c.effH() * 2.54f * 0.85f
        "pot" -> 11.4f
        "cap", "buzzer" -> c.def().heightMm * 0.85f
        "servo" -> c.def().heightMm * 0.62f + 5.5f
        "relay" -> c.def().heightMm * 0.82f
        else -> c.def().heightMm * 0.7f
    }

    /** Высота контактного штыря модели (куда садятся провода), мм. */
    private fun pinZ(c: PlacedComponent): Float = when (c.typeId) {
        "esp32" -> 5.2f
        "oled", "tft", "tft18", "lcd1602", "nrf24" -> 4.6f
        "led", "resistor", "cap" -> 1.1f
        "button" -> 2.6f
        "bat18650", "bat9v", "lipo" -> 4.0f
        "servo", "relay" -> 3.4f
        "pot" -> 3.6f
        else -> 3.0f
    }

    private fun compWorld(c: PlacedComponent): FloatArray {
        val x = c.gridX * 2.54f
        val y = c.gridY * 2.54f
        val w = c.effW() * 2.54f
        val d = c.effH() * 2.54f
        return floatArrayOf(x, y, w, d)
    }

    private fun pinWorld(c: PlacedComponent, pin: PinDef): Vec {
        val (px, py) = c.pinPos(pin)
        return Vec(
            (c.gridX + px) * 2.54f + 1.27f,
            (c.gridY + py) * 2.54f + 1.27f,
            pinZ(c)
        )
    }

    // ---------------- отрисовка ----------------

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val proj = AppState.project
        val state = CircuitSim.compute()
        val cam = camera()

        // фон
        paint.style = Paint.Style.FILL
        paint.color = Color.parseColor("#0F1B24")
        canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), paint)

        val faces = mutableListOf<Face>()

        // плата (3D-плита)
        addBox(faces, 0f, 0f, -1.6f, boardW(), boardD(), 1.6f, Color.parseColor("#14532D"))

        // STL-корпус в той же 3D-сцене
        if (AppState.showCaseIn3D) {
            AppState.caseModel?.let { m ->
                val offX = boardW() / 2f - (m.min[0] + m.max[0]) / 2f
                val offY = boardD() / 2f - (m.min[1] + m.max[1]) / 2f
                val offZ = -m.min[2]
                val tris = m.triangles
                val stride = if (tris.size > 2200) (tris.size / 2200) + 1 else 1
                var i = 0
                while (i < tris.size) {
                    val t = tris[i]
                    val pts = (0 until 3).map { k ->
                        Vec(t.v[k * 3] + offX, t.v[k * 3 + 1] + offY, t.v[k * 3 + 2] + offZ)
                    }
                    faces.add(Face(pts, Color.parseColor("#90CAF9"), 70))
                    i += stride
                }
            }
        }

        // компоненты — детализированные модели (как настоящие детали)
        for (c in proj.components) {
            val def = c.def()
            val (x, y, w, d) = compWorld(c)
            val pcb = 1.6f // толщина печатной платы модулей
            when (c.typeId) {
                "esp32" -> {
                    // синяя плата DevKit
                    addBox(faces, x, y, 0f, w, d, pcb, Color.parseColor("#1A237E"))
                    // гребёнки штырей по обеим сторонам (чёрный пластик + золотые штыри ниже)
                    addBox(faces, x - 0.4f, y + 1f, pcb, 2.2f, d - 2f, 2.4f, Color.parseColor("#111111"))
                    addBox(faces, x + w - 1.8f, y + 1f, pcb, 2.2f, d - 2f, 2.4f, Color.parseColor("#111111"))
                    // модуль ESP32-WROOM в серебристом экране
                    addBox(faces, x + w * 0.22f, y + d * 0.30f, pcb, w * 0.56f, d * 0.42f, 3.4f, Color.parseColor("#C0C6CE"))
                    // антенна (светлый участок платы в торце модуля)
                    addBox(faces, x + w * 0.22f, y + d * 0.055f, pcb, w * 0.56f, d * 0.20f, 0.8f, Color.parseColor("#3949AB"))
                    // micro-USB разъём
                    addBox(faces, x + w * 0.30f, y - 0.8f, pcb, w * 0.40f, 5.5f, 3.2f, Color.parseColor("#9AA0A6"))
                    // кнопки EN и BOOT
                    addBox(faces, x + w * 0.06f, y + 1.2f, pcb, 3.4f, 3.4f, 2.2f, Color.parseColor("#212121"))
                    addBox(faces, x + w * 0.82f, y + 1.2f, pcb, 3.4f, 3.4f, 2.2f, Color.parseColor("#212121"))
                    // стабилизатор и мелочёвка
                    addBox(faces, x + w * 0.40f, y + d * 0.78f, pcb, w * 0.22f, d * 0.10f, 2.2f, Color.parseColor("#263238"))
                    addBox(faces, x + w * 0.15f, y + d * 0.82f, pcb, 2.4f, 2.4f, 1.6f, Color.parseColor("#5D4037"))
                    addBox(faces, x + w * 0.78f, y + d * 0.82f, pcb, 2.4f, 2.4f, 1.6f, Color.parseColor("#5D4037"))
                }
                "oled", "tft", "tft18", "lcd1602" -> {
                    addBox(faces, x, y, 0f, w, d, pcb, def.color)
                    // рамка и стекло экрана
                    val ix = w * 0.07f; val iy = d * 0.08f
                    addQuad(
                        faces, listOf(
                            Vec(x + ix, y + iy, pcb + 0.12f), Vec(x + w - ix, y + iy, pcb + 0.12f),
                            Vec(x + w - ix, y + d - iy, pcb + 0.12f), Vec(x + ix, y + d - iy, pcb + 0.12f)
                        ), if (c.typeId == "lcd1602") Color.parseColor("#9CCC65") else Color.parseColor("#05131A")
                    )
                    // микросхема-драйвер сзади экрана
                    addBox(faces, x + w * 0.38f, y + d * 0.88f, pcb, w * 0.24f, d * 0.08f, 1.4f, Color.parseColor("#111111"))
                }
                "nrf24" -> {
                    addBox(faces, x, y, 0f, w, d, pcb, Color.parseColor("#0D47A1"))
                    // чип nRF24 и кварц
                    addBox(faces, x + w * 0.30f, y + d * 0.38f, pcb, w * 0.40f, d * 0.30f, 1.6f, Color.parseColor("#111111"))
                    addCylinderZ(faces, x + w * 0.78f, y + d * 0.55f, pcb, 1.6f, 1.4f, Color.parseColor("#C0C6CE"))
                    // печатная антенна «змейкой» в торце
                    addBox(faces, x + w * 0.08f, y + d * 0.05f, pcb, w * 0.84f, d * 0.06f, 0.5f, Color.parseColor("#FFD54F"))
                    addBox(faces, x + w * 0.08f, y + d * 0.16f, pcb, w * 0.84f, d * 0.06f, 0.5f, Color.parseColor("#FFD54F"))
                    addBox(faces, x + w * 0.08f, y + d * 0.27f, pcb, w * 0.84f, d * 0.06f, 0.5f, Color.parseColor("#FFD54F"))
                }
                "led" -> {
                    val ledColor = when (c.props["colorName"]) {
                        "Зелёный" -> Color.GREEN; "Синий" -> Color.BLUE; "Жёлтый" -> Color.YELLOW
                        "Белый" -> Color.WHITE; else -> Color.RED
                    }
                    val cxm = x + w / 2f; val cym = y + d / 2f
                    val r = minOf(w, d) * 0.36f
                    // ножки
                    addBox(faces, cxm - r * 0.55f, cym - 0.35f, 0f, 0.7f, 0.7f, 3.2f, Color.parseColor("#C0C6CE"))
                    addBox(faces, cxm + r * 0.55f - 0.7f, cym - 0.35f, 0f, 0.7f, 0.7f, 3.2f, Color.parseColor("#C0C6CE"))
                    // цилиндрическое тело + купол (полупрозрачные)
                    addCylinderZ(faces, cxm, cym, 2.8f, r, 2.6f, ledColor, 170)
                    addDome(faces, cxm, cym, 5.4f, r, ledColor, 170)
                    // юбка у основания
                    addCylinderZ(faces, cxm, cym, 2.6f, r * 1.18f, 0.9f, ledColor, 190)
                }
                "resistor" -> {
                    val cym = y + d / 2f
                    val bodyLen = w * 0.52f
                    val bx1 = x + (w - bodyLen) / 2f
                    val bx2 = bx1 + bodyLen
                    // аксиальные выводы
                    addBox(faces, x + 0.4f, cym - 0.25f, 0.9f, bx1 - x - 0.4f, 0.5f, 0.5f, Color.parseColor("#C0C6CE"))
                    addBox(faces, bx2, cym - 0.25f, 0.9f, x + w - bx2 - 0.4f, 0.5f, 0.5f, Color.parseColor("#C0C6CE"))
                    // тело-цилиндр и цветные полоски кода
                    addCylinderX(faces, bx1, bx2, cym, 2.1f, 1.55f, Color.parseColor("#D7A86E"))
                    val stripeCols = listOf(Color.RED, Color.RED, Color.parseColor("#795548"), Color.parseColor("#FFD700"))
                    val ohms = c.props["ohms"] ?: "220"
                    stripeCols.forEachIndexed { i, col ->
                        val sx = bx1 + bodyLen * (0.22f + i * 0.17f)
                        addCylinderX(faces, sx, sx + bodyLen * 0.09f, cym, 2.1f, 1.60f, col)
                    }
                    if (ohms == "0") Unit // полоски декоративны
                }
                "button" -> {
                    addBox(faces, x, y, 0f, w, d, 2.2f, Color.parseColor("#263238"))
                    val pressed = AppState.pressedButtons.contains(c.id)
                    addCylinderZ(faces, x + w / 2f, y + d / 2f, 2.2f, minOf(w, d) * 0.26f, if (pressed) 1.2f else 2.4f,
                        if (pressed) Color.parseColor("#69F0AE") else Color.parseColor("#EF5350"))
                }
                "bat18650" -> {
                    // цилиндр, лежащий вдоль X, + плюсовой контакт
                    addCylinderX(faces, x + 0.5f, x + w - 0.5f, y + d / 2f, d * 0.42f, d * 0.40f, Color.parseColor("#2E7D32"))
                    addCylinderX(faces, x + w - 1.6f, x + w - 0.2f, y + d / 2f, d * 0.42f, d * 0.18f, Color.parseColor("#C0C6CE"))
                    addBox(faces, x + w * 0.2f, y + d * 0.30f, d * 0.62f, w * 0.6f, d * 0.4f, 0.5f, Color.parseColor("#A5D6A7"))
                }
                "bat9v" -> {
                    addBox(faces, x, y, 0f, w, d, def.heightMm * 0.82f, Color.parseColor("#212121"))
                    addCylinderZ(faces, x + w * 0.32f, y + d / 2f, def.heightMm * 0.82f, 1.8f, 1.8f, Color.parseColor("#C0C6CE"))
                    addCylinderZ(faces, x + w * 0.68f, y + d / 2f, def.heightMm * 0.82f, 1.3f, 1.8f, Color.parseColor("#9AA0A6"))
                }
                "lipo" -> {
                    addBox(faces, x, y, 0f, w, d, 3.6f, Color.parseColor("#F9A825"))
                    addBox(faces, x + w * 0.1f, y + d * 0.2f, 3.6f, w * 0.8f, d * 0.6f, 0.6f, Color.parseColor("#FFECB3"))
                }
                "cap" -> {
                    addCylinderZ(faces, x + w / 2f, y + d / 2f, 0f, minOf(w, d) * 0.40f, def.heightMm * 0.8f, Color.parseColor("#283593"))
                    addCylinderZ(faces, x + w / 2f, y + d / 2f, def.heightMm * 0.8f, minOf(w, d) * 0.40f, 0.5f, Color.parseColor("#C0C6CE"))
                }
                "buzzer" -> {
                    addCylinderZ(faces, x + w / 2f, y + d / 2f, 0f, minOf(w, d) * 0.42f, def.heightMm * 0.75f, Color.parseColor("#212121"))
                    addCylinderZ(faces, x + w / 2f, y + d / 2f, def.heightMm * 0.75f, minOf(w, d) * 0.13f, 0.5f, Color.parseColor("#0F1B24"))
                }
                "pot" -> {
                    addBox(faces, x, y, 0f, w, d, 4.5f, Color.parseColor("#263238"))
                    addCylinderZ(faces, x + w / 2f, y + d / 2f, 4.5f, minOf(w, d) * 0.20f, 5.5f, Color.parseColor("#B0BEC5"))
                    addBox(faces, x + w / 2f - 0.5f, y + d / 2f, 10.2f, 1f, minOf(w, d) * 0.34f, 1.2f, Color.parseColor("#ECEFF1"))
                }
                "servo" -> {
                    addBox(faces, x, y, 0f, w, d, def.heightMm * 0.62f, Color.parseColor("#1565C0"))
                    addCylinderZ(faces, x + w / 2f, y + d / 2f, def.heightMm * 0.62f, minOf(w, d) * 0.22f, 3.5f, Color.parseColor("#ECEFF1"))
                    addBox(faces, x + w * 0.12f, y + d * 0.42f, def.heightMm * 0.62f + 3.8f, w * 0.76f, d * 0.16f, 1.4f, Color.parseColor("#FFFFFF"))
                }
                "relay" -> {
                    addBox(faces, x, y, 0f, w, d, def.heightMm * 0.78f, Color.parseColor("#1565C0"))
                    addQuad(
                        faces, listOf(
                            Vec(x + w * 0.15f, y + d * 0.2f, def.heightMm * 0.78f + 0.1f),
                            Vec(x + w * 0.85f, y + d * 0.2f, def.heightMm * 0.78f + 0.1f),
                            Vec(x + w * 0.85f, y + d * 0.8f, def.heightMm * 0.78f + 0.1f),
                            Vec(x + w * 0.15f, y + d * 0.8f, def.heightMm * 0.78f + 0.1f)
                        ), Color.parseColor("#0D47A1")
                    )
                }
                "dht22" -> {
                    addBox(faces, x, y, 0f, w, d, def.heightMm * 0.6f, Color.parseColor("#ECEFF1"))
                    addBox(faces, x + w * 0.2f, y + d * 0.2f, def.heightMm * 0.6f, w * 0.6f, d * 0.6f, 1.2f, Color.parseColor("#90CAF9"))
                }
                "perfboard" -> {
                    addBox(faces, x, y, 0f, w, d, 1.2f, Color.parseColor("#1B5E20"))
                }
                else -> {
                    addBox(faces, x, y, 0f, w, d, def.heightMm * 0.7f, def.color)
                }
            }

            // штыревые выводы модулей: золотые штыри на гребёнках
            if (c.typeId in listOf("esp32", "oled", "tft", "tft18", "lcd1602", "nrf24", "dht22", "relay", "servo")) {
                for (pin in def.pins) {
                    val pw = pinWorld(c, pin)
                    addCylinderZ(faces, pw.x, pw.y, 1.2f, 0.55f, (pinZ(c) - 1.2f).coerceAtLeast(0.6f), Color.parseColor("#FFD54F"), 255, 8)
                }
            }
        }

        // глубина и отрисовка граней (алгоритм художника)
        val light = norm(Vec(-0.45f, -0.55f, 0.75f))
        for (f in faces) {
            f.depth = f.pts.map { project(it, cam).depth }.average().toFloat()
        }
        faces.sortByDescending { it.depth }
        for (f in faces) {
            val scr = f.pts.map { project(it, cam) }
            val path = Path()
            scr.forEachIndexed { i, p -> if (i == 0) path.moveTo(p.x, p.y) else path.lineTo(p.x, p.y) }
            path.close()
            val n = if (f.pts.size >= 3) norm(cross(f.pts[1] - f.pts[0], f.pts[2] - f.pts[1])) else Vec(0f, 0f, 1f)
            val factor = 0.45f + 0.55f * abs(dot(n, light))
            paint.style = Paint.Style.FILL
            paint.color = shade(f.color, factor)
            paint.alpha = f.alpha
            canvas.drawPath(path, paint)
            paint.style = Paint.Style.STROKE
            paint.strokeWidth = 0.7f
            paint.color = Color.argb(55, 0, 0, 0)
            paint.alpha = f.alpha
            canvas.drawPath(path, paint)
            paint.alpha = 255
        }

        // отверстия платы (не под компонентами)
        paint.style = Paint.Style.FILL
        for (gy in 0 until proj.boardRows) {
            for (gx in 0 until proj.boardCols) {
                val under = proj.components.any { c -> gx >= c.gridX && gx < c.gridX + c.effW() && gy >= c.gridY && gy < c.gridY + c.effH() }
                if (under) continue
                val p = project(Vec(gx * 2.54f + 1.27f, gy * 2.54f + 1.27f, 0.12f), cam)
                if (p.x < -10 || p.x > width + 10 || p.y < -10 || p.y > height + 10) continue
                paint.color = Color.parseColor("#0B3D20")
                canvas.drawCircle(p.x, p.y, (0.75f * p.scale).coerceIn(1f, 5f), paint)
            }
        }

        // шелкография на плате
        run {
            val tp = project(Vec(3f, boardD() - 3f, 0.15f), cam)
            paint.style = Paint.Style.FILL
            paint.color = Color.argb(200, 255, 255, 255)
            paint.textSize = 8f * density
            paint.textAlign = Paint.Align.LEFT
            canvas.drawText("CONNECT PCB • 2.54 мм • REV 1.3", tp.x, tp.y, paint)
            val tp2 = project(Vec(boardW() - 4f, 3f, 0.15f), cam)
            paint.textAlign = Paint.Align.RIGHT
            canvas.drawText("3D SIM", tp2.x, tp2.y, paint)
        }

        // провода (дуги в 3D) и пайка (дорожки по плате)
        for (w in proj.wires) {
            val a = AppState.compById(w.aComp) ?: continue
            val b = AppState.compById(w.bComp) ?: continue
            val pa = a.def().pins.firstOrNull { it.name == w.aPin } ?: continue
            val pb = b.def().pins.firstOrNull { it.name == w.bPin } ?: continue
            val va = pinWorld(a, pa)
            val vb = pinWorld(b, pb)
            val pts3: List<Vec> = if (w.style == "solder") {
                listOf(
                    Vec(va.x, va.y, 0.35f), Vec(vb.x, va.y, 0.35f), Vec(vb.x, vb.y, 0.35f)
                )
            } else {
                val topZ = maxOf(va.z, vb.z) + 7f
                (0..16).map { i ->
                    val t = i / 16f
                    val mt = 1 - t
                    val x = mt * mt * mt * va.x + 3 * mt * mt * t * va.x + 3 * mt * t * t * vb.x + t * t * t * vb.x
                    val y = mt * mt * mt * va.y + 3 * mt * mt * t * va.y + 3 * mt * t * t * vb.y + t * t * t * vb.y
                    val z = mt * mt * mt * va.z + 3 * mt * mt * t * topZ + 3 * mt * t * t * topZ + t * t * t * vb.z
                    Vec(x, y, z)
                }
            }
            val scrPts = pts3.map { project(it, cam) }
            val path = Path()
            scrPts.forEachIndexed { i, p -> if (i == 0) path.moveTo(p.x, p.y) else path.lineTo(p.x, p.y) }
            paint.style = Paint.Style.STROKE
            paint.color = if (w.style == "solder") Color.parseColor("#C0C0C0") else w.color
            val avgScale = scrPts.map { it.scale }.average().toFloat()
            paint.strokeWidth = (if (w.style == "solder") 0.9f else 0.55f) * avgScale.coerceIn(2f, 9f)
            canvas.drawPath(path, paint)
            // точки пайки
            paint.style = Paint.Style.FILL
            for (p in listOf(scrPts.first(), scrPts.last())) {
                paint.color = Color.parseColor("#ECEFF1")
                canvas.drawCircle(p.x, p.y, (0.85f * p.scale).coerceIn(2.5f, 9f), paint)
                paint.color = Color.parseColor("#90A4AE")
                canvas.drawCircle(p.x, p.y, (0.4f * p.scale).coerceIn(1.2f, 4.5f), paint)
            }
        }

        // выделение выбранного
        AppState.selected()?.let { c ->
            val (x, y, w, d) = compWorld(c)
            val h = visualTop(c) + 0.4f
            val corners = listOf(Vec(x, y, h), Vec(x + w, y, h), Vec(x + w, y + d, h), Vec(x, y + d, h))
            val path = Path()
            corners.map { project(it, cam) }.forEachIndexed { i, p -> if (i == 0) path.moveTo(p.x, p.y) else path.lineTo(p.x, p.y) }
            path.close()
            paint.style = Paint.Style.STROKE
            paint.color = Color.YELLOW
            paint.strokeWidth = 3.5f
            canvas.drawPath(path, paint)
            paint.style = Paint.Style.FILL
        }

        // пины
        val showNames = AppState.mode in listOf("wire", "solder")
        for (c in proj.components) {
            val selected = c.id == AppState.selectedId
            for (pin in c.def().pins) {
                val p = project(pinWorld(c, pin), cam)
                val isPending = pendingPin?.let { it.first.id == c.id && it.second.name == pin.name } == true
                // живые выходы ESP: HIGH — зелёный, LOW-выход — тёмный, остальное — золотой штырь
                val espKey = "${c.id}:${pin.name}"
                val isEspOut = c.typeId == "esp32" && AppState.pinModes[espKey] == "OUTPUT"
                val espHigh = isEspOut && AppState.pinOutputs[espKey] == 1 && AppState.running
                paint.style = Paint.Style.FILL
                paint.color = when {
                    isPending -> Color.YELLOW
                    espHigh -> Color.parseColor("#22C55E")
                    isEspOut -> Color.parseColor("#475569")
                    else -> Color.parseColor("#FFD54F")
                }
                val pinR = (0.85f * p.scale).coerceIn(2.8f, 9f)
                if (espHigh) {
                    paint.alpha = 110
                    canvas.drawCircle(p.x, p.y, pinR * 2.1f, paint)
                    paint.alpha = 255
                }
                canvas.drawCircle(p.x, p.y, pinR, paint)
                paint.color = Color.parseColor("#4E342E")
                canvas.drawCircle(p.x, p.y, (0.32f * p.scale).coerceIn(1f, 3.5f), paint)
                if (showNames || selected) {
                    paint.color = Color.WHITE
                    paint.textSize = 9f * density
                    paint.textAlign = Paint.Align.CENTER
                    canvas.drawText(pin.name, p.x, p.y - (1.2f * p.scale).coerceIn(5f, 14f), paint)
                }
            }
        }

        // живые элементы поверх: LED, кнопки, экраны
        paint.textAlign = Paint.Align.CENTER
        for (c in proj.components) {
            val (x, y, w, d) = compWorld(c)
            val h = visualTop(c)
            val center = project(Vec(x + w / 2f, y + d / 2f, h + 0.3f), cam)
            when (c.typeId) {
                "led" -> {
                    val st = state.leds[c.id]
                    val base = when (c.props["colorName"]) {
                        "Зелёный" -> Color.GREEN; "Синий" -> Color.BLUE; "Жёлтый" -> Color.YELLOW
                        "Белый" -> Color.WHITE; else -> Color.RED
                    }
                    val r = (minOf(w, d) * 0.34f * center.scale).coerceIn(4f, 26f)
                    if (st != null && st.burnt) {
                        paint.color = Color.DKGRAY
                        canvas.drawCircle(center.x, center.y, r, paint)
                        paint.color = Color.RED
                        paint.textSize = 12f * density
                        canvas.drawText("✕", center.x, center.y + 4f * density, paint)
                    } else {
                        if (st != null && st.brightness > 0.02f && AppState.running) {
                            paint.color = base
                            paint.alpha = (120 * st.brightness).toInt()
                            canvas.drawCircle(center.x, center.y, r * (1.5f + st.brightness), paint)
                            paint.alpha = 255
                        }
                        paint.color = base
                        paint.alpha = if (st != null && st.brightness > 0.02f && AppState.running) 255 else 120
                        canvas.drawCircle(center.x, center.y, r, paint)
                        paint.alpha = 255
                    }
                }
                "button" -> {
                    val pressed = AppState.pressedButtons.contains(c.id)
                    paint.color = if (pressed) Color.parseColor("#69F0AE") else Color.parseColor("#EF5350")
                    canvas.drawCircle(center.x, center.y, (minOf(w, d) * 0.30f * center.scale).coerceIn(4f, 20f), paint)
                    paint.color = Color.WHITE
                    paint.textSize = 8f * density
                    canvas.drawText(if (pressed) "ON" else "○", center.x, center.y + 3f * density, paint)
                }
                "oled", "tft", "tft18", "lcd1602" -> {
                    val txt = c.props["displayText"] ?: ""
                    if (txt.isNotEmpty()) {
                        paint.color = if (c.typeId == "lcd1602") Color.parseColor("#1B5E20") else Color.parseColor("#80DEEA")
                        paint.textSize = 8.5f * density
                        val shown = if (txt.length > 18) txt.take(18) + "…" else txt
                        canvas.drawText(shown, center.x, center.y + 3f * density, paint)
                    }
                }
            }
            // подпись компонента
            paint.color = Color.WHITE
            paint.textSize = 8f * density
            paint.textAlign = Paint.Align.CENTER
            val title = when (c.typeId) {
                "resistor" -> "R ${c.props["ohms"] ?: "220"}Ω"
                "tft18" -> "TFT 1.8\""
                else -> c.def().title
            }
            val short = if (title.length > 18) title.take(18) else title
            canvas.drawText(short, center.x, center.y - (minOf(w, d) * 0.55f * center.scale).coerceIn(8f, 30f), paint)
        }

        // подсказка камеры
        paint.color = Color.parseColor("#B0BEC5")
        paint.textSize = 9f * density
        paint.textAlign = Paint.Align.LEFT
        val caseHint = if (AppState.showCaseIn3D && AppState.caseModel != null) " • корпус: ${AppState.caseModel!!.name}" else ""
        canvas.drawText("🧊 3D • режим «Камера»: ведите палец — вращение, щипок — зум$caseHint", 12f, 26f, paint)
    }

    // ---------------- касания ----------------

    fun hitPin(sx: Float, sy: Float): Pair<PlacedComponent, PinDef>? {
        val cam = camera()
        var best: Pair<PlacedComponent, PinDef>? = null
        var bestD = 30f * density
        for (c in AppState.project.components) {
            for (p in c.def().pins) {
                val s = project(pinWorld(c, p), cam)
                val d = hypot(sx - s.x, sy - s.y)
                if (d < bestD) { bestD = d; best = c to p }
            }
        }
        return best
    }

    fun hitComponent(sx: Float, sy: Float): PlacedComponent? {
        val cam = camera()
        for (c in AppState.project.components.asReversed()) {
            val (x, y, w, d) = compWorld(c)
            val h = c.def().heightMm
            val corners = listOf(Vec(x, y, h), Vec(x + w, y, h), Vec(x + w, y + d, h), Vec(x, y + d, h))
                .map { project(it, cam) }
            if (pointInPoly(sx, sy, corners)) return c
        }
        return null
    }

    private fun pointInPoly(x: Float, y: Float, poly: List<Scr>): Boolean {
        var inside = false
        var j = poly.size - 1
        for (i in poly.indices) {
            val xi = poly[i].x; val yi = poly[i].y
            val xj = poly[j].x; val yj = poly[j].y
            if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi)) inside = !inside
            j = i
        }
        return inside
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                parent?.requestDisallowInterceptTouchEvent(true)
                lastX = event.x; lastY = event.y
                lastPinch = 0f
                val hitP = hitPin(event.x, event.y)
                val hitC = hitComponent(event.x, event.y)
                when (AppState.mode) {
                    "camera" -> { /* движение обработается в MOVE */ }
                    "wire", "solder" -> {
                        if (hitP != null) {
                            val pending = pendingPin
                            if (pending == null) {
                                pendingPin = hitP
                                AppState.log("3D пайка: выбран ${hitP.first.def().title}.${hitP.second.name} — коснитесь второго пина")
                            } else if (pending.first.id == hitP.first.id && pending.second.name == hitP.second.name) {
                                pendingPin = null
                            } else {
                                val colors = listOf(0xFFD32F2F.toInt(), 0xFFFF6F00.toInt(), 0xFFF9A825.toInt(), 0xFF2E7D32.toInt(), 0xFF1565C0.toInt(), 0xFF6A1B9A.toInt())
                                AppState.project.wires.add(
                                    Wire(
                                        aComp = pending.first.id, aPin = pending.second.name,
                                        bComp = hitP.first.id, bPin = hitP.second.name,
                                        style = if (AppState.mode == "solder") "solder" else "wire",
                                        color = if (AppState.mode == "solder") 0xFF90A4AE.toInt() else colors[AppState.project.wires.size % colors.size]
                                    )
                                )
                                AppState.log("${if (AppState.mode == "solder") "🔥 Припаяно" else "🔌 Провод"}: ${pending.first.def().title}.${pending.second.name} — ${hitP.first.def().title}.${hitP.second.name}")
                                pendingPin = null
                                AppState.notifyChanged()
                            }
                            invalidate(); return true
                        }
                    }
                    "delete" -> {
                        if (hitC != null) {
                            AppState.log("🗑 Удалено: ${hitC.def().title}")
                            AppState.deleteComponent(hitC.id)
                            invalidate(); return true
                        }
                    }
                    "sim" -> {
                        if (hitC != null && hitC.typeId == "button") {
                            if (AppState.pressedButtons.contains(hitC.id)) AppState.pressedButtons.remove(hitC.id) else AppState.pressedButtons.add(hitC.id)
                            AppState.log(if (AppState.pressedButtons.contains(hitC.id)) "🔘 Кнопка НАЖАТА (3D)" else "🔘 Кнопка отпущена (3D)")
                            AppState.notifyChanged(); invalidate(); return true
                        }
                        if (hitC != null && hitC.typeId == "pot") {
                            val cur = hitC.props["value"]?.toIntOrNull() ?: 512
                            hitC.props["value"] = ((cur + 128) % 1024).toString()
                            AppState.log("🎛 Потенциометр: ${hitC.props["value"]}/1023")
                            AppState.notifyChanged(); invalidate(); return true
                        }
                        if (hitC != null) { AppState.selectedId = hitC.id; AppState.notifyChanged(); invalidate(); return true }
                    }
                    else -> { // move
                        if (hitC != null) {
                            AppState.selectedId = hitC.id
                            dragComp = hitC
                            dragStartGridX = hitC.gridX; dragStartGridY = hitC.gridY
                            dragStartTouchX = event.x; dragStartTouchY = event.y
                            AppState.notifyChanged(); invalidate(); return true
                        } else {
                            AppState.selectedId = null
                            AppState.notifyChanged(); invalidate()
                        }
                    }
                }
                return true // жест наш — особенно важно для режима камеры
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                if (event.pointerCount >= 2) {
                    lastPinch = hypot(event.getX(0) - event.getX(1), event.getY(0) - event.getY(1))
                }
            }
            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount >= 2) {
                    val d = hypot(event.getX(0) - event.getX(1), event.getY(0) - event.getY(1))
                    if (lastPinch > 0f && d > 0f) {
                        zoom = (zoom * d / lastPinch).coerceIn(0.55f, 2.6f)
                        invalidate()
                    }
                    lastPinch = d
                    return true
                }
                if (AppState.mode == "camera") {
                    camAzimuth -= (event.x - lastX) * 0.008f
                    camElevation = (camElevation + (event.y - lastY) * 0.006f).coerceIn(0.12f, 1.42f)
                    lastX = event.x; lastY = event.y
                    invalidate(); return true
                }
                val dc = dragComp
                if (dc != null && AppState.mode == "move") {
                    val cam = camera()
                    val o = project(Vec(0f, 0f, 0f), cam)
                    val ax = project(Vec(2.54f, 0f, 0f), cam)
                    val ay = project(Vec(0f, 2.54f, 0f), cam)
                    val exx = ax.x - o.x; val exy = ax.y - o.y
                    val eyx = ay.x - o.x; val eyy = ay.y - o.y
                    val det = exx * eyy - eyx * exy
                    if (abs(det) > 0.001f) {
                        val dxPx = event.x - dragStartTouchX
                        val dyPx = event.y - dragStartTouchY
                        val dgx = (dxPx * eyy - dyPx * eyx) / det
                        val dgy = (exx * dyPx - exy * dxPx) / det
                        val nx = (dragStartGridX + Math.round(dgx)).coerceIn(0, AppState.project.boardCols - dc.effW())
                        val ny = (dragStartGridY + Math.round(dgy)).coerceIn(0, AppState.project.boardRows - dc.effH())
                        if (nx != dc.gridX || ny != dc.gridY) {
                            dc.gridX = nx; dc.gridY = ny
                            invalidate()
                        }
                    }
                    return true
                }
                lastX = event.x; lastY = event.y
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (dragComp != null) {
                    dragComp = null
                    onComponentMoved?.invoke()
                    AppState.notifyChanged()
                }
                lastPinch = 0f
                parent?.requestDisallowInterceptTouchEvent(false)
            }
        }
        return super.onTouchEvent(event)
    }
}
