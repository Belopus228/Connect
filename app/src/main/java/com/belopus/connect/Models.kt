package com.belopus.connect

import java.util.UUID

data class PinDef(val name: String, val dx: Int, val dy: Int)

data class CompDef(
    val id: String,
    val title: String,
    val desc: String,
    val wGrid: Int,
    val hGrid: Int,
    val heightMm: Float,
    val color: Int,
    val pins: List<PinDef>,
    val defaultProps: Map<String, String> = emptyMap()
)

data class PlacedComponent(
    val id: String = UUID.randomUUID().toString().take(8),
    var typeId: String = "led_red",
    var gridX: Int = 2,
    var gridY: Int = 2,
    var rotation: Int = 0,
    var label: String = "",
    val props: MutableMap<String, String> = mutableMapOf(),
    var insideCase: Boolean = false,
    var caseXmm: Float = 5f,
    var caseYmm: Float = 5f,
    var caseZmm: Float = 2f
) {
    fun def(): CompDef = Catalog.byId(typeId)
    fun effW(): Int = if (rotation % 180 == 90) def().hGrid else def().wGrid
    fun effH(): Int = if (rotation % 180 == 90) def().wGrid else def().hGrid
    fun widthMm(): Float = effW() * 2.54f
    fun depthMm(): Float = effH() * 2.54f

    /** pin grid position after rotation */
    fun pinPos(pin: PinDef): Pair<Int, Int> {
        val d = def()
        return when (((rotation % 360) + 360) % 360) {
            90 -> Pair(d.hGrid - 1 - pin.dy, pin.dx)
            180 -> Pair(d.wGrid - 1 - pin.dx, d.hGrid - 1 - pin.dy)
            270 -> Pair(pin.dy, d.wGrid - 1 - pin.dx)
            else -> Pair(pin.dx, pin.dy)
        }
    }
}

data class Wire(
    val id: String = UUID.randomUUID().toString().take(8),
    var aComp: String = "",
    var aPin: String = "",
    var bComp: String = "",
    var bPin: String = "",
    var style: String = "wire", // wire | solder
    var color: Int = 0xFFD32F2F.toInt()
)

data class Project(
    var name: String = "Мой проект",
    var boardCols: Int = 30,
    var boardRows: Int = 18,
    val components: MutableList<PlacedComponent> = mutableListOf(),
    val wires: MutableList<Wire> = mutableListOf(),
    var firmwareCode: String = FirmwareExamples.BLINK,
    var firmwareBinName: String = "",
    var stlName: String = "",
    var flashed: Boolean = false
)

object FirmwareExamples {
    const val BLINK = """// === Blink: светодиод на D13 ===
pinMode(13, OUTPUT);
digitalWrite(13, HIGH);
delay(500);
digitalWrite(13, LOW);
delay(500);
"""

    const val BUTTON_LED = """// === Кнопка -> светодиод ===
// Кнопка в демо-схеме припаяна к D32 и GND, нажатие = LOW
pinMode(13, OUTPUT);
pinMode(32, INPUT_PULLUP);
if (digitalRead(32) == LOW) {
  digitalWrite(13, HIGH);
  Serial.println("Кнопка нажата!");
} else {
  digitalWrite(13, LOW);
}
delay(80);
"""

    const val OLED_HELLO = """// === OLED дисплей + мигание ===
pinMode(13, OUTPUT);
oled.clear();
oled.print("Привет, Connect!");
Serial.println("OLED: Привет, Connect!");
digitalWrite(13, HIGH);
delay(800);
oled.print("ESP32 работает :)");
digitalWrite(13, LOW);
delay(800);
"""

    const val NRF_SEND = """// === nRF24L01: передатчик ===
pinMode(13, OUTPUT);
nrf.send("PING от ESP32");
Serial.println("nRF24: отправлено PING");
digitalWrite(13, HIGH);
delay(300);
digitalWrite(13, LOW);
delay(700);
"""

    const val TRAFFIC = """// === Светофор: 3 светодиода ===
// Красный D13, жёлтый D12, зелёный D14
pinMode(13, OUTPUT);
pinMode(12, OUTPUT);
pinMode(14, OUTPUT);
digitalWrite(13, HIGH);
digitalWrite(12, LOW);
digitalWrite(14, LOW);
delay(1200);
digitalWrite(13, LOW);
digitalWrite(12, HIGH);
delay(500);
digitalWrite(12, LOW);
digitalWrite(14, HIGH);
Serial.println("Зелёный!");
delay(1200);
digitalWrite(14, LOW);
"""

    val ALL: List<Pair<String, String>> = listOf(
        "💡 Blink — светодиод D13" to BLINK,
        "🔘 Кнопка управляет LED" to BUTTON_LED,
        "🖥 OLED: Привет, Connect!" to OLED_HELLO,
        "📡 nRF24L01 передатчик" to NRF_SEND,
        "🚦 Светофор (3 LED)" to TRAFFIC
    )
}
