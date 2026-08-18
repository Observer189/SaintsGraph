# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Runtime core with an xNode-shaped API: `Node`, `NodeGraph`, `NodePort`,
  `[Input]`/`[Output]`, `[CreateNodeMenu]`, `[NodeTint]`, `[NodeWidth]`,
  `[DisallowMultipleNodes]`, `[RequireNode]`, `[PortTypeOverride]`, `[NodeEnum]`.
- Graph-level single edge storage (`NodeEdge`) instead of xNode's mirrored
  per-port connection lists.
- Lazy, per-type port reflection — no assembly scanning, no assembly-name
  filtering pitfalls.
- Edit-mode test suite for the runtime core.
