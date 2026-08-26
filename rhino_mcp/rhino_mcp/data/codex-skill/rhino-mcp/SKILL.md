---
name: rhino-mcp
description: Start and use the installed Rhino MCP bridge for an explicitly routed Rhino or Grasshopper request. Use when the user invokes $rhino-mcp or begins the request with /RhinoMCP; do not use for unrelated work.
---

# Rhino MCP

Treat the text after `/RhinoMCP` as the user's Rhino or Grasshopper task. An
explicit `$rhino-mcp` invocation is equivalent.

Before reading or changing the model, call `ensure_rhino_ready` with
`open_dashboard=true`. This check starts Rhino 8 when it is closed, waits for
the local bridge, and displays the connection page in Chrome when available.

Continue only after the tool reports that the bridge is connected. If startup
fails, report its `next_step` exactly and do not attempt geometry operations.
Once connected, use the safe structured tools, prefer batches for multiple
changes, and preserve the normal dry-run and undo safeguards.
