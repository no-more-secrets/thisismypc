# Controllers pending upstream

Controllers written here and submitted to OpenRGB, carried in the engine until
the upstream merge lands in the pinned submodule. Each folder is a verbatim copy
of the merge request's files (GPL-2.0-or-later, taken under this repo's GPLv3),
compiled beside `third-party/OpenRGB/Controllers/**` by the engine project.

| Folder | Upstream merge request | Head commit | Remove when |
| --- | --- | --- | --- |
| `RoyalKludgeController/` | [OpenRGB !3568, Add Royal Kludge R98 Pro RGB support](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3568), by Sam | `28011446` | The submodule pin includes the merge; a duplicate detector registration is the sign it landed. |

Keep the files identical to the merge request so a later upstream diff is
trivial; fix things upstream first, then refresh the copy.
