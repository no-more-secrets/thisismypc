// Forced include for every OpenRGB translation unit in the lighting engine.
// OpenRGB.pro derives these from git at build time; the engine pins them to the
// submodule commit so two builds of one tag produce one binary.
#pragma once

#ifndef VERSION_STRING
#define VERSION_STRING "0.9+ (1.0rc3.1) ThisIsMyPC engine"
#endif
#ifndef BUILDDATE_STRING
#define BUILDDATE_STRING "pinned"
#endif
#ifndef GIT_COMMIT_ID
#define GIT_COMMIT_ID "5e81e26fcc65d3dacfb76b0a30ec0142ec7bb131"
#endif
#ifndef GIT_COMMIT_DATE
#define GIT_COMMIT_DATE "2026-08-23"
#endif
#ifndef GIT_BRANCH
#define GIT_BRANCH "release_candidate_1.0rc3.1"
#endif
