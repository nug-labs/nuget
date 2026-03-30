This directory receives `nuglabs_core.wasm` during CI/package builds.

- Source of truth: `https://github.com/nug-labs/sdk-core`
- The publish pipeline clones that repo, builds the WASM artifact, and copies it here before `dotnet pack`.

Do not commit `.wasm` binaries in this repository.
