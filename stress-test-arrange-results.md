# Stress Test Results: arrange_domain_model (Phase 20)

**Date**: 2026-02-22
**Tool**: `arrange_domain_model` (tool #76)
**Methodology**: Based on `test-methodology.md` — dual validation (check_model + check_project_errors)

---

## Test Results

### TEST 01: Standalone arrange on MyFirstModule (6 entities, 8 associations)
```
SETUP: Existing module with Customer, Category, Product, Order, OrderLine, Review
ARRANGE: OK — 6 entities arranged, 2 trees, 0 orphans
LAYOUT:
  Tree 1: Customer(175,50) → Order(50,250) → OrderLine(50,420), Review(300,250)
  Tree 2: Category(750,50) → Product(750,220)
  Bounding box: 900x520
CHECK_MODEL: PASS (0 errors, 0 warnings)
CHECK_MPR: PASS (0 errors)
RESULT: PASS
```

### TEST 02: Standalone arrange on Shipping module (3 entities, 4 associations)
```
SETUP: Existing module with Carrier, Shipment, ShipmentItem (+ cross-module assocs)
ARRANGE: OK — 3 entities arranged, 1 tree, 0 orphans
LAYOUT:
  ShipmentItem(50,50) → Shipment(50,220) → Carrier(50,405)
  Bounding box: 200x505
CHECK_MODEL: PASS (0 errors, 0 warnings)
CHECK_MPR: PASS (0 errors)
RESULT: PASS
```

### TEST 03: Auto-arrange via create_domain_model_from_schema (5 entities, 6 associations)
```
SETUP: Created LayoutTest module
CREATE: 5 entities (Department, Employee, Project, Task, TimeEntry) + 6 associations
AUTO_ARRANGE: auto_arranged=true (triggered automatically after schema creation)
LAYOUT:
  Department(175,50) → Employee(50,220) → Task(50,390) → TimeEntry(50,560)
                     → Project(300,220)
  1 tree, 0 orphans, bounding box 450x660
CHECK_MODEL: PASS (0 errors, 0 warnings)
RESULT: PASS
```

### TEST 04: Create entities + associations separately, then arrange
```
SETUP: Created LayoutTest2 module
CREATE_ENTITIES: 6 entities (School, Teacher, Student, Course, Enrollment, Library)
  auto_arranged=true (entities arranged as grid, no associations yet)
CREATE_ASSOCIATIONS: 6 associations added separately
ARRANGE: OK — re-arranged with association awareness
LAYOUT:
  School(300,50) → Teacher(50,220) → Course(50,390) → Enrollment(50,560)
                 → Student(300,220)
                 → Library(550,220)
  1 tree, 0 orphans, bounding box 700x660
CHECK_MODEL: PASS (0 errors, 0 warnings)
RESULT: PASS
```

### TEST 05: Orphan entities (mixed connected + unconnected)
```
SETUP: Created OrphanTest module
CREATE: 5 entities (3 orphans + 2 connected with 1 association)
ARRANGE: OK — 5 entities arranged, 4 trees, 0 orphans
LAYOUT:
  Connected1(50,50) → Connected2(50,220)
  Orphan1(500,50), Orphan2(950,50), Orphan3(1400,50)
  Orphans treated as single-entity trees, placed side-by-side
NOTE: "orphans" count=0 because unconnected entities are valid single-root trees
CHECK_MODEL: PASS (0 errors, 0 warnings)
RESULT: PASS
```

### TEST 06: Edge cases — empty module + non-existent module
```
EMPTY MODULE:
  ARRANGE: OK — success=true, "No entities to arrange", entities_arranged=0
  RESULT: PASS

NON-EXISTENT MODULE:
  ARRANGE: OK — success=false, error="Module 'NonExistent' not found"
  RESULT: PASS (correct error handling)
```

### TEST 07: Full dual validation across all test modules
```
check_model LayoutTest:  PASS (0 errors, 0 warnings, 5 entities, 6 associations)
check_model LayoutTest2: PASS (0 errors, 0 warnings, 6 entities, 6 associations)
check_model OrphanTest:  PASS (0 errors, 0 warnings, 5 entities, 1 association)
check_project_errors:    PASS (0 errors, 0 warnings — full MPR validation)

POST-CLEANUP:
  Deleted 4 test modules (LayoutTest, LayoutTest2, OrphanTest, EmptyTest)
  check_project_errors: PASS (0 errors, 0 warnings)
RESULT: PASS
```

---

## Summary

```
=== FINAL SUMMARY ===
TOTAL TESTS: 7
PASS: 7 (TEST 01, 02, 03, 04, 05, 06, 07)
PARTIAL PASS: 0
FAIL: 0

BUGS FOUND: 0

NOTES:
- Orphan entities are treated as single-entity trees (correct behavior)
- Auto-arrange after create_multiple_entities triggers even without associations
  (arranges as grid initially, can be re-arranged after associations added)
- Auto-arrange after create_domain_model_from_schema works perfectly since
  entities + associations are created in same transaction
- Cross-module associations (Shipping module) don't affect intra-module layout
- Empty module returns graceful success with entities_arranged=0
- Non-existent module returns clear error message
```

---

## Layout Algorithm Observations

| Scenario | Trees | Orphans | Layout Quality |
|----------|-------|---------|---------------|
| 6 entities, 8 associations | 2 | 0 | Good — two distinct trees side-by-side |
| 3 entities, 4 associations | 1 | 0 | Good — vertical chain |
| 5 entities, 6 associations | 1 | 0 | Good — hierarchical tree |
| 6 entities, 6 associations | 1 | 0 | Good — root centered above 3 children |
| 5 entities, 1 association | 4 | 0 | Good — connected pair + 3 singles spread out |
| 0 entities | N/A | N/A | Graceful no-op |
