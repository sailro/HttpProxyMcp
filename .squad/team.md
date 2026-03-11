# Squad Team

> httpproxymcp — HTTP/HTTPS MITM proxy with traffic capture and MCP server

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Morpheus | Lead / Architect | `.squad/agents/morpheus/charter.md` | 🏗️ Active |
| Tank | Backend Dev — Proxy & Networking | `.squad/agents/tank/charter.md` | 🔧 Active |
| Mouse | Backend Dev — Storage & MCP | `.squad/agents/mouse/charter.md` | 🔧 Active |
| Switch | Tester / QA | `.squad/agents/switch/charter.md` | 🧪 Active |
| Scribe | Session Logger | `.squad/agents/scribe/charter.md` | 📋 Active |
| Ralph | Work Monitor | — | 🔄 Monitor |

## Project Context

- **Project:** httpproxymcp
- **Stack:** .NET 10, C#, SQLite
- **User:** Sebastien Lebreton
- **Created:** 2026-03-11
- **Universe:** The Matrix
- **Description:** HTTP/HTTPS MITM proxy with traffic capture and MCP server. Intercepts HTTPS via dynamic cert generation from root CA, stores all traffic in SQLite, exposes MCP tools for querying/filtering/searching captured traffic.
