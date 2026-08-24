# Rhino native package

`RhinoMCP.Plugin` is a Rhino 8 `.rhp` plug-in that loads at startup, exposes a
compact connection strip in the active viewport, opens a live local browser dashboard,
and hosts the framed localhost bridge. The dashboard is embedded in the plug-in, binds
only to loopback, and needs no separate web server or internet connection.
`RhinoMCP.Grasshopper`
is a bundled `.gha` add-on that starts when Grasshopper opens; no canvas file or
script component is required.

Build both assemblies and a Package Manager `.yak` archive:

```sh
./scripts/build-yak.sh
```

The resulting package is written to `dist/` with both assemblies under `net7.0/`
and `net48/`. Both builds target the Rhino 8.0 SDK, so the same package works on
older Rhino 8 installations (including 8.1) and with either Rhino runtime.

After the package owner has logged in to the Rhino package server, publish it with:

```sh
./scripts/publish-yak.sh
```
