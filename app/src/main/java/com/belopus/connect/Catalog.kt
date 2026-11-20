package com.belopus.connect

import android.graphics.Color

object Catalog {

    private fun pins(vararg p: Triple<String, Int, Int>): List<PinDef> =
        p.map { PinDef(it.first, it.second, it.third) }

    val defs: List<CompDef> = listOf(
        CompDef(
            "esp32", "ESP32 DevKit V1", "Wi-Fi + Bluetooth, 3.3В логика. Сюда заливается прошивка (вкладка «Код ESP»). Пины D13,D12,D14,D27,D26,D25,D33,D32 слева; VIN,GND,D23,D22,D21,D19,D18,D5 справа.",
            11, 20, 12f, Color.parseColor("#263238"),
            pins(
                Triple("3V3", 0, 1), Triple("GND", 0, 3), Triple("D13", 0, 5),
                Triple("D12", 0, 7), Triple("D14", 0, 9), Triple("D27", 0, 11),
                Triple("D26", 0, 13), Triple("D25", 0, 15), Triple("D33", 0, 17),
                Triple("D32", 0, 19),
                Triple("VIN", 10, 1), Triple("GND2", 10, 3), Triple("D23", 10, 5),
                Triple("D22", 10, 7), Triple("D21", 10, 9), Triple("D19", 10, 11),
                Triple("D18", 10, 13), Triple("D5", 10, 15), Triple("TX", 10, 17),
                Triple("RX", 10, 19)
            ),
            mapOf("firmware" to "blink")
        ),
        CompDef(
            "oled", "OLED SSD1306 128×64", "Дисплей I2C: VCC,GND,SCL,SDA. Текст выводится командой oled.print(\"...\") из прошивки.",
            12, 7, 10f, Color.parseColor("#102027"),
            pins(Triple("VCC", 2, 0), Triple("GND", 4, 0), Triple("SCL", 7, 0), Triple("SDA", 9, 0)),
            mapOf("displayText" to "Connect OS")
        ),
        CompDef(
            "tft", "TFT ILI9341 2.4\"", "Цветной дисплей SPI 320×240.",
            12, 16, 11f, Color.parseColor("#4A148C"),
            pins(
                Triple("VCC", 0, 1), Triple("GND", 0, 3), Triple("CS", 0, 5),
                Triple("RST", 0, 7), Triple("DC", 0, 9), Triple("MOSI", 0, 11),
                Triple("SCK", 0, 13), Triple("LED", 0, 15)
            ),
            mapOf("displayText" to "TFT 320x240")
        ),
        CompDef(
            "tft18", "TFT 1.8\" ST7735 128×160", "Цветной TFT-дисплей 1.8 дюйма (ST7735, 128×160), SPI: VCC,GND,SCK,SDA(MOSI),RES,DC,CS,BL(подсветка). В прошивке: tft.print(\"...\"), tft.clear(). Виден и в 2D, и в 3D.",
            13, 20, 11f, Color.parseColor("#B71C1C"),
            pins(
                Triple("VCC", 1, 19), Triple("GND", 3, 19), Triple("SCK", 5, 19),
                Triple("SDA", 6, 19), Triple("RES", 8, 19), Triple("DC", 9, 19),
                Triple("CS", 11, 19), Triple("BL", 12, 19)
            ),
            mapOf("displayText" to "TFT 1.8 128x160")
        ),
        CompDef(
            "lcd1602", "LCD 1602", "Текстовый дисплей 16×2, параллельный интерфейс.",
            18, 6, 12f, Color.parseColor("#1B5E20"),
            pins(
                Triple("VSS", 1, 0), Triple("VDD", 3, 0), Triple("RS", 5, 0),
                Triple("E", 7, 0), Triple("D4", 9, 0), Triple("D5", 11, 0),
                Triple("D6", 13, 0), Triple("D7", 15, 0)
            ),
            mapOf("displayText" to "LCD 1602 ready")
        ),
        CompDef(
            "nrf24", "nRF24L01", "Радиомодуль 2.4 ГГц SPI. Канал и адрес — в свойствах. Команда nrf.send(\"...\") в прошивке.",
            7, 7, 14f, Color.parseColor("#0D47A1"),
            pins(
                Triple("VCC", 1, 0), Triple("GND", 3, 0), Triple("CE", 5, 0),
                Triple("CSN", 5, 6), Triple("SCK", 3, 6), Triple("MOSI", 1, 6),
                Triple("MISO", 5, 3)
            ),
            mapOf("channel" to "76", "address" to "NODE1", "lastPacket" to "—")
        ),
        CompDef(
            "led", "Светодиод", "A = анод (+, длинная нога), K = катод (−). Без резистора от 3.3/5В может СГОРЕТЬ в симуляции!",
            3, 4, 9f, Color.parseColor("#B71C1C"),
            pins(Triple("A", 0, 3), Triple("K", 2, 3)),
            mapOf("colorName" to "Красный", "brightness" to "0")
        ),
        CompDef(
            "resistor", "Резистор", "Номинал меняется кнопкой «Свойства»: 10Ω…10kΩ. Ставится последовательно со светодиодом.",
            6, 2, 5f, Color.parseColor("#D7A86E"),
            pins(Triple("1", 0, 0), Triple("2", 5, 0)),
            mapOf("ohms" to "220")
        ),
        CompDef(
            "button", "Тактовая кнопка", "При нажатии в режиме Симуляции замыкает 1—2. Подключение: ESP-пин — кнопка — GND + INPUT_PULLUP.",
            5, 5, 7f, Color.parseColor("#37474F"),
            pins(Triple("1", 0, 0), Triple("2", 4, 0), Triple("3", 0, 4), Triple("4", 4, 4)),
            mapOf("pressed" to "false")
        ),
        CompDef(
            "bat18650", "Батарея 18650", "Li-ion 3.7В. «+» — питание, «−» — GND.",
            9, 4, 19f, Color.parseColor("#2E7D32"),
            pins(Triple("+", 8, 1), Triple("-", 0, 1)),
            mapOf("voltage" to "3.7")
        ),
        CompDef(
            "bat9v", "Батарея 9В «Крона»", "9В. Для ESP32 подавать на VIN, для LED — только через резистор!",
            5, 7, 16f, Color.parseColor("#212121"),
            pins(Triple("+", 1, 0), Triple("-", 3, 0)),
            mapOf("voltage" to "9")
        ),
        CompDef(
            "lipo", "Li-Po аккумулятор", "3.7В, плоский пакет.",
            8, 5, 6f, Color.parseColor("#F9A825"),
            pins(Triple("+", 7, 2), Triple("-", 0, 2)),
            mapOf("voltage" to "3.7")
        ),
        CompDef(
            "buzzer", "Пьезо-зуммер", "tone()/ноты — в журнале симуляции.",
            4, 4, 9f, Color.parseColor("#455A64"),
            pins(Triple("+", 0, 3), Triple("-", 3, 3)),
            mapOf()
        ),
        CompDef(
            "dht22", "Датчик DHT22", "Температура и влажность. DATA — на любой D-пин ESP.",
            7, 5, 12f, Color.parseColor("#0277BD"),
            pins(Triple("VCC", 0, 2), Triple("DATA", 2, 2), Triple("GND", 6, 2)),
            mapOf("temp" to "24.5", "hum" to "48")
        ),
        CompDef(
            "pot", "Потенциометр 10k", "Крайние — VCC/GND, средний SIG — аналоговый сигнал (крутится в симуляции).",
            6, 6, 12f, Color.parseColor("#6A1B9A"),
            pins(Triple("1", 0, 5), Triple("SIG", 3, 5), Triple("2", 5, 5)),
            mapOf("value" to "512")
        ),
        CompDef(
            "servo", "Сервопривод SG90", "VCC (5В), GND, SIG — сигнал. servo.write(угол) в прошивке.",
            6, 9, 22f, Color.parseColor("#EF6C00"),
            pins(Triple("VCC", 1, 8), Triple("SIG", 3, 8), Triple("GND", 5, 8)),
            mapOf("angle" to "90")
        ),
        CompDef(
            "relay", "Реле 5В", "IN — сигнал от ESP, COM/NO/NC — коммутируемая цепь.",
            9, 7, 16f, Color.parseColor("#1565C0"),
            pins(
                Triple("VCC", 0, 1), Triple("GND", 0, 3), Triple("IN", 0, 5),
                Triple("COM", 8, 1), Triple("NO", 8, 3), Triple("NC", 8, 5)
            ),
            mapOf("on" to "false")
        ),
        CompDef(
            "cap", "Конденсатор 100µF", "+ / −, сглаживает питание.",
            3, 4, 10f, Color.parseColor("#283593"),
            pins(Triple("+", 0, 3), Triple("-", 2, 3)),
            mapOf()
        ),
        CompDef(
            "perfboard", "Макетная плата (доп.)", "Дополнительная плата для пайки 12×8 отверстий — можно ставить рядом как расширитель.",
            12, 8, 2f, Color.parseColor("#1B5E20"),
            pins(Triple("A1", 0, 0), Triple("A2", 11, 0), Triple("B1", 0, 7), Triple("B2", 11, 7)),
            mapOf()
        )
    )

    private val map: Map<String, CompDef> = defs.associateBy { it.id }

    fun byId(id: String): CompDef = map[id] ?: defs[0]
}
