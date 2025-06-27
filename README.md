# TypeSpec SDK Sample with AZC Error Fixer

This repository serves two main purposes:
1. Demonstrates how to generate a standalone C# SDK from TypeSpec definitions
2. Provides an AI-powered tool (AzcErrorFixer) to automatically fix AZC analyzer violations

## Repository Structure

```
├── src/                           - TypeSpec source files
│   ├── main.tsp                  - Main TypeSpec definitions
│   └── client.tsp                - Client customizations
├── AzcErrorFixer/               - Automated AZC error fixing tool
├── helper/                      - Reference implementations
└── Directory.Build.props        - Project-wide MSBuild properties
```


### AzcErrorFixer Tool

### Overview
AzcErrorFixer is an AI-powered tool that automatically detects and fixes AZC analyzer violations in TypeSpec files. It uses Azure AI to analyze error logs and generate appropriate fixes in the client.tsp file.

### Features
- **Automated Error Detection**: Scans TypeSpec output for AZC violations
- **AI-Powered Analysis**: Uses Azure AI to understand and fix violations
- **Iterative Fixing**: Multiple passes to resolve all issues
- **Smart Client.tsp Generation**: Creates or updates client.tsp with proper customizations
- **Validation**: Ensures fixes comply with Azure SDK guidelines

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
- Validate the changes

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

3. **Performance Optimization**
- Adjust MaxIterations in settings
- Monitor vector store indexing
- Check file upload sizes

## Contributing
Contributions are welcome! Please read our contributing guidelines and submit pull requests for any improvements.

## License
[MIT License](LICENSE)