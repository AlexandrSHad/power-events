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

// Seq logging configuration (disable to stop sending logs via MQTT)
#define SEQ_LOGGING_ENABLED true

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
#define COLOR_CPU_ARC          COLOR_CHECKMARK
#define COLOR_RAM_ARC          lv_color_hex(0x00D4FF)  // Cyan
#define COLOR_ARC_BACKGROUND   lv_color_hex(0x2A2A2A)  // Dark gray track
#define COLOR_GPU_TEMP         lv_color_hex(0xFF6D00)  // Orange for GPU temp

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

// Metrics data
static float current_cpu_percent = 0.0f;
static float current_ram_percent = 0.0f;
static float target_cpu_percent = 0.0f;
static float target_ram_percent = 0.0f;

// Metrics screen UI elements
static lv_obj_t* metrics_screen = NULL;
static lv_obj_t* cpu_arc = NULL;
static lv_obj_t* ram_arc = NULL;
static lv_obj_t* center_label = NULL;

// Temperature data
static float target_cpu_temp = 0.0f;
static float current_cpu_temp = 0.0f;
static float target_gpu_temp = 0.0f;
static float current_gpu_temp = 0.0f;
static bool cpu_temp_received = false;
static bool gpu_temp_received = false;

// Battery data
static float target_battery_percent = 0.0f;
static float current_battery_percent = 0.0f;
static bool battery_charging = false;
static bool battery_data_received = false;

// Temperature and battery UI elements
static lv_obj_t* cpu_temp_label = NULL;
static lv_obj_t* gpu_temp_label = NULL;
static lv_obj_t* battery_fill_obj = NULL;
static lv_obj_t* battery_pct_label = NULL;
static lv_obj_t* battery_body_obj = NULL;

// WiFi and MQTT
WiFiConnection wifi("ESP32-Display-setup", RGB_LED_PIN);
MqttClient mqtt(MQTT_BROKER, MQTT_PORT);

// =============================================================================
// Seq logging function - sends logs to MQTT topic
// =============================================================================
void logToSeq(const char* level, const char* message) {
    // Always log to serial
    Serial0.printf("[%s] %s\n", level, message);
    
    // Only send to Seq via MQTT if enabled
    #if SEQ_LOGGING_ENABLED
    char topic[128];
    snprintf(topic, sizeof(topic), "logs/esp32/%s/%s", 
             WiFi.getHostname() ? WiFi.getHostname() : "unknown", 
             level);
    mqtt.publish(topic, message);
    #endif
}

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

lv_obj_t* create_metrics_screen() {
    // Create new screen
    lv_obj_t* screen = lv_obj_create(NULL);

    // Set dark background
    lv_obj_set_style_bg_color(screen, COLOR_BACKGROUND, 0);
    lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);

    // CPU Arc (outer) - 200px diameter, 16px width
    cpu_arc = lv_arc_create(screen);
    lv_obj_set_size(cpu_arc, 200, 200);
    lv_obj_center(cpu_arc);
    lv_arc_set_rotation(cpu_arc, 135);  // Start from bottom-left
    lv_arc_set_bg_angles(cpu_arc, 0, 270);  // 270° sweep
    lv_arc_set_angles(cpu_arc, 0, 0);  // Start at 0%
    lv_obj_remove_style(cpu_arc, NULL, LV_PART_KNOB);  // Remove knob
    lv_obj_clear_flag(cpu_arc, LV_OBJ_FLAG_CLICKABLE);  // Not clickable

    // CPU arc background (track)
    lv_obj_set_style_arc_color(cpu_arc, COLOR_ARC_BACKGROUND, LV_PART_MAIN);
    lv_obj_set_style_arc_width(cpu_arc, 16, LV_PART_MAIN);

    // CPU arc indicator
    lv_obj_set_style_arc_color(cpu_arc, COLOR_CPU_ARC, LV_PART_INDICATOR);
    lv_obj_set_style_arc_width(cpu_arc, 16, LV_PART_INDICATOR);
    lv_obj_set_style_arc_rounded(cpu_arc, true, LV_PART_INDICATOR);  // Rounded ends

    // RAM Arc (inner) - 160px diameter, 14px width
    ram_arc = lv_arc_create(screen);
    lv_obj_set_size(ram_arc, 160, 160);
    lv_obj_center(ram_arc);
    lv_arc_set_rotation(ram_arc, 135);  // Start from bottom-left
    lv_arc_set_bg_angles(ram_arc, 0, 270);  // 270° sweep
    lv_arc_set_angles(ram_arc, 0, 0);  // Start at 0%
    lv_obj_remove_style(ram_arc, NULL, LV_PART_KNOB);  // Remove knob
    lv_obj_clear_flag(ram_arc, LV_OBJ_FLAG_CLICKABLE);  // Not clickable

    // RAM arc background (track)
    lv_obj_set_style_arc_color(ram_arc, COLOR_ARC_BACKGROUND, LV_PART_MAIN);
    lv_obj_set_style_arc_width(ram_arc, 14, LV_PART_MAIN);

    // RAM arc indicator
    lv_obj_set_style_arc_color(ram_arc, COLOR_RAM_ARC, LV_PART_INDICATOR);
    lv_obj_set_style_arc_width(ram_arc, 14, LV_PART_INDICATOR);
    lv_obj_set_style_arc_rounded(ram_arc, true, LV_PART_INDICATOR);  // Rounded ends

    // CPU Temperature label (left of center)
    cpu_temp_label = lv_label_create(screen);
    lv_label_set_text(cpu_temp_label, "--°");
    lv_obj_set_style_text_font(cpu_temp_label, &lv_font_montserrat_20, 0);
    lv_obj_set_style_text_color(cpu_temp_label, COLOR_CPU_ARC, 0);
    lv_obj_align(cpu_temp_label, LV_ALIGN_CENTER, -28, 0);

    // GPU Temperature label (right of center)
    gpu_temp_label = lv_label_create(screen);
    lv_label_set_text(gpu_temp_label, "--°");
    lv_obj_set_style_text_font(gpu_temp_label, &lv_font_montserrat_20, 0);
    lv_obj_set_style_text_color(gpu_temp_label, COLOR_GPU_TEMP, 0);
    lv_obj_align(gpu_temp_label, LV_ALIGN_CENTER, 28, 0);

    // CPU Legend container
    lv_obj_t* cpu_legend = lv_obj_create(screen);
    lv_obj_remove_style_all(cpu_legend);
    lv_obj_set_size(cpu_legend, LV_SIZE_CONTENT, LV_SIZE_CONTENT);
    lv_obj_set_flex_flow(cpu_legend, LV_FLEX_FLOW_ROW);
    lv_obj_set_flex_align(cpu_legend, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_gap(cpu_legend, 5, 0);
    lv_obj_align(cpu_legend, LV_ALIGN_BOTTOM_MID, 0, -32);

    lv_obj_t* cpu_dot = lv_obj_create(cpu_legend);
    lv_obj_remove_style_all(cpu_dot);
    lv_obj_set_size(cpu_dot, 8, 8);
    lv_obj_set_style_radius(cpu_dot, LV_RADIUS_CIRCLE, 0);
    lv_obj_set_style_bg_color(cpu_dot, COLOR_CPU_ARC, 0);
    lv_obj_set_style_bg_opa(cpu_dot, LV_OPA_COVER, 0);

    lv_obj_t* cpu_label = lv_label_create(cpu_legend);
    lv_label_set_text(cpu_label, "CPU");
    lv_obj_set_style_text_font(cpu_label, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(cpu_label, COLOR_GRAYISH_WHITE, 0);

    // RAM Legend container
    lv_obj_t* ram_legend = lv_obj_create(screen);
    lv_obj_remove_style_all(ram_legend);
    lv_obj_set_size(ram_legend, LV_SIZE_CONTENT, LV_SIZE_CONTENT);
    lv_obj_set_flex_flow(ram_legend, LV_FLEX_FLOW_ROW);
    lv_obj_set_flex_align(ram_legend, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_gap(ram_legend, 5, 0);
    lv_obj_align(ram_legend, LV_ALIGN_BOTTOM_MID, 0, -52);

    lv_obj_t* ram_dot = lv_obj_create(ram_legend);
    lv_obj_remove_style_all(ram_dot);
    lv_obj_set_size(ram_dot, 8, 8);
    lv_obj_set_style_radius(ram_dot, LV_RADIUS_CIRCLE, 0);
    lv_obj_set_style_bg_color(ram_dot, COLOR_RAM_ARC, 0);
    lv_obj_set_style_bg_opa(ram_dot, LV_OPA_COVER, 0);

    lv_obj_t* ram_label = lv_label_create(ram_legend);
    lv_label_set_text(ram_label, "RAM");
    lv_obj_set_style_text_font(ram_label, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(ram_label, COLOR_GRAYISH_WHITE, 0);

    // Battery icon at bottom (dynamic)
    battery_body_obj = lv_obj_create(screen);
    lv_obj_remove_style_all(battery_body_obj);
    lv_obj_set_size(battery_body_obj, 30, 16);
    lv_obj_set_style_radius(battery_body_obj, 2, 0);
    lv_obj_set_style_border_color(battery_body_obj, COLOR_GRAYISH_WHITE, 0);
    lv_obj_set_style_border_width(battery_body_obj, 2, 0);
    lv_obj_align(battery_body_obj, LV_ALIGN_BOTTOM_MID, -15, -10);

    // Battery terminal
    lv_obj_t* battery_terminal = lv_obj_create(screen);
    lv_obj_remove_style_all(battery_terminal);
    lv_obj_set_size(battery_terminal, 3, 8);
    lv_obj_set_style_bg_color(battery_terminal, COLOR_GRAYISH_WHITE, 0);
    lv_obj_set_style_bg_opa(battery_terminal, LV_OPA_COVER, 0);
    lv_obj_align_to(battery_terminal, battery_body_obj, LV_ALIGN_OUT_RIGHT_MID, 0, 0);

    // Battery fill (dynamic)
    battery_fill_obj = lv_obj_create(battery_body_obj);
    lv_obj_remove_style_all(battery_fill_obj);
    lv_obj_set_size(battery_fill_obj, 0, 10);  // Start at 0, updated dynamically
    lv_obj_set_style_bg_color(battery_fill_obj, COLOR_CHECKMARK, 0);
    lv_obj_set_style_bg_opa(battery_fill_obj, LV_OPA_COVER, 0);
    lv_obj_align(battery_fill_obj, LV_ALIGN_LEFT_MID, 2, 0);

    // Battery percentage label
    battery_pct_label = lv_label_create(screen);
    lv_label_set_text(battery_pct_label, "--");
    lv_obj_set_style_text_font(battery_pct_label, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(battery_pct_label, COLOR_GRAYISH_WHITE, 0);
    lv_obj_align_to(battery_pct_label, battery_body_obj, LV_ALIGN_OUT_RIGHT_MID, 8, 0);

    return screen;
}

void update_metrics_animation(lv_timer_t* timer) {
    const float SMOOTHING = 0.15f;  // Adjust for smoother/faster animation
    const float TEMP_SMOOTHING = 0.3f;  // Faster for temperatures
    const float BATTERY_SMOOTHING = 0.1f;  // Slower for battery

    // Smooth interpolation - arcs
    current_cpu_percent += (target_cpu_percent - current_cpu_percent) * SMOOTHING;
    current_ram_percent += (target_ram_percent - current_ram_percent) * SMOOTHING;

    // Smooth interpolation - temperatures
    current_cpu_temp += (target_cpu_temp - current_cpu_temp) * TEMP_SMOOTHING;
    current_gpu_temp += (target_gpu_temp - current_gpu_temp) * TEMP_SMOOTHING;

    // Smooth interpolation - battery
    current_battery_percent += (target_battery_percent - current_battery_percent) * BATTERY_SMOOTHING;

    // Update arc angles (0-270°)
    if (cpu_arc != NULL) {
        lv_arc_set_angles(cpu_arc, 0, (int)(current_cpu_percent * 2.7f));
    }
    if (ram_arc != NULL) {
        lv_arc_set_angles(ram_arc, 0, (int)(current_ram_percent * 2.7f));
    }

    // Update temperature labels
    if (cpu_temp_label != NULL && cpu_temp_received) {
        char buf[8];
        snprintf(buf, sizeof(buf), "%d°", (int)current_cpu_temp);
        lv_label_set_text(cpu_temp_label, buf);
    }
    if (gpu_temp_label != NULL && gpu_temp_received) {
        char buf[8];
        snprintf(buf, sizeof(buf), "%d°", (int)current_gpu_temp);
        lv_label_set_text(gpu_temp_label, buf);
    }

    // Update battery
    if (battery_fill_obj != NULL && battery_data_received) {
        int fill_width = (int)(current_battery_percent / 100.0f * 26.0f);
        if (fill_width < 0) fill_width = 0;
        if (fill_width > 26) fill_width = 26;
        lv_obj_set_width(battery_fill_obj, fill_width);

        // Color coding: green when charging or >20%, amber 10-20%, red <10%
        lv_color_t fill_color;
        if (battery_charging) {
            fill_color = COLOR_CHECKMARK;  // Always green when charging
        } else if (current_battery_percent > 20.0f) {
            fill_color = COLOR_CHECKMARK;  // Green
        } else if (current_battery_percent > 10.0f) {
            fill_color = COLOR_QUESTION_MARK;  // Amber
        } else {
            fill_color = lv_color_hex(0xFF0000);  // Red
        }
        lv_obj_set_style_bg_color(battery_fill_obj, fill_color, 0);

        // Update percentage label
        if (battery_pct_label != NULL) {
            char buf[8];
            snprintf(buf, sizeof(buf), "%d%%", (int)current_battery_percent);
            lv_label_set_text(battery_pct_label, buf);
        }
    }
}

// =============================================================================
// MQTT callback for power events
// =============================================================================
void onPowerEvent(const char* topic, const char* payload) {
    JsonDocument doc;
    if (deserializeJson(doc, payload) != DeserializationError::Ok) {
        logToSeq("error", "Failed to parse JSON payload");
        return;
    }

    const char* state = doc["State"];
    if (state == nullptr) {
        logToSeq("error", "State field not found in payload");
        return;
    }

    char msg[64];
    snprintf(msg, sizeof(msg), "Received power state: %s", state);
    logToSeq("info", msg);

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

    // Screen switching logic - metrics screen used for Awake status
    if (newStatus != current_status) {
        current_status = newStatus;

        // Use metrics screen for AWAKE status, regular screens for others
        if (current_status == STATUS_AWAKE) {
            lv_scr_load(metrics_screen);
        } else {
            lv_scr_load(screens[current_status]);
        }

        Serial0.printf("Switched to: %s\n",
            current_status == STATUS_OFF ? "OFF" :
            current_status == STATUS_STANDBY ? "Standby" :
            current_status == STATUS_AWAKE ? "Awake (Metrics)" : "Unknown");
    }
}

void onSystemMetrics(const char* topic, const char* payload) {
    JsonDocument doc;
    if (deserializeJson(doc, payload) != DeserializationError::Ok) {
        Serial0.println("Failed to parse system-metrics JSON");
        return;
    }

    if (doc.containsKey("CpuPercent") && doc.containsKey("RamPercent")) {
        target_cpu_percent = constrain((float)doc["CpuPercent"], 0.0f, 100.0f);
        target_ram_percent = constrain((float)doc["RamPercent"], 0.0f, 100.0f);

        Serial0.printf("Metrics: CPU=%.1f%% RAM=%.1f%%\n",
            target_cpu_percent, target_ram_percent);
    }

    // Parse temperature data
    if (!doc["CpuTempCelsius"].isNull()) {
        target_cpu_temp = constrain((float)doc["CpuTempCelsius"], 0.0f, 150.0f);
        cpu_temp_received = true;
        Serial0.printf("CPU Temp: %.1f°C\n", target_cpu_temp);
    }
    if (!doc["GpuTempCelsius"].isNull()) {
        target_gpu_temp = constrain((float)doc["GpuTempCelsius"], 0.0f, 150.0f);
        gpu_temp_received = true;
        Serial0.printf("GPU Temp: %.1f°C\n", target_gpu_temp);
    }

    // Parse battery data
    if (!doc["BatteryPercent"].isNull()) {
        target_battery_percent = constrain((float)doc["BatteryPercent"], 0.0f, 100.0f);
        battery_data_received = true;
        Serial0.printf("Battery: %.0f%%\n", target_battery_percent);
    }
    if (!doc["BatteryCharging"].isNull()) {
        battery_charging = (bool)doc["BatteryCharging"];
        Serial0.printf("Charging: %s\n", battery_charging ? "Yes" : "No");
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

    // AWAKE screen replaced by metrics screen
    // Serial0.println("Creating AWAKE screen...");
    // screens[STATUS_AWAKE] = create_status_screen(STATUS_AWAKE);
    // Serial0.printf("AWAKE screen created: %p\n", screens[STATUS_AWAKE]);

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
  mqtt.subscribe("system-metrics", onSystemMetrics);
  Serial0.println("MQTT connected and subscribed!");

  // Setup status screens (OFF, STANDBY, UNKNOWN)
  setup_status_screens();

  // Create metrics screen (used for AWAKE status)
  Serial0.println("Creating metrics screen...");
  metrics_screen = create_metrics_screen();

  // Start with UNKNOWN screen
  current_status = STATUS_UNKNOWN;
  lv_scr_load(screens[current_status]);

  // Start animation timer (33ms = ~30 FPS)
  lv_timer_create(update_metrics_animation, 33, NULL);

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
