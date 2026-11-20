package com.belopus.connect

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View
import kotlin.math.cos
import kotlin.math.sin

/** 3D-превью STL: вращение пальцем, закраска по нормали */
class Case3DView @JvmOverloads constructor(ctx: Context, attrs: AttributeSet? = null) : View(ctx, attrs) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private var rotX = -0.7f
    private var rotY = 0.6f
    private var lastX = 0f
    private var lastY = 0f

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        paint.style = Paint.Style.FILL
        paint.color = Color.parseColor("#102027")
        canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), paint)
        val model = AppState.caseModel
        if (model == null) {
            paint.color = Color.WHITE
            paint.textSize = 34f
            paint.textAlign = Paint.Align.CENTER
            canvas.drawText("Корпус не выбран", width / 2f, height / 2f - 20f, paint)
            paint.textSize = 24f
            canvas.drawText("Импортируйте STL или выберите готовый ↓", width / 2f, height / 2f + 30f, paint)
            return
        }
        val cx = (model.min[0] + model.max[0]) / 2f
        val cy = (model.min[1] + model.max[1]) / 2f
        val cz = (model.min[2] + model.max[2]) / 2f
        val size = maxOf(model.sizeX(), model.sizeY(), model.sizeZ()).coerceAtLeast(1f)
        val scale = (minOf(width, height) * 0.38f) / (size / 2f)

        data class Face(val pts: FloatArray, val z: Float, val shade: Int)
        val faces = mutableListOf<Face>()
        val tris = model.triangles
        val stride = if (tris.size > 2500) (tris.size / 2500) + 1 else 1
        var i = 0
        while (i < tris.size) {
            val t = tris[i]
            val pts = FloatArray(6)
            var zSum = 0f
            for (k in 0 until 3) {
                var x = (t.v[k * 3] - cx).toDouble()
                var y = (t.v[k * 3 + 1] - cy).toDouble()
                var z = (t.v[k * 3 + 2] - cz).toDouble()
                // rotY
                val x1 = x * cos(rotY) - z * sin(rotY)
                val z1 = x * sin(rotY) + z * cos(rotY)
                x = x1; z = z1
                // rotX
                val y2 = y * cos(rotX) - z * sin(rotX)
                val z2 = y * sin(rotX) + z * cos(rotX)
                y = y2; z = z2
                pts[k * 2] = width / 2f + (x * scale).toFloat()
                pts[k * 2 + 1] = height / 2f - (y * scale).toFloat()
                zSum += z.toFloat()
            }
            // нормаль после поворотов (приближённо через исходную)
            var nx = t.nx.toDouble(); var ny = t.ny.toDouble(); var nz = t.nz.toDouble()
            val nnx = nx * cos(rotY) - nz * sin(rotY); val nnz = nx * sin(rotY) + nz * cos(rotY); nx = nnx; nz = nnz
            val nny = ny * cos(rotX) - nz * sin(rotX); nz = ny * sin(rotX) + nz * cos(rotX); ny = nny
            val light = (0.35 + 0.65 * kotlin.math.abs(nz)).coerceIn(0.2, 1.0)
            val shade = (90 + 140 * light).toInt().coerceIn(0, 255)
            faces.add(Face(pts, zSum / 3f, shade))
            i += stride
        }
        faces.sortBy { it.z }
        for (f in faces) {
            paint.color = Color.rgb(f.shade / 2, f.shade * 3 / 4, f.shade)
            paint.style = Paint.Style.FILL
            val path = Path()
            path.moveTo(f.pts[0], f.pts[1]); path.lineTo(f.pts[2], f.pts[3]); path.lineTo(f.pts[4], f.pts[5]); path.close()
            canvas.drawPath(path, paint)
            paint.style = Paint.Style.STROKE
            paint.strokeWidth = 0.6f
            paint.color = Color.argb(60, 0, 0, 0)
            canvas.drawPath(path, paint)
        }
        paint.style = Paint.Style.FILL
        paint.color = Color.WHITE
        paint.textSize = 24f
        paint.textAlign = Paint.Align.LEFT
        val triShown = faces.size
        canvas.drawText("${model.name} • ${model.triangles.size} треуг. (показано $triShown)", 16f, 36f, paint)
        canvas.drawText(
            "Размер: ${"%.1f".format(model.sizeX())} × ${"%.1f".format(model.sizeY())} × ${"%.1f".format(model.sizeZ())} мм • ведите палец для вращения",
            16f, height - 16f, paint
        )
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.action) {
            MotionEvent.ACTION_DOWN -> { lastX = event.x; lastY = event.y; parent?.requestDisallowInterceptTouchEvent(true) }
            MotionEvent.ACTION_MOVE -> {
                rotY += (event.x - lastX) * 0.01f
                rotX += (event.y - lastY) * 0.01f
                lastX = event.x; lastY = event.y
                invalidate()
                return true
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> parent?.requestDisallowInterceptTouchEvent(false)
        }
        return super.onTouchEvent(event)
    }
}

/** Вид сверху: корпус и компоненты внутри */
class CaseTopView @JvmOverloads constructor(ctx: Context, attrs: AttributeSet? = null) : View(ctx, attrs) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    var onFitText: ((String, Boolean) -> Unit)? = null

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        paint.style = Paint.Style.FILL
        paint.color = Color.parseColor("#263238")
        canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), paint)
        val model = AppState.caseModel
        if (model == null) {
            paint.color = Color.WHITE; paint.textSize = 28f; paint.textAlign = Paint.Align.CENTER
            canvas.drawText("Нет корпуса", width / 2f, height / 2f, paint)
            return
        }
        val wmm = model.sizeX().coerceAtLeast(1f)
        val dmm = model.sizeY().coerceAtLeast(1f)
        val pad = 36f
        val scale = minOf((width - pad * 2) / wmm, (height - pad * 2) / dmm)
        val ox = (width - wmm * scale) / 2f
        val oy = (height - dmm * scale) / 2f
        // корпус
        paint.style = Paint.Style.STROKE
        paint.color = Color.parseColor("#80DEEA"); paint.strokeWidth = 3f
        canvas.drawRect(ox, oy, ox + wmm * scale, oy + dmm * scale, paint)
        paint.style = Paint.Style.FILL
        paint.color = Color.WHITE; paint.textSize = 22f; paint.textAlign = Paint.Align.LEFT
        canvas.drawText("Вид сверху • ${"%.0f".format(wmm)}×${"%.0f".format(dmm)} мм", ox, oy - 10f, paint)

        var allFit = true
        var fitMsg = StringBuilder()
        for (c in AppState.project.components.filter { it.insideCase }) {
            val cw = c.widthMm(); val cd = c.depthMm()
            val x = ox + c.caseXmm * scale
            val y = oy + c.caseYmm * scale
            val fits = c.caseXmm >= 0 && c.caseYmm >= 0 &&
                (c.caseXmm + cw) <= wmm && (c.caseYmm + cd) <= dmm &&
                (c.caseZmm + c.def().heightMm) <= model.sizeZ()
            if (!fits) allFit = false
            paint.style = Paint.Style.FILL
            paint.color = if (fits) Color.argb(200, 105, 240, 174) else Color.argb(220, 239, 83, 80)
            canvas.drawRoundRect(x, y, x + cw * scale, y + cd * scale, 6f, 6f, paint)
            paint.color = Color.BLACK
            paint.textSize = 20f
            paint.textAlign = Paint.Align.CENTER
            val short = c.def().title.take(14)
            canvas.drawText(short, x + cw * scale / 2f, y + cd * scale / 2f + 6f, paint)
            canvas.drawText("z=${"%.0f".format(c.caseZmm)}", x + cw * scale / 2f, y + cd * scale / 2f + 28f, paint)
            fitMsg.append(if (fits) "✓ ${c.def().title} влезает\n" else "✕ ${c.def().title} НЕ влезает (${"%.0f".format(cw)}×${"%.0f".format(cd)}×${c.def().heightMm}мм)\n")
        }
        if (AppState.project.components.none { it.insideCase }) {
            paint.color = Color.parseColor("#B0BEC5"); paint.textSize = 24f; paint.textAlign = Paint.Align.CENTER
            canvas.drawText("Отметьте детали «В корпус» ниже", width / 2f, height - 18f, paint)
        }
        onFitText?.invoke(fitMsg.toString(), allFit)
    }
}
