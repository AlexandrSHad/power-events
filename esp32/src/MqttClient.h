#pragma once

#include <Arduino.h>
#include <PubSubClient.h>
#include <WiFiClient.h>

typedef void (*MqttMessageCallback)(const char* topic, const char* payload);

class MqttClient {
public:
  MqttClient(const char* broker, uint16_t port);

  void connect();
  void subscribe(const char* topic, MqttMessageCallback callback);
  void loop();
  bool isConnected();

private:
  WiFiClient _wifiClient;
  PubSubClient _mqttClient;
  const char* _broker;
  uint16_t _port;
  const char* _subscribeTopic;
  MqttMessageCallback _userCallback;

  void reconnect();
  static void mqttCallback(char* topic, byte* payload, unsigned int length);
  static MqttClient* _instance;
};
