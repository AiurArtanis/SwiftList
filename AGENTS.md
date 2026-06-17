# AI Agent Development and Collaboration Guidelines for C# Projects

When interacting with this repository, performing code modification, compilation, testing, or deployment, the AI agent must strictly adhere to the following guidelines:

1. **On-Demand and Narrow-Scope Compilation**
   * Compile projects only when absolutely necessary.
   * **Blind compilation of the entire solution (e.g., `dotnet build *.sln` or `*.slnx`) is strictly prohibited**. You must compile only the specific subproject/csproj to which the modified files belong (for example: `dotnet build Src/MySubProject.csproj`).
   * This maximizes compilation efficiency, reduces resource usage, and avoids file lock conflicts in large workspaces.

2. **MANDATORY: Stop Service and Terminate Running Process Before Compilation**
   * **You MUST ALWAYS stop the background service (`SwiftList.Service` / `SwiftList.Service.exe`) first, and then terminate any running instance of the SwiftList application (`SwiftList.App.exe`) BEFORE starting any compilation/build** to release binary and DLL locks.
   * Since the service and application may run with elevated privileges (e.g., Administrator), commands should be executed with administrator rights if needed.
   * **Do NOT automatically restart the service or launch the application after the compilation is complete** unless explicitly requested by the user.
   * **Recommended command sequence to execute before building**:
     ```powershell
     # Stop the SwiftList Windows Service (requires Admin privileges)
     powershell -Command "Start-Process net -ArgumentList 'stop SwiftList.Service' -Verb RunAs -WindowStyle Hidden"
     # Force kill running processes
     taskkill /f /im SwiftList.App.exe
     taskkill /f /im SwiftList.Service.exe
     # Elevate taskkill to runas to kill if running with admin privileges
     powershell -Command "Start-Process taskkill -ArgumentList '/f /im SwiftList.App.exe' -Verb RunAs -WindowStyle Hidden"
     powershell -Command "Start-Process taskkill -ArgumentList '/f /im SwiftList.Service.exe' -Verb RunAs -WindowStyle Hidden"
     ```

3. **Code Formatting Before Committing & Authorized Git Actions**
   * Before committing any changes, you must run code formatting on the target project/solution to enforce code styles (configured in `.editorconfig`):
     ```powershell
     dotnet format style <PathToProjectOrSolution> --severity warn
     ```
   * Never execute `git commit` or `git push` without explicit user authorization. All code changes must be reviewed and submitted under the direct instructions of the user.
   * **Commit Message Standard**: All commit messages must be written in **English**.

4. **Launch and Debug Workflows**
   * When launching the built executable, to prevent crashes due to incorrect current working directories, you must explicitly specify the output directory of the target binary as the working directory.
   * **For direct user local launching**, the recommended concise command is:
     ```powershell
     powershell -Command "Start-Process -FilePath '<AppOutputPath>\<AppName>.exe' -WorkingDirectory '<AppOutputPath>'"
     ```
   * **For the AI assistant launching in the background agent and requiring the process to keep running on the desktop**:
     ```powershell
     powershell -Command "Start-Process <AppOutputPath>\<AppName>.exe -WorkingDirectory <AppOutputPath> -Wait"
     ```

5. **Strict Code File Line Limit (Modularization & Decoupling)**
   * All `.cs` and `.xaml` code files must be strictly kept under **300 lines**.
   * Before every compilation/build, you must check the line counts of the modified files. If any file exceeds 300 lines, it must be refactored and decoupled.
   * **Do not use `partial` classes or partial views as a shortcut to bypass this limit**. Instead, perform structural decoupling by extracting clean helper classes, utilizing C# extension methods, or grouping logical subcomponents into subfolders.

6. **Clean File Naming and Directory Namespace Hierarchy**
   * Do not create multi-dot source code files such as `Class.Helper.cs` or `Feature.Service.cs`.
   * Instead, strictly use subdirectories and nested namespace hierarchies (e.g., place the helper in `Feature/Helper.cs` and match it with the nested namespace `MyProject.Feature`). This keeps filenames simple, intuitive, and standard.

7. **App Versioning, Tagging, and Release Flow**
   * When releasing a new version, follow these steps in order:
     1. Locate `<Version>X.Y.Z</Version>` inside the primary `.csproj` file and bump it to the next version (e.g., `2.0.0`).
     2. Commit all functional/code modifications (after running `dotnet format`).
     3. Commit the version bump change:
        ```bash
        git add <PathToProjectOrSolution>
        git commit -m "bump: version vX.Y.Z"
        ```
     4. Tag the commit with the version number (prefixed with `v`):
        ```bash
        git tag vX.Y.Z
        ```
     5. Push both the branch commits and the tag to the remote repository.
