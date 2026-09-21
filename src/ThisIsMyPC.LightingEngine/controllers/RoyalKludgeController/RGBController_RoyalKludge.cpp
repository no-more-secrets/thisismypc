/*---------------------------------------------------------*\
| RGBController_RoyalKludge.cpp                            |
|                                                          |
|   RGBController for Royal Kludge RGB keyboards           |
|                                                          |
|   Samuel Boland                              04 Sep 2026 |
|                                                          |
|   This file is part of the OpenRGB project               |
|   SPDX-License-Identifier: GPL-2.0-or-later              |
\*---------------------------------------------------------*/

#include <chrono>
#include "RGBControllerKeyNames.h"
#include "RGBController_RoyalKludge.h"

using namespace std::chrono_literals;

#define NA 0xFFFFFFFF

static const unsigned int r98_pro_led_map[6][19] =
{
    {   0,   6,  12,  18,  24,  30,  36,  42,  48,  54,  60,  66,  72,  78,  NA,  90,  96, 102, 108 },
    {   1,   7,  13,  19,  25,  31,  37,  43,  49,  55,  61,  67,  73,  79,  NA,  91,  97, 103, 109 },
    {   2,   8,  14,  20,  26,  32,  38,  44,  50,  56,  62,  68,  74,  80,  NA,  92,  98, 104, 110 },
    {   3,   9,  15,  21,  27,  33,  39,  45,  51,  57,  63,  69,  NA,  81,  NA,  93,  99, 105,  NA  },
    {   4,  NA,  16,  22,  28,  34,  40,  46,  52,  58,  64,  70,  NA,  82,  88,  94, 100, 106, 112 },
    {   5,  11,  17,  NA,  NA,  35,  NA,  NA,  53,  59,  NA,  NA,  NA,  83,  89,  95, 101, 107,  NA  }
};

static const char* r98_pro_led_names[6][19] =
{
    {
        KEY_EN_ESCAPE, KEY_EN_F1, KEY_EN_F2, KEY_EN_F3, KEY_EN_F4, KEY_EN_F5, KEY_EN_F6,
        KEY_EN_F7, KEY_EN_F8, KEY_EN_F9, KEY_EN_F10, KEY_EN_F11, KEY_EN_F12, KEY_EN_DELETE,
        KEY_EN_UNUSED, KEY_EN_PRINT_SCREEN, KEY_EN_PAGE_UP, KEY_EN_PAGE_DOWN, KEY_EN_END
    },
    {
        KEY_EN_BACK_TICK, KEY_EN_1, KEY_EN_2, KEY_EN_3, KEY_EN_4, KEY_EN_5, KEY_EN_6,
        KEY_EN_7, KEY_EN_8, KEY_EN_9, KEY_EN_0, KEY_EN_MINUS, KEY_EN_EQUALS, KEY_EN_BACKSPACE,
        KEY_EN_UNUSED, KEY_EN_NUMPAD_LOCK, KEY_EN_NUMPAD_DIVIDE, KEY_EN_NUMPAD_TIMES, KEY_EN_NUMPAD_MINUS
    },
    {
        KEY_EN_TAB, KEY_EN_Q, KEY_EN_W, KEY_EN_E, KEY_EN_R, KEY_EN_T, KEY_EN_Y, KEY_EN_U,
        KEY_EN_I, KEY_EN_O, KEY_EN_P, KEY_EN_LEFT_BRACKET, KEY_EN_RIGHT_BRACKET, KEY_EN_ANSI_BACK_SLASH,
        KEY_EN_UNUSED, KEY_EN_NUMPAD_7, KEY_EN_NUMPAD_8, KEY_EN_NUMPAD_9, KEY_EN_NUMPAD_PLUS
    },
    {
        KEY_EN_CAPS_LOCK, KEY_EN_A, KEY_EN_S, KEY_EN_D, KEY_EN_F, KEY_EN_G, KEY_EN_H, KEY_EN_J,
        KEY_EN_K, KEY_EN_L, KEY_EN_SEMICOLON, KEY_EN_QUOTE, KEY_EN_UNUSED, KEY_EN_ANSI_ENTER,
        KEY_EN_UNUSED, KEY_EN_NUMPAD_4, KEY_EN_NUMPAD_5, KEY_EN_NUMPAD_6, KEY_EN_UNUSED
    },
    {
        KEY_EN_LEFT_SHIFT, KEY_EN_UNUSED, KEY_EN_Z, KEY_EN_X, KEY_EN_C, KEY_EN_V, KEY_EN_B, KEY_EN_N,
        KEY_EN_M, KEY_EN_COMMA, KEY_EN_PERIOD, KEY_EN_FORWARD_SLASH, KEY_EN_UNUSED, KEY_EN_RIGHT_SHIFT,
        KEY_EN_UP_ARROW, KEY_EN_NUMPAD_1, KEY_EN_NUMPAD_2, KEY_EN_NUMPAD_3, KEY_EN_NUMPAD_ENTER
    },
    {
        KEY_EN_LEFT_CONTROL, KEY_EN_LEFT_WINDOWS, KEY_EN_LEFT_ALT, KEY_EN_UNUSED, KEY_EN_UNUSED, KEY_EN_SPACE,
        KEY_EN_UNUSED, KEY_EN_UNUSED, KEY_EN_RIGHT_ALT, KEY_EN_RIGHT_FUNCTION, KEY_EN_UNUSED, KEY_EN_UNUSED,
        KEY_EN_UNUSED, KEY_EN_LEFT_ARROW, KEY_EN_DOWN_ARROW, KEY_EN_RIGHT_ARROW, KEY_EN_NUMPAD_0,
        KEY_EN_NUMPAD_PERIOD, KEY_EN_UNUSED
    }
};

/**------------------------------------------------------------------*\
    @name Royal Kludge R98 Pro
    @category Keyboard
    @type USB
    @save :x:
    @direct :white_check_mark:
    @effects :x:
    @detectors DetectRoyalKludgeControllers
    @comment
\*-------------------------------------------------------------------*/

RGBController_RoyalKludge::RGBController_RoyalKludge(RoyalKludgeController* controller_ptr)
{
    controller              = controller_ptr;
    name                    = controller->GetName();
    vendor                  = "Royal Kludge";
    type                    = DEVICE_TYPE_KEYBOARD;
    description             = "Royal Kludge RGB Keyboard";
    location                = controller->GetDeviceLocation();
    serial                  = controller->GetSerial();

    mode Direct;
    Direct.name             = "Direct";
    Direct.value            = 0xFFFF;
    Direct.flags            = MODE_FLAG_HAS_PER_LED_COLOR;
    Direct.color_mode       = MODE_COLORS_PER_LED;
    modes.push_back(Direct);

    SetupZones();

    keepalive_thread_run = true;
    keepalive_thread     = new std::thread(&RGBController_RoyalKludge::KeepaliveThread, this);
}

RGBController_RoyalKludge::~RGBController_RoyalKludge()
{
    Shutdown();

    keepalive_thread_run = false;
    keepalive_thread->join();
    delete keepalive_thread;

    delete controller;
}

void RGBController_RoyalKludge::SetupZones()
{
    unsigned int matrix_map[6][19];
    unsigned int total_led_count = 0;

    for(unsigned int row = 0; row < 6; row++)
    {
        for(unsigned int col = 0; col < 19; col++)
        {
            if(r98_pro_led_map[row][col] == NA)
            {
                matrix_map[row][col] = NA;
                continue;
            }

            led new_led;
            new_led.name  = r98_pro_led_names[row][col];
            new_led.value = r98_pro_led_map[row][col];
            leds.push_back(new_led);

            matrix_map[row][col] = total_led_count++;
        }
    }

    zone new_zone;
    new_zone.name       = ZONE_EN_KEYBOARD;
    new_zone.type       = ZONE_TYPE_MATRIX;
    new_zone.leds_min   = total_led_count;
    new_zone.leds_max   = total_led_count;
    new_zone.leds_count = total_led_count;
    new_zone.matrix_map.Set(6, 19, (unsigned int*)matrix_map);
    zones.push_back(new_zone);

    SetupColors();
}

void RGBController_RoyalKludge::DeviceUpdateLEDs()
{
    RGBColor framebuffer[ROYAL_KLUDGE_RGB_LED_COUNT] = { 0 };

    for(unsigned int led_idx = 0; led_idx < leds.size(); led_idx++)
    {
        framebuffer[leds[led_idx].value] = colors[led_idx];
    }

    controller->SetLEDs(framebuffer);
}

void RGBController_RoyalKludge::DeviceUpdateZoneLEDs(int /*zone*/)
{
    DeviceUpdateLEDs();
}

void RGBController_RoyalKludge::DeviceUpdateSingleLED(int /*led*/)
{
    DeviceUpdateLEDs();
}

void RGBController_RoyalKludge::DeviceUpdateMode()
{
    DeviceUpdateLEDs();
}

void RGBController_RoyalKludge::KeepaliveThread()
{
    while(keepalive_thread_run.load())
    {
        controller->SendKeepalive();
        std::this_thread::sleep_for(100ms);
    }
}
