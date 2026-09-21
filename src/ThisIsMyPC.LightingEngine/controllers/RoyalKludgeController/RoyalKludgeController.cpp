/*---------------------------------------------------------*\
| RoyalKludgeController.cpp                                |
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

#include <cstring>
#include "LogManager.h"
#include "RoyalKludgeController.h"
#include "StringUtils.h"

RoyalKludgeController::RoyalKludgeController(hid_device* dev_handle, const hid_device_info& info, const std::string& dev_name)
{
    dev                 = dev_handle;
    location            = info.path;
    name                = dev_name;
    report_initialized  = false;

    if(info.serial_number != nullptr)
    {
        serial = StringUtils::wstring_to_string(info.serial_number);
    }

    memset(report, 0x00, sizeof(report));

    /*-----------------------------------------------------*\
    | Report 0x09 contains an eight-byte header followed by |
    | 126 RGB entries and 134 bytes of padding              |
    \*-----------------------------------------------------*/
    report[0] = ROYAL_KLUDGE_REPORT_ID;
    report[1] = 0x08;
    report[4] = 0x01;
    report[6] = 0x7A;
    report[7] = 0x01;
}

RoyalKludgeController::~RoyalKludgeController()
{
    hid_close(dev);
}

std::string RoyalKludgeController::GetDeviceLocation()
{
    return("HID: " + location);
}

std::string RoyalKludgeController::GetName()
{
    return(name);
}

std::string RoyalKludgeController::GetSerial()
{
    return(serial);
}

void RoyalKludgeController::SetLEDs(RGBColor* colors)
{
    std::lock_guard<std::mutex> lock(report_mutex);

    for(unsigned int led_idx = 0; led_idx < ROYAL_KLUDGE_RGB_LED_COUNT; led_idx++)
    {
        unsigned int offset = 8 + (led_idx * 3);

        /*-------------------------------------------------*\
        | The R98 Pro framebuffer uses GRB byte order       |
        \*-------------------------------------------------*/
        report[offset]     = RGBGetGValue(colors[led_idx]);
        report[offset + 1] = RGBGetRValue(colors[led_idx]);
        report[offset + 2] = RGBGetBValue(colors[led_idx]);
    }

    report_initialized = true;
    SendReport();
}

void RoyalKludgeController::SendKeepalive()
{
    std::lock_guard<std::mutex> lock(report_mutex);

    if(report_initialized && (std::chrono::steady_clock::now() - last_send_time) >= std::chrono::milliseconds(100))
    {
        SendReport();
    }
}

bool RoyalKludgeController::SendReport()
{
    int result = hid_send_feature_report(dev, report, sizeof(report));

    last_send_time = std::chrono::steady_clock::now();

    if(result != sizeof(report))
    {
        LOG_ERROR("[Royal Kludge] Failed to send RGB report: got %d, expected %d", result, (int)sizeof(report));
        return(false);
    }

    return(true);
}
