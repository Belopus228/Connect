package com.belopus.connect

import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.abs

data class StlTriangle(val v: FloatArray, val nx: Float, val ny: Float, val nz: Float)

data class StlModel(
    val name: String,
    val triangles: List<StlTriangle>,
    val min: FloatArray,
    val max: FloatArray
) {
    fun sizeX() = max[0] - min[0]
    fun sizeY() = max[1] - min[1]
    fun sizeZ() = max[2] - min[2]

    fun volumeMm3(): Double {
        var vol = 0.0
        for (t in triangles) {
            val ax = t.v[0].toDouble(); val ay = t.v[1].toDouble(); val az = t.v[2].toDouble()
            val bx = t.v[3].toDouble(); val by = t.v[4].toDouble(); val bz = t.v[5].toDouble()
            val cx = t.v[6].toDouble(); val cy = t.v[7].toDouble(); val cz = t.v[8].toDouble()
            val crossX = by * cz - bz * cy
            val crossY = bz * cx - bx * cz
            val crossZ = bx * cy - by * cx
            vol += (ax * crossX + ay * crossY + az * crossZ) / 6.0
        }
        return abs(vol)
    }
}

object StlParser {

    fun parse(bytes: ByteArray, name: String): StlModel {
        if (bytes.size >= 84) {
            val bb = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
            val count = bb.getInt(80)
            val expected = 84L + count.toLong() * 50L
            if (count > 0 && count < 2_000_000 && expected == bytes.size.toLong()) {
                return parseBinary(bb, count, name)
            }
        }
        val text = String(bytes, Charsets.UTF_8)
        if (text.contains("facet")) return parseAscii(text, name)
        // fallback: binary без точного размера
        if (bytes.size >= 84) {
            val bb = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
            val count = ((bytes.size - 84) / 50).coerceAtMost(500_000)
            if (count > 0) return parseBinary(bb, count, name)
        }
        throw IllegalArgumentException("Не удалось распознать STL (ни бинарный, ни ASCII)")
    }

    private fun parseBinary(bb: ByteBuffer, count: Int, name: String): StlModel {
        val tris = ArrayList<StlTriangle>(count.coerceAtMost(200_000))
        var min = floatArrayOf(Float.MAX_VALUE, Float.MAX_VALUE, Float.MAX_VALUE)
        var max = floatArrayOf(-Float.MAX_VALUE, -Float.MAX_VALUE, -Float.MAX_VALUE)
        bb.position(84)
        for (i in 0 until count) {
            val nx = bb.float; val ny = bb.float; val nz = bb.float
            val v = FloatArray(9)
            for (k in 0 until 9) {
                v[k] = bb.float
                val axis = k % 3
                if (v[k] < min[axis]) min[axis] = v[k]
                if (v[k] > max[axis]) max[axis] = v[k]
            }
            bb.short // attribute
            tris.add(StlTriangle(v, nx, ny, nz))
        }
        return StlModel(name, tris, min, max)
    }

    private fun parseAscii(text: String, name: String): StlModel {
        val vertexRe = Regex("""vertex\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)""")
        val verts = vertexRe.findAll(text).map {
            floatArrayOf(
                it.groupValues[1].toFloatOrNull() ?: 0f,
                it.groupValues[2].toFloatOrNull() ?: 0f,
                it.groupValues[3].toFloatOrNull() ?: 0f
            )
        }.toList()
        val tris = mutableListOf<StlTriangle>()
        var min = floatArrayOf(Float.MAX_VALUE, Float.MAX_VALUE, Float.MAX_VALUE)
        var max = floatArrayOf(-Float.MAX_VALUE, -Float.MAX_VALUE, -Float.MAX_VALUE)
        verts.forEach { v ->
            for (a in 0..2) {
                if (v[a] < min[a]) min[a] = v[a]
                if (v[a] > max[a]) max[a] = v[a]
            }
        }
        var i = 0
        while (i + 2 < verts.size + 0 && i + 2 <= verts.size - 1) {
            val a = verts[i]; val b = verts[i + 1]; val c = verts[i + 2]
            // нормаль через кросс
            val ux = b[0] - a[0]; val uy = b[1] - a[1]; val uz = b[2] - a[2]
            val vx = c[0] - a[0]; val vy = c[1] - a[1]; val vz = c[2] - a[2]
            var nx = uy * vz - uz * vy; var ny = uz * vx - ux * vz; var nz = ux * vy - uy * vx
            val len = kotlin.math.sqrt(nx * nx + ny * ny + nz * nz)
            if (len > 0) { nx /= len; ny /= len; nz /= len }
            tris.add(StlTriangle(floatArrayOf(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2]), nx, ny, nz))
            i += 3
        }
        if (tris.isEmpty()) throw IllegalArgumentException("ASCII STL: не найдены треугольники")
        return StlModel(name, tris, min, max)
    }
}

/** Готовые корпуса, генерируемые кодом (как будто STL) */
object StlPresets {

    private fun cuboid(name: String, w: Float, d: Float, h: Float): StlModel {
        // 12 треугольников кубоида, origin в углу, мм
        val p = arrayOf(
            floatArrayOf(0f, 0f, 0f), floatArrayOf(w, 0f, 0f),
            floatArrayOf(w, d, 0f), floatArrayOf(0f, d, 0f),
            floatArrayOf(0f, 0f, h), floatArrayOf(w, 0f, h),
            floatArrayOf(w, d, h), floatArrayOf(0f, d, h)
        )
        val faces = listOf(
            intArrayOf(0, 1, 2, 0, 2, 3), intArrayOf(4, 6, 5, 4, 7, 6),
            intArrayOf(0, 4, 5, 0, 5, 1), intArrayOf(1, 5, 6, 1, 6, 2),
            intArrayOf(2, 6, 7, 2, 7, 3), intArrayOf(3, 7, 4, 3, 4, 0)
        )
        val tris = mutableListOf<StlTriangle>()
        for (f in faces) {
            for (s in 0..1) {
                val a = p[f[s * 3]]; val b = p[f[s * 3 + 1]]; val c = p[f[s * 3 + 2]]
                tris.add(StlTriangle(floatArrayOf(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2]), 0f, 0f, 1f))
            }
        }
        return StlModel(name, tris, floatArrayOf(0f, 0f, 0f), floatArrayOf(w, d, h))
    }

    private fun cylinder(name: String, r: Float, h: Float): StlModel {
        val seg = 28
        val tris = mutableListOf<StlTriangle>()
        val centerB = floatArrayOf(r, r, 0f)
        val centerT = floatArrayOf(r, r, h)
        for (i in 0 until seg) {
            val a1 = (2 * Math.PI * i / seg).toFloat()
            val a2 = (2 * Math.PI * (i + 1) / seg).toFloat()
            val x1 = r + r * kotlin.math.cos(a1); val y1 = r + r * kotlin.math.sin(a1)
            val x2 = r + r * kotlin.math.cos(a2); val y2 = r + r * kotlin.math.sin(a2)
            // бок
            tris.add(StlTriangle(floatArrayOf(x1, y1, 0f, x2, y2, 0f, x2, y2, h), 0f, 0f, 1f))
            tris.add(StlTriangle(floatArrayOf(x1, y1, 0f, x2, y2, h, x1, y1, h), 0f, 0f, 1f))
            // крышки
            tris.add(StlTriangle(floatArrayOf(centerB[0], centerB[1], 0f, x2, y2, 0f, x1, y1, 0f), 0f, 0f, -1f))
            tris.add(StlTriangle(floatArrayOf(centerT[0], centerT[1], h, x1, y1, h, x2, y2, h), 0f, 0f, 1f))
        }
        return StlModel(name, tris, floatArrayOf(0f, 0f, 0f), floatArrayOf(2 * r, 2 * r, h))
    }

    val ALL: List<StlModel> by lazy {
        listOf(
            cuboid("Корпус 100×70×25 мм", 100f, 70f, 25f),
            cuboid("Мини-корпус 60×40×15 мм", 60f, 40f, 15f),
            cuboid("Корпус 120×80×35 мм", 120f, 80f, 35f),
            cylinder("Круглый корпус Ø80×20 мм", 40f, 20f)
        )
    }

    fun byName(name: String): StlModel? = ALL.firstOrNull { it.name == name }
}
