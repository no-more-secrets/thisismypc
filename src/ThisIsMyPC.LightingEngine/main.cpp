/*---------------------------------------------------------*\
| ThisIsMyPC lighting engine                                |
|                                                           |
|   Headless host for OpenRGB's device core: detects every  |
|   device OpenRGB supports and serves them over the SDK    |
|   socket on loopback. No GUI, no plugins, no Qt.          |
|                                                           |
|   Protocol with the host process:                         |
|     stdout  "ready <port>"  once the server listens       |
|             "error <text>"  when it cannot start          |
|     stdin   "rescan"        detect devices again          |
|             "stop" or EOF   clean shutdown                |
|                                                           |
|   Command line: OpenRGB's own options apply; the host     |
|   passes --config <dir> --server-port <n> --loglevel <n>. |
|   The server is always started and never auto-connects.   |
|                                                           |
|   SPDX-License-Identifier: GPL-3.0-only                   |
|   Built on OpenRGB (GPL-2.0-or-later).                    |
\*---------------------------------------------------------*/

#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>
#include <chrono>

#include "cli.h"
#include "LogManager.h"
#include "NetworkServer.h"
#include "ResourceManager.h"

using namespace std::chrono_literals;

/*---------------------------------------------------------*\
| Same one-millisecond timer resolution OpenRGB's Windows    |
| entry point requests: effect timing depends on it.        |
\*---------------------------------------------------------*/
static void RaiseTimerResolution()
{
    typedef LONG (NTAPI *NtSetTimerResolutionFn)(ULONG desired, BOOLEAN set, PULONG current);

    HMODULE ntdll = GetModuleHandleA("ntdll.dll");
    if(ntdll)
    {
        NtSetTimerResolutionFn setResolution = (NtSetTimerResolutionFn)GetProcAddress(ntdll, "NtSetTimerResolution");
        if(setResolution)
        {
            ULONG current = 0;
            setResolution(10000, TRUE, &current);
        }
    }

    /*-----------------------------------------------------*\
    | Keep the resolution while the window (none) is hidden: |
    | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 4.  |
    \*-----------------------------------------------------*/
    PROCESS_POWER_THROTTLING_STATE throttling { PROCESS_POWER_THROTTLING_CURRENT_VERSION, 4, 0 };
    SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &throttling, sizeof(throttling));
}

static volatile bool stop_requested = false;

static BOOL WINAPI ConsoleHandler(DWORD)
{
    stop_requested = true;
    return TRUE;
}

static void Announce(const char* line)
{
    fputs(line, stdout);
    fputc('\n', stdout);
    fflush(stdout);
}

int main(int argc, char* argv[])
{
    SetConsoleCtrlHandler(ConsoleHandler, TRUE);
    RaiseTimerResolution();

    /*-----------------------------------------------------*\
    | OpenRGB's parser reads --config, --server-port,       |
    | --loglevel and the rest. The engine forces server mode |
    | and never becomes a client of another OpenRGB.        |
    \*-----------------------------------------------------*/
    unsigned int flags = cli_pre_detection(argc, argv);
    if(flags & RET_FLAG_PRINT_HELP)
    {
        return EXIT_FAILURE;
    }
    flags |= RET_FLAG_START_SERVER | RET_FLAG_NO_AUTO_CONNECT;
    flags &= ~(unsigned int)RET_FLAG_START_GUI;

    NetworkServer* server = ResourceManager::get()->GetServer();
    if(!server)
    {
        Announce("error no SDK server");
        return EXIT_FAILURE;
    }
    server->SetHost("127.0.0.1");

    ResourceManager::get()->Initialize(
        false,
        !(flags & RET_FLAG_NO_DETECT),
        true,
        (flags & RET_FLAG_CLI_POST_DETECTION) != 0);
    ResourceManager::get()->WaitForInitialization();
    ResourceManager::get()->WaitForDeviceDetection();

    if(!server->GetOnline())
    {
        Announce("error SDK server did not come online");
        ResourceManager::get()->Cleanup();
        return EXIT_FAILURE;
    }

    std::string ready = "ready " + std::to_string(server->GetPort());
    Announce(ready.c_str());
    LOG_INFO("[engine] Serving devices on 127.0.0.1:%u", server->GetPort());

    /*-----------------------------------------------------*\
    | Commands arrive one per line; EOF means the host is    |
    | gone. A background reader lets the loop also honor     |
    | console control events.                               |
    \*-----------------------------------------------------*/
    std::thread reader([]()
    {
        char line[128];
        while(fgets(line, sizeof(line), stdin))
        {
            if(strncmp(line, "stop", 4) == 0)
            {
                break;
            }
            if(strncmp(line, "rescan", 6) == 0)
            {
                ResourceManager::get()->RescanDevices();
                ResourceManager::get()->WaitForDeviceDetection();
                Announce("detected");
            }
        }
        stop_requested = true;
    });

    while(!stop_requested)
    {
        std::this_thread::sleep_for(100ms);
    }

    LOG_INFO("[engine] Stopping");
    ResourceManager::get()->Cleanup();
    /*-----------------------------------------------------*\
    | The reader may still block in fgets when a console     |
    | event stopped us; the process ends without joining it. |
    \*-----------------------------------------------------*/
    reader.detach();
    fflush(stdout);
    ExitProcess(EXIT_SUCCESS);
}
