# TypeSpec SDK Sample with AZC Error Fixer

This repository serves two main purposes:
1. Demonstrates how to generate a standalone C# SDK from TypeSpec definitions
2. Provides an AI-powered tool (AzcErrorFixer) to automatically fix AZC analyzer violations and compilation errors

## Repository Structure

```
├── src/                           - TypeSpec source files
│   ├── main.tsp                  - Main TypeSpec definitions
│   └── client.tsp                - Client customizations
├── AzcErrorFixer/                - Automated AZC error fixing tool
├── helper/                       - Reference implementations
└── Directory.Build.props         - Project-wide MSBuild properties
```

### AzcErrorFixer Tool

### Overview
AzcErrorFixer is an AI-powered tool that automatically detects and fixes both AZC analyzer violations and TypeSpec compilation errors in TypeSpec files. It uses Azure AI to analyze error logs and generate appropriate fixes in the client.tsp file, with support for iterative compilation error fixing.

### Features
- **Automated Error Detection**: Scans TypeSpec output for AZC violations and compilation errors
- **AI-Powered Analysis**: Uses Azure AI to understand and fix violations
- **Iterative Fixing**: Multiple passes to resolve all issues
- **Smart Client.tsp Generation**: Creates or updates client.tsp with proper customizations
- **Validation**: Ensures fixes comply with Azure SDK guidelines
- **Compilation Error Recovery**: Detects and fixes TypeSpec compilation errors
- **Resource Management**: Efficient cleanup of AI resources between iterations
- **Backup Creation**: Automatic backup of source files before modifications

### Prerequisites
- Azure AI resource with deployed model (e.g., GPT-4)
- .NET 9.0 or later
- Azure CLI with authenticated session
- TypeSpec compiler and dependencies

### Configuration
Create `appsettings.json` in the AzcErrorFixer directory:
```json
{
  "ProjectEndpoint": "https://your-azure-ai-endpoint",
  "Model": "deployment-name",
  "MaxIterations": 5,
  "TypeSpecSrcPath": "../src",
  "LogPath": "../tsp-output/@azure-tools/typespec-csharp/src/azc-errors.log"
}
```

### Using AzcErrorFixer

1. **Build the Tool**
```sh
cd AzcErrorFixer
dotnet build
```

2. **Run the Fixer**
```sh
dotnet run
```

3. **Monitor Progress**
The tool will:
- Create an AI agent session
- Upload TypeSpec files and error logs
- Generate fixes for detected violations
- Update client.tsp with corrections
- Validate changes and fix compilation errors
- Create backups before modifications
- Clean up resources between iterations

### Error Handling Process

1. **AZC Error Resolution**
- Detects AZC violations in build output
- Generates fixes using AI analysis
- Updates client.tsp with corrections
- Validates changes against SDK guidelines

2. **Compilation Error Resolution**
- Detects TypeSpec compilation errors
- Attempts multiple fix iterations if needed
- Validates successful compilation
- Maximum retry attempts configurable

3. **Resource Management**
- Creates backups before modifications
- Manages AI agent resources efficiently
- Cleans up threads and vector stores between iterations
- Maintains agent context for improved performance

### Error Fixers

The tool includes an extensible error fixing system located in the `Core/ErrorFixers` directory. You can easily add support for new AZC error types:

1. **Available Error Fixers**
- `Azc00012FixerTool.cs`: Handles single-word type name violations
- `Azc00030FixerTool.cs`: Handles model naming convention violations

2. **Adding New Error Fixers**
To add support for a new AZC error type:

```csharp
public class NewAzcErrorFixerTool : IErrorFixerTool
{
    public string ErrorCode => "AZC####"; // Your error code
    
    public string GetFixSuggestion(AzcError error)
    {
        // Implement your fixing logic here
        return "Your fix suggestion";
    }
}
```

3. **Registration**
Register your new fixer in `Startup.cs`:
```csharp
services.AddTransient<IErrorFixerTool, NewAzcErrorFixerTool>();
```

4. **Best Practices for Error Fixers**
- Keep each fixer focused on a single error type
- Provide clear, actionable suggestions
- Include examples in comments
- Add test cases for your fixer
- Document any special handling requirements

The error fixers work together with the AI agent to provide targeted solutions for specific AZC violations, making the fixes more reliable and consistent.

### Common Scenarios

1. **Fixing AZC0030 (Model Naming)**
```typespec
// Before
model Disk {
  // properties
}

// After (in client.tsp)
@@clientName(Disk, "ComputeDisk", "csharp");
```

2. **Multiple Violations**
The tool can handle multiple AZC errors in a single pass:
- AZC0008: Missing service version
- AZC0012: Incorrect model naming
- AZC0015: Operation group naming
- AZC0020: Parameter naming

### Troubleshooting

1. **Connection Issues**
- Verify Azure AI endpoint is accessible
- Check DefaultAzureCredential setup
- Ensure proper RBAC permissions

2. **Fix Validation Failures**
- Review error logs for specific AZC violations
- Check client.tsp syntax
- Verify TypeSpec compiler version
- Examine compilation error details

3. **Performance Issues**
- Check network connectivity
- Monitor resource cleanup between iterations
- Verify AI model deployment status
- Review backup creation success