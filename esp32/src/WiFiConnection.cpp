#include "WiFiConnection.h"
#include <WiFiManager.h>
#include <esp_wifi.h>

WiFiConnection::WiFiConnection(const char* apName, uint8_t ledPin)
  : _apName(apName), _ledPin(ledPin) {}

void WiFiConnection::connect() {
  WiFi.mode(WIFI_STA);

  if (hasCredentials()) {
    showLed(5, 5, 0);  // Yellow - has credentials, connecting
    delay(1000);
  } else {
    showLed(0, 0, 5);  // Blue - no credentials, portal will open
  }

  WiFiManager wm;
  bool connected = wm.autoConnect(_apName);

  if (!connected) {
    ESP.restart();
  }
}

void WiFiConnection::resetCredentials() {
  WiFiManager wm;
  wm.resetSettings();
}

bool WiFiConnection::hasCredentials() {
  wifi_config_t conf;
  esp_wifi_get_config(WIFI_IF_STA, &conf);
  return strlen((char*)conf.sta.ssid) > 0;
}

void WiFiConnection::showLed(uint8_t r, uint8_t g, uint8_t b) {
  neopixelWrite(_ledPin, r, g, b);
}
