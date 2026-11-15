# Contributing

Thanks for your interest. FootballEngine is in active development and the design moves fast, so open an issue before starting anything large.

## Setup

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/<your-username>/FootballEngine.git
cd FootballEngine
dotnet build
dotnet fsi scripts/test.fsx --fast
```

`dotnet fsi scripts/test.fsx` runs the full gate. Tests use Expecto, so `dotnet test` is not the command. `docs/TESTING.md` explains filters and reports.

## Rules that matter

[AGENTS.md](AGENTS.md) is the full guide. The short version:

* **Systems never call each other.** They talk through the match world and events.
* **Every behaviour number lives in `BalanceConfig`,** with a name that says what it does.
* **No `failwith` or `Unchecked.defaultof` in runtime code.** Use `Result` and `Option`.
* **Comments explain why, never what.**
* **Format with Fantomas.** The pre-commit hook checks it: `dotnet fantomas .`
* **A pure refactor must not change the match hash.** Check with `dotnet fsi scripts/dev.fsx hash`.

## Pull requests

* One focused change per PR, with [Conventional Commits](https://www.conventionalcommits.org/) messages.
* Say what changed and why, and link the issue.
* Tests must pass. New behaviour comes with a test that fails without it.

## Bugs

Open an issue with steps to reproduce, what you expected, what happened, your OS and a screenshot if it helps. A match seed makes any match bug reproducible.

## License

By contributing you agree your work is released under [GPL 3.0](LICENSE).
