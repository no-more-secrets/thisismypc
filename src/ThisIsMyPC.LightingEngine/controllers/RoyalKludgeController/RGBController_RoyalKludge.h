/*---------------------------------------------------------*\
| RGBController_RoyalKludge.h                              |
|                                                          |
|   RGBController for Royal Kludge RGB keyboards           |
|                                                          |
|   Samuel Boland                              04 Sep 2026 |
|                                                          |
|   This file is part of the OpenRGB project               |
|   SPDX-License-Identifier: GPL-2.0-or-later              |
\*---------------------------------------------------------*/

#pragma once

#include <atomic>
#include <thread>
#include "RGBController.h"
#include "RoyalKludgeController.h"

class RGBController_RoyalKludge : public RGBController
{
public:
    RGBController_RoyalKludge(RoyalKludgeController* controller_ptr);
    ~RGBController_RoyalKludge();

    void SetupZones();

    void DeviceUpdateLEDs();
    void DeviceUpdateZoneLEDs(int zone);
    void DeviceUpdateSingleLED(int led);
    void DeviceUpdateMode();

    void KeepaliveThread();

private:
    RoyalKludgeController* controller;
    std::thread*            keepalive_thread;
    std::atomic<bool>       keepalive_thread_run;
};
