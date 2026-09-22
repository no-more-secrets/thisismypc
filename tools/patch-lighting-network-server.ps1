param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Output
)

$ErrorActionPreference = 'Stop'
$expected = '7B393739810518D1E18AD9BE85DFD67B2A3F549B8201449431B894BF417E604A'
$text = [System.IO.File]::ReadAllText($Source).Replace("`r`n", "`n")
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $actual = [System.BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
}
finally {
    $sha.Dispose()
}
if ($actual -ne $expected) {
    throw 'The pinned OpenRGB NetworkServer.cpp changed. Review and update the SDK authentication patch.'
}
$declaration = @(
    'static std::string ThisIsMyPC_sdk_token;'
    'void ThisIsMyPC_SetSdkToken(const std::string& token)'
    '{'
    '    ThisIsMyPC_sdk_token = token;'
    '}'
) -join "`n"
$anchor = 'NetworkClientInfo::NetworkClientInfo()'
if ($text.Split(@($anchor), [System.StringSplitOptions]::None).Length -ne 2) {
    throw 'OpenRGB client constructor anchor changed.'
}
$text = $text.Replace($anchor, $declaration + "`n`n" + $anchor)

$guard = @(
    '        // Authenticate before adding the socket to ServerClients or starting'
    '        // its SDK listener. Untrusted peers receive no device data or controls.'
    '        if(ThisIsMyPC_sdk_token.size() != 64)'
    '        {'
    '            delete client_info;'
    '            continue;'
    '        }'
    '        char supplied[64] = {};'
    '        size_t received = 0;'
    '        ULONGLONG deadline = GetTickCount64() + 3000;'
    '        while(received < sizeof(supplied))'
    '        {'
    '            ULONGLONG now = GetTickCount64();'
    '            if(now >= deadline) break;'
    '            DWORD remaining = (DWORD)(deadline - now);'
    '            if(setsockopt(client_info->client_sock, SOL_SOCKET, SO_RCVTIMEO,'
    '                          (const char*)&remaining, sizeof(remaining)) != 0) break;'
    '            int count = recv(client_info->client_sock, supplied + received,'
    '                             (int)(sizeof(supplied) - received), 0);'
    '            if(count <= 0) break;'
    '            received += (size_t)count;'
    '        }'
    '        unsigned char mismatch = (unsigned char)(received != sizeof(supplied));'
    '        for(size_t i = 0; i < sizeof(supplied); i++)'
    '            mismatch |= (unsigned char)(supplied[i] ^ ThisIsMyPC_sdk_token[i]);'
    '        if(mismatch != 0)'
    '        {'
    '            delete client_info;'
    '            continue;'
    '        }'
    '        DWORD credential_timeout = 0;'
    '        if(setsockopt(client_info->client_sock, SOL_SOCKET, SO_RCVTIMEO,'
    '                      (const char*)&credential_timeout, sizeof(credential_timeout)) != 0 ||'
    '           send(client_info->client_sock, "\x01", 1, 0) != 1)'
    '        {'
    '            delete client_info;'
    '            continue;'
    '        }'
    ''
) -join "`n"
$anchor = '        u_long arg = 0;'
if ($text.Split(@($anchor), [System.StringSplitOptions]::None).Length -ne 2) {
    throw 'OpenRGB accepted-socket anchor changed.'
}
$text = $text.Replace($anchor, $guard + $anchor)
$directory = [System.IO.Path]::GetDirectoryName($Output)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
if (-not [System.IO.File]::Exists($Output) -or
    [System.IO.File]::ReadAllText($Output) -cne $text) {
    [System.IO.File]::WriteAllText($Output, $text, (New-Object System.Text.UTF8Encoding($false)))
}
