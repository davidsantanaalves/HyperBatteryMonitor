# Hyper Battery Monitor v2.3.1 — auto94 provenance audit

Audit date: 2026-10-06. This concerns **2.3.1 final**; Beta 1 is historical.

## Conclusion and change gate

**Classification B: current implementation independent of auto94, with historical use.**
No relevant copied or adapted implementation from auto94's project was identified
in the audited current source. Historical interface-selection heuristics show
adaptation, but their distinctive logic was removed before this audit. Shared
command bytes, identifiers and battery offsets remain; independent NGENUITY
evidence confirms those protocol facts. This finding permits removing the visible
acknowledgement as a description of the current implementation, while preserving
the historical reference and original MIT notice conservatively.

The conclusion, historical finding, protocol evidence, runtime/build dependencies
and notice decision were communicated before any project files were modified.
No device, HID or monitoring implementation was changed for this audit.

This is a technical provenance assessment. It does not establish clean-room
development or make an absolute legal determination about copyright obligations.
Independent confirmation of facts alone would not erase derivation of code.

## Sources and reproducibility

- Source of truth: the working tree in `Source Code`, initially clean.
- Branch: `develop/v2.3.1`.
- HEAD: `483a7a8c4d4ca11da872165374b667653b9c17af`.
- `.csproj` version: `2.3.1`; displayed product: Hyper Battery Monitor.
- `../GPT Context/PROJECT_CONTEXT.md` and `../GPT Context/Research.md` were read
  completely before analysis. They reside outside this Git repository.
- The context's `234f71b` source snapshot is historical relative to this HEAD.
- Upstream repository: [auto94/HyperX-Cloud-2-Battery-Monitor](https://github.com/auto94/HyperX-Cloud-2-Battery-Monitor).
- Upstream snapshot: `634e0451147a0e7e9075a40abc1641950e2436cc`, downloaded with
  its Git history into `/tmp/hbm-auto94-provenance`. No upstream code was executed.
- Main upstream comparison: [MainForm.cpp](https://github.com/auto94/HyperX-Cloud-2-Battery-Monitor/blob/634e0451147a0e7e9075a40abc1641950e2436cc/Cloud2BatteryMonitorUI/MainForm.cpp),
  `MainForm.h`, `SettingsHelper.cpp/.h`, UI and project/dependency files.
- `git log`, `git show`, `git diff` (including whitespace-insensitive diffs) and
  `git blame` were used. No history was rewritten, committed or pushed.

The actual consolidated `Research.md` SHA-256 is
`8ad6dc1b1b606695d19b84501eb353ce949fd289087730f0c8d14bf1f0f0bb07`.
It differs from the hash recorded in the context; the actual readable document
was used and preserved unchanged. This audit does not resolve that older hash.

The following preserved research files were inspected and their SHA-256 values
match `Research.md` Section 14:

| Artifact under `../Research/` | SHA-256 |
|---|---|
| `NGENUITY-5.38.0-Discovery.md` | `c01db569b7b924ee0b8ec18d06844353eef7be387b6c9446540ce535b22f01db` |
| `NGENUITY-5.38.0-Command-Decompilation.md` | `4df63f4673b5077833320682a886fff148d3dc8a4b822198823c87c4eb5e7099` |
| `NGENUITY-5.38.0-Response-Handlers.md` | `a13ce63f377e096ce939cc5e92613956148bf2012195f20a2c72c87bdd145d47` |
| `NGENUITY-Additional-Headsets-Protocol-Evidence.md` | `014810c6c58fd4e0dcb9a30e3fc9bf4065a1764c0b5c8f8220ef5fa9d9a479b5` |

These record independent analysis of NGENUITY 5.38.0.0, primarily
`NGenuity2Helper.exe` hash
`4050323a84ea3c0d7b3f6d576c24ae7bbfb4224380d0233596d20bd7f26314a9`.
The binaries were not re-decompiled during this audit. The artifacts' older HBM
implementation descriptions were treated as historical, not as current source.

## Similarities classified

### 1. Copied code in the current source

None identified. No upstream class or method was found textually reused.
As a supporting check, all non-generated HBM C# files were compared with upstream
`.cpp`, `.h` and `.hpp` files, including vendored sources: no exact nontrivial
line of at least 25 characters matched after whitespace removal. This check is
language-sensitive and cannot alone exclude a translated adaptation; the
method-level and historical analysis below is the primary evidence.

### 2. Historically adapted code, absent from the current source

The distinctive upstream `getHeadsetDevicePath()` algorithm in `MainForm.cpp`
lines 74–118 uses a Cloud III S usage-page/usage special case, then chooses the
highest usage with usage-page tie-breaking. HBM commit `52c84f5` (2026-09-16)
introduced matching behavior in `HidConnection.FindDevice`:

```csharp
capabilities.Value.Usage > bestUsage ||
(capabilities.Value.Usage == bestUsage &&
 capabilities.Value.UsagePage >= bestUsagePage)
```

It also introduced `RequiredUsagePage = 448`, `RequiredUsage = 1` for Cloud III S,
and `PreferHighestUsage = true` for Core, Alpha and Stinger 2. The combination of
special case and tie-break policy is evidence of historical adaptation, beyond
coincidental VID/PID values. The native Windows enumeration implementation was
HBM-specific, rather than a textual copy of upstream HIDAPI enumeration.

Commit `573b360` (2026-09-29) removed all these selection branches and definition
fields, replaced broad PID matching with the explicit interfaces documented by
NGENUITY, and replaced configured 52/20-byte sizes with runtime HID capabilities.
The current `FindDevicePath` selects a matching path without those heuristics.
`HIDP_CAPS.Usage` and `.UsagePage` still appear because the Windows ABI struct
requires those fields; they no longer implement or select the old heuristic.

The historical generic query in `HyperXBatteryDeviceBase.QueryBatteryAsync`
wrote a configured command, read one reply and indexed its battery byte. Together
with the 52/20 sizes and device expansion this reflects historical use of the
reference. The generic write/read idea and range check alone are not distinctive
enough to establish substantial surviving derivation. Current registered devices
use their own continuous dispatchers or the Stinger Core feature-report override.
The base's retained direct fallback uses cancellation, parser hooks and HBM
events; no upstream product-string dispatch, fixed buffers or special read survives.

### 3. Protocol facts shared with the upstream project

Upstream `getBatteryLevel()` (`MainForm.cpp`, lines 132–225) dispatches by
manufacturer/product name, builds commands into a fixed 52-byte buffer, reads
20 bytes with HIDAPI and returns a selected byte. Current HBM uses definitions,
runtime lengths, validated device parsers and coordinated asynchronous requests.

| Current HBM file | Shared identity / battery facts | Independent evidence |
|---|---|---|
| `Devices/Cloud3WirelessDevice.cs` | `03F0:05B7`; `66 89`; battery `[4]` | `Research.md` §5.1; command artifact §1; response artifact Cloud III section |
| `Devices/Cloud2CoreWirelessDevice.cs` | `03F0:0995`; `66 89`; battery `[4]` | §5.4; command artifact §8; response artifact Core section |
| `Devices/Cloud3SWirelessDevice.cs` | `03F0:06BE`; `0C 02 03 01 00 06`; battery `[6]`; invalid `FF` | §5.2; command artifact §4; response artifact III S response gate |
| `Devices/CloudAlphaWirelessDevice.cs` | `03F0:098D`; `21 BB 0B`; battery `[3]` | §5.5; command artifact §12; response artifact Alpha section |
| `Devices/CloudStinger2WirelessDevice.cs` | `03F0:0D93`; `06 FF BB 02`; battery `[7]` | §5.6; command artifact §16; response artifact Stinger 2 section |
| `Devices/CloudFlightSDevice.cs` | Its 16-byte battery prefix also coincides with upstream's Kingston Cloud II command | §6.1 independently confirms that prefix for Flight S, its endpoint and response signature |

An identical command or offset is needed to speak the same hardware protocol;
it is not, by itself, evidence of copied implementation. The common 0–100
validation is ordinary percent validation. HBM rejects malformed/out-of-range
reports instead of mapping upstream's error/off result to zero; zero can remain
a valid battery value in HBM.

The following current interface selectors are independently documented in
`Research.md` Sections 3 and 5: III `MI_03&Col01`, Core `MI_03&Col02`, III S
`MI_03&Col05`, Alpha `MI_03&Col01`, Stinger 2 `MI_03&Col03`. Upstream selects
these devices through usage heuristics and names rather than these HBM patterns.

### 4. Current independent implementations relative to auto94

- `Cloud3WirelessDevice`: `ReaderLoopAsync` / `ProcessInputReport` and pending
  `TaskCompletionSource` requests. The continuous reader already existed at
  initial HBM commit `0a33ea3`; it is not upstream's per-refresh open/read/close.
- Core, III S and Alpha: `TryParseBatteryReport`, `TryParseChargeStatusReport`,
  `TryParseMicrophoneMuteReport` were added in `7840b51` (2026-09-28), validating
  device-specific response and notification selectors through the HBM base reader.
- Stinger 2: its protocol parser/continuous reader and charging mapping were
  added in `573b360`, replacing the generic path. Mute remains unsupported.
- `HyperXBatteryDeviceBase`: asynchronous request completion, state events,
  reader cancellation and optional report matcher; later extensions support
  compound state replies. No upstream equivalent architecture was identified.
- `HidConnection`: `SetupDi*` enumeration, `CreateFile`/`SafeFileHandle`, separate
  streams, runtime `HidP_GetCaps` lengths, and feature-report calls. Windows ABI
  signatures/constants are platform facts. No HIDAPI or upstream wrapper is used.
- `HyperXDeviceManager`, `HyperXDeviceRegistration`, `HyperXDeviceDefinition`,
  `IHyperXDevice`: registry/factory/capability model, name normalization, explicit
  interface matching, responsive probing and TIO guidance. No upstream equivalent
  class, parallel VID/PID-table algorithm, product-name fallback or highest-usage
  selection remains.
- Flight S, Flight Wireless, Stinger Core, Flight 2 and Mix 2 were introduced in
  `9b8403d` (2026-09-30); upstream has no implementation of those models.
  `Research.md` §§6.1–6.4 and 6.7 records their command/response/status evidence.
  Flight's voltage lookup, Stinger Core's voltage knots/feature exchange and
  Flight 2's ACK/routing behavior have no upstream counterpart.
- `Monitoring/BatteryMonitor.cs`: cancellation-driven loop, device events,
  reconnection, charging and initial mute query. Upstream instead refreshes from
  its WinForms timer and treats battery zero as inactive.
- `BatteryRemainingTimeEstimator.cs` / `BatteryRemainingTimeFormatter.cs`:
  discharge history, median rates, plausibility checks and localized rounding;
  no corresponding upstream algorithm exists.

Charging/mute selectors and values follow `Research.md` §§5.1, 5.2, 5.4–5.6:
III/Core `8A/0C`, `86/0A`, value `[2]`; III S `48/0A`, `04/03`, response `[6]`
and notification `[5]`; Alpha `0C/26`, `0A/23`, `[3]`; Stinger 2 charge selector
`03`, `[4]`, codes `0/1/2/3`. Upstream's battery-only routine does not implement
these status paths. HBM's existing connection/retry behavior was inspected and
preserved; this audit does not claim it implements every NGENUITY connection query.

Real-hardware validation in §8.3 covers normal HBM operation on III, Alpha and
Stinger 2. It is not proof that every state or other registered device was tested.

## Historical timeline

| HBM commit | Relevant event |
|---|---|
| `0a33ea3`, 2026-09-07 | Initial III implementation with native HID and continuous reader |
| `52c84f5`, 2026-09-16 | Core, III S, Alpha, Stinger 2 and generic base added; historical usage-selection adaptation |
| `ece4507`, 2026-09-16 | About acknowledgement and original three-language strings added |
| `fe8c751`, 2026-09-18 | README acknowledgement and Special Thanks added |
| `7840b51`, 2026-09-28 | Protocol-specific status parsers and coordinated base reader introduced |
| `573b360`, 2026-09-29 | Old interface heuristics/sizes removed; Stinger 2 parser rewritten |
| `9b8403d`, 2026-09-30 | Five further models, feature transport and correlated-report support added |
| `70d0fc7`, 2026-10-05 | Acknowledgement carried into 11 embedded JSON catalogs |
| `483a7a8`, 2026-10-06 | Audited current final-version baseline |

Files were revised in place; the audit does not describe every old file as deleted
or wholly rewritten. Ordinary HBM lifecycle code and confirmed facts survived.

## Runtime, build and license findings

The only direct NuGet package reference in `HyperXBatteryMonitor.csproj` is
`Microsoft.Toolkit.Uwp.Notifications` 7.1.2. The MSIX project references the HBM
project and its assets. Neither references auto94's solution, HIDAPI, pugixml,
native library files or compiled components. Device communication uses Windows
system DLLs, not NGENUITY binaries. Research is a source of technical knowledge,
not a runtime/build dependency on NGENUITY or auto94.

The original `LICENSE.txt` states **Copyright (c) 2023 auto94** and MIT terms.
The complete original text was read. Its history shows that notice since upstream
commit `079b94a`. MIT requires preservation of its copyright/permission notice
in copies or substantial portions; it does not expressly require an About card.
No technical basis for requiring this card as a current-code attribution was found.

`THIRD_PARTY_NOTICES.md` retains that exact original notice as a conservative
record of historical adaptation, without asserting current derivation. HBM's
existing `LICENSE` is untouched. This does not retroactively change obligations
for older source or distributed binaries. No build/package workflow was altered,
and no installer/MSIX inclusion or publication of the new documentation is claimed.

## Files inspected and changes

Inspected: all 15 `Devices/*.cs`, `Hid/HidConnection.cs`, all three `Monitoring/*.cs`,
device-state integration in `HyperXBatteryMonitorApplicationContext.cs`, detection
and About/Settings partials and layout helpers, localization loader, all 11 catalogs,
README, LICENSE, `.csproj`, `.wapproj`, release notes and repository-wide references.
No `THIRD_PARTY_NOTICES*`, `NOTICE*` or `COPYING*` existed in the source repository.
Historical source and upstream files identified above were also inspected.

Changed:

- `Settings/SettingsForm.About.cs`: entire acknowledgement card, repository
  sub-layout/link/callback removed; now-unused spacing constant removed; legal
  card is the last content row with no trailing inter-card gap.
- All 11 `Languages/*.json`: only `AboutAcknowledgementsTitle`,
  `AboutAcknowledgementsText`, `AboutAcknowledgementsThanks` and
  `AboutAcknowledgementsRepository` removed; 182 becomes 178 keys.
- `README.md`: current-code claim and Special Thanks removed; current protocol
  basis described, with links to the historical notice and this audit. The
  pre-existing 2.3.1 direct installer link returned 404; it was replaced by a
  working Releases-page link, retaining the expected final filename as text.
- `THIRD_PARTY_NOTICES.md` and this report: new historical/evidence documentation.
- `../GPT Context/PROJECT_CONTEXT.md`: audit state, notice decision and 178-key
  count updated; previous acknowledgement and development use recorded factually.

`Research.md`, underlying research, release history/notes, assets, LICENSE,
device/HID/monitoring files and build/install/package workflows remain unchanged.

## Validation and remaining limits

Static validation passed:

- All 11 JSON catalogs parse without duplicate keys and contain exactly the same
  178 keys. Only the four acknowledgement entries were removed; every retained
  translation equals its baseline value.
- Composite-format braces, placeholder syntax and index sets were checked in all
  retained entries against English. No malformed or mismatched entry was found.
  This was a static check, not execution of the .NET resource loader.
- Repository-wide searches found no targeted attribution or deleted localization
  keys in C# source or catalogs. No acknowledgement controls, link callback or
  obsolete margin constant remain. About has five populated content rows and
  the final legal card has no trailing inter-card gap.
- SHA-256 comparison of all 216 baseline tracked files confirmed only 13 changed:
  the 11 catalogs, About partial and README. All other tracked files, including
  device/HID/monitoring code, LICENSE, assets and workflows, remain byte-identical.
  The two added repository files are this report and the historical notice.
- `Research.md` retains its audited hash. The embedded original MIT notice is
  byte-for-byte equal to upstream `LICENSE.txt`.
- All relative Markdown links resolve to existing local files. The five About
  URLs, seven README image URLs and upstream evidence links responded HTTP 200.
  The README's pre-existing direct 2.3.1 installer URL responded 404 and was
  replaced by the accessible Releases page. The existing Microsoft Store URL
  responded 403; the web tool also could not access it, so its availability is
  unverified here. The Store link was preserved rather than guessed/replaced.
- `git diff --check` passed; branch and HEAD remain unchanged. The requested
  context update is outside the repository and is listed separately above.

Remaining-reference classification:

| Location | Classification / decision |
|---|---|
| `THIRD_PARTY_NOTICES.md` | Historical reference; original copyright/license notice retained conservatively |
| This audit report | Technical evidence and historical comparison; necessary to substantiate the decision |
| `README.md` audit-link filename | Technical pointer; visible link text contains no acknowledgement |
| `../GPT Context/PROJECT_CONTEXT.md` | Historical/technical audit state; necessary for future work |
| Git history | Historical evidence; unchanged |
| C# source, About, 11 catalogs, LICENSE, release notes and other tracked documentation | No targeted occurrence remaining |

No improper current-code attribution was found after the edits.

No build, runtime HID test, new hardware capture or Windows visual execution
was authorized/run.
About layout consistency can therefore be assessed statically, but live rendering
at 100–200% DPI and all themes cannot be certified by this audit.

The available history cannot establish undocumented pre-Git authorship, which
reference was read first for every fact, or absence of every conceivable influence.
It does support the narrower finding: no relevant identifiable auto94-derived
implementation remains in the inspected current source. The independent NGENUITY
evidence complements that code/history comparison; it does not replace it.

Remaining occurrences of `auto94` or `HyperX-Cloud-2-Battery-Monitor` belong to
this technical audit, the necessary/conservative original notice and historical
context. They are intentionally retained. Git history is historical evidence.
Any remaining user-visible/current-implementation attribution would be improper
under this conclusion and is checked separately from those preserved records.
