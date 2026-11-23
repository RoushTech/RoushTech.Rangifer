# Rangifer

[![CI](https://github.com/RoushTech/RoushTech.Rangifer/actions/workflows/ci.yml/badge.svg)](https://github.com/RoushTech/RoushTech.Rangifer/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**RoushTech's shared .NET tools.**

Named after *Rangifer*, the reindeer genus.

## Modules

| Module | Package | Description |
|---|---|---|
| [Thin EF Core migration designers](docs/thin-migration-designers.md) | [`RoushTech.Rangifer.EntityFrameworkCore`](https://www.nuget.org/packages/RoushTech.Rangifer.EntityFrameworkCore/) | Runtime half of thin migration designers: rebuilds each migration's target model from a chain of model deltas |
| | [`RoushTech.Rangifer.EntityFrameworkCore.Design`](https://www.nuget.org/packages/RoushTech.Rangifer.EntityFrameworkCore.Design/) | Design-time half: `dotnet ef migrations add` writes thin designers |
| [`rangifer`](docs/cli.md) | [`RoushTech.Rangifer.Cli`](https://www.nuget.org/packages/RoushTech.Rangifer.Cli/) | The `rangifer` dotnet tool: converts existing migration designers |
| [Mail](docs/mail.md) | [`RoushTech.Rangifer.Mail`](https://www.nuget.org/packages/RoushTech.Rangifer.Mail/) | Liquid email templates that encode every value, an SMTP transport, one-click unsubscribe headers and Resend webhook checks |
