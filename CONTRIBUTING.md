# How to Contribute to FootballEngine

## Welcome

Thank you for your interest in contributing to FootballEngine. This is a deterministic, offline football simulation engine written entirely in F#. The project aims to deliver realistic sports management based on 2D physics, with no dice or random elements. Every contribution counts, whether it's fixing bugs, adding features, writing tests, or improving documentation.

## Getting Started

1. **Fork** the repository from GitHub
2. Clone your fork to your machine:
   ```bash
   git clone https://github.com/TU-USERNAME/FootballEngine.git
   cd FootballEngine
   ```
3. Build the project to verify everything is in order:
   ```bash
   dotnet build
   ```
4. Run the tests:
   ```bash
   dotnet test
   ```
5. Create a branch for your contribution:
   ```bash
   git checkout -b feature/branch-name
   ```

## Types of Contributions

- **Bug fixes** — Fixing errors in the simulation, logic, or UI
- **New features** — New functionality within the engine's domain
- **Tests** — Test coverage for existing code
- **Documentation** — READMEs, guides, code comments
- **Performance improvements** — Simulation optimization or benchmarks
- **Translations** — Internationalization of the project

## Development Process

1. **Open an issue first** if the feature is large. This allows the approach to be discussed before writing code
2. **Branch naming:**
   - `feature/` — for new features
   - `fix/` — for bug fixes
   - `test/` — for adding or improving tests
   - `docs/` — for documentation
3. **Conventional Commits:**
   - `feat: description` — for new features
   - `fix: description` — for bug fixes
   - `test: description` — for tests
   - `docs: description` — for documentation
   - `refactor: description` — for code restructuring
4. **Pull Request** with a clear description of the change
5. **Code review** — Wait for the change to be reviewed before merging

## Code Style

Follow the project's conventions to maintain consistency:

- Follow the official **F# style guide**
- Use **Fantomas** as the formatter (runs automatically)
- **Do not add comments** unless explicitly requested
- **Do not use `failwith`** in runtime code — use `Result` types for error handling
- **Do not use `Unchecked.defaultof`** — initialize values safely
- Use **`Result` types** to propagate errors instead of exceptions
- Keep the code clean and functional

## Testing

- **Write tests** for any changes you make
- Follow the project's **3-layer structure** (Client, Core, Infrastructure)
- Use the **test data builders** found in `Infrastructure/Builders.fs`
- Ensure all tests pass before opening a PR:
  ```bash
  dotnet test
  ```

## Pull Requests

- **Descriptive title** that summarizes the change
- In the description:
  - **What changed** — technical summary of the change
  - **Why** — the reason or motivation behind the change
  - **Reference the issue** it resolves (e.g., `Closes #123`)
- All tests must pass
- Build without warnings
- Keep PRs focused — one change per PR

## Reporting Bugs

If you find a bug, open an issue with the following information:

- **Steps to reproduce** — what you did to reach the error
- **Expected behavior** — what you expected to happen
- **Actual behavior** — what actually happened
- **Environment** — .NET version, operating system, project version
- **Screenshots** if applicable

## License

FootballEngine is distributed under the **GNU General Public License v3.0**. By contributing, you agree that your contributions will be distributed under the same license.

## Where to Start?

- Check the issues labeled `good first issue` for simple contributions
- Review the `README.md` to understand the project's architecture
- Explore the directory structure to familiarize yourself with the code
- If you have questions, open a `question` issue

Thank you for contributing to FootballEngine.
