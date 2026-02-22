# Comprehensive Microflow Stress Test Results (Run 2)
# Started: 2026-02-21
# Module: MyFirstModule
# Protocol: check_model (in-memory) + check_project_errors (mx.exe MPR check) after EVERY step

---

=== TEST 01: MF_BasicCRUD ===
SETUP: Created TestCustomer (FullName:String, Email:String, Age:Integer)
CREATE_MF: OK — MF_BasicCRUD (Boolean)
ADD_ACTIVITIES: OK (5 created: create_object, change_attribute, commit, retrieve, delete_object)
READ_BACK: 5 activities confirmed (create_object, change_object, commit, retrieve, delete)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
RETRY: N/A
CLEANUP: OK — deleted MF_BasicCRUD, TestCustomer
POST_CLEANUP_MODEL: PASS (0 errors, 0 entities, 0 microflows)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 02: MF_ListProcessing ===
SETUP: Created TestProduct (ProductName:String, Price:Decimal, InStock:Boolean)
CREATE_MF: OK — MF_ListProcessing (Integer)
ADD_ACTIVITIES: OK (4 created: retrieve, filter_list, sort_list, aggregate_list)
READ_BACK: 4 activities confirmed (retrieve, filter, sort, aggregate_list)
NOTE: sort descending:true reads back as descending:false (known issue from Run 1)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
RETRY: N/A
CLEANUP: OK — deleted MF_ListProcessing, TestProduct
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS (with NOTE on sort direction)
---

=== TEST 03: MF_AssociationTraversal ===
SETUP: Created TestDepartment (DeptName:String), TestEmployee (EmpName:String, Salary:Decimal), association TestDepartment_TestEmployees (one-to-many)
CREATE_MF: OK — MF_AssociationTraversal (Void)
ADD_ACTIVITIES: PARTIAL (4/5 created)
  - create_object x2: OK
  - change_association: OK (reads back as change_object with memberKind:association) — BUG-020 NOT REPRODUCED in Run 2!
  - commit: OK
  - retrieve_by_association: FAILED — BUG-021 CONFIRMED
READ_BACK: 4 activities confirmed (commit, change_object, create_object x2)
CHECK_MODEL: PASS (0 errors) — 4 valid activities, no model errors
CHECK_MPR: PASS (0 errors)
RETRY: N/A (retrieve_by_association is confirmed bug from Run 1)
CLEANUP: OK — deleted MF_AssociationTraversal, TestEmployee, TestDepartment
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PARTIAL PASS — change_association works, retrieve_by_association FAILS (BUG-021)
---

=== TEST 04: MF_MicroflowCall ===
SETUP: Created TestItem (ItemName:String, Quantity:Integer), helper MF_Helper_GetCount (Integer)
CREATE_MF: OK — MF_MicroflowCall (Integer)
ADD_ACTIVITIES: OK (2 created: create_object, microflow_call)
READ_BACK: 2 activities confirmed (microflow_call, create_object)
NOTE: commit:"Yes" reads back as "No" — BUG-022 confirmed
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
RETRY: N/A
CLEANUP: OK — deleted MF_MicroflowCall, MF_Helper_GetCount, TestItem
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS (with BUG-022 note)
---

=== TEST 05: MF_ObjectRollback ===
SETUP: Created TestNote (Title:String, Body:String)
CREATE_MF: OK — MF_ObjectRollback (Void)
ADD_ACTIVITIES: OK (3 created: create_object, change_attribute, rollback)
READ_BACK: 3 activities confirmed (rollback, change_object, create_object)
NOTE: commit:"Yes" reads back as "No" — BUG-022 confirmed again
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
RETRY: N/A
CLEANUP: OK — deleted MF_ObjectRollback, TestNote
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 06: MF_BinaryListOps ===
SETUP: Created TestTag (TagName:String)
CREATE_MF: OK — MF_BinaryListOps (Boolean)
ADD_ACTIVITIES: OK (6 created: create_list x2, union_lists, subtract_lists, intersect_lists, contains_in_list)
READ_BACK: 6 activities confirmed (contains, intersect, subtract, union, create_list x2)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
RETRY: N/A
CLEANUP: OK — deleted MF_BinaryListOps, TestTag
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 07: MF_UnaryListOps ===
SETUP: Created TestRecord (RecordName:String, Priority:Integer)
CREATE_MF: OK — MF_UnaryListOps (Void)
ADD_ACTIVITIES: OK (4 created: retrieve, head_of_list, tail_of_list, find_in_list)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
RETRY: N/A
CLEANUP: OK — deleted MF_UnaryListOps, TestRecord
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 08: MF_ReduceList ===
SETUP: Created TestInvoiceLine (LineAmount:Decimal, Description:String)
CREATE_MF: OK — MF_ReduceList (Decimal)
ADD_ACTIVITIES: OK (2 created: retrieve, reduce_list)
CHECK_MODEL: PASS (0 errors) — IN-MEMORY CHECK MISSES THE ERROR!
CHECK_MPR: FAIL (1 error: CE0117 "Error(s) in expression." at reduce_list activity)
RETRY: Deleted MF, recreated with same params → STILL FAILING (CE0117)
  *** BUG-023 CONFIRMED: reduce_list expression error not caught by check_model ***
  Expression used: "$accumulator + $currentObject/LineAmount"
  mx.exe reports: CE0117 at Aggregate list activity 'Run reducer over AllLines'
CLEANUP: OK — deleted MF_ReduceList, TestInvoiceLine
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: FAIL (BUG-023: reduce_list expression error)
---

=== TEST 09: MF_ChangeObjectMembers ===
SETUP: Created TestProfile (FirstName:String, LastName:String, Active:Boolean)
CREATE_MF: OK — MF_ChangeObjectMembers (Void)
ADD_ACTIVITIES: OK (2 created: create_object, change_object with members)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_ChangeObjectMembers, TestProfile
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 10: MF_ListAddRemoveClear ===
SETUP: Created TestTask (TaskName:String, Done:Boolean)
CREATE_MF: OK — MF_ListAddRemoveClear (Void)
ADD_ACTIVITIES: OK (5 created: create_object, create_list, change_list add/remove/clear)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_ListAddRemoveClear, TestTask
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 11: MF_ReturnObject ===
SETUP: Created TestResult (ResultValue:String, Score:Integer)
CREATE_MF: OK — MF_ReturnObject (Object, entity:MyFirstModule.TestResult)
ADD_ACTIVITIES: OK (1 created: create_object)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_ReturnObject, TestResult
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 12: MF_ReturnList + XPath ===
SETUP: Created TestEvent (EventName:String, Active:Boolean)
CREATE_MF: OK — MF_ReturnList (List, entity:MyFirstModule.TestEvent)
ADD_ACTIVITIES: OK (1 created: retrieve with xpath:[Active = true()])
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_ReturnList, TestEvent
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 13: MF_RetrieveVariants ===
SETUP: Created TestConfig (ConfigKey:String, ConfigValue:String)
CREATE_MF: OK — MF_RetrieveVariants (Void)
ADD_ACTIVITIES: OK (3 created: retrieve first, retrieve all, retrieve custom limit:10 offset:0)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_RetrieveVariants, TestConfig
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 14: MF_WithParameters ===
SETUP: Created TestLog (Message:String, Level:Integer)
CREATE_MF: OK — MF_WithParameters (Boolean, params: LogMessage:String, LogLevel:Integer, IsError:Boolean)
ADD_ACTIVITIES: OK (1 created: create_object with param references $LogMessage, $LogLevel)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_WithParameters, TestLog
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 15: MF_AggregateVariants ===
SETUP: Created TestSale (Amount:Decimal, SaleDate:DateTime)
CREATE_MF: OK — MF_AggregateVariants (Void)
ADD_ACTIVITIES: OK (5 created: retrieve, aggregate sum/average/min/max)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_AggregateVariants, TestSale
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 16: MF_ModifyActivity ===
SETUP: Created TestModify (Value:String)
CREATE_MF: OK — MF_ModifyActivity (Void)
ADD_ACTIVITIES: OK (2 created: create_object, commit)
MODIFY: OK — set create_object commit=Yes (pos 2), set commit refreshInClient=true (pos 1)
READ_BACK: Verified create_object.commit="Yes", commit.refreshInClient=true
NOTE: modify_microflow_activity correctly sets commit=Yes (confirms BUG-022 workaround)
NOTE: refresh_in_client param must be boolean (true), not string ("true")
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_ModifyActivity, TestModify
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 17: MF_InsertBefore ===
SETUP: Created TestInsert (InsertVal:String)
CREATE_MF: OK — MF_InsertBefore (Void)
ADD_ACTIVITIES: OK (2 initial: create_object, commit)
INSERT: OK — inserted retrieve before position 1 (commit)
READ_BACK: 3 activities confirmed (commit, create_object, retrieve)
CHECK_MODEL: PASS (0 errors)
CHECK_MPR: PASS (0 errors)
CLEANUP: OK — deleted MF_InsertBefore, TestInsert
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: PASS
---

=== TEST 18: MF_UrlAndUpdate ===
SETUP: Created TestEndpoint (Data:String)
CREATE_MF: OK — MF_UrlAndUpdate (String)
ADD_ACTIVITIES: OK (1 created: retrieve first)
SET_URL: OK — v1/test/endpoint
UPDATE_MF: OK — return_type changed to Boolean
READ_BACK: Verified url="v1/test/endpoint", returnType="Boolean"
CHECK_MODEL: PASS (0 errors) — IN-MEMORY CHECK MISSES THE ERROR!
CHECK_MPR: FAIL (1 error: CE0117 "Error(s) in expression." at End event)
RETRY: Deleted MF, recreated String→Boolean again → STILL FAILING (CE0117)
  *** BUG-024 CONFIRMED: update_microflow return type change doesn't update end event expression ***
  mx.exe reports: CE0117 at End event
CLEANUP: OK — deleted MF_UrlAndUpdate, TestEndpoint
POST_CLEANUP_MODEL: PASS (0 errors)
POST_CLEANUP_MPR: PASS (0 errors)
RESULT: FAIL (BUG-024: update_microflow end event expression not updated)
---

=== FINAL SUMMARY (Run 2) ===
TOTAL TESTS: 18
PASS: 15 (TEST 01, 02, 04, 05, 06, 07, 09, 10, 11, 12, 13, 14, 15, 16, 17)
PARTIAL PASS: 1 (TEST 03 — change_association OK, retrieve_by_association FAILS)
FAIL: 2 (TEST 08, TEST 18)

BUGS FOUND:
- BUG-021 (confirmed): retrieve_by_association fails silently during creation
- BUG-022 (confirmed): create_object commit param ignored during creation (workaround: modify_microflow_activity)
- BUG-023 (NEW in Run 2): reduce_list expression produces CE0117 — check_model misses it, mx.exe catches it
- BUG-024 (NEW in Run 2): update_microflow return type change doesn't update end event expression — CE0117 at End event

NOTES:
- BUG-020 NOT REPRODUCED: change_association works correctly in Run 2
- sort_list descending:true reads back as descending:false (cosmetic/read issue)
- modify_microflow_activity refresh_in_client requires boolean type, not string
- check_model (in-memory) misses expression errors that mx.exe (check_project_errors) catches
  → CRITICAL: Always use check_project_errors for reliable validation!

