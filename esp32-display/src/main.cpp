#include <Arduino.h>
#include <SPI.h>
#include <TFT_eSPI.h>

TFT_eSPI tft = TFT_eSPI();

void setup() {
  // put your setup code here, to run once:
  Serial0.begin(115200);
  Serial0.println("TFT_eSPI initialization...");
  tft.init();
  Serial0.println("TFT_eSPI initialized");
  tft.setRotation(1);
  tft.fillScreen(TFT_BLACK);
  tft.setTextColor(TFT_WHITE, TFT_BLACK);
  tft.setTextSize(2);
  tft.setCursor(10, 10);
  tft.println("Hello, TFT!");
}

void loop() {
  // put your main code here, to run repeatedly:
}
