Role



Ship tests/fixtures to keep the pipeline stable.



Inputs



Example JSON plans (valid/invalid).



Outputs



Unit tests for parser/validator/mapper.



A headless smoke test simulating a generate/apply cycle.



Tests



Parser: wrong type at events\[0].steps\[2].wait.ms; missing actors referenced by dialog.



Validator: bounds violations; duplicate actor ids; overly long lines.



Mapper: produces expected number of events/messages; no null refs.



Layout: overlap resolution works.



Fixtures



/tests/fixtures/plan\_valid\_01.json



/tests/fixtures/plan\_invalid\_missing\_actor.json



/tests/fixtures/plan\_overlap\_layout.json



Acceptance Criteria



ctest (or your test runner) passes locally on Linux/Windows.

