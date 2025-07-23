# Test Database Retrieve Activity with Custom Range

This document contains test examples for the updated database retrieve functionality that now supports custom range parameters (limit and offset) instead of just "all" or "first" options.

## Test Case 1: Custom Range with Limit 10 and Offset 0

This should create a database retrieve activity that retrieves the first 10 items.

```json
{
  "microflow_name": "TestMicroflow",
  "activity_type": "retrieve_from_database",
  "activity_config": {
    "entityName": "Customer",
    "outputVariable": "CustomerList",
    "range": "custom",
    "limit": 10,
    "offset": 0,
    "xpath": ""
  }
}
```

## Test Case 2: Custom Range with Limit 5 and Offset 10

This should create a database retrieve activity that skips the first 10 items and retrieves the next 5.

```json
{
  "microflow_name": "TestMicroflow",
  "activity_type": "retrieve_from_database",
  "activity_config": {
    "entityName": "Customer",
    "outputVariable": "CustomerList",
    "range": "custom",
    "limit": 5,
    "offset": 10,
    "xpath": ""
  }
}
```

## Test Case 3: Default Custom Range (no limit/offset specified)

This should create a database retrieve activity with default limit 10 and offset 0.

```json
{
  "microflow_name": "TestMicroflow",
  "activity_type": "retrieve_from_database",
  "activity_config": {
    "entityName": "Customer",
    "outputVariable": "CustomerList",
    "range": "custom"
  }
}
```

## Test Case 4: "All" Range (backward compatibility)

This should still work as before, retrieving all items.

```json
{
  "microflow_name": "TestMicroflow",
  "activity_type": "retrieve_from_database",
  "activity_config": {
    "entityName": "Customer",
    "outputVariable": "CustomerList",
    "range": "all"
  }
}
```

## Test Case 5: "First" Range (backward compatibility)

This should still work as before, retrieving only the first item.

```json
{
  "microflow_name": "TestMicroflow",
  "activity_type": "retrieve_from_database",
  "activity_config": {
    "entityName": "Customer",
    "outputVariable": "CustomerList",
    "range": "first"
  }
}
```

## Expected Behavior

- **Custom Range**: Uses the complex `CreateDatabaseRetrieveSourceActivity` overload with `(IMicroflowExpression startingIndex, IMicroflowExpression amount)` parameter
- **All Range**: Uses the boolean overload with `retrieveJustFirstItem = false`
- **First Range**: Uses the boolean overload with `retrieveJustFirstItem = true`

The custom range implementation should create proper `IMicroflowExpression` objects for the offset and limit values using `IMicroflowExpressionService.CreateFromString()`.

## Key Changes Made

1. **Enhanced Parameter Extraction**: Added support for `limit` and `offset` parameters with defaults (10 and 0 respectively)
2. **Multiple Overload Support**: Now uses different `CreateDatabaseRetrieveSourceActivity` overloads based on range type
3. **Expression Creation**: Uses `IMicroflowExpressionService` to create proper expressions for numeric limit/offset values
4. **Backward Compatibility**: Maintains support for existing "all" and "first" range options

## Testing Instructions

1. Create a microflow first using the `create_microflow` tool
2. Use the `create_microflow_activity` tool with one of the test cases above
3. Check that the activity is created successfully with the expected range configuration
4. Verify in Mendix Studio Pro that the Range is set to "Custom" with the correct Limit and Offset values

## Integration with MCP Client

When using through an MCP client like Claude Desktop, you can call this as:

```javascript
await use_mcp_tool({
  server_name: "mendix-studio-pro",
  tool_name: "create_microflow_activity",
  arguments: {
    microflow_name: "TestMicroflow",
    activity_type: "retrieve_from_database",
    activity_config: {
      entityName: "Customer",
      outputVariable: "CustomerList",
      range: "custom",
      limit: 10,
      offset: 0
    }
  }
});
```
