/*---------------------------------------------------------*\
| RoyalKludgeController.h                                  |
|                                                          |
|   Driver for Royal Kludge RGB keyboards                  |
|                                                          |
|   Protocol based on rk-m75 by Lightning-13               |
|                                                          |
|   Samuel Boland                              04 Sep 2026 |
|                                                          |
|   This file is part of the OpenRGB project               |
|   SPDX-License-Identifier: GPL-2.0-or-later              |
\*---------------------------------------------------------*/

#pragma once

#include <chrono>
#include <hidapi.h>
#include <mutex>
#include <string>
#include "RGBController.h"

#define ROYAL_KLUDGE_REPORT_ID          0x09
#define ROYAL_KLUDGE_REPORT_SIZE        520
#define ROYAL_KLUDGE_RGB_LED_COUNT      126

class RoyalKludgeController
{
public:
    RoyalKludgeController(hid_device* dev_handle, const hid_device_info& info, const std::string& dev_name);
    ~RoyalKludgeController();

    std::string GetDeviceLocation();
    std::string GetName();
    std::string GetSerial();

    void SetLEDs(RGBColor* colors);
    void SendKeepalive();

private:
    bool SendReport();

    hid_device*                                        dev;
    std::string                                        location;
    std::string                                        name;
    std::string                                        serial;
    unsigned char                                      report[ROYAL_KLUDGE_REPORT_SIZE];
    bool                                               report_initialized;
    std::mutex                                         report_mutex;
    std::chrono::time_point<std::chrono::steady_clock> last_send_time;
};
