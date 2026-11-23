# Contributing

Issues and pull requests are welcome. For a security problem, see [SECURITY.md](SECURITY.md) instead.

## Build and test

You need the .NET 10 SDK, and Node.js for the JavaScript tests.

```bash
dotnet build Glazor.slnx
```

```bash
dotnet test Glazor.Tests/Glazor.Tests.csproj
```

- Build and test inside the repository. Some tests read files from the repository's own tree, so a build with its output
  outside it fails them.
- The tests of the components' `.razor.js` modules run under Node. Without Node they are skipped, not failed: install it
  before you trust a green run that touched JavaScript.
- The tests run on SQLite, in memory. Glazor itself is used and tested on SQL Server; another provider may order rows
  differently, as the [roadmap](ROADMAP.md) says, and support for one is a welcome contribution.

To see a change in the components, run the demo and open the address it prints:

```bash
dotnet run --project Glazor.Demo
```

## Before you change behaviour

Several things in Glazor look like bugs and are deliberate. [CLAUDE.md](CLAUDE.md) lists them with their reasons; read it
before proposing to change one, and say why the reason no longer holds.

## Pull requests

- Open them against `main`, one change per pull request, as small as does the job, in the style of the code around it.
- A change of behaviour comes with a test that fails without it. An argument check that only refuses bad input needs none,
  and a log message is not tested.
- A change to a `.razor.js` module must also be tried in a real browser: the Node tests use fake elements, and their
  measurements do not follow a browser's.
- Update the docs in `docs/` when a documented behaviour or parameter changes.
- Comments are short and say why, never what the code already says.

By contributing you agree that your contribution is licensed under the [Apache License 2.0](LICENSE.txt), as the rest of
the project is.
