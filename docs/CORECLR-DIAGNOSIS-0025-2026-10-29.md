# CoreCLR native crash: diagnosis, cycle 0025 (2026-10-29)

Status: DIAGNOSIS ONLY. Nothing registered, granted, activated or executed. No application source changed.
Verdict: the crash is NOT reproducible in isolation and is NOT attributable to AgentGateway code. The evidence points at the host platform. That is a hypothesis, not a confirmed root cause.

## What was checked

Windows Application and System logs for 2026-10-29 (read-only), WER archive, LocalDumps config, debugger inventory, then a synthetic repro outside the repo tree.

## Evidence

1. The fault is not specific to our process. Same-day access violations (0xc0000005) and fast-fails (0xc0000409) hit unrelated modules:
   - .NET 10.0.14: `dotnet.exe` coreclr.dll (14:24); `pwsh.exe` clrjit.dll (14:26 AV, 14:58 fast-fail).
   - .NET 11 RC: `VBCSCompiler.exe` coreclr.dll (05:30).
   - .NET Framework 4.8: `devenv.exe` clr.dll, `mscorsvw.exe` clr.dll, `BackgroundDownload.exe` clrjit.dll.
   - Native, no .NET at all: `SearchIndexer.exe`, `WindowsPackageManagerServer.exe`, `KAPS.exe` (4 times, same fault offset), `ShellExperienceHost.exe`, `SystemSettings.exe`, `svchost` (AppXSvc, StateRepository).
2. The "runner exit 0xC0000409" at 14:58 was `pwsh.exe` itself faulting in clrjit.dll 10.0.14 (WER BEX64). It was not the gateway and not the 429 path.
3. Host instability today: 5 Kernel-Power 41 unexpected power losses, and 2 bugchecks:
   - 00:29 0x3B SYSTEM_SERVICE_EXCEPTION (c0000005 in kernel mode).
   - 15:55 0x20001 HYPERVISOR_ERROR (parameters not decoded here).
   - 26 WHEA id 17 corrected PCIe AER events on the NVIDIA GPU endpoint (bus 1). Corrected, so informational only.
4. Platform facts: Windows 11 Enterprise reporting 10.0.26300 while system binaries are 10.0.26100.x (confirm the servicing channel); i9-14900HX; BIOS 1.24.0 (2026-07-21); 31.7 GB RAM; .NET 8.0.33, 10.0.14 and 11.0.0-rc.1 runtimes co-installed.

## Minimal repro (outside repo: `G:\pmcro-diag\repro`)

No project code, no network beyond loopback, no secrets, no models, no workers. Triage-type dump armed per process (`DOTNET_DbgEnableMiniDump=1`, type 3) and kept local.
- JitLoop: parallel JIT, dynamic methods, generics and GC pressure, 10 s per run.
- WebHammer: Kestrel on loopback with a fixed-window limiter returning 429 on `/healthz`, hammered in-process (hundreds of thousands of 429s per run). One process, no pwsh runner.

| Set | Runtime | Runs | Faults |
|---|---|---|---|
| JitLoop | 10.0.14 | 25 | 0 |
| WebHammer | 10.0.14 | 15 | 0 |
| JitLoop | 11.0.0 RC | 5 | 0 |
| WebHammer | 11.0.0 RC | 5 | 0 |

Corrections to the record: the first batch of 10 runs labelled "net11rc" in `G:\pmcro-diag\runs.log` actually ran on 10.0.14 because `DOTNET_ROLL_FORWARD=Major` did not engage; the 10 valid 11 RC runs are in `runs11.out.txt`. The `%time%` stamps in `runs.log` repeat within a block (batch parse-time expansion); they are not durations. No new crash event appeared during any run. Zero dumps were produced.

## What this does not show

About 8 minutes of stress with no fault does not prove the runtime is innocent; the original crashes were sparse. It does show the 429 plus `/healthz` pattern is not a trigger.

## Why there is no dump of the real crash

- WER kept only `Report.wer` for the four dotnet/pwsh crashes; the `.mdmp` files were already removed.
- `LocalDumps` exists in the registry but has no values, so no dumps are retained for new crashes.
- No debugger is installed (cdb, WinDbg, dotnet-dump and procdump all absent), and native frames need one.
- `C:\WINDOWS\MEMORY.DMP` from the bugchecks was not opened. It holds whole-system memory and needs administrator rights.

## Hypotheses, ranked

1. Host platform instability (firmware, microcode, driver, or hardware): fits the multi-runtime, multi-vendor pattern and the power losses and bugchecks. Medium confidence.
2. Unstable pre-release or mixed-channel OS build: weak to medium.
3. Third-party service interference (KAPS.exe repeatedly faults): weak.
4. A runtime defect in 10.0.14 clrjit/coreclr: weak, because Framework 4.8 and the 11 RC fault too and the repro is clean.

## Host-administrator actions that would settle it (not performed)

- Enable WER LocalDumps (mini or triage) for dotnet.exe and pwsh.exe into a folder outside the repo.
- Have an administrator open `MEMORY.DMP` with WinDbg, run vendor memory and CPU diagnostics, and check for newer BIOS and microcode.
- Confirm the Windows servicing channel for build 26300.

Self-validation only. No independent authenticated Checker PASS exists.
