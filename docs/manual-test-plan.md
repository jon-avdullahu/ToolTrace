# Manual test plan and automation status

Run before every release on a running instance (`dotnet run --project src/ToolTrace.Api`).
Each check says whether it is already automated. The aim is to move checks from the
"manual" column into the test suite over time, so the release checklist gets shorter, not longer.

| # | Check | How to test by hand | Automated? |
|---|---|---|---|
| 1 | A clean tool log is accepted completely | `POST /api/events` with `samples/etch-01-shift.ndjson`; expect 19 accepted, 0 rejected | Yes: `The_clean_shift_log_is_accepted_completely`, API `Utilization_endpoint_returns_the_e10_breakdown` |
| 2 | Broken lines are rejected with a line number and a reason, good lines are kept | Upload `samples/etch-01-defects.ndjson`; read every reason in the response as if you were the tool engineer: is it clear what to fix? | Counts and line numbers: yes (`The_defect_log_rejects_exactly_the_broken_lines`). **Clarity of the wording: manual** |
| 3 | Timestamps without a UTC offset are rejected | Send `"at":"2026-11-16T06:00:00"` | Yes: parser theory cases |
| 4 | Resent events are not counted twice, also across uploads and UTC offsets | Upload the same file twice; then send one event in `+01:00` and again in `Z` | Yes: `Uploading_the_same_file_twice_stores_nothing_new`, `Resent_events_are_counted_as_duplicates_even_with_a_different_offset` |
| 5 | Wrong units are rejected before spec checks | Send a ChamberPressure sample in `Pa` | Yes: `Rejects_a_sample_whose_unit_differs_from_the_spec` |
| 6 | E10 breakdown matches a hand calculation | Pick a window, add up the minutes per state on paper, compare | Yes for the sample shift (`The_shift_log_gives_the_expected_e10_breakdown`). **New real-world logs: manual** |
| 7 | Spec violations match the recipe limits, limits are inclusive | `GET /api/runs/R-1001/violations` | Yes: `SpecTests`, `Violations_endpoint_lists_out_of_spec_samples` |
| 8 | Bad queries give a clear 400/404 problem response | Missing `from`, reversed window, unknown tool or run | Yes: `Bad_queries_get_clear_error_responses` |
| 9 | Oversized uploads are refused and nothing is stored | Upload a file above `Ingestion:MaxBodyBytes` | Yes: `Oversized_upload_is_rejected_without_storing_anything` |
| 10 | Files with Windows line endings work | Save a sample with CRLF and upload it | Yes: `Handles_windows_line_endings_and_blank_lines` |
| 11 | Concurrent uploads do not lose or corrupt data | Upload from several terminals at once | Store level: yes (`Is_safe_under_concurrent_writes`). **End to end under real load: manual** |
| 12 | OpenAPI document is published and matches the endpoints | Open `/openapi/v1.json` | Partly: the document is checked for one route |
| 13 | Startup fails loudly on a broken spec configuration | Set `Lower` above `Upper` in `appsettings.json` and start | Catalogue validation: yes (`A_broken_spec_configuration_fails_at_startup`). **Startup message: manual** |
| 14 | Behaviour around the daylight-saving change (last Sunday of March / October) | Upload a log that crosses 02:00-03:00 local time with `+01:00` and `+02:00` offsets | **Manual; next candidate for automation** |

## Found while testing

- A state change exactly at the start of a window can be handled with `<` or `<=`; mutation testing
  showed both give the same result (a zero-length segment), so there is no test for the difference
  on purpose.
- Uploading the defect log after the shift log reports 2 duplicates instead of 1, because its first
  line repeats an event that is already stored. Correct behaviour, but worth knowing when reading
  ingest reports.
