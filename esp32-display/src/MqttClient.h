#pragma once

#include <Arduino.h>
#include <PubSubClient.h>
#include <WiFiClient.h>
#include <vector>

typedef void (*MqttMessageCallback)(const char* topic, const char* payload);

struct TopicHandler {
    String topic;
    MqttMessageCallback callback;
};

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
  std::vector<TopicHandler> _subscriptions;

  void reconnect();
  static void mqttCallback(char* topic, byte* payload, unsigned int length);
  static MqttClient* _instance;
};
