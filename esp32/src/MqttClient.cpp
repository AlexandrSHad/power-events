#include "MqttClient.h"

MqttClient* MqttClient::_instance = nullptr;

MqttClient::MqttClient(const char* broker, uint16_t port)
  : _mqttClient(_wifiClient), _broker(broker), _port(port),
    _subscribeTopic(nullptr), _userCallback(nullptr) {
  _instance = this;
}

void MqttClient::connect() {
  _mqttClient.setServer(_broker, _port);
  _mqttClient.setCallback(mqttCallback);
  reconnect();
}

void MqttClient::subscribe(const char* topic, MqttMessageCallback callback) {
  _subscribeTopic = topic;
  _userCallback = callback;
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
      if (_subscribeTopic != nullptr) {
        _mqttClient.subscribe(_subscribeTopic);
      }
    } else {
      delay(5000);
    }
  }
}

void MqttClient::mqttCallback(char* topic, byte* payload, unsigned int length) {
  if (_instance != nullptr && _instance->_userCallback != nullptr) {
    char message[length + 1];
    memcpy(message, payload, length);
    message[length] = '\0';
    _instance->_userCallback(topic, message);
  }
}
