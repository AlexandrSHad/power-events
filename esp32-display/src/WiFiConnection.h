#pragma once

#include <Arduino.h>

class WiFiConnection {
public:
  WiFiConnection(const char* apName, uint8_t ledPin);

  // Connects to WiFi. Blocks until connected or restarts if failed.
  void connect();

  // Resets saved credentials (for testing)
  void resetCredentials();

private:
  const char* _apName;
  uint8_t _ledPin;

  bool hasCredentials();
  void showLed(uint8_t r, uint8_t g, uint8_t b);
};
