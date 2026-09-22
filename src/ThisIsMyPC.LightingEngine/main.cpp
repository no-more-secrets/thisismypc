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
|     stdin   "auth <token>"  first line, before detection |
|             "rescan"        detect devices again          |
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
#include "DetectionManager.h"
#include "LogManager.h"
#include "NetworkServer.h"

void ThisIsMyPC_SetSdkToken(const std::string& token);
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

/*---------------------------------------------------------*| The order OpenRGB's own service uses on stop.             |
\*---------------------------------------------------------*/
static void Shutdown()
{
    ResourceManager::get()->ServiceShutdown();
    DetectionManager::get()->Cleanup();
}

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

    // The host sends this secret through the child stdin pipe. It never appears
    // on the command line, stdout, or in the OpenRGB log.
    char credential[80] = {};
    if(!fgets(credential, sizeof(credential), stdin) ||
       strncmp(credential, "auth ", 5) != 0 ||
       strlen(credential) != 70 ||
       credential[69] != '\n')
    {
        Announce("error missing private SDK credential");
        return EXIT_FAILURE;
    }
    std::string token(credential + 5, 64);
    if(token.find_first_not_of("0123456789ABCDEF") != std::string::npos)
    {
        Announce("error invalid private SDK credential");
        return EXIT_FAILURE;
    }
    ThisIsMyPC_SetSdkToken(token);

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

    /*-----------------------------------------------------*    | OpenRGB 1.0 creates the server inside Initialize from  |
    | the --server-host and --server-port defaults the      |
    | parser recorded; the host passes both.                |
    \*-----------------------------------------------------*/
    ResourceManager::get()->Initialize(
        false,
        !(flags & RET_FLAG_NO_DETECT),
        true,
        (flags & RET_FLAG_CLI_POST_DETECTION) != 0,
        false);
    ResourceManager::get()->WaitForInitialization();
    ResourceManager::get()->WaitForDetection();

    NetworkServer* server = ResourceManager::get()->GetServer();
    if(!server || !server->GetOnline())
    {
        Announce("error SDK server did not come online");
        Shutdown();
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
                ResourceManager::get()->WaitForDetection();
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
    Shutdown();
    /*-----------------------------------------------------*\
    | The reader may still block in fgets when a console     |
    | event stopped us; the process ends without joining it. |
    \*-----------------------------------------------------*/
    reader.detach();
    fflush(stdout);
    ExitProcess(EXIT_SUCCESS);
}
