#include "sqlite3.h"

__declspec(dllexport) int sqlite3_key(sqlite3* db, const void* key, int keyBytes) {
    (void)db;
    (void)key;
    (void)keyBytes;
    return SQLITE_ERROR;
}

__declspec(dllexport) int sqlite3_key_v2(
    sqlite3* db,
    const char* databaseName,
    const void* key,
    int keyBytes) {
    (void)db;
    (void)databaseName;
    (void)key;
    (void)keyBytes;
    return SQLITE_ERROR;
}

__declspec(dllexport) int sqlite3_rekey(sqlite3* db, const void* key, int keyBytes) {
    (void)db;
    (void)key;
    (void)keyBytes;
    return SQLITE_ERROR;
}

__declspec(dllexport) int sqlite3_rekey_v2(
    sqlite3* db,
    const char* databaseName,
    const void* key,
    int keyBytes) {
    (void)db;
    (void)databaseName;
    (void)key;
    (void)keyBytes;
    return SQLITE_ERROR;
}
