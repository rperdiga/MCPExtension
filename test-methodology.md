# MCP Extension — Stress Test Methodology

## Purpose

This document defines a reusable methodology for comprehensive stress testing of MCP Extension tools against Studio Pro. It is designed to be adapted across tool categories: Microflows, Domain Model, Pages, and others.

---

## Core Principles

1. **Isolation** — Use a dedicated empty module (e.g., `MyFirstModule`) as a test sandbox
2. **Immediate cleanup** — Each test cleans up after itself to prevent error compounding
3. **Dual validation** — Always run BOTH `check_model` (in-memory) AND `check_project_errors` (mx.exe MPR-level)
4. **Retry once** — If a test fails, delete and recreate once to confirm the bug is reproducible
5. **No fixes during testing** — Log bugs, move on, fix in a separate session
6. **Sequential execution** — Each test must leave the module clean before the next starts

---

## Test Protocol (per test)

```
STEP 1 — SETUP
  Create required model elements (entities, associations, enums, etc.) in the test module.

STEP 2 — CREATE
  Create the target artifact (microflow, page, etc.) and add sub-elements (activities, widgets, etc.)

STEP 3 — VERIFY (read-back)
  Use the appropriate read/introspection tool to confirm:
  - Correct count of sub-elements
  - Correct types, names, variables
  - Properties match what was requested

STEP 4 — CHECK_MODEL (in-memory)
  Run: check_model module_name=<TestModule>
  Expected: 0 errors, 0 warnings
  NOTE: This check may miss expression-level errors!

STEP 5 — CHECK_MPR (mx.exe disk-level)
  Run: check_project_errors
  Expected: 0 errors
  CRITICAL: This catches errors that check_model misses (CE0117 expression errors, etc.)

STEP 6 — IF ERROR: RETRY
  - Delete the artifact
  - Recreate with identical parameters
  - Run Steps 4-5 again
  - If still failing → LOG as confirmed bug, do NOT attempt to fix
  - If now passing → LOG as flaky/intermittent, note it

STEP 7 — CLEANUP
  Delete in reverse dependency order:
  - Artifacts (microflows, pages) first
  - Then associations
  - Then entities
  - Then enums/constants

STEP 8 — CONFIRM_MODEL (post-cleanup)
  Run: check_model module_name=<TestModule>
  Must show 0 errors, 0 entities, 0 microflows (clean slate)

STEP 9 — CONFIRM_MPR (post-cleanup)
  Run: check_project_errors
  Must show 0 errors (clean slate confirmed at disk level)
```

---

## Log Format

Each test writes structured results to a results file:

```
=== TEST XX: <TestName> ===
SETUP: Created <elements with types>
CREATE_<TYPE>: OK | FAIL (error details)
ADD_<SUBELEMENTS>: OK (N created: types...) | PARTIAL (M/N) | FAIL
READ_BACK: N elements confirmed (types...) | MISMATCH (expected X got Y)
CHECK_MODEL: PASS (0 errors) | FAIL (N errors: ...)
CHECK_MPR: PASS (0 errors) | FAIL (N errors: CE0XXX "message" at location)
RETRY: N/A | Retried → PASS | Retried → STILL FAILING (confirmed bug)
CLEANUP: OK — deleted <elements> | FAIL (leftover: ...)
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS | PARTIAL PASS | FAIL (BUG-XXX: description)
---
```

---

## Bug Logging Format

```
BUG-XXX: <short title>
  Category: <Microflow|DomainModel|Page|Other>
  Tool: <tool_name>
  Symptom: <what happens>
  Expected: <what should happen>
  check_model: PASS | FAIL (does in-memory check catch it?)
  check_project_errors: PASS | FAIL (does mx.exe catch it?)
  Reproducible: Yes (confirmed) | Intermittent | Not reproduced in Run 2
  Workaround: <if any>
```

---

## Test Design Guidelines

### 1. Coverage Matrix
For each tool category, build a matrix of:
- **All tool operations** (create, read, modify, delete)
- **All parameter variants** (types, optional params, edge cases)
- **All sub-element types** (activity types for microflows, attribute types for entities, widget types for pages)

### 2. Scenario Design
Each test scenario should:
- Target a **specific set of related operations** (not just one tool call)
- Use **realistic names and values** that would appear in real apps
- Test **parameter combinations** that are commonly used together
- Include at least one **introspection/verification step**

### 3. Cleanup Order
Always delete in reverse dependency order to avoid orphan references:
```
Pages → Microflows → Associations → Entities → Enums → Constants → Modules
```

### 4. Parameter Name Reference
Document the exact parameter names each tool expects, since they may differ from schema names:
```
Tool: create_microflow
  - name (not microflow_name)
  - returnType (not return_type) — PascalCase!
  - module_name
  - parameters: [{name, type}]
  - returnEntity (for Object/List return types)

Tool: update_microflow
  - return_type (snake_case!) — different from create_microflow!

Tool: create_microflow_activities
  - module_name, microflow_name
  - activities: [{type, ...params per type}]

Tool: modify_microflow_activity
  - position (not activity_position)
  - refresh_in_client: must be boolean, not string

Tool: insert_before_activity
  - activity.type (not activity_type) — dot notation inside the activity object

Tool: delete_model_element
  - element_type required (e.g., "entity")

Tool: create_association
  - parent/child (not parent_entity/child_entity)
  - name (not association_name)
```

---

## Dual Validation: Why Both Checks Matter

| Check | Method | Catches | Misses |
|-------|--------|---------|--------|
| `check_model` | In-memory Extensions API | Structural errors, missing references, type mismatches | Expression errors (CE0117), end event issues |
| `check_project_errors` | mx.exe on MPR file | ALL errors including expressions, XPath, end events | Nothing (authoritative) |

**Rule**: A test only PASSES if BOTH checks return 0 errors.

The in-memory check is faster but unreliable for expression validation. The mx.exe check is slower (~2-3 seconds) but is the authoritative source of truth.

---

## Results Summary Format

```
=== FINAL SUMMARY (Run N) ===
TOTAL TESTS: XX
PASS: XX (TEST NN, NN, ...)
PARTIAL PASS: XX (TEST NN — description)
FAIL: XX (TEST NN, NN)

BUGS FOUND:
- BUG-XXX: <title>
- BUG-XXX: <title>

NOTES:
- <observation 1>
- <observation 2>
```

---

## Adapting for Other Tool Categories

### Domain Model Tests (future)
- Entity CRUD (all attribute types: String, Integer, Decimal, Boolean, DateTime, Long, Binary, HashedString, Enumeration, AutoNumber)
- Association variants (one-to-one, one-to-many, many-to-many, reference/reference set)
- Delete behaviors (all 4 combinations)
- Generalization/inheritance
- Calculated attributes
- Event handlers
- Validation rules
- Indexes

### Page Tests (future)
- Page creation with different layouts
- Widget placement (data view, list view, template grid, data grid)
- Widget nesting
- Data source configuration
- Visibility conditions
- Styling/classes

### Constants & Enumerations Tests (future)
- Create/read/update/delete constants (all value types)
- Create/read/update/delete enumerations
- Enumeration values (add, remove, reorder)
- Constants used in microflow expressions

---

## Known Bugs (as of Run 2 — 2026-02-21)

| Bug | Category | Tool | Status | Workaround |
|-----|----------|------|--------|------------|
| BUG-021 | Microflow | retrieve_by_association | Confirmed | Use retrieve + filter instead |
| BUG-022 | Microflow | create_object (commit param) | Confirmed | Use modify_microflow_activity after creation |
| BUG-023 | Microflow | reduce_list (expression) | Confirmed | None yet — expression CE0117 |
| BUG-024 | Microflow | update_microflow (return type) | Confirmed | Create microflow with correct return type initially |

### Resolved Bugs
| Bug | Status |
|-----|--------|
| BUG-020 | change_association — NOT REPRODUCED in Run 2 (was intermittent or fixed) |

### Cosmetic Issues
- `sort_list` descending:true reads back as descending:false (may be read-only display issue)
- `create_object` commit:"Yes" reads back as "No" even when workaround applied (display vs actual)
