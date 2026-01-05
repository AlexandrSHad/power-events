#include <Arduino.h>
#include "WiFiConnection.h"
#include "MqttClient.h"

#define RGB_LED_PIN 48
#define MQTT_BROKER "rpi.local"
#define MQTT_PORT 1883
#define MQTT_TOPIC "power-events"

WiFiConnection wifi("ESP32-S3-01-setup", RGB_LED_PIN);
MqttClient mqtt(MQTT_BROKER, MQTT_PORT);

volatile bool powerEventReceived = false;
unsigned long eventLedStartTime = 0;
bool eventLedActive = false;

void onPowerEvent(const char* topic, const char* payload) {
  powerEventReceived = true;
}

void setup() {
  pinMode(RGB_LED_PIN, OUTPUT);
  wifi.connect();

  mqtt.connect();
  mqtt.subscribe(MQTT_TOPIC, onPowerEvent);
}

void loop() {
  mqtt.loop();

  if (powerEventReceived) {
    powerEventReceived = false;
    eventLedActive = true;
    eventLedStartTime = millis();
    neopixelWrite(RGB_LED_PIN, 5, 0, 5);  // Purple
  }

  if (eventLedActive && (millis() - eventLedStartTime >= 2000)) {
    eventLedActive = false;
    neopixelWrite(RGB_LED_PIN, 0, 0, 0);
  }

  if (!eventLedActive) {
    // Awaiting messages - blink green
    neopixelWrite(RGB_LED_PIN, 0, 5, 0);
    delay(500);
    neopixelWrite(RGB_LED_PIN, 0, 0, 0);
    delay(2000);
  }
}
