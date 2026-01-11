#include <Arduino.h>
#include <ArduinoJson.h>
#define LGFX_USE_V1
#include <LovyanGFX.hpp>
#include <lvgl.h>
#include "WiFiConnection.h"
#include "MqttClient.h"

// Pin definitions
#define TFT_MOSI 11
#define TFT_SCLK 12
#define TFT_CS   10
#define TFT_DC   13
#define TFT_RST  14
#define TFT_BL   9
#define RGB_LED_PIN 48

// MQTT configuration
#define MQTT_BROKER "rpi.local"
#define MQTT_PORT 1883
#define MQTT_TOPIC "power-events"

// =============================================================================
// Color Palette
// =============================================================================
#define COLOR_BACKGROUND       lv_color_hex(0x141829)  // Dark blue-gray
#define COLOR_INNER_CIRCLE     lv_color_hex(0x1A1A1A)  // Darker green (less intense)
// #define COLOR_OFF_CIRCLE       lv_color_hex(0xB02D38)  // Darker red (less intense)
// #define COLOR_STANDBY_CIRCLE   lv_color_hex(0x1A1A1A)  // Black/very dark
// #define COLOR_AWAKE_CIRCLE     lv_color_hex(0x1E4D38)  // Darker green (less intense)
// #define COLOR_UNKNOWN_CIRCLE   lv_color_hex(0x4A3429)  // Darker brown (less intense)
#define COLOR_GRAYISH_WHITE    lv_color_hex(0xC4C4C4)  // Grayish white for OFF
//#define COLOR_WHITE            lv_color_hex(0xFFFFFF)  // White
//#define COLOR_OFF_TEXT         lv_color_hex(0xCCCCCC)  // Grayish white for OFF
#define COLOR_CHECKMARK        lv_color_hex(0x00E676)  // Intense green
#define COLOR_QUESTION_MARK    lv_color_hex(0xFFB300)  // Yellow/amber

// =============================================================================
// Layout Constants
// =============================================================================
#define CIRCLE_DIAMETER        200
#define CIRCLE_RADIUS          100

// =============================================================================
// Power Status Enum
// =============================================================================
typedef enum {
    STATUS_OFF,
    STATUS_STANDBY,
    STATUS_AWAKE,
    STATUS_UNKNOWN,
    STATUS_COUNT
} PowerStatus;

// =============================================================================
// Global Variables
// =============================================================================
static PowerStatus current_status = STATUS_UNKNOWN;
static lv_obj_t* screens[STATUS_COUNT] = {NULL, NULL, NULL, NULL};

// WiFi and MQTT
WiFiConnection wifi("ESP32-Display-setup", RGB_LED_PIN);
MqttClient mqtt(MQTT_BROKER, MQTT_PORT);

// =============================================================================
// LovyanGFX configuration for GC9A01
// =============================================================================
class LGFX : public lgfx::LGFX_Device
{
  lgfx::Panel_GC9A01 _panel_instance;
  lgfx::Bus_SPI _bus_instance;
  lgfx::Light_PWM _light_instance;

public:
  LGFX(void)
  {
    {
      auto cfg = _bus_instance.config();
      cfg.spi_host = SPI2_HOST;
      cfg.spi_mode = 0;
      cfg.freq_write = 40000000;
      cfg.freq_read = 16000000;
      cfg.spi_3wire = false;
      cfg.use_lock = true;
      cfg.dma_channel = SPI_DMA_CH_AUTO;
      cfg.pin_sclk = TFT_SCLK;
      cfg.pin_mosi = TFT_MOSI;
      cfg.pin_miso = -1;
      cfg.pin_dc = TFT_DC;
      _bus_instance.config(cfg);
      _panel_instance.setBus(&_bus_instance);
    }

    {
      auto cfg = _panel_instance.config();
      cfg.pin_cs = TFT_CS;
      cfg.pin_rst = TFT_RST;
      cfg.pin_busy = -1;
      cfg.panel_width = 240;
      cfg.panel_height = 240;
      cfg.offset_x = 0;
      cfg.offset_y = 0;
      cfg.offset_rotation = 0;
      cfg.dummy_read_pixel = 8;
      cfg.dummy_read_bits = 1;
      cfg.readable = false;
      cfg.invert = true;
      cfg.rgb_order = false;
      cfg.dlen_16bit = false;
      cfg.bus_shared = false;
      _panel_instance.config(cfg);
    }

    {
      auto cfg = _light_instance.config();
      cfg.pin_bl = TFT_BL;
      cfg.invert = false;
      cfg.freq = 44100;
      cfg.pwm_channel = 7;
      _light_instance.config(cfg);
      _panel_instance.setLight(&_light_instance);
    }

    setPanel(&_panel_instance);
  }
};

static LGFX gfx;

// LVGL display buffer
static const uint32_t screenWidth = 240;
static const uint32_t screenHeight = 240;
static lv_disp_draw_buf_t draw_buf;
static lv_color_t *buf1;
static lv_color_t *buf2;
static lv_disp_drv_t disp_drv;

// =============================================================================
// LVGL display flush callback
// =============================================================================
void my_disp_flush(lv_disp_drv_t *disp, const lv_area_t *area, lv_color_t *color_p)
{
  uint32_t w = (area->x2 - area->x1 + 1);
  uint32_t h = (area->y2 - area->y1 + 1);

  gfx.startWrite();
  gfx.setAddrWindow(area->x1, area->y1, w, h);
  gfx.writePixels((lgfx::rgb565_t *)&color_p->full, w * h);
  gfx.endWrite();

  lv_disp_flush_ready(disp);
}

// =============================================================================
// Apply drop shadow to main circle - big shadow extending to screen edges
// =============================================================================
void apply_circle_shadow(lv_obj_t* circle) {
    lv_obj_set_style_shadow_width(circle, 50, 0);   // Large shadow
    lv_obj_set_style_shadow_spread(circle, 10, 0);  // Spread outward
    lv_obj_set_style_shadow_color(circle, lv_color_hex(0x000000), 0);
    lv_obj_set_style_shadow_opa(circle, LV_OPA_70, 0);
    lv_obj_set_style_shadow_ofs_x(circle, 0, 0);    // Centered
    lv_obj_set_style_shadow_ofs_y(circle, 0, 0);    // Centered
}

// =============================================================================
// Draw crescent moon icon (for Standby) - clean centered design
// =============================================================================
void draw_crescent_moon(lv_obj_t* parent) {
    // White outer circle (main moon body)
    lv_obj_t* moon_outer = lv_obj_create(parent);
    lv_obj_remove_style_all(moon_outer);
    lv_obj_set_size(moon_outer, 60, 60);
    lv_obj_align(moon_outer, LV_ALIGN_CENTER, 0, -10);
    lv_obj_set_style_radius(moon_outer, LV_RADIUS_CIRCLE, 0);
    lv_obj_set_style_bg_color(moon_outer, COLOR_GRAYISH_WHITE, 0);
    lv_obj_set_style_bg_opa(moon_outer, LV_OPA_COVER, 0);

    // Dark circle to cut out crescent shape
    lv_obj_t* moon_cutout = lv_obj_create(parent);
    lv_obj_remove_style_all(moon_cutout);
    lv_obj_set_size(moon_cutout, 50, 50);
    lv_obj_align(moon_cutout, LV_ALIGN_CENTER, 17, -18);
    lv_obj_set_style_radius(moon_cutout, LV_RADIUS_CIRCLE, 0);
    lv_obj_set_style_bg_color(moon_cutout, COLOR_INNER_CIRCLE, 0);
    lv_obj_set_style_bg_opa(moon_cutout, LV_OPA_COVER, 0);
}

// =============================================================================
// Draw checkmark icon (for Awake) - centered checkmark
// =============================================================================
void draw_checkmark(lv_obj_t* parent) {
    // Checkmark points - visual center considered
    // Short arm goes down-right, long arm goes up-right
    static lv_point_t checkmark_points[3] = {
        {0, 30},   // Start (left point)
        {20, 50},  // Bottom vertex (the corner)
        {70, 0}    // End (top right)
    };

    lv_obj_t* line = lv_line_create(parent);
    lv_line_set_points(line, checkmark_points, 3);
    // Visual center of checkmark is around the vertex, not geometric center
    // Vertex is at x=20, so offset should be about -20 to center the vertex
    // Adjust Y to be slightly above center
    //lv_obj_align(line, LV_ALIGN_CENTER, -25, -20);
    lv_obj_align(line, LV_ALIGN_CENTER, 10, -10);

    lv_obj_set_style_line_color(line, COLOR_CHECKMARK, 0);
    lv_obj_set_style_line_width(line, 16, 0);
    lv_obj_set_style_line_rounded(line, true, 0);
}

// =============================================================================
// Draw question mark icon (for Unknown) - large ? with shadow
// =============================================================================
void draw_question_mark(lv_obj_t* parent) {
    // Use font-based question mark for cleaner look
    lv_obj_t* label = lv_label_create(parent);
    lv_label_set_text(label, "?");
    lv_obj_set_style_text_font(label, &lv_font_montserrat_48, 0);
    lv_obj_set_style_text_color(label, COLOR_QUESTION_MARK, 0);
    lv_obj_align(label, LV_ALIGN_CENTER, 0, -10);

    // Create a backing container for shadow effect
    // Note: LVGL labels don't directly support shadows, but we position it for visual effect
}

// =============================================================================
// Create a status screen
// =============================================================================
lv_obj_t* create_status_screen(PowerStatus status) {
    // Create new screen
    lv_obj_t* screen = lv_obj_create(NULL);

    // Set dark background
    lv_obj_set_style_bg_color(screen, COLOR_BACKGROUND, 0);
    lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);

    // Create main circle container - perfectly centered
    lv_obj_t* circle = lv_obj_create(screen);
    lv_obj_remove_style_all(circle);
    lv_obj_set_size(circle, CIRCLE_DIAMETER, CIRCLE_DIAMETER);
    lv_obj_center(circle);  // Perfect center
    lv_obj_set_style_radius(circle, LV_RADIUS_CIRCLE, 0);
    lv_obj_set_style_bg_opa(circle, LV_OPA_COVER, 0);
    lv_obj_clear_flag(circle, LV_OBJ_FLAG_SCROLLABLE);

    // Apply big shadow extending to edges
    apply_circle_shadow(circle);

    // Configure based on status
    const char* status_text = "";
    lv_color_t text_color = COLOR_GRAYISH_WHITE;

    lv_obj_set_style_bg_color(circle, COLOR_INNER_CIRCLE, 0);

    switch(status) {
        case STATUS_OFF:
            {
                // OFF text - larger and grayish, centered
                lv_obj_t* off_label = lv_label_create(circle);
                lv_label_set_text(off_label, "OFF");
                lv_obj_set_style_text_font(off_label, &lv_font_montserrat_48, 0);
                lv_obj_set_style_text_color(off_label, COLOR_GRAYISH_WHITE, 0);
                lv_obj_center(off_label);
            }
            return screen;

        case STATUS_STANDBY:
            draw_crescent_moon(circle);
            status_text = "Standby";
            text_color = COLOR_GRAYISH_WHITE;
            break;

        case STATUS_AWAKE:
            draw_checkmark(circle);
            status_text = "Awake";
            text_color = COLOR_CHECKMARK;
            break;

        case STATUS_UNKNOWN:
            draw_question_mark(circle);
            status_text = "Unknown";
            text_color = COLOR_GRAYISH_WHITE;
            break;

        default:
            break;
    }

    // Add status text below icon
    if(strlen(status_text) > 0) {
        lv_obj_t* label = lv_label_create(circle);
        lv_label_set_text(label, status_text);
        lv_obj_set_style_text_font(label, &lv_font_montserrat_20, 0);
        lv_obj_set_style_text_color(label, text_color, 0);
        lv_obj_align(label, LV_ALIGN_CENTER, 0, 60);
    }

    return screen;
}

// =============================================================================
// MQTT callback for power events
// =============================================================================
void onPowerEvent(const char* topic, const char* payload) {
    JsonDocument doc;
    if (deserializeJson(doc, payload) != DeserializationError::Ok) {
        Serial0.println("Failed to parse JSON payload");
        return;
    }

    const char* state = doc["State"];
    if (state == nullptr) {
        Serial0.println("State field not found in payload");
        return;
    }

    Serial0.printf("Received state: %s\n", state);

    PowerStatus newStatus;
    if (strcmp(state, "Awake") == 0) {
        newStatus = STATUS_AWAKE;
    } else if (strcmp(state, "Standby") == 0) {
        newStatus = STATUS_STANDBY;
    } else if (strcmp(state, "Off") == 0) {
        newStatus = STATUS_OFF;
    } else {
        newStatus = STATUS_UNKNOWN;
    }

    if (newStatus != current_status) {
        current_status = newStatus;
        lv_scr_load(screens[current_status]);
        Serial0.printf("Switched to: %s\n",
            current_status == STATUS_OFF ? "OFF" :
            current_status == STATUS_STANDBY ? "Standby" :
            current_status == STATUS_AWAKE ? "Awake" : "Unknown");
    }
}

// =============================================================================
// Setup status screens
// =============================================================================
void setup_status_screens() {
    Serial0.println("Creating status screens...");

    // Create all 4 screens
    Serial0.println("Creating OFF screen...");
    screens[STATUS_OFF] = create_status_screen(STATUS_OFF);
    Serial0.printf("OFF screen created: %p\n", screens[STATUS_OFF]);

    Serial0.println("Creating STANDBY screen...");
    screens[STATUS_STANDBY] = create_status_screen(STATUS_STANDBY);
    Serial0.printf("STANDBY screen created: %p\n", screens[STATUS_STANDBY]);

    Serial0.println("Creating AWAKE screen...");
    screens[STATUS_AWAKE] = create_status_screen(STATUS_AWAKE);
    Serial0.printf("AWAKE screen created: %p\n", screens[STATUS_AWAKE]);

    Serial0.println("Creating UNKNOWN screen...");
    screens[STATUS_UNKNOWN] = create_status_screen(STATUS_UNKNOWN);
    Serial0.printf("UNKNOWN screen created: %p\n", screens[STATUS_UNKNOWN]);
}

// =============================================================================
// Setup
// =============================================================================
void setup() {
  Serial0.begin(115200);
  delay(1000);
  Serial0.println("\n\n=== Power Status Display ===");

  // Initialize display
  Serial0.println("Initializing display...");
  gfx.init();
  gfx.setBrightness(128); // 0-255 range: 255 - 100% brightness, 128 - 50%, 64 - 25%, 32 ~ 12%
  gfx.fillScreen(TFT_BLACK);
  Serial0.println("Display initialized!");

  // Initialize LVGL
  Serial0.println("Initializing LVGL...");
  lv_init();

  // Allocate buffers for LVGL
  uint32_t bufSize = screenWidth * 40; // 40 lines buffer
  buf1 = (lv_color_t*)heap_caps_malloc(bufSize * sizeof(lv_color_t), MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL);
  buf2 = (lv_color_t*)heap_caps_malloc(bufSize * sizeof(lv_color_t), MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL);

  if (!buf1 || !buf2) {
    Serial0.println("ERROR: Failed to allocate LVGL buffers!");
    while(1) delay(100);
  }
  Serial0.println("LVGL buffers allocated");

  // Initialize LVGL draw buffer
  lv_disp_draw_buf_init(&draw_buf, buf1, buf2, bufSize);

  // Initialize display driver
  lv_disp_drv_init(&disp_drv);
  disp_drv.hor_res = screenWidth;
  disp_drv.ver_res = screenHeight;
  disp_drv.flush_cb = my_disp_flush;
  disp_drv.draw_buf = &draw_buf;
  lv_disp_drv_register(&disp_drv);

  Serial0.println("LVGL initialized!");

  // Connect to WiFi
  Serial0.println("Connecting to WiFi...");
  wifi.connect();
  Serial0.println("WiFi connected!");

  // Connect to MQTT broker
  Serial0.println("Connecting to MQTT broker...");
  mqtt.connect();
  mqtt.subscribe(MQTT_TOPIC, onPowerEvent);
  Serial0.println("MQTT connected and subscribed!");

  // Setup power status UI screens
  setup_status_screens();

  // Set default status
  current_status = STATUS_UNKNOWN;
  lv_scr_load(screens[current_status]);

  Serial0.println("\n=== Setup complete! ===");
  Serial0.println("Listening for power events on MQTT topic: " MQTT_TOPIC);
}

// =============================================================================
// Main loop
// =============================================================================
void loop() {
  mqtt.loop();  // Process MQTT messages
  lv_tick_inc(5);
  lv_timer_handler();
  delay(5);
}
