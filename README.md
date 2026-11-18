# ToolTrace

[![CI](https://github.com/jon-avdullahu/tooltrace/actions/workflows/ci.yml/badge.svg)](https://github.com/jon-avdullahu/tooltrace/actions/workflows/ci.yml)

A small **.NET 10 / C#** service for equipment automation data in a semiconductor fab: it ingests
event logs from production tools, rejects bad data with a clear reason, and reports how each tool
spent its time (SEMI E10 states) and which sensor readings were outside their recipe limits.

The focus of the project is **testing**: 64 automated tests (unit, golden-file and API integration
tests), about 95% line coverage, a mutation check of the test suite, CI on every push, and a manual
test plan that tracks which release checks are already automated.

## The problem

Fab tools report what they are doing: which state they are in (productive, waiting, in maintenance,
down) and sensor readings during each process run. Engineers use that data to decide which tool is
losing time and whether a run stayed within its recipe. If the data is wrong, those decisions are
wrong, and the classic errors are quiet ones:

- a timestamp without a time zone, which shifts every duration by an hour;
- a pressure reported in Pa and compared against a limit in mTorr;
- a tool resending events, which double-counts time;
- a clock that is years in the future;
- a malformed line that makes the whole file fail.

ToolTrace refuses to guess. Every bad line is rejected with its line number and a reason, every good
line is kept, and duplicates are recognised even when the same instant is written with a different
UTC offset.

## What it does

| Endpoint | Purpose |
|---|---|
| `POST /api/events` | Upload a tool log (newline-delimited JSON). Returns accepted, duplicate and rejected counts with reasons. |
| `GET /api/equipment/{id}/utilization?from=&to=` | Minutes per SEMI E10 state, availability and utilisation for a time window. |
| `GET /api/runs/{runId}/violations` | Sensor samples outside the recipe's spec limits, plus samples that have no spec at all. |
| `GET /openapi/v1.json`, `GET /health` | API description and health check. |

Log format, one JSON object per line:

```json
{"type":"state","equipment":"ETCH-01","at":"2026-11-16T06:00:00+01:00","state":"PRODUCTIVE"}
{"type":"sample","equipment":"ETCH-01","at":"2026-11-16T06:10:00+01:00","run":"R-1001","recipe":"ETCH-OX-200","parameter":"ChamberPressure","value":11.8,"unit":"mTorr"}
```

**E10 terms used.** Operations time = known time minus non-scheduled time. Uptime = productive +
standby + engineering. Availability = uptime / operations time; utilisation = productive /
operations time. Time before the first known state is reported as *unknown* and left out of the
ratios instead of being guessed.

## How it is tested

```
tests/ToolTrace.Core.Tests   50 tests  parser, validation, store, spec checks, E10 maths, golden files
tests/ToolTrace.Api.Tests    14 tests  the real app hosted in memory (WebApplicationFactory), over HTTP
```

- **Edge cases first.** The E10 calculator is tested for state carried in from before the window,
  changes exactly on the window boundaries, equal timestamps, unsorted input, mixed UTC offsets,
  non-scheduled time and windows with no data.
- **Golden files.** `samples/` holds a clean shift log and a log with one known defect per line.
  Tests assert the exact accepted, duplicate and rejected lines, so a change in parsing or reporting
  cannot slip through.
- **Integration tests over HTTP.** The API tests run the real routing, dependency injection,
  configuration and JSON settings, and only replace the clock (`TimeProvider`), so "the future" is
  deterministic.
- **Do the tests catch bugs?** I planted bugs on purpose (exclusive spec limits, accepting timestamps
  without an offset, a doubled clock-skew tolerance) and checked that tests fail for each one. One
  planted change survived; it turned out to be equivalent (it cannot change any result), which is
  documented in the test plan.
- **Coverage** is measured on our code only (generated code excluded, see `coverage.runsettings`):
  about 95% of lines.
- **CI** (GitHub Actions) checks formatting, builds with warnings treated as errors, runs all tests
  with coverage and uploads the results.
- **Manual testing** is planned in [`docs/manual-test-plan.md`](docs/manual-test-plan.md): 14 release
  checks, each marked as automated, partly automated or manual, with the next candidate for
  automation.

## Run it

Requires the .NET 10 SDK. Open `ToolTrace.slnx` in Visual Studio and use Test Explorer, or:

```bash
dotnet test                                    # all 64 tests
dotnet run --project src/ToolTrace.Api         # then, in a second terminal:

curl -X POST --data-binary @samples/etch-01-shift.ndjson http://localhost:5000/api/events
curl "http://localhost:5000/api/equipment/ETCH-01/utilization?from=2026-11-16T06:00:00%2B01:00&to=2026-11-16T14:00:00%2B01:00"
curl http://localhost:5000/api/runs/R-1001/violations
```

(The port is printed when the app starts.)

## Structure

```
src/ToolTrace.Core     domain: events, parser, validation, in-memory store, spec checks, E10 calculator
src/ToolTrace.Api      ASP.NET Core minimal API, configuration (spec limits in appsettings.json)
tests/                 xUnit test projects
samples/               tool logs used by the tests and for manual testing
docs/                  manual test plan
```

## Limits and next steps

- The store is in memory; a real deployment would put a database behind `IEventStore`.
- Next tests to automate: behaviour across the daylight-saving change, and a load test for
  concurrent uploads.
- Spec limits are illustrative values, not real recipe data.

Personal learning project, not affiliated with any company. Built with AI-assisted development
(Claude Code); I reviewed and can explain every part of the code and tests.
