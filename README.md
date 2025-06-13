# TypeSpec Standalone SDK Sample (AZC0030 Error Trigger)

This repository demonstrates how to use [TypeSpec](https://typespec.io/) to generate a standalone C# SDK from a TypeSpec definition, with the specific goal of triggering the AZC0030 error. The generated SDK is standalone, requiring some manual adjustments to the generated files, particularly `.csproj` and `Directory.Build.props`, to ensure it builds and runs independently.

## Objective

- **Trigger AZC0030 error**: This repo is intentionally structured to surface the AZC0030 error during build or analysis.
- **Standalone SDK**: The generated C# SDK does not depend on the Azure SDK monorepo structure and is meant to be built and used independently.
- **Manual adjustments**: Some changes are required in the generated files to support standalone operation.

## Repository Structure

- `src/` - TypeSpec source files (`main.tsp`)
- `tsp-output/` - Generated C# SDK output
- `.gitignore` - Standard ignores for Node, TypeSpec, and .NET
- `Directory.Build.props` - Project-wide MSBuild properties and package references
- `README.md` - This documentation
> The `helper` folder has a sample `sample-tsp-output` for reference.

## Getting Started

Follow these steps to reproduce the scenario and trigger the AZC0030 error:

### 1. Install Dependencies

```sh
npm install
```

### 2. Clone Azure SDK for .NET

Clone the [azure/azure-sdk-for-net](https://github.com/Azure/azure-sdk-for-net) repository. This is required because the generated SDK references shared sources from this repo.

```sh
git clone https://github.com/Azure/azure-sdk-for-net.git
```

### 3. Compile TypeSpec

Run the TypeSpec compiler to generate the C# SDK from your TypeSpec definition:

```sh
npx tsp compile ./src/main.tsp
```

This will emit generated C# code into the `tsp-output/@azure-tools/typespec-csharp/` directory.

### 4. Update `Directory.Build.props`

The generated SDK expects to find Azure Core shared sources. Update the `<AzureCoreSharedSources>` property in the root `Directory.Build.props` to point to your local clone of the Azure SDK for .NET:

```xml
<PropertyGroup>
  <AzureCoreSharedSources>C:\path\to\azure-sdk-for-net\sdk\core\Azure.Core\src\Shared\</AzureCoreSharedSources>
</PropertyGroup>
```

Replace `C:\path\to\azure-sdk-for-net\` with the actual path where you cloned the repo.

### 5. Replace Generated `.csproj` and Add `Nuget.config`

- Replace the `.csproj` file in the generated folder (`tsp-output/@azure-tools/typespec-csharp/src/`) with the `.csproj` file present in the `helper` folder.
- Move the  `Nuget.config` from helper folder to the same level as the generated `.csproj` (i.e., in `tsp-output/@azure-tools/typespec-csharp/src/`).

### 6. Build the Generated SDK

Navigate to the generated SDK directory and build the project:

```sh
cd tsp-output/@azure-tools/typespec-csharp/src
dotnet build --no-incremental
```

## Notes on Standalone SDK Adjustments

- **.csproj and Directory.Build.props**: The generated `.csproj` and `Directory.Build.props` files may require manual tweaks to ensure all dependencies are correctly referenced and the project builds as a standalone SDK. These changes are not required to be done by the Users who generate SDKs for Azure.
- **NuGet Feeds**: The `Nuget.config` in the generated output is set up to use both `nuget.org` and the Azure SDK DevOps feed.
- **Analyzer Integration**: The repo is set up to use Azure SDK analyzers, which will surface the AZC0030 error as intended.

## Troubleshooting

- **AZC0030 Error**: If the error does not appear, ensure that analyzers are enabled and the shared sources path is correct.
- **Dependency Issues**: Double-check that all required NuGet packages are restored and the Azure SDK for .NET repo is up to date.

## License