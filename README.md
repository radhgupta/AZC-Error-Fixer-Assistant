# TypeSpec SDK Sample

This repository contains a sample project demonstrating the use of [TypeSpec](https://typespec.io/) for generating SDKs, including C# code generation and Azure SDK analyzer integration.

## Structure

- `src/` - TypeSpec source files
- `tsp-output/` - Generated code output
- `.gitignore` - Standard ignores for Node, TypeSpec, and .NET
- `README.md` - Project overview

## Getting Started

1. Install dependencies:
   ```sh
   npm install
   ```

2. Compile TypeSpec:
   ```sh
   npx tsp compile ./src/main.tsp
   ```

3. Build the generated .NET project:
   ```sh
   cd tsp-output/@azure-tools/typespec-csharp/src
   dotnet build
   ```

## License

MIT