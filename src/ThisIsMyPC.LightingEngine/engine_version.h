// Forced include for every OpenRGB translation unit in the lighting engine.
// OpenRGB.pro derives these from git at build time; the engine pins them to the
// submodule commit so two builds of one tag produce one binary.
#pragma once

#ifndef VERSION_STRING
#define VERSION_STRING "1.0 ThisIsMyPC engine"
#endif
#ifndef BUILDDATE_STRING
#define BUILDDATE_STRING "pinned"
#endif
#ifndef GIT_COMMIT_ID
#define GIT_COMMIT_ID "81bbe18a84c2e507006f19dd252e397e40a56bfe"
#endif
#ifndef GIT_COMMIT_DATE
#define GIT_COMMIT_DATE "2026-09-11"
#endif
#ifndef GIT_BRANCH
#define GIT_BRANCH "release_1.0"
#endif
