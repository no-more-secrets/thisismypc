/*---------------------------------------------------------*\
| RoyalKludgeControllerDetect.cpp                          |
|                                                          |
|   Detector for Royal Kludge RGB keyboards                |
|                                                          |
|   Samuel Boland                              04 Sep 2026 |
|                                                          |
|   This file is part of the OpenRGB project               |
|   SPDX-License-Identifier: GPL-2.0-or-later              |
\*---------------------------------------------------------*/

#include <cwchar>
#include "DetectionManager.h"
#include "LogManager.h"
#include "RGBController_RoyalKludge.h"
#include "RoyalKludgeController.h"

#define ROYAL_KLUDGE_VID               0x258A
#define ROYAL_KLUDGE_R98_PRO_PID       0x020C
#define HID_DESCRIPTOR_MAX_SIZE        4096

static unsigned int ReadHIDItemValue(const unsigned char* data, unsigned int size)
{
    unsigned int value = 0;

    for(unsigned int byte_idx = 0; byte_idx < size; byte_idx++)
    {
        value |= ((unsigned int)data[byte_idx]) << (byte_idx * 8);
    }

    return(value);
}

static bool HasRoyalKludgeRGBReport(hid_device* dev)
{
    unsigned char descriptor[HID_DESCRIPTOR_MAX_SIZE];
    int descriptor_size = hid_get_report_descriptor(dev, descriptor, sizeof(descriptor));

    if(descriptor_size <= 0)
    {
        return(false);
    }

    unsigned int report_id    = 0;
    unsigned int report_size  = 0;
    unsigned int report_count = 0;

    for(unsigned int offset = 0; offset < (unsigned int)descriptor_size;)
    {
        unsigned char prefix = descriptor[offset++];

        if(prefix == 0xFE)
        {
            if(offset + 2 > (unsigned int)descriptor_size)
            {
                return(false);
            }

            unsigned int long_size = descriptor[offset];
            offset += 2 + long_size;
            continue;
        }

        unsigned int item_size = prefix & 0x03;
        item_size = (item_size == 3) ? 4 : item_size;

        if(offset + item_size > (unsigned int)descriptor_size)
        {
            return(false);
        }

        unsigned int item_type = (prefix >> 2) & 0x03;
        unsigned int item_tag  = (prefix >> 4) & 0x0F;
        unsigned int value     = ReadHIDItemValue(&descriptor[offset], item_size);

        if(item_type == 1)
        {
            if(item_tag == 7)
            {
                report_size = value;
            }
            else if(item_tag == 8)
            {
                report_id = value;
            }
            else if(item_tag == 9)
            {
                report_count = value;
            }
        }
        else if(item_type == 0 && item_tag == 11)
        {
            if(report_id == ROYAL_KLUDGE_REPORT_ID && report_size == 8 && report_count == ROYAL_KLUDGE_REPORT_SIZE - 1)
            {
                return(true);
            }
        }

        offset += item_size;
    }

    return(false);
}

DetectedControllers DetectRoyalKludgeControllers(hid_device_info* info, const std::string& name)
{
    DetectedControllers detected_controllers;

    if(info->product_string == nullptr || std::wcscmp(info->product_string, L"R98Pro") != 0)
    {
        LOG_WARNING("[Royal Kludge] Ignoring 258A:020C with unexpected product string");
        return(detected_controllers);
    }

    hid_device* dev = hid_open_path(info->path);

    if(dev)
    {
        if(HasRoyalKludgeRGBReport(dev))
        {
            RoyalKludgeController*     controller     = new RoyalKludgeController(dev, *info, name);
            RGBController_RoyalKludge* rgb_controller = new RGBController_RoyalKludge(controller);

            detected_controllers.push_back(rgb_controller);
        }
        else
        {
            hid_close(dev);
        }
    }

    return(detected_controllers);
}

/*---------------------------------------------------------*\
| Do not filter on usage here. Windows enumerates each top  |
| level collection, while Linux may expose one hidraw node  |
| for the entire interface. The report descriptor check     |
| above selects the RGB collection on both representations. |
\*---------------------------------------------------------*/
REGISTER_HID_DETECTOR("Royal Kludge R98 Pro", DetectRoyalKludgeControllers, ROYAL_KLUDGE_VID, ROYAL_KLUDGE_R98_PRO_PID);
