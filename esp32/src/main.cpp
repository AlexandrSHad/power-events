#include <Arduino.h>
#include "WiFiConnection.h"

#define RGB_LED_PIN 48

WiFiConnection wifi("ESP32-S3-01-setup", RGB_LED_PIN);

void setup() {
  pinMode(RGB_LED_PIN, OUTPUT);
  wifi.connect();
}

void loop() {
  // Normal operation - blink green
  neopixelWrite(RGB_LED_PIN, 0, 5, 0);
  delay(500);

  neopixelWrite(RGB_LED_PIN, 0, 0, 0);
  delay(2000);
}
