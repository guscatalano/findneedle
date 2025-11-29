# Storage Tests Documentation

## Overview

The `StorageTests` class provides comprehensive unit testing for the storage implementations in the FindNeedle project. These tests ensure that both `InMemoryStorage` and `SqliteStorage` implementations correctly satisfy the `ISearchStorage` interface contract.

## Architecture

### Test Strategy

The tests use a **parameterized testing approach** where each test method runs against multiple storage implementations:
- **InMemoryStorage**: Volatile, in-memory storage using `List<ISearchResult>`
- **SqliteStorage**: Persistent, database-backed storage using SQLite

This is achieved through MSTest's `[DataTestMethod]` and `[DataRow]` attributes, with a factory pattern to abstract storage creation.

### Key Components

#### Factory Pattern
```csharp
private (Func<ISearchStorage> create, Action cleanup) GetFactoryByKind(string kind)
```
- Provides polymorphic creation of storage instances
- Returns both a factory function and cleanup action
- Enables the same test logic to run against different implementations

#### Test Fixture: `DummySearchResult`
- Mock implementation of `ISearchResult`
- Uses fixed `DateTime` (2020-01-01) for deterministic tests
- Configurable message, username, and result source
- Returns constant values for other fields

#### Database Management
- `CreateUniqueSearchFile()`: Generates unique temp paths for SQLite databases
- `_createdDbPaths`: Tracks all created database files
- `TestCleanup()`: Ensures proper cleanup with retry logic for file locks

## Test Categories

### 1. Basic Functionality Tests

#### `ContentVerification`
**Purpose**: Verifies that data can be written and read back correctly with proper field values.

**Test Flow**:
1. Create storage instance
2. Add two `DummySearchResult` objects with different values
3. Read back all results
4. Assert count and field values match

**Validates**:
- Basic write operations
- Basic read operations
- Data integrity (message, username, resultSource)

#### `ContentAndDateRoundtrip`
**Purpose**: Tests data persistence across dispose/reopen cycles, including DateTime serialization.

**Test Flow**:
1. Create storage, add results, dispose
2. Reopen storage
3. Read results and verify all data including DateTime

**Validates**:
- Persistence (especially for SQLite)
- DateTime serialization/deserialization
- Proper disposal and reopening

#### `DisposeReopen`
**Purpose**: Ensures storage can be safely disposed and reopened.

**Test Flow**:
1. Create storage, add one result, dispose
2. Create new storage instance (reopen)
3. Call `GetStatistics()` to verify storage is functional

**Validates**:
- Proper dispose behavior
- Ability to reopen storage
- Statistics remain queryable

---

### 2. Input Validation Tests

#### `NullBatch_Throws`
**Purpose**: Verifies `ArgumentNullException` is thrown for null batch inputs.

**Test Flow**:
1. Create storage
2. Call `AddRawBatch(null)` - expect exception
3. Call `AddFilteredBatch(null)` - expect exception

**Validates**:
- Proper null argument handling
- Consistent validation across methods

#### `PreCancelledToken_PreventsWork`
**Purpose**: Tests that pre-cancelled cancellation tokens prevent any work from being performed.

**Test Flow**:
1. Create storage
2. Cancel `CancellationTokenSource` before operation
3. Attempt to add results with cancelled token
4. Verify no results were added

**Validates**:
- Cancellation token handling
- Early exit on cancellation
- No partial writes when pre-cancelled

---

### 3. Batching Tests

#### `BatchingBehavior`
**Purpose**: Verifies data is retrieved in correct batch sizes.

**Test Flow**:
1. Add 5 results
2. Read with batch size of 2
3. Verify 3 batches received: [2, 2, 1]
4. Verify correct data in first batch

**Validates**:
- Batch size is respected
- Final partial batch is delivered
- Data ordering in batches

#### `ExactBatching_Boundaries`
**Purpose**: Tests edge cases with various batch sizes.

**Test Flow**:
1. Add 5 results
2. Test with batch size 2 ? expect 3 batches
3. Test with batch size 10 ? expect 1 batch
4. Test with batch size 1 ? expect 5 batches

**Validates**:
- Batch size = item count
- Batch size > item count
- Batch size = 1 (individual items)

#### `LargePayloads_HandleAndBatch`
**Purpose**: Tests batching with large (100KB) payloads.

**Test Flow**:
1. Create string of 100,000 'X' characters
2. Add 3 results with large payload
3. Read with batch size 2
4. Verify 2 batches: [2, 1]

**Validates**:
- Large payload handling
- Batching works with large data
- No size-related failures

---

### 4. Concurrency Tests

#### `Concurrency_AddsArePresent`
**Purpose**: Tests thread-safe concurrent writes.

**Test Flow**:
1. Create 10 parallel tasks
2. Each task writes 100 results
3. Wait for all tasks to complete
4. Verify all 1,000 results are present

**Validates**:
- Thread-safe write operations
- No data loss in concurrent scenarios
- Lock mechanisms work correctly

#### `CancellationDuringWrite_StopsEarly`
**Purpose**: Tests cancellation during write operations.

**Test Flow**:
1. Start adding 10,000 results on background task
2. Cancel token after 5ms
3. Wait for operation to complete
4. Verify partial or complete write (depends on timing)

**Validates**:
- Mid-operation cancellation handling
- Graceful exit on cancellation
- No corruption from interrupted writes

#### `CancellationDuringRead_StopsEarly`
**Purpose**: Tests cancellation during read operations.

**Test Flow**:
1. Add 1,000 results
2. Start reading with batch size 1
3. Cancel token after 5ms (with sleep in callback)
4. Verify fewer than 1,000 results read

**Validates**:
- Read operation cancellation
- Early exit behavior
- Partial read handling

---

### 5. Data Integrity Tests

#### `Ordering_IsPreserved`
**Purpose**: Verifies insertion order is maintained during reads.

**Test Flow**:
1. Add 5 results with messages "O0" through "O4"
2. Read all results
3. Compare message order to input order

**Validates**:
- FIFO ordering
- No reordering during storage/retrieval
- Consistent ordering across implementations

#### `Isolation_RawVsFiltered`
**Purpose**: Tests that raw and filtered result collections are separate.

**Test Flow**:
1. Add 2 raw results (messages start with "raw")
2. Add 2 filtered results (messages start with "f")
3. Read raw results ? verify only "raw" messages
4. Read filtered results ? verify only "f" messages

**Validates**:
- Separate storage for raw vs filtered
- No cross-contamination
- Correct count for each collection

#### `MutationSafety_CallbackMutatingBatchDoesNotAffectStorage`
**Purpose**: Ensures callback mutations don't affect stored data.

**Test Flow**:
1. Add 5 results
2. Read with callback that clears the batch: `batch => batch.Clear()`
3. Read again normally
4. Verify all 5 results still present

**Validates**:
- Storage returns defensive copies or is unaffected by mutations
- Callback modifications don't corrupt storage
- Data safety

---

### 6. Persistence Tests

#### `Persistence_Sqlite_DataPersistsAcrossInstances` (SQLite only)
**Purpose**: Verifies data survives storage disposal and file size grows appropriately.

**Test Flow**:
1. Create storage, add 10 results, dispose
2. Create new storage instance (reopen)
3. Verify 10 results are present
4. Check database file exists and has size > 0

**Validates**:
- Data persists to disk
- Reopening reads persisted data
- Database file is created and contains data

---

### 7. Lifecycle Tests

#### `DisposeBehavior_MultipleDisposeAndUse`
**Purpose**: Tests multiple dispose calls and post-dispose behavior.

**Test Flow**:
1. Create storage
2. Call `Dispose()` twice
3. Attempt to use storage after disposal
4. InMemory: should work (no-op dispose)
5. SQLite: may throw `ObjectDisposedException` or `InvalidOperationException`

**Validates**:
- Multiple dispose calls are safe
- Post-dispose behavior is defined
- No crashes or undefined behavior

#### `Statistics_AreAccurate`
**Purpose**: Verifies `GetStatistics()` returns correct counts.

**Test Flow**:
1. Add 2 raw results
2. Add 1 filtered result
3. Get statistics
4. Verify: rawRecordCount = 2, filteredRecordCount = 1

**Validates**:
- Accurate record counts
- Statistics reflect actual storage state
- Separate counts for raw vs filtered

---

### 8. Performance Tests

#### `Performance_InsertOneMillion` 
**Category**: `[TestCategory("Performance")]`

**Purpose**: Inserts 1 million records in batches of 10,000 and measures resource usage.

**Test Flow**:
1. Prepare: Capture GC memory, process memory, DB size (if SQLite)
2. Insert 1,000,000 results in 100 batches of 10,000 each
3. Measure elapsed time
4. Capture memory after inserts
5. Verify all records written via `GetStatistics()`
6. Report metrics to console

**Metrics Reported**:
- **Timing**: Total seconds to insert 1M records
- **GC Memory Delta**: Change in `GC.GetTotalMemory()`
- **Process Memory Delta**: Change in `PrivateMemorySize64`
- **Storage-Reported Size**: `sizeInMemory` from `GetStatistics()`
- **Database File Size** (SQLite only): Physical file size delta
- **Per-Record Metrics**: All above divided by 1M

**Validates**:
- Performance characteristics
- Memory efficiency
- Scalability to large datasets
- Storage overhead per record

**Example Output**:
```
Inserted 1,000,000 records into InMemory in 2.45s
GC memory delta: 125,436,928 bytes (119.63 MB)
Process private memory delta: 156,000,000 bytes (148.77 MB)
Storage-reported sizeInMemory: 118,000,000 bytes (112.53 MB)
Per-record GC delta: 125.44 bytes
Per-record storage-reported size: 118.00 bytes
```

---

## Test Infrastructure

### Setup and Teardown

#### `TestInitialize()`
- Clears `_createdDbPaths` list
- Runs before each test method

#### `TestCleanup()`
- Deletes all database files in `_createdDbPaths`
- Implements retry logic for file locks (50ms delay)
- Runs after each test method

### Helper Methods

#### `CreateUniqueSearchFile()`
Returns: `(string searchedFile, string dbPath)`
- Generates unique GUID-based temp file path
- Uses `CachedStorage.GetCacheFilePath()` for DB location
- Registers DB path for cleanup
- Deletes existing DB file if present

#### Factory Methods

**`InMemoryFactory()`**
- Creates single `InMemoryStorage` instance
- Returns factory that always returns same instance
- Cleanup action calls `Dispose()`

**`SqliteFactory()`**
- Creates unique DB path via `CreateUniqueSearchFile()`
- Returns factory that creates new `SqliteStorage` with that path
- Cleanup action is no-op (TestCleanup handles file deletion)

**`GetFactoryByKind(string kind)`**
- Routes to appropriate factory based on kind: "InMemory" or "Sqlite"
- Throws `ArgumentException` for unknown kinds

---

## Test Attributes

### `[TestClass]`
Marks the class as containing test methods.

### `[DoNotParallelize]`
**Critical**: Prevents parallel execution to avoid file system race conditions with SQLite databases.

### `[DataTestMethod]`
Indicates a parameterized test method.

### `[DataRow("InMemory")]` / `[DataRow("Sqlite")]`
Provides parameter values for each test run. Most tests run twice: once for each storage type.

### `[TestCategory("Performance")]`
Applied to `Performance_InsertOneMillion` to allow filtering performance tests.

---

## Coverage Summary

| Category | Tests | Coverage |
|----------|-------|----------|
| Basic Functionality | 3 | CRUD operations, persistence |
| Input Validation | 2 | Null handling, pre-cancelled tokens |
| Batching | 3 | Various batch sizes, large payloads |
| Concurrency | 3 | Parallel writes, cancellation |
| Data Integrity | 3 | Ordering, isolation, mutation safety |
| Persistence | 1 | Cross-instance data survival (SQLite) |
| Lifecycle | 2 | Dispose behavior, statistics |
| Performance | 1 | 1M record insert with metrics |
| **Total** | **18** | **Comprehensive coverage** |

---

## Running the Tests

### Run All Storage Tests
```bash
dotnet test --filter "FullyQualifiedName~StorageTests"
```

### Run Specific Storage Type
```bash
# InMemory only
dotnet test --filter "FullyQualifiedName~StorageTests&DisplayName~InMemory"

# SQLite only
dotnet test --filter "FullyQualifiedName~StorageTests&DisplayName~Sqlite"
```

### Run Performance Tests
```bash
dotnet test --filter "TestCategory=Performance"
```

### Exclude Performance Tests
```bash
dotnet test --filter "TestCategory!=Performance"
```

---

## Implementation Details

### Thread Safety (InMemoryStorage)
- Uses `lock(_sync)` around all list modifications
- Creates snapshots for read operations to avoid holding locks during callbacks
- Callback execution happens outside lock scope

### Transactions (SqliteStorage)
- Batch inserts use SQLite transactions for atomicity
- Transaction committed only if no cancellation requested
- Early exit on cancellation (may leave partial transaction uncommitted)

### Memory Calculation Fix
**Bug Fixed**: `CalculateResultSize()` now accounts for all fields, not just message:
- All 8 string fields: message, machineName, username, taskName, opCode, source, searchableData, resultSource
- DateTime: 8 bytes
- Level enum: 4 bytes
- Total overhead: 12 bytes per record

Previous implementation only counted message size, significantly underreporting memory usage.

---

## Design Patterns Used

### Strategy Pattern
Tests interact with storage through the `ISearchStorage` interface, allowing different implementations to be swapped transparently.

### Factory Pattern
`GetFactoryByKind()` creates storage instances based on string parameter, centralizing creation logic.

### Test Fixture Pattern
`DummySearchResult` provides consistent, deterministic test data across all tests.

### Arrange-Act-Assert Pattern
All tests follow AAA structure:
1. **Arrange**: Create storage and test data
2. **Act**: Perform operations (add, read, etc.)
3. **Assert**: Verify expected outcomes

---

## Known Behaviors

### InMemory Dispose
- `Dispose()` is a no-op (no resources to release)
- Storage remains usable after disposal
- Included for interface compliance

### SQLite Dispose
- Closes and disposes database connection
- Further operations throw `ObjectDisposedException` or `InvalidOperationException`
- File locks released after disposal

### Cancellation Timing
- InMemory operations may complete before cancellation due to speed
- Tests account for this with lenient assertions in cancellation scenarios
- `CancellationDuringWrite_StopsEarly` accepts partial or complete writes for InMemory

---

## Future Enhancements

Potential test additions:
- **Error Handling**: Test behavior with corrupted SQLite databases
- **Stress Testing**: Extended duration tests with continuous writes/reads
- **Memory Leaks**: Long-running tests to detect memory leaks
- **Recovery**: Test recovery from partial failures
- **Schema Migration**: Test database schema version upgrades
- **Query Performance**: Test read performance with large datasets
- **Compression**: Test with compressed data if implemented

---

## Dependencies

### Test Framework
- **MSTest**: `Microsoft.VisualStudio.TestTools.UnitTesting`

### Storage Implementations
- `FindPluginCore.Implementations.Storage.InMemoryStorage`
- `FindPluginCore.Implementations.Storage.SqliteStorage`

### Interfaces
- `FindNeedlePluginLib.Interfaces.ISearchStorage`
- `FindNeedlePluginLib.Interfaces.ISearchResult`

### Utilities
- `FindNeedleCoreUtils.CachedStorage`: Database file path management

### External
- `Microsoft.Data.Sqlite`: SQLite database provider
- `System.Threading.Tasks`: Parallel operations
- `System.Diagnostics`: Performance measurement

---

## Troubleshooting

### File Lock Errors
**Symptom**: `IOException` during `TestCleanup()`
**Solution**: `TestCleanup()` implements 50ms retry logic. If persistent, check for orphaned processes holding database locks.

### Performance Test Failures
**Symptom**: `Performance_InsertOneMillion` fails with incorrect count
**Solution**: Verify sufficient memory available. Test requires ~150-200MB for InMemory, disk space for SQLite.

### Concurrent Test Failures
**Symptom**: Random failures in `Concurrency_AddsArePresent`
**Solution**: Ensure `[DoNotParallelize]` attribute is present. Check for external processes interfering with test database files.

---

## Contributing

When adding new tests:
1. Follow the parameterized test pattern with `[DataTestMethod]` and `[DataRow]`
2. Use the factory pattern via `GetFactoryByKind()`
3. Add both "InMemory" and "Sqlite" data rows (unless testing storage-specific behavior)
4. Include descriptive test names that indicate what is being tested
5. Add cleanup via `factory.cleanup()` at test end
6. Update this README with test documentation

---

## License

Part of the FindNeedle project. See main project LICENSE for details.
