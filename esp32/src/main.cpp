#include <Arduino.h>
#include <WiFiManager.h>
#include <esp_wifi.h>

// Built-in RGB LED pin
#define RGB_LED_PIN 48

WiFiManager wm;

bool hasWiFiCredentials() {
  wifi_config_t conf;
  esp_wifi_get_config(WIFI_IF_STA, &conf);
  return strlen((char*)conf.sta.ssid) > 0;
}

void setup() {
  pinMode(RGB_LED_PIN, OUTPUT);

  //wm.resetSettings();

  // Must init WiFi in STA mode before checking credentials
  WiFi.mode(WIFI_STA);

  if (hasWiFiCredentials()) {
    // Has saved credentials - show yellow
    neopixelWrite(RGB_LED_PIN, 5, 5, 0);
    delay(2000);
  } else {
    // No credentials - show blue (config portal needed)
    neopixelWrite(RGB_LED_PIN, 0, 0, 5);
  }

  // Starts config portal if no saved credentials, otherwise connects automatically
  // "ESP32-Setup" is the AP name users will see
  bool connected = wm.autoConnect("ESP32-S3-01-setup");

  if (!connected) {
    // Failed to connect - restart
    ESP.restart();
  }
}

void loop() {
  if (WiFi.status() == WL_CONNECTED) {
    // Connected - blink green
    neopixelWrite(RGB_LED_PIN, 0, 5, 0);
    delay(500);

    neopixelWrite(RGB_LED_PIN, 0, 0, 0);
    delay(2000);
  } else {
    // Not connected - blink red
    neopixelWrite(RGB_LED_PIN, 5, 0, 0);
    delay(200);

    neopixelWrite(RGB_LED_PIN, 0, 0, 0);
    delay(200);
  }
}
