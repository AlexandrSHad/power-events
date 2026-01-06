#include <Arduino.h>
#include <ArduinoJson.h>
#include "WiFiConnection.h"
#include "MqttClient.h"

#define RGB_LED_PIN 48
#define MQTT_BROKER "rpi.local"
#define MQTT_PORT 1883
#define MQTT_TOPIC "power-events"

WiFiConnection wifi("ESP32-S3-01-setup", RGB_LED_PIN);
MqttClient mqtt(MQTT_BROKER, MQTT_PORT);

String eventState = "Unknown";

void onPowerEvent(const char* topic, const char* payload) {
  JsonDocument doc;
  if (deserializeJson(doc, payload) != DeserializationError::Ok) {
    return;
  }

  const char* state = doc["State"];
  
  if (state == nullptr) {
    Serial0.println("State: null");
    return;
  }

  Serial0.printf("State: %s\n", state);
  eventState = state;
}

void setup() {
  Serial0.begin(115200);

  Serial0.println("Starting the board...");
  pinMode(RGB_LED_PIN, OUTPUT);

  Serial0.println("Connecting to WiFi...");
  wifi.connect();

  Serial0.println("Connecting to EMQX broker...");
  mqtt.connect();
  mqtt.subscribe(MQTT_TOPIC, onPowerEvent);

  Serial0.println("Started the board. Listening for power events...");
}

void loop() {
  mqtt.loop();

  if (eventState == "Awake") {
    neopixelWrite(RGB_LED_PIN, 0, 5, 0);  // Green
  } else if (eventState == "Standby") {
    neopixelWrite(RGB_LED_PIN, 5, 0, 0);  // Red
  } else {
    neopixelWrite(RGB_LED_PIN, 5, 0, 5);  // Purple
  }
  delay(250);
  neopixelWrite(RGB_LED_PIN, 0, 0, 0);
  delay(500);
}
