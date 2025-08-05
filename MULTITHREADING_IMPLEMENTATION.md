# Multithreaded SDK Generation and Error Checking Implementation

This document describes the implementation of multithreaded SDK generation with error checking and configurable logging in UES (Unreal Engine Sharp).

## Problem Statement

The original issue requested:
1. **Multithreaded SDK generation** for better performance
2. **Error checking** to prevent infinite loops during SDK generation
3. **Option to disable failed memory chunk reading logging** to reduce log noise

## Solution Overview

The implementation adds three new configuration options to `UESConfig` and enhances the `SDKGenerator` with parallel processing and timeout protection.

## New Configuration Options

### 1. EnableMultithreadedSDKGeneration
- **Type**: `bool`
- **Default**: `true`
- **Purpose**: Controls whether SDK generation uses parallel processing
- **Usage**:
  ```csharp
  UESConfig.EnableMultithreadedSDKGeneration = true;  // Enable parallel processing
  UESConfig.EnableMultithreadedSDKGeneration = false; // Use sequential processing
  ```

### 2. DisableMemoryReadFailureLogging
- **Type**: `bool`
- **Default**: `false`
- **Purpose**: Controls whether memory read failures are logged
- **Usage**:
  ```csharp
  UESConfig.DisableMemoryReadFailureLogging = true;  // Disable memory read failure logging
  UESConfig.DisableMemoryReadFailureLogging = false; // Enable memory read failure logging
  ```

### 3. SDKGenerationTimeoutSeconds
- **Type**: `int`
- **Default**: `300` (5 minutes)
- **Purpose**: Sets the timeout for SDK generation to prevent infinite loops
- **Usage**:
  ```csharp
  UESConfig.SDKGenerationTimeoutSeconds = 600; // 10 minute timeout
  UESConfig.SDKGenerationTimeoutSeconds = 60;  // 1 minute timeout
  ```

## Enhanced SDK Generation

### Multithreading Implementation

When `EnableMultithreadedSDKGeneration` is enabled, the SDK generator uses:
- `Parallel.For` for object scanning
- `Parallel.ForEach` for package generation
- `ConcurrentDictionary` and `ConcurrentBag` for thread-safe collections
- Optimal degree of parallelism based on processor count

### Infinite Loop Protection

Multiple layers of protection prevent infinite loops:

1. **Timeout Protection**: The entire SDK generation process is wrapped in a `CancellationToken` with the configured timeout
2. **Iteration Limits**: Individual loops have maximum iteration counts:
   - Object traversal: 1,000 iterations
   - Field generation: 10,000 iterations
   - Function generation: 10,000 iterations
   - Parameter generation: 1,000 iterations
3. **Circular Reference Detection**: `HashSet` collections track visited objects to prevent circular references
4. **Cancellation Support**: All loops check for cancellation requests

### Error Handling

- Memory read failures use the new `Logger.LogMemoryReadFailure()` method
- This method respects the `DisableMemoryReadFailureLogging` setting
- Critical errors are still logged regardless of the setting
- Failed operations are gracefully handled without stopping the entire process

## Usage Examples

### Basic Configuration
```csharp
// Enable all new features (default)
UESConfig.EnableMultithreadedSDKGeneration = true;
UESConfig.DisableMemoryReadFailureLogging = false;
UESConfig.SDKGenerationTimeoutSeconds = 300;

// Generate SDK
var engine = new UnrealEngine();
engine.GenerateSDK("output/path");
```

### Performance-Focused Configuration
```csharp
// Maximize performance, minimize logging
UESConfig.EnableMultithreadedSDKGeneration = true;
UESConfig.DisableMemoryReadFailureLogging = true;
UESConfig.SDKGenerationTimeoutSeconds = 600; // Longer timeout for large games

var engine = new UnrealEngine();
engine.GenerateSDK("output/path");
```

### Debug-Friendly Configuration
```csharp
// Disable multithreading for easier debugging, enable all logging
UESConfig.EnableMultithreadedSDKGeneration = false;
UESConfig.DisableMemoryReadFailureLogging = false;
UESConfig.SDKGenerationTimeoutSeconds = 60; // Shorter timeout for quick feedback
UESConfig.EnableVerboseLogging = true;

var engine = new UnrealEngine();
engine.GenerateSDK("output/path");
```

## Performance Benefits

- **Multithreading**: Up to N-times faster SDK generation on N-core systems
- **Reduced Logging**: Eliminates verbose memory read failure logs when not needed
- **Timeout Protection**: Prevents hanging applications and provides quick feedback

## Backwards Compatibility

- All existing code continues to work without changes
- Default settings maintain existing behavior
- New features are opt-in and don't affect existing functionality

## Implementation Details

### Files Modified
- `UES/UESConfig.cs` - Added new configuration options
- `UES/Logger.cs` - Added `LogMemoryReadFailure()` method
- `UES/SDK/SDKGenerator.cs` - Enhanced with multithreading and timeout support
- `UES/Memory/ExternalMemory.cs` - Updated chunked reading to use configurable logging

### Key Classes
- `SDKGenerator` - Main class handling SDK generation with new features
- `UESConfig` - Configuration management
- `Logger` - Enhanced logging with configurable memory read failure logging

The implementation provides a robust, performant, and configurable SDK generation system that addresses all the requirements in the original problem statement.