#include "MqttClient.h"

MqttClient* MqttClient::_instance = nullptr;

MqttClient::MqttClient(const char* broker, uint16_t port)
  : _mqttClient(_wifiClient), _broker(broker), _port(port) {
  _instance = this;
  _mqttClient.setBufferSize(512);
}

void MqttClient::connect() {
  _mqttClient.setServer(_broker, _port);
  _mqttClient.setCallback(mqttCallback);
  reconnect();
}

void MqttClient::subscribe(const char* topic, MqttMessageCallback callback) {
  _subscriptions.push_back({String(topic), callback});
  if (_mqttClient.connected()) {
    _mqttClient.subscribe(topic);
  }
}

void MqttClient::loop() {
  if (!_mqttClient.connected()) {
    reconnect();
  }
  _mqttClient.loop();
}

bool MqttClient::isConnected() {
  return _mqttClient.connected();
}

void MqttClient::reconnect() {
  while (!_mqttClient.connected()) {
    String clientId = "ESP32-" + String(random(0xffff), HEX);

    if (_mqttClient.connect(clientId.c_str())) {
      for (auto& sub : _subscriptions) {
        _mqttClient.subscribe(sub.topic.c_str());
      }
    } else {
      delay(2000);
    }
  }
}

void MqttClient::mqttCallback(char* topic, byte* payload, unsigned int length) {
  Serial0.printf("MQTT received on topic: %s\n", topic);

  if (_instance != nullptr) {
    char message[length + 1];
    memcpy(message, payload, length);
    message[length] = '\0';

    // Find matching subscription
    for (auto& sub : _instance->_subscriptions) {
      if (strcmp(topic, sub.topic.c_str()) == 0 && sub.callback != nullptr) {
        Serial0.printf("Payload: %s\n", message);
        sub.callback(topic, message);
        return;
      }
    }
  }
}
