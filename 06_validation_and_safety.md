Role



Add guardrails so we never corrupt a field. Validate JSON and content; sandbox “Apply” behind a clear preview diff.



Inputs



ScenePlan + current field state.



Outputs



Validation results with levels: INFO/WARN/ERROR.



UI list of issues with quick links to offending items.



Checks



Schema compliance (types).



Actor ids referenced in steps exist.



Text length ≤ 100 chars/line; no control chars.



Event trigger zones in bounds.



Script step coverage: unknown types → ERROR.



Optional content checks (profanity filter toggle).



Acceptance Criteria



“Apply” disabled when there are ERRORS.



WARNINGS allowed with confirmation.

